# Team rules

How everyone on LoomRoom works, people and Claude sessions alike. `CLAUDE.md` loads this file, so every Claude session on any computer follows it. These rules are the project lead's (Farzan's) standing feedback, collected 2026-09 to 2026-11. Most were learned the hard way. When the lead decides something new that should hold for everyone, add it here (or to `CLAUDE.md`), not to one computer's private memory.

Rules that already sit in `CLAUDE.md` (texel size, tile seams, no coplanar faces, lights on props, no menu entries, no obsolete APIs, Unity CLI only, our own design, the apartment never hidden) are not repeated here.

## Done means looked at

- **Look before you say done.** Capture any visible change in game, crop and enlarge the part you changed, and check it yourself against what was asked: gaps, sizes, texel size against its neighbours, how it reads from where the player stands. A passing number or test script is not a look. Report what still looks wrong.
- **Review in the game's own light**, from the closest the player can get, with no added fill light (a fill light once hid every fault on a face). List every fault you see and fix each one. If one is left, say "not done" and name it. Whether a visible flaw is acceptable is the lead's call, not yours.
- **Review every item of a batch on its own**, side by side with its source, close up, and then in the running game. A handful glanced at from a distance is not a review. Props are checked from every side (front, sides, back, top, undersides, bevels) at 4 or more screen pixels per texel.
- **Review every generated image at full size before using it**: moved or rearranged views, changed silhouettes, patterns copied from the reference, text or extra objects, wrong materials.
- **Motion at the full frame rate.** Judge animation from 30 fps frame sheets or the video at full rate, also from the side and from behind, never from a 3-4 fps overview. Before sending a video, go through it frame by frame and write a numbered list of every fault (popping, clipping, lighting, pose, timing, framing). Send it only when the list is empty.
- **Would the lead be happy with it?** If not, don't show it: fix it first. "No faults found" is not the bar; the lead's own complaints in that task being fixed is. If it needs the lead's input (taste, a reference), ask for that instead of sending unfinished work.
- **Found something broken or weak in your work? Fix it.** Don't report it and hand the work over (the lead, 2026-11-09, after icons came back three times with a list of known faults). A list of what is still weak is not a result: keep going until the list is empty, then show it. Tell the lead about a fault only when you judge it can't be fixed, and say why.
- **Play it, frame by frame.** Intros, cutscenes, lighting and other player-facing sequences are done only after playing them through, capturing frames and polishing. Say what was checked and what needs a human (Claude can't hear audio).
- **When the lead says they will test it themselves,** stop any capture or recording loops and just say when it is ready.

## Fixing and changing things

- **Fix, don't replace.** Asked to fix an asset, find what is actually broken in it (measure it against its source) and fix that. Never swap in a different model or start new art without asking: time already spent on an asset is worth keeping.
- **Fix only the part that was named.** A complaint about one arm, prop or system is about that one. Don't retime, reroute or rebuild the rest as a side fix (the sword combo was broken once by "fixing" the shield). The combo's hits chain straight into each other: each hands back to Idle only at 0.9 of its clip.
- **Don't balance the game down** from a review or a hits-to-kill estimate: the lead plays it and finds it easier than the numbers say. Treat "too hard" as a question for the lead. Raising difficulty or fixing broken numbers is fine.
- **Test in the editor** (Play mode, evals). Make or launch builds only when asked: builds take over the computer.
- **Keep bought libraries.** Unused sound, Synty, ThirdParty and Meshy libraries are kept for later; don't list them as reclaimable space. Licensed or ripped music is the exception: flag it.
- **The Town is out of scope** until the lead brings it in: no tests, balance, art or findings for it.
- **TikTok mode is on on purpose** (`DungeonMaster.tiktokMode` in `Room.unity`): the narrow, centred feed lines are not a fault. Anything placed against the feed must work with it on.
- **The class portraits are human artists' work.** Judge where art came from by the project's own record (git history, Meshy folders, `Tools/` scripts) before calling anything AI-made.

## Art

- **Use Blender for new models** (Blender 5.2, headless `blender --background --python script.py`, FBX out). Keep the `.blend` next to the `.fbx`. Round things are modelled round.
- **Low poly, PS1/PS2.** A Meshy head is at most about what the body it sits on has (around 1,000 triangles). Get the low count from Meshy (`meshy.py remesh --polycount <n>`); Blender decimation from millions tears models apart. Report in-game triangle counts.
- **No Meshy unless the lead asks for it** for that task. By default make textures by rules from the model (`Tools/prop_structure.py`, `Tools/prop_organic.py`, `PixelRestyle`) and by hand.
- **Approve concepts before 3D.** When Meshy is asked for: make the concepts, review them yourself, show a contact sheet and stop. Build only the ones the lead picks.
- **Small pixel edits by hand** (a mark, a crop, colour matching, compositing, making things tile), in the house style with colours sampled from the texture itself. Meshy is only for new painting you can't do well by hand.
- **Match the approved concept.** An art pass is done only when an in-game render holds up side by side with the approved concept. Show the side by side.
- **Drawn-by-code icons and items** read as turned objects: a small 3D form turned a quarter toward the viewer and seen a little from above, layered plates with bevels and cast shadows, uneven wear, cool shadows and warm highlights, reflective metal. Never mirror details left to right, never use speckle as texture, and give every item in a set its own construction and silhouette. Expect at least three more rounds after you think it is done.
- **Meshy textures** are a 2048 atlas: never import below 1024 px source. The chosen in-game look for Meshy characters is 512 px, Point filter.
- **Presentations and decks** wear the game's own identity (the menu's teal haze, the gold title art, the game's fonts, the Dungeon Master's cyan voice, screenshots in rough gold frames), never a clean square template. Cream or white paper is a per-case choice, not a fault.

