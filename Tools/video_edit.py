"""Cut vertical (1080x1920) TikTok/Shorts videos from recorded footage with ffmpeg.

  python Tools/video_edit.py <edit.json> [--preview]

The edit file lists shots and a soundtrack:

{
  "out": "Assets/Videos/Edits/my video.mp4",
  "music": {"file": "Assets/Game/Audio/RPG_04_Cathedral.mp3", "start": 12.0, "volume": 0.9, "fade_out": 1.5},
  "game_audio": 0.6,                       # default volume of the footage's own sound
  "shots": [
    {"src": "Assets/Videos/Morning take.mp4", "in": 2.0, "dur": 2.5,
     "frame": "crop",                      # crop (9:16 cut from 16:9), fit (whole frame over a blurred copy), portrait (already 9:16)
     "x": 0.0,                             # crop only: -1 left .. 1 right
     "zoom": [1.0, 1.08],                  # start and end zoom (slow push-in)
     "speed": 1.0,                         # <1 slow motion
     "grade": {"brightness": 0.03, "contrast": 1.1, "saturation": 1.1, "gamma": 1.0},
     "shade": 0.0,                         # darken the bottom (hides in-game captions under TikTok's own text)
     "audio": 0.6,                         # this shot's game audio volume (overrides game_audio)
     "flash": false,                       # white flash on the first frames (impact cut)
     "text": [{"t": "Every morning", "at": 0.2, "to": 2.4, "style": "caption"}]},
    {"card": true, "dur": 3.0, "title": "LOOMROOM", "sub": "Follow for more", "bg": "Assets/Videos/x.mp4", "bg_in": 4.0}
  ]
}

Text styles: caption (big white, upper third), quote (the Dungeon Master's cyan, in quotes), title, small.
Everything stays inside TikTok's safe area (clear of the top bar, the right-hand buttons and the bottom caption).
Output: H.264 High, 30 fps, yuv420p, AAC 192 kbps, about -14 LUFS, faststart.
"""
import json
import os
import shutil
import subprocess
import sys
import tempfile

W, H, FPS = 1080, 1920, 30
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
FONTS = os.path.join(ROOT, "Assets", "Game", "UI", "Fonts")
STYLES = {
    # font, size, colour, y (centre line), border
    "caption": ("PixelOperator-Bold.ttf", 96, "white", 560, 7),
    "quote": ("PixelOperator-Bold.ttf", 84, "0x8FE6EE", 560, 7),
    "title": ("PIXEARG_.TTF", 120, "white", 820, 8),
    "sub": ("PixelOperator-Bold.ttf", 72, "0xE0D6B5", 1010, 6),
    "small": ("PixelOperator-Bold.ttf", 52, "white", 1240, 5),
    "low": ("PixelOperator-Bold.ttf", 90, "white", 1280, 7),
}


def run(cmd, cwd=None):
    r = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True)
    if r.returncode != 0:
        sys.stderr.write(" ".join(cmd) + "\n" + r.stderr[-3000:])
        raise SystemExit(1)
    return r


def probe(path):
    r = run(["ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height:format=duration", "-of", "json", path])
    d = json.loads(r.stdout)
    s = d["streams"][0]
    return s["width"], s["height"], float(d["format"]["duration"])


def has_audio(path):
    r = run(["ffprobe", "-v", "error", "-select_streams", "a", "-show_entries", "stream=index", "-of", "csv=p=0", path])
    return bool(r.stdout.strip())


def wrap(text, style):
    # Pixel fonts are wide: wrap long lines so they stay inside the safe width (about 900 px).
    size = STYLES[style][1]
    per_line = max(8, int(940 / (size * 0.47)))
    words, lines, line = text.split(), [], ""
    for w in words:
        if len(line) + len(w) + (1 if line else 0) > per_line and line:
            lines.append(line)
            line = w
        else:
            line = (line + " " + w).strip()
    if line:
        lines.append(line)
    return "\n".join(lines)


def text_filters(items, work, index):
    out = []
    for n, item in enumerate(items or []):
        style = item.get("style", "caption")
        font, size, colour, y, border = STYLES[style]
        size = item.get("size", size)
        y = item.get("y", y)
        t = item["t"]
        if style == "quote" and not t.startswith("\""):
            t = f"“{t}”"
        t = wrap(t, style) if item.get("wrap", True) else t
        path = os.path.join(work, f"text_{index}_{n}.txt")
        # Unix line endings: drawtext draws a Windows "\r" as an extra blank line.
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(t)
        a, b = item.get("at", 0.0), item.get("to", 99.0)
        fade = float(item.get("fade", .12))
        # Text from the first frame is there at once; a fade-in would leave the opening frame empty.
        fade_in = fade if float(a) > 0 else 0.0
        alpha = (f"if(lt(t,{a}),0,if(lt(t,{a + fade_in}),(t-{a})/{max(fade_in, 1e-3)},if(lt(t,{b - fade}),1,if(lt(t,{b}),({b}-t)/{fade},0))))"
                 if fade > 0 else f"between(t,{a},{b})")
        lines = t.count("\n") + 1
        top = y - lines * size * 0.62
        out.append(
            f"drawtext=fontfile='{font}':textfile='{os.path.basename(path)}':fontsize={size}:fontcolor={colour}:"
            f"borderw={border}:bordercolor=black@0.95:line_spacing={int(size * 0.12)}:x=(w-text_w)/2:y={top:.0f}:"
            f"alpha='{alpha}':text_align=center")
    return out


