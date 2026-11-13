# Setting up a computer for LoomRoom

What a new team member (or a second computer) needs to work on the project, with or without Claude.

## The project

1. Get access to the GitHub repository `FarzanZand/LoomRoom` and clone it. The history is about 4.6 GB (no Git LFS).
2. Install **Unity 6000.6.8f1** with Unity Hub or the Unity CLI (`unity install 6000.6.8f1`), plus Android build support if you build for Android. The editor version must match `ProjectSettings/ProjectVersion.txt` exactly.
3. Open the project. The first open rebuilds the `Library` folder, which takes a long time; later opens are quick.
4. The game is `Assets/Game/Scenes/Room.unity`. `Assets/Game/README.md` says where to go to change what, and `Docs/Folder-layout.md` has the folder rules.

**Making builds** (only on a computer that builds): builds use IL2CPP since 2026-11-13. Add the editor's **Windows Build Support (IL2CPP)** module, and install Visual Studio 2022 or newer with the **Desktop development with C++** workload and a **Windows 11 SDK** (Visual Studio Installer > Modify > Individual components). Without the SDK the build stops at "Could not set up a toolchain".

**Odin Inspector** must be 4.1.0.0 or newer for Unity 6.6 (it is in the repository; 4.0.1.6 threw an error every frame after each script reload).

**Licences.** The project uses paid assets: Synty packs, Odin Inspector, the Pixel Crushers Dialogue System and others in `Assets/ThirdParty`. Check each one's licence terms for every new person before they get the repository.

## Tools the scripts use

- **Python 3.12** with `numpy`, `scipy`, `Pillow` and `requests` (`pip install numpy scipy pillow requests`). The art and sound scripts in `Tools/` use them.
- **Blender 5.2** for models (`Tools/*.py` scripts that import `bpy` run inside it, headless). Scripts look for it at `C:\Program Files\Blender Foundation\Blender 5.2\blender.exe`.
- **Meshy** (only when a task asks for it): your own API key in `~/.meshy/api_key`, never in the repository. `python Tools/meshy.py --help`.
- **The asset library** (optional): bought packs live in a separate Unity project, `C:/Game projects/AssetLibrary`, outside git. `python Tools/asset_library.py` pulls single assets from it into LoomRoom. You only need it to bring in new pack content; ask the lead for the pack files.

## Claude

Claude Code reads the project's rules from the repository, so every teammate's Claude works by the same rules:

- `CLAUDE.md` (the project, its systems and its rules; it loads `Docs/Team-rules.md`, how we work),
- `.claude/skills/loomroom-art/SKILL.md` (the art pipeline),
- `Docs/` (the style guide and the rest; `CLAUDE.md` points to them).

To set Claude up:

1. **Install the Unity CLI** and its skill: `irm https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.ps1 | iex` in PowerShell (set `$env:UNITY_CLI_CHANNEL='beta'` first), then `unity skill install`. Claude drives the open editor through it; the project already has the `com.unity.pipeline` package it needs. Run `unity status` with the project open to check it connects.
2. **Never Unity MCP servers.** If you have Coplay MCP or UnityMCP set up from elsewhere, Claude must not use them here (the rule is in `CLAUDE.md`).
3. **Personal settings stay personal:** your permissions go in `.claude/settings.local.json` and your own instructions in `CLAUDE.local.md` at the project root (both git-ignored), or in `~/.claude/`. Claude's memory on your computer is yours alone and isn't shared.
4. **New rules for everyone go in the repository.** When the lead settles something that should hold for the whole team, it goes into `Docs/Team-rules.md` or `CLAUDE.md` and is committed, so every computer gets it with the next pull.

## Working on more than one computer

- **One computer, many sessions** (the lead's setup): the Claude sessions share one checkout and one editor; `Docs/Team-rules.md` > Several Claude sessions in one checkout says how.
- **Your own computer:** work on a branch of your own (`git switch -c <name>/<topic>`), pull `master` into it often, and merge it back through a pull request. Commit and push before you move to another computer, and pull before you start there.
- **Scenes and prefabs merge badly.** Agree who edits `Room.unity` (and any big prefab) at a time; two people changing the same scene between merges leads to a hand merge.
