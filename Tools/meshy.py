"""Meshy bridge: generate, retexture, remesh and rig models from the command line.

Results land in Assets/Game/Meshy/<name>/ (model files, textures, thumbnail, meshy.json).

API key: MESHY_API_KEY env var, or the first line of ~/.meshy/api_key.

  python Tools/meshy.py balance
  python Tools/meshy.py text "a wooden treasure chest with iron bands" --name Chest
  python Tools/meshy.py image ref.png --name Goblin --pose t-pose
  python Tools/meshy.py retexture Assets/Game/Meshy/Chest/Chest.fbx "mossy, rotten wood" --name ChestMossy
  python Tools/meshy.py remesh <model or task id> --polycount 3000 --name ChestLow
  python Tools/meshy.py rig <model or task id> --height 1.8 --name GoblinRigged
  python Tools/meshy.py concept "goblin archer, front view" --name GoblinRef
  python Tools/meshy.py concept "a new character in this exact style" --ref style.png --name StyledRef
  python Tools/meshy.py fetch <kind> <task id> --name X     (download a finished or interrupted task)

Common options: --polycount N, --quad, --smart, --pbr, --tex-res 2k|4k|8k, --geo-res standard|2k|4k,
--model meshy-7.1|meshy-6|meshy-6-lite|latest, --formats fbx,glb, --out <folder>.
"""

import argparse
import base64
import json
import mimetypes
import os
import re
import sys
import time
from pathlib import Path

import requests

API = "https://api.meshy.ai/openapi"
ROOT = Path(__file__).resolve().parent.parent
DEFAULT_OUT = ROOT / "Assets" / "Game" / "Meshy"
DEFAULT_FORMATS = ["fbx", "glb"]

# kind -> (endpoint, label)
KINDS = {
    "text-preview": ("v2/text-to-3d", "text to 3D (shape)"),
    "text-refine": ("v2/text-to-3d", "text to 3D (texture)"),
    "image": ("v1/image-to-3d", "image to 3D"),
    "retexture": ("v1/retexture", "retexture"),
    "remesh": ("v1/remesh", "remesh"),
    "rig": ("v1/rigging", "rigging"),
    "concept": ("v1/text-to-image", "concept image"),
    "concept-ref": ("v1/image-to-image", "concept image from references"),
}


def api_key():
    key = os.environ.get("MESHY_API_KEY", "").strip()
    if not key:
        f = Path.home() / ".meshy" / "api_key"
        if f.exists():
            key = f.read_text(encoding="utf-8").strip().splitlines()[0].strip()
    if not key:
        sys.exit("No Meshy API key. Set MESHY_API_KEY or put it in ~/.meshy/api_key")
    return key


def session():
    s = requests.Session()
    s.headers["Authorization"] = f"Bearer {api_key()}"
    return s


def call(s, method, path, body=None):
    r = s.request(method, f"{API}/{path}", json=body, timeout=60)
    if r.status_code >= 400:
        msg = r.text
        try:
            msg = r.json().get("message", msg)
        except ValueError:
            pass
        hints = {401: "bad API key", 402: "not enough credits", 429: "rate limited, try again shortly"}
        sys.exit(f"Meshy {r.status_code}: {msg}" + (f" ({hints[r.status_code]})" if r.status_code in hints else ""))
    return r.json()


def data_uri(path, mime=None):
    p = Path(path)
    mime = mime or mimetypes.guess_type(p.name)[0] or "application/octet-stream"
    return f"data:{mime};base64,{base64.b64encode(p.read_bytes()).decode()}"


def model_input(src):
    """A local model file becomes a data URI, a URL stays a URL, anything else is a task id."""
    if Path(src).exists():
        return {"model_url": data_uri(src, "application/octet-stream")}
    if src.startswith(("http://", "https://", "data:")):
        return {"model_url": src}
    return {"input_task_id": src}


def image_input(src):
    return data_uri(src) if Path(src).exists() else src


