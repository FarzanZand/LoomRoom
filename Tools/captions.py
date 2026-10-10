"""Speech to captions with Whisper on the GPU, for TikTok edits.

  python Tools/captions.py <video or audio> [--out file.srt] [--model large-v3-turbo] [--words N]

Writes an .srt next to the input (or --out). --words splits captions into short chunks of N words,
which suits vertical video (default 4; 0 keeps Whisper's full sentences). Burn in with ffmpeg:

  ffmpeg -i clip.mp4 -vf "subtitles=clip.srt:force_style='FontName=Arial,FontSize=18,Alignment=2,MarginV=90'" out.mp4

Needs: pip install faster-whisper nvidia-cublas-cu12 "nvidia-cudnn-cu12==9.*" "av>=14,<16".
The first run downloads the model (about 1.6 GB) into the Hugging Face cache.
"""
import argparse
import glob
import os
import site
import sys


def add_cuda_dlls():
    # The pip CUDA wheels keep their DLLs under site-packages/nvidia/*/bin, which Windows doesn't search.
    for root in site.getsitepackages() + [site.getusersitepackages()]:
        for d in glob.glob(os.path.join(root, "nvidia", "*", "bin")):
            os.add_dll_directory(d)
            os.environ["PATH"] = d + os.pathsep + os.environ["PATH"]


def stamp(t):
    ms = int(round(t * 1000))
    h, ms = divmod(ms, 3600000)
    m, ms = divmod(ms, 60000)
    s, ms = divmod(ms, 1000)
    return f"{h:02}:{m:02}:{s:02},{ms:03}"


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("input")
    ap.add_argument("--out")
    ap.add_argument("--model", default="large-v3-turbo")
    ap.add_argument("--words", type=int, default=4)
    ap.add_argument("--language", default=None)
    args = ap.parse_args()

    add_cuda_dlls()
    from faster_whisper import WhisperModel

    model = WhisperModel(args.model, device="cuda", compute_type="float16")
    segments, info = model.transcribe(args.input, word_timestamps=True, language=args.language, vad_filter=True)

    cues = []
    for seg in segments:
        words = [w for w in (seg.words or []) if w.word.strip()]
        if args.words <= 0 or not words:
            cues.append((seg.start, seg.end, seg.text.strip()))
            continue
        for i in range(0, len(words), args.words):
            chunk = words[i:i + args.words]
            cues.append((chunk[0].start, chunk[-1].end, "".join(w.word for w in chunk).strip()))

    out = args.out or os.path.splitext(args.input)[0] + ".srt"
    with open(out, "w", encoding="utf-8") as f:
        for n, (a, b, text) in enumerate(cues, 1):
            f.write(f"{n}\n{stamp(a)} --> {stamp(b)}\n{text}\n\n")
    print(f"{len(cues)} captions ({info.language}) -> {out}")


if __name__ == "__main__":
    sys.exit(main())
