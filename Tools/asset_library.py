"""Brings bought assets from the library project into LoomRoom, with everything they use.

Every bought pack (Synty and any other) is kept in a separate Unity project outside git (C:/Game projects/AssetLibrary),
so the repository only holds what the game uses. This copies chosen assets across with their .meta files and at the same
paths, so GUIDs (and every reference between prefabs, meshes, materials, textures and shaders) stay as they are, and
a later import of the whole pack merges cleanly instead of making duplicates.

    python Tools/asset_library.py find <words>            library assets whose path has all the words (prefabs first)
    python Tools/asset_library.py pull <asset|folder> ... [--dry-run] [--scripts]
                                                       copy the assets and what they depend on into LoomRoom
    python Tools/asset_library.py deps <asset|folder> ... the same as pull --dry-run
    python Tools/asset_library.py unpack <file.unitypackage> ...
                                                       put a downloaded pack into the library (as Unity's import
                                                       would: same paths and GUIDs, newer files replace older ones);
                                                       Unity imports it the next time the library is opened

An asset is a path under the library's Assets ("Synty/PolygonDungeon/Prefabs/SM_Prop_Barrel_01.prefab", with or
without "Assets/"), or a folder (everything in it). Dependencies are found from the GUIDs written in each asset and
its .meta (a model's remapped materials), followed to the end. Nothing in LoomRoom is ever overwritten: an asset
whose GUID is already in the project is skipped, and a file already at the target path with another GUID is reported
as a conflict and left alone. Scripts (.cs, .dll) are left out unless --scripts is given (they compile into the
game's one assembly), and listed so their components can be removed. GUIDs not in the library (packages, Unity's
built-in assets) are left as they are. Unity picks the files up on its next refresh.

Options: --library <path> (default C:/Game projects/AssetLibrary, or ASSET_LIBRARY), --project <path> (this repo)."""
import os, re, shutil, sys, tarfile

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.normpath(os.path.join(HERE, ".."))
LIBRARY = os.environ.get("ASSET_LIBRARY", r"C:\Game projects\AssetLibrary")

GUID_LINE = re.compile(rb"^guid:\s*([0-9a-f]{32})", re.M)
# YAML ("guid: x"), and JSON in shader graphs, escaped or not ("guid":"x", \"guid\":\"x\")
GUID_REF = re.compile(rb'guid\\?"?\s*:\s*\\?"?([0-9a-f]{32})')
BUILT_IN = {"0000000000000000e000000000000000", "0000000000000000f000000000000000", "0" * 32}
SCRIPTS = {".cs", ".dll", ".asmdef", ".asmref"}
NO_REFS = {".fbx", ".obj", ".blend", ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".tiff", ".exr", ".hdr",
           ".wav", ".mp3", ".ogg", ".aif", ".ttf", ".otf", ".glb", ".blend1", ".dll", ".pdb", ".mdb", ".pdf", ".zip",
           ".unitypackage", ".mp4", ".mov", ".bytes", ".data"}


def index(root):
    """guid -> path (relative to the project, "Assets/..."), read from every .meta under root/Assets."""
    found = {}
    assets = os.path.join(root, "Assets")
    for folder, dirs, files in os.walk(assets):
        for name in files:
            if not name.endswith(".meta"):
                continue
            meta = os.path.join(folder, name)
            try:
                with open(meta, "rb") as f:
                    head = f.read(400)
            except OSError:
                continue
            m = GUID_LINE.search(head)
            if m:
                found[m.group(1).decode()] = os.path.relpath(meta[:-5], root).replace("\\", "/")
    return found