def grade_filter(g):
    if not g:
        return None
    return (f"eq=brightness={g.get('brightness', 0)}:contrast={g.get('contrast', 1)}:"
            f"saturation={g.get('saturation', 1)}:gamma={g.get('gamma', 1)}")


def render_shot(shot, i, work, default_audio):
    out = os.path.join(work, f"shot_{i:03d}.mp4")
    dur = float(shot["dur"])
    if shot.get("card"):
        return render_card(shot, i, work, out)
    src = os.path.join(ROOT, shot["src"]) if not os.path.isabs(shot["src"]) else shot["src"]
    sw, sh, _ = probe(src)
    speed = float(shot.get("speed", 1.0))
    span = dur * speed
    z0, z1 = shot.get("zoom", [1.0, 1.0])
    frame = shot.get("frame", "portrait" if sh > sw else "crop")
    # Zoom over the shot's duration, on frame count (30 fps output).
    frames = max(1, int(round(dur * FPS)))
    zexpr = f"({z0}+({z1}-{z0})*min(on/{frames},1))"
    v = [f"setpts=(PTS-STARTPTS)/{speed}", f"fps={FPS}"]
    if frame == "crop":
        cw = int(round(sh * 9 / 16)) // 2 * 2
        x = (sw - cw) / 2 * (1 + float(shot.get("x", 0)))
        v += [f"crop={cw}:{sh}:{int(x)}:0", f"scale={W * 2}:{H * 2}:flags=lanczos",
              f"zoompan=z='{zexpr}':x='iw/2-(iw/zoom/2)':y='(ih-ih/zoom)/2*(1+{float(shot.get("y", 0))})':d=1:s={W}x{H}:fps={FPS}"]
    elif frame == "portrait":
        v += [f"scale={W * 2}:{H * 2}:flags=lanczos",
              f"zoompan=z='{zexpr}':x='iw/2-(iw/zoom/2)':y='(ih-ih/zoom)/2*(1+{float(shot.get("y", 0))})':d=1:s={W}x{H}:fps={FPS}"]
    g = grade_filter(shot.get("grade"))
    if g:
        v.append(g)
    vf = ",".join(v)
    if frame == "fit":
        # fit: the whole frame (or "region": [x0, y0, x1, y1] as fractions) across the width, over a blurred copy.
        x0, y0, x1, y1 = shot.get("region", [0, 0, 1, 1])
        rw, rh = sw * (x1 - x0), sh * (y1 - y0)
        fh = int(round(W * rh / rw)) // 2 * 2
        g2 = ("," + g) if g else ""
        region = f"crop={int(rw)}:{int(rh)}:{int(sw * x0)}:{int(sh * y0)}," if shot.get("region") else ""
        vf = (f"[0:v]setpts=(PTS-STARTPTS)/{speed},fps={FPS}{g2},split=2[a][b];"
              f"[a]{region}scale={W}:{H}:force_original_aspect_ratio=increase,crop={W}:{H},boxblur=40:2,eq=brightness={shot.get('bg_darken', -0.25)}:saturation=1.1[bg];"
              f"[b]{region}scale={W * 2}:{fh * 2}:flags=lanczos,zoompan=z='{zexpr}':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':d=1:s={W}x{fh}:fps={FPS}[fg];"
              f"[bg][fg]overlay=0:(H-h)/2+{int(shot.get('fit_y', 0))}")
    post = []
    shade = float(shot.get("shade", 0))
    if shade > 0:
        top = float(shot.get("shade_from", 0.78))
        post.append(f"drawbox=x=0:y={int(H * top)}:w={W}:h={int(H * (1 - top)) + 2}:color=black@{shade}:t=fill")
    if shot.get("fadein"):
        post.append(f"fade=t=in:st=0:d={shot['fadein']}")
    if shot.get("fadeout"):
        post.append(f"fade=t=out:st={dur - float(shot['fadeout']):.3f}:d={shot['fadeout']}")
    if shot.get("flash"):
        post.append("fade=t=in:st=0:d=0.18:color=white")
    post += text_filters(shot.get("text"), work, i)
    if frame == "fit":
        graph = vf + ("," + ",".join(post) if post else "") + "[v]"
    else:
        graph = "[0:v]" + vf + ("," + ",".join(post) if post else "") + "[v]"
    vol = float(shot.get("audio", default_audio))
    cmd = ["ffmpeg", "-v", "error", "-y", "-ss", str(shot["in"]), "-t", f"{span + 0.2:.3f}", "-i", src]
    if has_audio(src) and vol > 0:
        tempo = []
        s = speed
        while s < 0.5:
            tempo.append("atempo=0.5")
            s /= 0.5
        tempo.append(f"atempo={s}")
        graph += f";[0:a]asetpts=PTS-STARTPTS,{','.join(tempo)},volume={vol},aresample=48000,aformat=channel_layouts=stereo[a]"
        amap = ["-map", "[a]"]
    else:
        cmd += ["-f", "lavfi", "-t", f"{dur}", "-i", "anullsrc=r=48000:cl=stereo"]
        amap = ["-map", "1:a"]
    cmd += ["-filter_complex", graph, "-map", "[v]"] + amap + [
        "-t", f"{dur:.3f}", "-c:v", "libx264", "-preset", "medium", "-crf", "16", "-pix_fmt", "yuv420p",
        "-c:a", "aac", "-b:a", "192k", "-ar", "48000", out]
    run(cmd, cwd=work)
    return out


