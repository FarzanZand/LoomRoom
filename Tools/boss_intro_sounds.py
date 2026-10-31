"""Boss intro sounds, layered from the project's sound libraries with ffmpeg (safe to run again).

  Spotlight clunk(.wav, B): a theatre follow spot thrown on: a heavy latch pitched down, a metal clank and a low thump
  in the floor, then the lamp's mains hum swelling in with a thin filament sizzle, in a big hall's echo.
  Boss landing(.wav, B): a body set down on the stage: a heavy slam, feet on stone, an armour jangle, a falling sub boom
  and a little grit, in the same echo.

Writes to Assets/Game/Audio/Clips/Dialogue/. The fight asset picks one (BossFight > Intro > Sounds); the B takes are
there to swap in.
"""
import os
import subprocess

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
A = os.path.join(ROOT, "Assets")
LIB = os.path.join(A, "ThirdParty", "Sound Effects Library")
FPC = os.path.join(A, "ThirdParty", "FPC", "Audio")
OUT = os.path.join(A, "Game", "Audio", "Clips", "Dialogue")
HALL = "aecho=0.8:0.7:23|37|53|79|113|167|229|311:0.32|0.28|0.24|0.2|0.16|0.12|0.09|0.06"   # a dense hall, no single repeats


def clip(path, at=0.0, volume=1.0, pitch=1.0, filters="", start=0.0, end=0.0):
    """A library clip: from start to end seconds of the file (its main hit lined up by start), pitched (slower and
    lower below 1), then filtered, at the given time in the mix."""
    return dict(path=path, at=at, volume=volume, pitch=pitch, filters=filters, start=start, end=end)


def tone(expr, seconds, at=0.0, volume=1.0, filters=""):
    return dict(expr=expr, seconds=seconds, at=at, volume=volume, filters=filters)


def render(name, layers, seconds):
    args, chains = ["ffmpeg", "-y", "-v", "error"], []
    for i, layer in enumerate(layers):
        if "path" in layer:
            args += ["-i", layer["path"]]
            pitch = f"asetrate=48000*{layer['pitch']}," if layer["pitch"] != 1 else ""
            cut = f"atrim=start={layer['start']}" + (f":end={layer['end']}" if layer["end"] else "") + ",asetpts=PTS-STARTPTS,"
            source = f"[{i}:a]aresample=48000,aformat=channel_layouts=mono,{cut}{pitch}aresample=48000"
        else:
            args += ["-f", "lavfi", "-i", f"aevalsrc='{layer['expr']}':s=48000:d={layer['seconds']}"]
            source = f"[{i}:a]aformat=channel_layouts=mono"
        extra = f",{layer['filters']}" if layer["filters"] else ""
        delay = int(layer["at"] * 1000)
        chains.append(f"{source}{extra},volume={layer['volume']},adelay={delay}[l{i}]")
    mix = "".join(f"[l{i}]" for i in range(len(layers)))
    graph = ";".join(chains) + f";{mix}amix=inputs={len(layers)}:normalize=0:duration=longest,{HALL}," \
        f"atrim=0:{seconds},afade=t=out:st={seconds - .35}:d=0.35,alimiter=limit=0.89:level=false[out]"
    path = os.path.join(OUT, name)
    raw = path + ".raw.wav"
    subprocess.run(args + ["-filter_complex", graph, "-map", "[out]", "-ar", "48000", "-ac", "1", raw], check=True)
    # Peaks brought to -1 dB, so every take plays as loud as the next and the fight's volume sets it.
    probe = subprocess.run(["ffmpeg", "-hide_banner", "-i", raw, "-af", "volumedetect", "-f", "null", "-"], capture_output=True, text=True).stderr
    peak = float(probe.split("max_volume:")[1].split("dB")[0])
    subprocess.run(["ffmpeg", "-y", "-v", "error", "-i", raw, "-af", f"volume={-1 - peak:.2f}dB", path], check=True)
    os.remove(raw)
    print("wrote", path, f"(+{-1 - peak:.1f} dB)")


HUM = "0.5*sin(2*PI*60*t)+0.32*sin(2*PI*120*t)+0.16*sin(2*PI*180*t)+0.07*sin(2*PI*300*t)"
SUB = "sin(2*PI*(58*t-16*t*t))*exp(-4.5*t)"