def refs(root, rel):
    """GUIDs an asset points at: in its own text (if it is text) and in its .meta."""
    out = set()
    path = os.path.join(root, rel)
    for p, own in ((path, False), (path + ".meta", True)):
        if not os.path.isfile(p) or (not own and os.path.splitext(p)[1].lower() in NO_REFS):
            continue
        with open(p, "rb") as f:
            data = f.read()
        if not own and b"\0" in data[:8000]:
            print(f"  note: {rel} is binary, its references can't be read")
            continue
        for m in GUID_REF.finditer(data):
            out.add(m.group(1).decode())
        if own:
            m = GUID_LINE.search(data)
            if m:
                out.discard(m.group(1).decode())
    return out - BUILT_IN


def asset_path(arg):
    arg = arg.replace("\\", "/").strip("/")
    lib = LIBRARY.replace("\\", "/").rstrip("/") + "/"
    if arg.lower().startswith(lib.lower()):
        arg = arg[len(lib):]
    if not arg.startswith("Assets/"):
        arg = "Assets/" + arg
    return arg


def starting(args, by_path):
    """The library assets named on the command line (a folder gives every asset in it)."""
    picked = []
    for arg in args:
        rel = asset_path(arg)
        full = os.path.join(LIBRARY, rel)
        if os.path.isdir(full):
            inside = [p for p in by_path if p.startswith(rel + "/") and not os.path.isdir(os.path.join(LIBRARY, p))]
            if not inside:
                print(f"nothing in {rel}")
            picked += inside
        elif rel in by_path:
            picked.append(rel)
        else:
            sys.exit(f"not in the library: {rel}")
    return picked


def pull(args, dry, scripts):
    print("reading the library ...")
    lib = index(LIBRARY)
    by_path = {p: g for g, p in lib.items()}
    print("reading LoomRoom ...")
    have = index(PROJECT)

    todo = starting(args, by_path)
    seen, order, outside, left_scripts = set(), [], set(), []
    while todo:
        rel = todo.pop()
        guid = by_path[rel]
        if guid in seen:
            continue
        seen.add(guid)
        ext = os.path.splitext(rel)[1].lower()
        if ext in SCRIPTS and not scripts:
            left_scripts.append(rel)
            continue
        order.append(rel)
        for g in refs(LIBRARY, rel):
            if g in lib:
                todo.append(lib[g])
            else:
                outside.add(g)

    copy, there, conflicts, size = [], [], [], 0
    for rel in sorted(order):
        guid = by_path[rel]
        if guid in have:
            there.append((rel, have[guid]))
            continue
        target = os.path.join(PROJECT, rel)
        if os.path.exists(target) or os.path.exists(target + ".meta"):
            conflicts.append(rel)
            continue
        copy.append(rel)
        size += os.path.getsize(os.path.join(LIBRARY, rel))

    for rel in copy:
        print(("  would copy " if dry else "  copy ") + rel)
    for rel, at in there:
        if rel != at:
            print(f"  already in LoomRoom at another path: {rel} -> {at}")
    for rel in conflicts:
        print(f"  CONFLICT, left alone: {rel} exists in LoomRoom with another GUID")
    for rel in sorted(left_scripts):
        print(f"  script left out (use --scripts to bring it): {rel}")

    if not dry:
        folders = set()
        for rel in copy:
            parent = os.path.dirname(rel)
            while parent and parent != "Assets":
                folders.add(parent)
                parent = os.path.dirname(parent)
        for folder in sorted(folders):
            dst = os.path.join(PROJECT, folder)
            if not os.path.isdir(dst):
                os.makedirs(dst)
                meta = os.path.join(LIBRARY, folder + ".meta")
                if os.path.isfile(meta) and not os.path.exists(dst + ".meta"):
                    shutil.copy2(meta, dst + ".meta")
        for rel in copy:
            src, dst = os.path.join(LIBRARY, rel), os.path.join(PROJECT, rel)
            shutil.copy2(src, dst)
            shutil.copy2(src + ".meta", dst + ".meta")

    verb = "would copy" if dry else "copied"
    print(f"{verb} {len(copy)} files ({size / 1048576:.1f} MB), {len(there)} already in LoomRoom, "
          f"{len(conflicts)} conflicts, {len(left_scripts)} scripts left out, "
          f"{len(outside)} references to packages or built-in assets")