def render_card(shot, i, work, out):
    dur = float(shot["dur"])
    texts = []
    if shot.get("title"):
        texts.append({"t": shot["title"], "style": "title", "at": shot.get("title_at", 0.1), "wrap": False})
    if shot.get("sub"):
        texts.append({"t": shot["sub"], "style": "sub", "at": shot.get("sub_at", 0.5)})
    texts += shot.get("text", [])
    post = ",".join(text_filters(texts, work, i))
    if shot.get("bg"):
        src = os.path.join(ROOT, shot["bg"])
        graph = (f"[0:v]fps={FPS},scale={W}:{H}:force_original_aspect_ratio=increase,crop={W}:{H},"
                 f"boxblur={shot.get('blur', 30)}:2,eq=brightness={shot.get('darken', -0.25)}:saturation=0.8,{post}[v]")
        cmd = ["ffmpeg", "-v", "error", "-y", "-ss", str(shot.get("bg_in", 0)), "-t", f"{dur}", "-i", src]
    else:
        graph = f"[0:v]{post}[v]"
        cmd = ["ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-t", f"{dur}", "-i", f"color=c=0x0B0D0D:s={W}x{H}:r={FPS}"]
    cmd += ["-f", "lavfi", "-t", f"{dur}", "-i", "anullsrc=r=48000:cl=stereo",
            "-filter_complex", graph, "-map", "[v]", "-map", "1:a", "-t", f"{dur}",
            "-c:v", "libx264", "-preset", "medium", "-crf", "16", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k", out]
    run(cmd, cwd=work)
    return out


def main():
    spec_path = sys.argv[1]
    spec = json.load(open(spec_path, encoding="utf-8"))
    out = os.path.join(ROOT, spec["out"])
    os.makedirs(os.path.dirname(out), exist_ok=True)
    work = tempfile.mkdtemp(prefix="vedit_")
    for f in os.listdir(FONTS):
        if f.lower().endswith((".ttf", ".otf")):
            shutil.copy(os.path.join(FONTS, f), work)
    default_audio = float(spec.get("game_audio", 0.6))
    parts = []
    for i, shot in enumerate(spec["shots"]):
        parts.append(render_shot(shot, i, work, default_audio))
        print(f"shot {i + 1}/{len(spec['shots'])}", flush=True)
    with open(os.path.join(work, "list.txt"), "w") as f:
        for p in parts:
            f.write(f"file '{os.path.basename(p)}'\n")
    joined = os.path.join(work, "joined.mp4")
    run(["ffmpeg", "-v", "error", "-y", "-f", "concat", "-safe", "0", "-i", "list.txt", "-c", "copy", joined], cwd=work)
    total = sum(float(s["dur"]) for s in spec["shots"])
    music = spec.get("music")
    if music:
        mfile = os.path.join(ROOT, music["file"])
        fo = float(music.get("fade_out", 1.5))
        fi = float(music.get("fade_in", 0.0))
        mv = float(music.get("volume", 0.9))
        afilter = (f"[1:a]atrim=start={music.get('start', 0)},asetpts=PTS-STARTPTS,atrim=0:{total},"
                   f"afade=t=in:st=0:d={max(fi, 0.01)},afade=t=out:st={total - fo}:d={fo},volume={mv}[m];"
                   f"[0:a][m]amix=inputs=2:normalize=0:duration=first,loudnorm=I=-14:TP=-1.5:LRA=11,alimiter=limit=0.79:level=false[a]")
        cmd = ["ffmpeg", "-v", "error", "-y", "-i", joined, "-i", mfile]
    else:
        afilter = "[0:a]loudnorm=I=-14:TP=-1.5:LRA=11,alimiter=limit=0.79:level=false[a]"
        cmd = ["ffmpeg", "-v", "error", "-y", "-i", joined]
    cmd += ["-filter_complex", afilter, "-map", "0:v", "-map", "[a]", "-c:v", "libx264", "-profile:v", "high",
            "-preset", "slow", "-crf", "18", "-maxrate", "12M", "-bufsize", "24M", "-pix_fmt", "yuv420p", "-r", str(FPS),
            "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-movflags", "+faststart", "-t", f"{total:.3f}", out]
    run(cmd)
    shutil.rmtree(work, ignore_errors=True)
    print(f"done {out} ({total:.1f}s)")


if __name__ == "__main__":
    main()