def create(s, kind, body):
    endpoint, label = KINDS[kind]
    task_id = call(s, "POST", endpoint, body)["result"]
    print(f"{label}: task {task_id}")
    return task_id


def wait(s, kind, task_id):
    endpoint, label = KINDS[kind]
    last = None
    while True:
        t = call(s, "GET", f"{endpoint}/{task_id}")
        status, progress = t.get("status"), t.get("progress", 0)
        line = f"  {label}: {status} {progress}%"
        if status == "PENDING" and t.get("preceding_tasks"):
            line += f" ({t['preceding_tasks']} ahead in queue)"
        if line != last:
            print(line, flush=True)
            last = line
        if status == "SUCCEEDED":
            if t.get("consumed_credits") is not None:
                print(f"  used {t['consumed_credits']} credits")
            return t
        if status in ("FAILED", "CANCELED"):
            sys.exit(f"{label} {status.lower()}: {(t.get('task_error') or {}).get('message', 'no reason given')}")
        time.sleep(5)


def safe(name):
    return re.sub(r"[^A-Za-z0-9 _-]+", "", name).strip().replace(" ", "_") or "Model"


def download(url, path):
    with requests.get(url, stream=True, timeout=300) as r:
        r.raise_for_status()
        path.parent.mkdir(parents=True, exist_ok=True)
        with open(path, "wb") as f:
            for chunk in r.iter_content(1 << 16):
                f.write(chunk)
    print(f"  saved {path.relative_to(ROOT) if path.is_relative_to(ROOT) else path}")


def save(task, name, out, formats, extra=None):
    folder = Path(out) / name
    folder.mkdir(parents=True, exist_ok=True)
    urls = dict(task.get("model_urls") or {})
    result = task.get("result") or {}  # rigging puts its files here
    if result.get("rigged_character_fbx_url"):
        urls["fbx"] = result["rigged_character_fbx_url"]
    if result.get("rigged_character_glb_url"):
        urls["glb"] = result["rigged_character_glb_url"]
    for fmt in formats:
        if urls.get(fmt):
            download(urls[fmt], folder / f"{name}.{fmt}")
        else:
            print(f"  (no {fmt} in this result)")
    for anim, url in (result.get("basic_animations") or {}).items():
        if anim.endswith("_fbx_url") and url:
            download(url, folder / f"{name}_{anim[:-len('_fbx_url')]}.fbx")
    textures = task.get("texture_urls") or []
    if isinstance(textures, dict):
        textures = [textures]
    for i, tex in enumerate(textures):
        for channel, url in tex.items():
            if url:
                suffix = "" if i == 0 else f"_{i}"
                download(url, folder / f"{name}_{channel}{suffix}.png")
    for i, url in enumerate(task.get("image_urls") or []):
        download(url, folder / f"{name}{'' if i == 0 else f'_{i}'}.png")
    if task.get("thumbnail_url"):
        download(task["thumbnail_url"], folder / f"{name}_thumbnail.png")
    meta_path = folder / "meshy.json"
    meta = json.loads(meta_path.read_text(encoding="utf-8")) if meta_path.exists() else {"tasks": []}
    entry = {"id": task.get("id"), "type": task.get("type"), "credits": task.get("consumed_credits")}
    for k in ("prompt", "texture_prompt", "text_style_prompt"):
        if task.get(k):
            entry[k] = task[k]
    entry.update(extra or {})
    meta["tasks"].append(entry)
    meta_path.write_text(json.dumps(meta, indent=2), encoding="utf-8")
    print(f"Done: {folder.relative_to(ROOT) if folder.is_relative_to(ROOT) else folder}")
    print(f"Task id for follow-ups (retexture/remesh/rig): {task.get('id')}")


def mesh_opts(a, body):
    if a.polycount:
        body["should_remesh"] = True
        body["target_polycount"] = a.polycount
    if a.quad:
        body["should_remesh"] = True
        body["topology"] = "quad"
    if a.smart:
        body["model_type"] = "smart-topology"
        body.pop("should_remesh", None)
    if a.pose:
        body["pose_mode"] = a.pose
    if a.geo_res:
        body["geometry_resolution"] = a.geo_res