def unpack(pkg):
    """A .unitypackage is a gzipped tar of <guid>/{asset, asset.meta, pathname}. Only those three are read, as plain
    bytes (nothing is extracted by tar itself), and only paths inside Assets/ are written."""
    staging = os.path.join(LIBRARY, "Temp", "asset_unpack")
    shutil.rmtree(staging, ignore_errors=True)
    with tarfile.open(pkg, "r|gz") as tar:
        for member in tar:
            if not member.isfile():
                continue
            parts = member.name.replace("\\", "/").lstrip("./").split("/")
            if len(parts) != 2 or not re.fullmatch(r"[0-9a-f]{32}", parts[0]) or \
                    parts[1] not in ("asset", "asset.meta", "pathname"):
                continue
            os.makedirs(os.path.join(staging, parts[0]), exist_ok=True)
            with tar.extractfile(member) as src, open(os.path.join(staging, *parts), "wb") as dst:
                shutil.copyfileobj(src, dst)

    lib = index(LIBRARY)
    files = folders = replaced = moved = 0
    outside = []
    for guid in sorted(os.listdir(staging)):
        part = os.path.join(staging, guid)
        try:
            with open(os.path.join(part, "pathname"), encoding="utf-8") as f:
                rel = f.readline().strip().replace("\\", "/")
        except OSError:
            continue
        if not rel.startswith("Assets/") or ".." in rel.split("/") or ":" in rel:
            outside.append(rel)
            continue
        if guid in lib and lib[guid] != rel:
            rel = lib[guid]
            moved += 1
        target = os.path.join(LIBRARY, rel)
        if os.path.isfile(os.path.join(part, "asset")):
            os.makedirs(os.path.dirname(target), exist_ok=True)
            replaced += os.path.exists(target)
            shutil.copyfile(os.path.join(part, "asset"), target)
            files += 1
        else:
            os.makedirs(target, exist_ok=True)
            folders += 1
        if os.path.isfile(os.path.join(part, "asset.meta")):
            shutil.copyfile(os.path.join(part, "asset.meta"), target + ".meta")
    shutil.rmtree(staging, ignore_errors=True)
    for rel in outside:
        print(f"  left out (not under Assets): {rel}")
    print(f"{os.path.basename(pkg)}: {files} files ({replaced} replaced), {folders} folders, "
          f"{moved} kept where the library already had them")


def find(words):
    words = [w.lower() for w in words]
    hits = []
    assets = os.path.join(LIBRARY, "Assets")
    for folder, dirs, files in os.walk(assets):
        for name in files:
            if name.endswith(".meta"):
                continue
            rel = os.path.relpath(os.path.join(folder, name), LIBRARY).replace("\\", "/")
            if all(w in rel.lower() for w in words):
                hits.append(rel)
    hits.sort(key=lambda p: (not p.endswith(".prefab"), p))
    for rel in hits[:200]:
        print(rel)
    if len(hits) > 200:
        print(f"... and {len(hits) - 200} more")


def main(argv):
    global LIBRARY, PROJECT
    args, dry, scripts = [], False, False
    it = iter(argv)
    for a in it:
        if a == "--dry-run":
            dry = True
        elif a == "--scripts":
            scripts = True
        elif a == "--library":
            LIBRARY = next(it)
        elif a == "--project":
            PROJECT = next(it)
        else:
            args.append(a)
    if len(args) < 2 or args[0] not in ("find", "pull", "deps", "unpack"):
        sys.exit(__doc__)
    if not os.path.isdir(os.path.join(LIBRARY, "Assets")):
        sys.exit(f"no library project at {LIBRARY}")
    if args[0] == "find":
        find(args[1:])
    elif args[0] == "unpack":
        for pkg in args[1:]:
            unpack(pkg)
    else:
        pull(args[1:], dry or args[0] == "deps", scripts)


if __name__ == "__main__":
    main(sys.argv[1:])
