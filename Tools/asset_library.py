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
                                                       would: same paths and GUIDs, newer files replace older ones,
                                                       one that lands on another asset's path goes beside it as
                                                       "<name> 1"); Unity imports it the next time the library opens
    python Tools/asset_library.py missing [<folder|file> ...] [--pull]
                                                       references in LoomRoom (all of Assets and ProjectSettings, or
                                                       the folders given) to assets it doesn't have: the ones the
                                                       library has, which --pull brings in, and the ones nowhere

An asset is a path under the library's Assets ("Synty/PolygonDungeon/Prefabs/SM_Prop_Barrel_01.prefab", with or
without "Assets/"), or a folder (everything in it). Dependencies are found from the GUIDs written in each asset and
its .meta (a model's remapped materials), and in binary assets (terrain data, NavMesh, lighting data) from the GUIDs
they keep as bytes, followed to the end. Each project's .meta GUIDs and LoomRoom's references are cached in its
Library folder (asset_library_*.json), so only changed files are read again. Nothing in LoomRoom is ever
overwritten: an asset whose GUID is already in the project is skipped, and a file already at the target path with
another GUID is reported as a conflict and left alone. Scripts (.cs, .dll) are left out unless --scripts is given
(they compile into the game's one assembly), and listed so their components can be removed. GUIDs not in the library (packages, Unity's
built-in assets) are left as they are. Unity picks the files up on its next refresh.

Options: --library <path> (default C:/Game projects/AssetLibrary, or ASSET_LIBRARY), --project <path> (this repo)."""
import json, os, re, shutil, struct, sys, tarfile

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.normpath(os.path.join(HERE, ".."))
LIBRARY = os.environ.get("ASSET_LIBRARY", r"C:\Game projects\AssetLibrary")

GUID_LINE = re.compile(rb"^guid:\s*([0-9a-f]{32})", re.M)
# YAML references ("guid: x, type: n"; a bare "guid: x" is an id of the asset's own, an audio mixer's parameters), and
# JSON in shader graphs, escaped or not ("guid":"x", \"guid\":\"x\")
GUID_REF = re.compile(rb'guid:\s*([0-9a-f]{32}),\s*type:|guid\\?"\s*:\s*\\?"([0-9a-f]{32})')
# ProjectSettings name scenes by a bare "guid: x" (EditorBuildSettings)
GUID_LOOSE = re.compile(rb'guid:\s*([0-9a-f]{32})()')
REFS_VERSION = 2   # bumped when what counts as a reference changes, so cached references are read again
BUILT_IN = {"0000000000000000e000000000000000", "0000000000000000f000000000000000", "0" * 32}
SCRIPTS = {".cs", ".dll", ".asmdef", ".asmref"}
NO_REFS = {".fbx", ".obj", ".blend", ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".tiff", ".exr", ".hdr",
           ".wav", ".mp3", ".ogg", ".aif", ".ttf", ".otf", ".glb", ".blend1", ".dll", ".pdb", ".mdb", ".pdf", ".zip",
           ".unitypackage", ".mp4", ".mov", ".bytes", ".data"}


def index(root, sub="Assets"):
    """guid -> path (relative to the project, "Assets/..."), read from every .meta under root/sub. Reading every
    .meta takes minutes on a large library, so what was read is kept in root/Library/asset_library_index*.json and a
    .meta is read again only when its time or size changed."""
    name = "asset_library_index" + ("" if sub == "Assets" else "_" + re.sub(r"\W+", "_", sub)) + ".json"
    cache_path = os.path.join(root, "Library", name)
    try:
        with open(cache_path, encoding="utf-8") as f:
            cache = json.load(f)
    except (OSError, ValueError):
        cache = {}
    fresh, found = {}, {}

    def walk(folder):
        try:
            entries = list(os.scandir(folder))
        except OSError:
            return
        for e in entries:
            if e.is_dir(follow_symlinks=False):
                walk(e.path)
            elif e.name.endswith(".meta"):
                rel = os.path.relpath(e.path[:-5], root).replace("\\", "/")
                st = e.stat()
                seen = cache.get(rel)
                if seen and seen[0] == st.st_mtime_ns and seen[1] == st.st_size:
                    guid = seen[2]
                else:
                    try:
                        with open(e.path, "rb") as f:
                            m = GUID_LINE.search(f.read(400))
                    except OSError:
                        continue
                    guid = m.group(1).decode() if m else ""
                fresh[rel] = [st.st_mtime_ns, st.st_size, guid]
                if guid:
                    found[guid] = rel

    walk(os.path.join(root, sub))
    if fresh != cache and os.path.isdir(os.path.join(root, "Library")):
        try:
            with open(cache_path + ".tmp", "w", encoding="utf-8") as f:
                json.dump(fresh, f, separators=(",", ":"))
            os.replace(cache_path + ".tmp", cache_path)
        except OSError:
            pass
    return found


def binary_guids(guids):
    """Binary assets (terrain data, NavMesh, lighting data) keep the GUIDs they point at as 16 bytes: each group of 8
    hex digits read backwards as a little-endian number. Maps those bytes to the GUIDs given."""
    return {b"".join(struct.pack("<I", int(g[i:i + 8][::-1], 16)) for i in range(0, 32, 8)): g for g in guids}


def refs(root, rel, known=None):
    """GUIDs an asset points at: in its own text, or for a binary asset the GUIDs of `known` (binary_guids) found in
    it, and in its .meta."""
    out = set()
    path = os.path.join(root, rel)
    for p, own in ((path, False), (path + ".meta", True)):
        if not os.path.isfile(p) or (not own and os.path.splitext(p)[1].lower() in NO_REFS):
            continue
        with open(p, "rb") as f:
            data = f.read()
        if not own and b"\0" in data[:8000]:
            if known is None:
                print(f"  note: {rel} is binary, its references can't be read")
            else:
                out.update(known[data[o:o + 16]] for o in range(len(data) - 15) if data[o:o + 16] in known)
            continue
        pattern = GUID_LOOSE if rel.startswith("ProjectSettings/") else GUID_REF
        for m in pattern.finditer(data):
            out.add((m.group(1) or m.group(2)).decode())
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
    known = binary_guids(lib)
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
        for g in refs(LIBRARY, rel, known):
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


def missing(args, do_pull, scripts):
    """References in LoomRoom to assets it doesn't have: the ones the library has (pulled with --pull) and the ones
    nowhere (broken). What each file points at is kept in Library/asset_library_refs.json, read again only when the
    file changes."""
    print("reading the library ...")
    lib = index(LIBRARY)
    print("reading LoomRoom ...")
    have = index(PROJECT)
    for sub in ("Packages", "Library/PackageCache"):
        if os.path.isdir(os.path.join(PROJECT, sub)):
            have.update(index(PROJECT, sub))
    known = binary_guids(set(lib) | set(have))

    scopes = [asset_path(a) for a in args] or ["Assets", "ProjectSettings"]
    files = []
    for scope in scopes:
        full = os.path.join(PROJECT, scope)
        if os.path.isfile(full):
            files.append(scope)
            continue
        if not os.path.isdir(full):
            sys.exit(f"not in LoomRoom: {scope}")
        for folder, dirs, names in os.walk(full):
            files += [os.path.relpath(os.path.join(folder, n), PROJECT).replace("\\", "/")
                      for n in names if not n.endswith(".meta")]

    cache_path = os.path.join(PROJECT, "Library", "asset_library_refs.json")
    try:
        with open(cache_path, encoding="utf-8") as f:
            cache = json.load(f)
        if cache.get("version") != REFS_VERSION:
            cache = {}
    except (OSError, ValueError):
        cache = {}
    cache["version"] = REFS_VERSION
    used_by = {}
    print(f"reading {len(set(files))} files ...")
    for rel in sorted(set(files)):
        full = os.path.join(PROJECT, rel)
        stamp = []
        for p in (full, full + ".meta"):
            try:
                st = os.stat(p)
                stamp += [st.st_mtime_ns, st.st_size]
            except OSError:
                stamp += [0, 0]
        seen = cache.get(rel)
        if seen and seen[0] == stamp:
            found = seen[1]
        else:
            found = sorted(refs(PROJECT, rel, known))
        cache[rel] = [stamp, found]
        for g in found:
            if g not in have:
                used_by.setdefault(g, []).append(rel)
    if os.path.isdir(os.path.join(PROJECT, "Library")):
        try:
            with open(cache_path + ".tmp", "w", encoding="utf-8") as f:
                json.dump(cache, f, separators=(",", ":"))
            os.replace(cache_path + ".tmp", cache_path)
        except OSError:
            pass

    pullable = sorted((lib[g], users) for g, users in used_by.items() if g in lib)
    broken = sorted((users[0], g, users) for g, users in used_by.items() if g not in lib)
    for rel, users in pullable:
        print(f"  in the library: {rel}  <- {users[0]}" + (f" and {len(users) - 1} more" if len(users) > 1 else ""))
    for first, g, users in broken:
        print(f"  nowhere: {g}  <- {first}" + (f" and {len(users) - 1} more" if len(users) > 1 else ""))
    print(f"{len(pullable)} missing assets the library has, {len(broken)} references to assets it hasn't either")
    if do_pull and pullable:
        pull([rel for rel, users in pullable], False, scripts)


def unpack(pkg):
    """A .unitypackage is a gzipped tar of <guid>/{asset, asset.meta, pathname}. Only those three are read, as plain
    bytes (nothing is extracted by tar itself), and only paths inside Assets/ are written."""
    staging = os.path.join(LIBRARY, "Temp", "asset_unpack")
    shutil.rmtree(staging, ignore_errors=True)
    try:
        unpack_from(pkg, staging)
    finally:
        shutil.rmtree(staging, ignore_errors=True)


def unpack_from(pkg, staging):
    with tarfile.open(pkg, "r|gz") as tar:
        for member in tar:
            if not member.isfile():
                continue
            parts = member.name.replace("\\", "/").lstrip("./").split("/")
            if (len(parts) != 2 or not re.fullmatch(r"[0-9a-f]{32}", parts[0])
                    or parts[1] not in ("asset", "asset.meta", "pathname")):
                continue
            os.makedirs(os.path.join(staging, parts[0]), exist_ok=True)
            with tar.extractfile(member) as src, open(os.path.join(staging, *parts), "wb") as dst:
                shutil.copyfileobj(src, dst)

    lib = index(LIBRARY)
    by_path = {p.lower(): g for g, p in lib.items()}
    files = folders = replaced = moved = 0
    outside, renamed = [], []
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
        is_file = os.path.isfile(os.path.join(part, "asset"))
        other = by_path.get(rel.lower())
        if other and other != guid:
            if not is_file:
                folders += 1          # a folder of the same name: its contents go in it, it keeps its own GUID
                continue
            # another asset holds the path: as Unity's import does, this one goes beside it as "<name> 1"
            stem, ext = os.path.splitext(rel)
            n = 1
            while f"{stem} {n}{ext}".lower() in by_path or os.path.exists(os.path.join(LIBRARY, f"{stem} {n}{ext}")):
                n += 1
            renamed.append((rel, f"{stem} {n}{ext}"))
            rel = f"{stem} {n}{ext}"
        by_path[rel.lower()] = guid
        target = os.path.join(LIBRARY, rel)
        if is_file:
            os.makedirs(os.path.dirname(target), exist_ok=True)
            replaced += os.path.exists(target)
            shutil.copyfile(os.path.join(part, "asset"), target)
            files += 1
        else:
            os.makedirs(target, exist_ok=True)
            folders += 1
        if os.path.isfile(os.path.join(part, "asset.meta")):
            shutil.copyfile(os.path.join(part, "asset.meta"), target + ".meta")
    for rel in outside:
        print(f"  left out (not under Assets): {rel}")
    for rel, to in renamed:
        print(f"  another asset is at {rel}: this one went to {to}")
    print(f"{os.path.basename(pkg)}: {files} files ({replaced} replaced, {len(renamed)} renamed), {folders} folders, "
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
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    args, dry, scripts, do_pull = [], False, False, False
    it = iter(argv)
    for a in it:
        if a == "--dry-run":
            dry = True
        elif a == "--pull":
            do_pull = True
        elif a == "--scripts":
            scripts = True
        elif a == "--library":
            LIBRARY = next(it)
        elif a == "--project":
            PROJECT = next(it)
        else:
            args.append(a)
    if not args or args[0] not in ("find", "pull", "deps", "unpack", "missing") or \
            (len(args) < 2 and args[0] != "missing"):
        sys.exit(__doc__)
    if not os.path.isdir(os.path.join(LIBRARY, "Assets")):
        sys.exit(f"no library project at {LIBRARY}")
    if args[0] == "find":
        find(args[1:])
    elif args[0] == "missing":
        missing(args[1:], do_pull, scripts)
    elif args[0] == "unpack":
        for pkg in args[1:]:
            unpack(pkg)
    else:
        pull(args[1:], dry or args[0] == "deps", scripts)


if __name__ == "__main__":
    main(sys.argv[1:])