def tex_opts(a, body):
    if a.pbr:
        body["enable_pbr"] = True
    if a.tex_res:
        body["texture_resolution"] = a.tex_res


def cmd_text(s, a):
    body = {"mode": "preview", "prompt": a.prompt, "target_formats": a.formats}
    if a.model:
        body["ai_model"] = a.model
    mesh_opts(a, body)
    preview = wait(s, "text-preview", create(s, "text-preview", body))
    if a.no_texture:
        return save(preview, a.name, a.out, a.formats)
    body = {"mode": "refine", "preview_task_id": preview["id"], "target_formats": a.formats}
    if a.texture:
        body["texture_prompt"] = a.texture
    if a.style_image:
        body["texture_image_url"] = image_input(a.style_image)
    tex_opts(a, body)
    save(wait(s, "text-refine", create(s, "text-refine", body)), a.name, a.out, a.formats, {"preview_id": preview["id"]})


def cmd_image(s, a):
    body = {"image_url": image_input(a.image), "target_formats": a.formats}
    if a.model:
        body["ai_model"] = a.model
    if a.no_texture:
        body["should_texture"] = False
    if a.texture:
        body["texture_prompt"] = a.texture
    mesh_opts(a, body)
    tex_opts(a, body)
    save(wait(s, "image", create(s, "image", body)), a.name, a.out, a.formats)


def cmd_retexture(s, a):
    body = model_input(a.source)
    body["target_formats"] = a.formats
    if a.style_image:
        body["image_style_url"] = image_input(a.style_image)
    elif a.prompt:
        body["text_style_prompt"] = a.prompt
    else:
        sys.exit("retexture needs a prompt or --style-image")
    if a.keep_uv:
        body["enable_original_uv"] = True
    if a.model:
        body["ai_model"] = a.model
    tex_opts(a, body)
    save(wait(s, "retexture", create(s, "retexture", body)), a.name, a.out, a.formats)


def cmd_remesh(s, a):
    body = model_input(a.source)
    body["target_formats"] = a.formats
    body["topology"] = "quad" if a.quad else "triangle"
    body["target_polycount"] = a.polycount or 30000
    save(wait(s, "remesh", create(s, "remesh", body)), a.name, a.out, a.formats)


def cmd_rig(s, a):
    body = model_input(a.source)
    body["height_meters"] = a.height
    save(wait(s, "rig", create(s, "rig", body)), a.name, a.out, a.formats)


def cmd_concept(s, a):
    body = {"prompt": a.prompt, "ai_model": a.image_model}
    if a.ref:
        # Image to image: the references set the style (or the subject); the prompt says what to draw.
        body["reference_image_urls"] = [image_input(r) for r in a.ref]
        if a.multi_view:
            body["generate_multi_view"] = True
        save(wait(s, "concept-ref", create(s, "concept-ref", body)), a.name, a.out, [])
        return
    if a.multi_view:
        body["generate_multi_view"] = True
    elif a.aspect:
        body["aspect_ratio"] = a.aspect
    if a.pose:
        body["pose_mode"] = a.pose
    if a.transparent:
        body["remove_background"] = True
    save(wait(s, "concept", create(s, "concept", body)), a.name, a.out, [])


def cmd_fetch(s, a):
    save(wait(s, a.kind, a.task_id), a.name, a.out, a.formats)


def cmd_balance(s, a):
    print(f"Meshy credits: {call(s, 'GET', 'v1/balance')['balance']}")