## Animation and characters

- **Animation lands as editable `.anim` clips** that animator states point at. Code only drives playback speed, layer weights, camera feel and input; never animate bones from scripts. Keep authored clips untouched and write derived clips next to them.
- **What others see mirrors first person.** Every player action shown from outside (the body others see, co-op teammates) is a simple mimic that starts the moment the first-person action does and lasts as long. Check every action from the watcher's side, not only the one reported.

## Writing

- **In-game text is short and literal.** Say what a thing is or does ("Damage with maces."). No dramatic flavour, no "the fallen", no edgy fantasy. No em dashes (the pixel font can't draw them).
- **Tooltips are label and number rows,** at most one short line of text. Never print the default or the obvious: no "normal" anything, nothing that repeats a tab, title or type line, no words a number already says.
- **Never name another game** anywhere: code, comments, tooltips, asset names, docs, commits, in-game text.

## Loot

- No drawbacks on items, no unidentified items, no curses, no items that work from the bag.
- No generated prefix or suffix names: items keep plain names; only hand-made Relics get names of their own.
- Stats only if players feel them; keep the list short.

## Content and code

- **One folder per thing.** Everything a thing uses sits in its folder (a spell's data, projectile, effects, sounds and hotbar item in `Combat/Spells/<Spell>/`). Repeated prefabs are variants of a base (projectiles of `Combat/Spells/_Base/Base Projectile`).
- **Code shape** (on top of `CLAUDE.md`'s settled decisions): data ScriptableObjects named `<Thing>Data`, managers `<Thing>Manager : Singleton<T>`, UI `<Thing>UI` / `<Thing>SlotUI`. Every static event gets a `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` reset. Coroutines, not async. A long "why" comment at the top of load-bearing files, designer-grade `[Tooltip]`s, `// ── Section ──` headers, no `#region`. One MonoBehaviour per file (several in one file show as missing scripts on prefabs).
- **Code you save must compile.** Unity picks up any `.cs` saved into the project at the next refresh, and a compile error stops everyone using that editor. Check that each type you use exists and is reachable the way you write it (static? nested?), and check the console soon after saving, warnings included.

## Git and working together

- **Commit and push only when asked.** Stage only your own paths and commit with a pathspec (`git commit -m "..." -- <paths>`); after committing, `git show --stat HEAD` must list only your files before you push. "Save and push everything" means the whole working tree; leave out `Assets/_Recovery`.
- **After a batch rebuild** (a whole prefab or many assets), `git diff` each touched file and put back any part that isn't yours.
- **Never merge the `coop` branch into master.** Master flows into coop. Plain bug fixes found on coop come over one commit at a time, only when they touch no multiplayer code.
- **Scenes merge badly.** Never edit `Room.unity` or the same prefab in two places at once. When only part of a scene file is yours, stage your hunks exactly (rebuild the staged file from `HEAD` by exact line numbers and check it against the working file); `git apply --unidiff-zero` misplaces insertions. A scene staged that way is committed with a plain `git commit` while nothing else is staged: naming the scene in a pathspec takes the whole working file.

### Several Claude sessions in one checkout

The lead runs several Claude sessions (a persona team) in the one checkout `C:/Game projects/LoomRoom`, sharing one editor on port 7800. In that checkout:

- **Book the editor** before Play, a recompile or saving a scene, and free it after: `sh Tools/book7800.sh take <name> "<what>"`, `free <name>`, `show`. "Free" is not "yours" when someone is queued.
- **No pull, rebase, stash or checkout of others' files.** Everyone commits in the same folder, so their commits are already in HEAD. Only `git fetch`, and `git merge --ff-only origin/master` when HEAD is really behind (an autostash pull once rewrote `Room.unity` under the open editor).
- **Keep unfinished code out of the project folder** until you hold the editor; don't fix another session's half-finished code to get a compile through (a file changed minutes ago is someone's work in progress).

## Unity CLI tips

- `unity command eval_file --file <path>` runs C# as a method body: no `using` lines; write `UnityEngine.Object`, never a bare `Object`. A failed compile prints only "Compilation Failed".
- "Main thread operation timed out after 5000ms" usually still completes: check with a second quick eval instead of retrying.
- With several editors open, always pass `--project-path "<project>"` (there is no working `--port`) and check the target project in a JSON reply: if the intended editor has closed, the CLI falls back to another one. Ports change after a restart; run `unity status`.
- `EditorApplication.delayCall` doesn't fire while the editor is unfocused. Files copied in from outside aren't imported while it is unfocused either: `AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport)`.
- Many scene objects are prefab instances: call `PrefabUtility.RecordPrefabInstancePropertyModifications(obj)` after every change from an eval, or the change is dropped on save. Check by reopening the scene from disk.
- Unity wipes the project's `Temp/` on every editor start: keep trackers and backups elsewhere. `capture_game_view --source screen` only works in Play mode and saves inside the project.
- `??` and `?.` don't work on destroyed Unity objects; use `== null`. In tests, reach the floor through `TableManager.Loader.Dungeon` (a `FindAnyObjectByType<DungeonGenerator>` may return a portal pocket).