# A breaker thrown: the switch's tick, a heavy metal chunk a hair after it (a metal door's slam, low), a shield's ring
# under it and a thump in the floor; then the lamp's hum swells in with a thin sizzle.
render("Spotlight clunk.wav", [
    clip(os.path.join(A, "Game", "Cinematics", "Intro", "Tabletop", "Lamp click.wav"), 0, .9, .6),
    clip(os.path.join(LIB, "Doors", "Drs_MetalDoor_Close_01.wav"), .035, 1.0, .8, "lowpass=f=2600", start=.21, end=1.2),
    clip(os.path.join(LIB, "Weapons", "Wpn_Block_Shield_01.wav"), .035, .3, .55, "lowpass=f=1400"),
    tone(SUB, .6, .035, .6),
    tone(HUM, 1.9, .1, .3, "afade=t=in:d=0.3,afade=t=out:st=0.8:d=1.1,lowpass=f=900"),
    clip(os.path.join(LIB, "Magic", "Mgc_Electric_Cast_Start_01.wav"), .1, .16, 1, "highpass=f=3000,afade=t=in:d=0.2,afade=t=out:st=0.4:d=0.8", end=1.3),
], 2.2)

# A heavier, older lamp: an iron latch dropping (its one hit), a door's low clang and a sub thump, the hum slower in.
render("Spotlight clunk B.wav", [
    clip(os.path.join(LIB, "SoundFX", "126041__mhtaylor67__gate-latch.wav"), 0, 1.0, .75, start=.6, end=1.1),
    clip(os.path.join(LIB, "Doors", "Drs_Dungeon_MetalDoubleDoor_Close_01.wav"), .0, .55, .7, "lowpass=f=1800"),
    tone(SUB, .7, 0, .6),
    tone(HUM, 2.0, .12, .32, "afade=t=in:d=0.45,afade=t=out:st=1:d=1,lowpass=f=900"),
], 2.3)

# The boss set down: a heavy slam (its hit lined up at the start), feet on stone, an armour jangle a beat after, a
# falling sub boom and a little grit.
render("Boss landing.wav", [
    clip(os.path.join(A, "Game", "Audio", "Clips", "Reveal", "Entrance slam.wav"), 0, .9, .9, start=.1),
    clip(os.path.join(FPC, "Rock", "Rock_Land_1.wav"), 0, .9, .85),
    clip(os.path.join(LIB, "Clothes", "Light_Armor_03.wav"), .05, .5, .9),
    tone(SUB, .8, 0, 1.0),
    clip(os.path.join(LIB, "Ambience", "Amb_Rock_Drop_Impact_02.wav"), .06, .35, 1, "highpass=f=500,afade=t=out:st=0.4:d=0.8", end=1.2),
], 1.8)

# Stone instead: a stone door's final slam, feet in grit, a jangle and the sub boom.
render("Boss landing B.wav", [
    clip(os.path.join(LIB, "Doors", "Drs_StoneDoor_Close_01.wav"), 0, .9, .85, start=2.66, end=3.9),
    clip(os.path.join(FPC, "Dirt", "Dirt_Land_2.wav"), 0, .9, .8),
    clip(os.path.join(LIB, "Clothes", "Light_Armor_06.wav"), .05, .45, .85),
    tone(SUB, .8, 0, 1.1),
], 1.8)

# The disco ball coming down on its chain: a winch's grinding run, slowing, and a clunk as the chain takes its weight
# (BossDiscoBall lowers it over 3 s).
render("Disco ball lower.wav", [
    clip(os.path.join(LIB, "Doors", "Portal_Mechanic_Start_01.wav"), 0, .8, 1.1, "afade=t=out:st=2.4:d=0.6", end=3.2),
    clip(os.path.join(LIB, "Doors", "Drs_Locked_02.wav"), 2.85, .9, .8, start=.1, end=.6),
    tone(SUB, .5, 2.9, .5),
], 3.8)

# The disco boss's last pose bursting: a pop, glittering drops and a shimmer of chimes.
render("Disco defeat burst.wav", [
    clip(os.path.join(A, "Game", "Audio", "Clips", "Misc", "pop waw.wav"), 0, 1.0, .9),
    clip(os.path.join(LIB, "Magic", "Mgc_Fading_Drops_Fire_02.wav"), .03, .8, 1.15),
    clip(os.path.join(LIB, "SoundFX", "WindChimes.mp3"), .05, .35, 1.2, "afade=t=out:st=0.8:d=1.2", end=2.2),
    tone(SUB, .6, 0, .6),
], 2.4)