def main():
    p = argparse.ArgumentParser(description="Meshy bridge for LoomRoom", formatter_class=argparse.RawDescriptionHelpFormatter, epilog=__doc__)
    sub = p.add_subparsers(dest="cmd", required=True)

    def common(sp, name_required=True):
        sp.add_argument("--name", required=name_required, help="folder and file name under the output folder")
        sp.add_argument("--out", default=str(DEFAULT_OUT), help="output folder (default Assets/Game/Meshy)")
        sp.add_argument("--formats", default=",".join(DEFAULT_FORMATS), type=lambda v: [f.strip() for f in v.split(",") if f.strip()])
        sp.add_argument("--model", help="meshy-7.1, meshy-6, meshy-6-lite or latest (default latest)")
        return sp

    def mesh(sp):
        sp.add_argument("--polycount", type=int, help="remesh to this many faces")
        sp.add_argument("--quad", action="store_true", help="quad topology")
        sp.add_argument("--smart", action="store_true", help="Smart Topology (clean low-poly, max 15k)")
        sp.add_argument("--pose", choices=["a-pose", "t-pose"], help="for characters you want to rig")
        sp.add_argument("--geo-res", choices=["standard", "2k", "4k"], help="geometry detail (meshy-7.1)")

    def tex(sp):
        sp.add_argument("--pbr", action="store_true", help="also metallic, roughness, normal maps")
        sp.add_argument("--tex-res", choices=["2k", "4k", "8k"])

    sp = common(sub.add_parser("text", help="text to 3D"))
    sp.add_argument("prompt")
    sp.add_argument("--texture", help="separate texture prompt")
    sp.add_argument("--style-image", help="texture from a reference image (file or URL)")
    sp.add_argument("--no-texture", action="store_true", help="stop after the untextured shape (cheaper)")
    mesh(sp); tex(sp)
    sp.set_defaults(fn=cmd_text)

    sp = common(sub.add_parser("image", help="image to 3D"))
    sp.add_argument("image", help="reference image file or URL (.png/.jpg)")
    sp.add_argument("--texture", help="texture prompt instead of the image's colours")
    sp.add_argument("--no-texture", action="store_true")
    mesh(sp); tex(sp)
    sp.set_defaults(fn=cmd_image)

    sp = common(sub.add_parser("retexture", help="repaint an existing model"))
    sp.add_argument("source", help="model file (.fbx/.glb/.obj/.stl), URL, or Meshy task id")
    sp.add_argument("prompt", nargs="?")
    sp.add_argument("--style-image", help="style reference image instead of a prompt")
    sp.add_argument("--keep-uv", action="store_true", help="keep the model's own UVs")
    tex(sp)
    sp.set_defaults(fn=cmd_retexture)

    sp = common(sub.add_parser("remesh", help="new topology / polycount"))
    sp.add_argument("source")
    sp.add_argument("--polycount", type=int)
    sp.add_argument("--quad", action="store_true")
    sp.set_defaults(fn=cmd_remesh)

    sp = common(sub.add_parser("rig", help="auto-rig a humanoid (adds walk/run clips)"))
    sp.add_argument("source")
    sp.add_argument("--height", type=float, default=1.7, help="height in metres")
    sp.set_defaults(fn=cmd_rig)

    sp = common(sub.add_parser("concept", help="text to image (reference art)"))
    sp.add_argument("prompt")
    sp.add_argument("--image-model", default="nano-banana-pro",
                    help="nano-banana, nano-banana-2, nano-banana-pro, gpt-image-2, gpt-image-2-5-flare, gpt-image-2-5-sunburst")
    sp.add_argument("--aspect", help="1:1, 16:9, 9:16, 4:3, 3:4")
    sp.add_argument("--multi-view", action="store_true")
    sp.add_argument("--pose", choices=["a-pose", "t-pose"])
    sp.add_argument("--transparent", action="store_true")
    sp.add_argument("--ref", action="append", help="reference image (file or URL) for image to image; repeat for up to 5")
    sp.set_defaults(fn=cmd_concept)

    sp = common(sub.add_parser("fetch", help="download a task by id"))
    sp.add_argument("kind", choices=list(KINDS))
    sp.add_argument("task_id")
    sp.set_defaults(fn=cmd_fetch)

    sub.add_parser("balance", help="credits left").set_defaults(fn=cmd_balance)

    a = p.parse_args()
    if getattr(a, "name", None):
        a.name = safe(a.name)
    a.fn(session(), a)


if __name__ == "__main__":
    main()
