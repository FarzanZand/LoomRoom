using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;

// Presentation only: the seeded layout and navigation are already ready before playback.
// Every touched state is restored, including on interruption or scene unload.
public sealed class TableLevelReveal : MonoBehaviour
{
    struct Piece { public Renderer renderer; public Vector3 position; public float delay; }
    readonly List<Piece> pieces = new();
    readonly List<(GameObject target, bool active)> actors = new();
    readonly List<(Light light, bool enabled, float delay)> lights = new();
    // Particle effects (torch flames and the like) stay switched off until their spot is built.
    readonly List<(GameObject effect, bool active, float delay)> effects = new();
    // The table stays whole under a pit until the build reaches it (TableCut).
    readonly List<(TableCut.Hole hole, float delay)> holes = new();
    readonly List<LineRenderer> outlines = new();
    readonly List<Renderer> liftedCeilings = new();
    readonly List<(Renderer renderer, bool forcedOff)> handRenderers = new();
    readonly List<(Renderer renderer, bool forcedOff)> roomRenderers = new();
    Camera output;
    CinemachineBrain brain;
    bool brainEnabled, orthographic;
    float size, fov, near, far;
    Vector3 cameraPosition;
    Quaternion cameraRotation;
    GameObject blueprint;
    ParticleSystem dust;
    DungeonGenerator dungeon;
    AudioSource sound;
    AudioClip soundClip;
    bool captured, completed, skipWindow;
    InputManager skipInput;
    PlayerViewPresentation entryHands;
    Matrix4x4 originalProjection;
    public bool IsPlaying { get; private set; }
    public bool SkipRequested { get; set; }
    public bool HasArrived { get; private set; }

    public IEnumerator Play(DungeonGenerator generated, TableLevelRevealSettings settings, System.Action beginEntry = null)
    {
        dungeon = generated;
        output = PlayerManager.HasInstance ? PlayerManager.Instance.OutputCamera : null;
        if (dungeon == null || output == null || settings == null) yield break;
        IsPlaying = true;
        try
        {
            brain = output.GetComponent<CinemachineBrain>();
            brainEnabled = brain != null && brain.enabled;
            if (brain != null) brain.enabled = false;
            cameraPosition = output.transform.position; cameraRotation = output.transform.rotation;
            orthographic = output.orthographic; size = output.orthographicSize; fov = output.fieldOfView;
            near = output.nearClipPlane; far = output.farClipPlane; originalProjection = output.projectionMatrix; captured = true;
            PlayerManager.Instance.SwapToPlayerImmediately(PlayerKind.Room);
            var data = dungeon.LevelData;
            float width = data.width * data.cellSize, depth = data.depth * data.cellSize;
            float viewSize = Mathf.Max(depth * Mathf.Sin(settings.cameraAngle * Mathf.Deg2Rad) + 4, width / Mathf.Max(.1f, output.aspect)) * .5f * settings.framingMargin;
            float height = Mathf.Max(settings.overheadHeight, depth * .5f / Mathf.Tan(settings.cameraAngle * Mathf.Deg2Rad) + 5);
            var cameraOffset = new Vector3(0, height, -height / Mathf.Tan(settings.cameraAngle * Mathf.Deg2Rad));
            var overview = dungeon.transform.position + cameraOffset;
            var overviewRotation = Quaternion.Euler(settings.cameraAngle, 0, 0);
            var overviewProjection = Matrix4x4.Ortho(-viewSize * output.aspect, viewSize * output.aspect, -viewSize, viewSize, .05f, Mathf.Max(far, height + depth + 100));
            if (settings.useRoomPlayerPOV)
            {
                var roomCamera = PlayerManager.Instance.GetPlayer(PlayerKind.Room).CameraRig.Camera;
                roomCamera.InternalUpdateCameraState(Vector3.up, -1);
                overview = roomCamera.State.GetFinalPosition(); overviewRotation = roomCamera.State.GetFinalOrientation();
                overviewProjection = Matrix4x4.Perspective(roomCamera.State.Lens.FieldOfView, output.aspect, output.nearClipPlane, output.farClipPlane);
                // Keep the exact view if the player was already looking from the room.
                if (Vector3.Distance(cameraPosition, overview) < 1f) { overview = cameraPosition; overviewRotation = cameraRotation; overviewProjection = originalProjection; }
            }
            // From the room the rooms may stay open (WorldManager.hideTopForGenerations); the ceilings come back on arrival.
            dungeon.ShowCeilingsFor(PlayerKind.Room);
            foreach (var character in dungeon.GetComponentsInChildren<Character>())
            { actors.Add((character.gameObject, character.gameObject.activeSelf)); character.gameObject.SetActive(false); }
            float range = Mathf.Max(1, new Vector2(width, depth).magnitude);
            foreach (var renderer in dungeon.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || renderer is ParticleSystemRenderer || renderer.GetComponentInParent<Canvas>() != null) continue;
                float distance = Vector3.Distance(renderer.bounds.center, dungeon.SpawnPoint) / range;
                // Floors first, then walls, then the ceilings close over the rooms.
                bool roof = dungeon.Ceilings != null && renderer.transform.IsChildOf(dungeon.Ceilings);
                float stage = roof ? .22f : renderer.bounds.size.y < .5f ? 0 : .08f;
                pieces.Add(new Piece { renderer = renderer, position = renderer.transform.position, delay = Mathf.Clamp01(distance + stage) * .72f });
                renderer.enabled = false;
            }
            // Torches come on as the build reaches them. A board's single overhead light is left on, or the
            // whole board would sit black until the end.
            var boardLight = dungeon.GetComponent<BoardReveal>() is { } board ? board.BoardLight : null;
            foreach (var light in dungeon.GetComponentsInChildren<Light>())
            {
                if (light == boardLight) continue;
                lights.Add((light, light.enabled, Vector3.Distance(light.transform.position, dungeon.SpawnPoint) / range * .72f)); light.enabled = false;
            }
            foreach (var system in dungeon.GetComponentsInChildren<ParticleSystem>())
            {
                // One entry per effect: nested systems follow their root.
                if (system.transform.parent != null && system.transform.parent.GetComponentInParent<ParticleSystem>() != null) continue;
                var go = system.gameObject;
                effects.Add((go, go.activeSelf, Vector3.Distance(go.transform.position, dungeon.SpawnPoint) / range * .72f));
                go.SetActive(false);
            }
            foreach (var hole in dungeon.TableHoles)
            {
                var at = new Vector3(hole.area.center.x, dungeon.SpawnPoint.y, hole.area.center.y);
                holes.Add((hole, Vector3.Distance(at, dungeon.SpawnPoint) / range * .72f)); hole.Open = false;
            }
            BuildBlueprint(settings);
            HideStartWall(overview);
            if (settings.dustPrefab != null)
            {
                dust = Instantiate(settings.dustPrefab, dungeon.transform.position + Vector3.up * .2f, Quaternion.identity, transform);
                // The shape is turned -90 on X to emit upward, so its local Y is the floor's depth and Z is height.
                var shape = dust.shape; shape.scale = new Vector3(width, depth, .1f);
                dust.Play();
            }
            yield return MoveCamera(overview, overviewRotation, overviewProjection, settings.cameraTransitionSeconds);
            float elapsed = 0, duration = settings.blueprintSeconds + settings.buildSeconds + settings.settleSeconds;
            bool started = false, settled = false;
            if (settings.allowSkip && InputManager.HasInstance)
            {
                skipInput = InputManager.Instance;
                skipInput.CancelPressed += OnSkipInput;
                skipInput.SubmitPressed += OnSkipInput;
            }
            skipWindow = settings.allowSkip;
            while (elapsed < duration)
            {
                if (settings.allowSkip && SkipRequested) break;
                float build = (elapsed - settings.blueprintSeconds) / Mathf.Max(.1f, settings.buildSeconds);
                foreach (var piece in pieces)
                {
                    if (piece.renderer == null) continue;
                    float t = Mathf.Clamp01((build - piece.delay) / Mathf.Max(.1f, settings.pieceDuration));
                    piece.renderer.enabled = t > 0;
                    piece.renderer.transform.position = piece.position - Vector3.up * settings.liftDistance * (1 - settings.rise.Evaluate(t));
                }
                foreach (var entry in lights) if (entry.light != null) entry.light.enabled = entry.enabled && build > entry.delay + .25f;
                foreach (var entry in holes) entry.hole.Open = build > entry.delay;
                foreach (var entry in effects)
                {
                    bool on = entry.active && build > entry.delay + .25f;
                    if (entry.effect != null && entry.effect.activeSelf != on) entry.effect.SetActive(on);
                }
                foreach (var line in outlines)
                {
                    var color = settings.blueprintColor;
                    float lineDelay = Vector3.Distance(line.GetPosition(0), dungeon.SpawnPoint) / range * .7f;
                    color.a *= Mathf.Clamp01((elapsed / Mathf.Max(.1f, settings.blueprintSeconds) - lineDelay) * 4) * (1 - Mathf.Clamp01(build));
                    line.startColor = line.endColor = color;
                }
                if (!started && build >= 0) { started = true; PlaySound(settings.buildSound, settings.volume); }
                if (!settled && build >= 1) { settled = true; PlaySound(settings.settleSound, settings.volume); foreach (var actor in actors) if (actor.target != null) actor.target.SetActive(actor.active); }
                elapsed += GameManager.CutsceneDelta;
                yield return null;
            }
            skipWindow = false;
            RestorePieces();
            if (blueprint != null) blueprint.SetActive(false);
            var tablePlayer = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
            // Switching players normally exposes the room avatar. The output camera is
            // still inside its head here, so suppress its visuals until travel finishes.
            var roomPlayer = PlayerManager.Instance.GetPlayer(PlayerKind.Room);
            if (roomPlayer != null)
                foreach (var renderer in roomPlayer.ActivationRoot.GetComponentsInChildren<Renderer>(true))
                { roomRenderers.Add((renderer, renderer.forceRenderingOff)); renderer.forceRenderingOff = true; }
            // First activation initializes the rig's Awake/Start and look pivots. Keep the
            // output under our control, and hold first-person hands until the flight ends.
            PlayerManager.Instance.SwapToPlayerImmediately(PlayerKind.Table);
            // The way in is the spawn room's skylight. Without one (a resumed floor), lift the
            // ceiling over the spawn point for the flight only.
            if (!dungeon.SpawnRoofOpen && dungeon.Ceilings != null)
                foreach (var r in dungeon.Ceilings.GetComponentsInChildren<Renderer>())
                {
                    var b = r.bounds; b.Expand(new Vector3(2f, 50f, 2f));
                    if (r.enabled && b.Contains(dungeon.SpawnPoint)) { liftedCeilings.Add(r); r.enabled = false; }
                }
            if (tablePlayer.ViewPresentation != null)
                foreach (var renderer in tablePlayer.ViewPresentation.GetComponentsInChildren<Renderer>(true))
                { handRenderers.Add((renderer, renderer.forceRenderingOff)); renderer.forceRenderingOff = true; }
            yield return null;
            if (tablePlayer.Combat != null) tablePlayer.Combat.PrepareEquippedPose();
            entryHands = tablePlayer.ViewPresentation;
            if (entryHands != null) entryHands.FollowTransitionCamera(output, 0);
            var targetCamera = tablePlayer.CameraRig.Camera;
            targetCamera.InternalUpdateCameraState(Vector3.up, -1);
            var target = targetCamera.State;
            var targetProjection = Matrix4x4.Perspective(target.Lens.FieldOfView, output.aspect, target.Lens.NearClipPlane, target.Lens.FarClipPlane);
            beginEntry?.Invoke();
            // Fly over the entrance and descend through its temporarily open roof.
            yield return MoveCamera(target.GetFinalPosition(), target.GetFinalOrientation(), targetProjection,
                SkipRequested ? .3f : settings.approachSeconds, true, targetCamera);
            output.fieldOfView = target.Lens.FieldOfView;
            HasArrived = true;
            ShowStartWall();
            RestoreCeilings();
            // The loadout is ready, but belongs only to the dungeon POV. Reveal it at
            // the destination, never attached to the room camera during the flight.
            if (entryHands != null) entryHands.FollowTransitionCamera(output, 0);
            foreach (var entry in handRenderers) if (entry.renderer != null) entry.renderer.forceRenderingOff = entry.forcedOff;
            completed = true;
        }
        finally { Restore(); }
    }

    IEnumerator MoveCamera(Vector3 destination, Quaternion rotation, Matrix4x4 projection, float seconds, bool enter = false, CinemachineCamera entryTarget = null)
    {
        var start = output.transform.position; var startRotation = output.transform.rotation;
        var startProjection = output.projectionMatrix;
        var aboveEntrance = destination + Vector3.up * 5;
        float elapsed = 0;
        while (elapsed < Mathf.Max(.1f, seconds))
        {
            // Gravity and camera pivots can settle after the player activates. Follow
            // the live destination so Cinemachine receives the same pose at handover.
            if (entryTarget != null)
            {
                entryTarget.InternalUpdateCameraState(Vector3.up, GameManager.CutsceneDelta);
                var live = entryTarget.State;
                destination = live.GetFinalPosition(); rotation = live.GetFinalOrientation();
                projection = Matrix4x4.Perspective(live.Lens.FieldOfView, output.aspect, live.Lens.NearClipPlane, live.Lens.FarClipPlane);
                aboveEntrance = destination + Vector3.up * 5;
            }
            float t = Mathf.SmoothStep(0, 1, elapsed / Mathf.Max(.1f, seconds));
            var position = Vector3.Lerp(start, destination, t);
            if (enter)
            {
                float u = 1 - t;
                position = u*u*u*start + 3*u*u*t*Vector3.Lerp(start, aboveEntrance, .7f)
                    + 3*u*t*t*aboveEntrance + t*t*t*destination;
            }
            output.transform.SetPositionAndRotation(position, Quaternion.Slerp(startRotation, rotation, t));
            var matrix = new Matrix4x4();
            for (int i = 0; i < 16; i++) matrix[i] = Mathf.Lerp(startProjection[i], projection[i], t);
            output.projectionMatrix = matrix;
            elapsed += GameManager.CutsceneDelta;
            yield return null;
        }
        if (entryTarget != null)
        {
            entryTarget.InternalUpdateCameraState(Vector3.up, GameManager.CutsceneDelta);
            var live = entryTarget.State;
            destination = live.GetFinalPosition(); rotation = live.GetFinalOrientation();
            projection = Matrix4x4.Perspective(live.Lens.FieldOfView, output.aspect, live.Lens.NearClipPlane, live.Lens.FarClipPlane);
        }
        output.transform.SetPositionAndRotation(destination, rotation); output.projectionMatrix = projection;
    }

    void RestoreCeilings()
    {
        foreach (var r in liftedCeilings) if (r != null) r.enabled = true;
        liftedCeilings.Clear();
    }

    void RestorePieces()
    {
        foreach (var piece in pieces) if (piece.renderer != null) { piece.renderer.transform.position = piece.position; piece.renderer.enabled = true; }
        foreach (var entry in lights) if (entry.light != null) entry.light.enabled = entry.enabled;
        foreach (var entry in effects) if (entry.effect != null) entry.effect.SetActive(entry.active);
        foreach (var actor in actors) if (actor.target != null) actor.target.SetActive(actor.active);
        foreach (var entry in holes) entry.hole.Open = true;
    }

    // Cancel and Submit arrive through InputManager's UI map, which is live during the cutscene state.
    void OnSkipInput() { if (skipWindow) SkipRequested = true; }

    void PlaySound(AudioClip clip, float volume)
    {
        StopSound();
        if (clip != null && AudioManager.HasInstance) { sound = AudioManager.Instance.PlaySFX2D(clip, volume); soundClip = clip; }
    }

    // The source is pooled: once our clip ends it may already be playing someone else's sound.
    void StopSound()
    {
        if (sound != null && sound.clip == soundClip) sound.Stop();
        sound = null; soundClip = null;
    }

    void BuildBlueprint(TableLevelRevealSettings settings)
    {
        if (settings.lineMaterial == null) return;
        blueprint = new GameObject("Dungeon blueprint"); blueprint.transform.SetParent(transform, false);
        var layout = dungeon.Layout; float half = dungeon.LevelData.cellSize * .5f;
        var directions = DungeonLayout.Neighbours;
        // Outline the actual floor perimeter, including the routed corridors.
        for (int x = 0; x < layout.floor.GetLength(0); x++) for (int y = 0; y < layout.floor.GetLength(1); y++)
        {
            if (!layout.floor[x, y]) continue;
            foreach (var dir in directions)
            {
                int nx = x + dir.x, ny = y + dir.y;
                if (nx >= 0 && ny >= 0 && nx < layout.floor.GetLength(0) && ny < layout.floor.GetLength(1) && layout.floor[nx, ny]) continue;
                var center = dungeon.Cell(new Vector2Int(x, y)) + new Vector3(dir.x * half, .05f, dir.y * half);
                var tangent = new Vector3(dir.y * half, 0, dir.x * half);
                Line(center - tangent, center + tangent, settings, settings.blueprintColor);
            }
        }
        // Entrance marker remains visible while the camera approaches it.
        var p = dungeon.SpawnPoint + Vector3.up * .12f;
        Line(p - Vector3.right, p + Vector3.right, settings, settings.entranceColor, false);
        Line(p - Vector3.forward, p + Vector3.forward, settings, settings.entranceColor, false);
    }
    void Line(Vector3 a, Vector3 b, TableLevelRevealSettings settings, Color color, bool fade = true)
    {
        var go = new GameObject("Plan line", typeof(LineRenderer)); go.transform.SetParent(blueprint.transform, false);
        var line = go.GetComponent<LineRenderer>(); line.sharedMaterial = settings.lineMaterial;
        line.useWorldSpace = true; line.positionCount = 2; line.SetPosition(0, a); line.SetPosition(1, b);
        line.startWidth = line.endWidth = settings.lineWidth;
        if (fade) color.a = 0f; // Hidden from creation, including the camera approach before playback.
        line.startColor = line.endColor = color;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
        if (fade) outlines.Add(line);
    }
    // Safe to call more than once (the coroutine's finally, then OnDisable): every list is emptied.
    // TableLevelData.hideStartWallInReveal: the starting room's wall on the viewer's side, hidden until arrival. The
    // walls are merged into static chunks (several walls each), so every chunk holding a piece of that wall is hidden whole.
    readonly List<Renderer> splitChunks = new(), hiddenCeiling = new();
    void HideStartWall(Vector3 viewer)
    {
        var data = dungeon.LevelData;
        if (data == null || !data.hideStartWallInReveal || dungeon.Layout == null) return;
        int start = dungeon.Layout.RoomAt(dungeon.CellOf(dungeon.SpawnPoint));
        if (start < 0) return;
        float cell = data.cellSize;
        // The room's cells: its middle, and its extent toward the viewer and to each side.
        var cells = new List<Vector3>();
        for (int x = 0; x < data.width; x++) for (int z = 0; z < data.depth; z++)
        { var p = new Vector2Int(x, z); if (dungeon.Layout.InRoom(start, p)) cells.Add(dungeon.Cell(p)); }
        if (cells.Count == 0) return;
        var centre = Vector3.zero; foreach (var c in cells) centre += c; centre /= cells.Count;
        var toViewer = viewer - centre; toViewer.y = 0f;
        if (toViewer.sqrMagnitude < .01f) return;
        toViewer.Normalize();
        var right = Vector3.Cross(Vector3.up, toViewer);
        float reach = 0f, side = 0f;
        foreach (var c in cells) { var d = c - centre; reach = Mathf.Max(reach, Vector3.Dot(d, toViewer)); side = Mathf.Max(side, Mathf.Abs(Vector3.Dot(d, right))); }
        var area = new Bounds(centre, new Vector3(cell * 2f, 100f, cell * 2f));
        foreach (var c in cells) area.Encapsulate(new Bounds(c, new Vector3(cell * 3f, 100f, cell * 3f)));
        bool NearWall(Bounds b)
        {
            var d = b.center - centre;
            float along = Vector3.Dot(d, toViewer), across = Mathf.Abs(Vector3.Dot(d, right));
            return b.size.y > .1f && along > reach + cell * .25f && along < reach + cell * 1.5f && across < side + cell * 1.2f;
        }
        // The starting room's ceiling stays off too until arrival, so the room reads from above.
        if (dungeon.Ceilings != null)
            foreach (var r in dungeon.Ceilings.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r.forceRenderingOff) continue;
                var c = r.bounds.center; bool over = false;
                foreach (var cellCentre in cells) if (Mathf.Abs(c.x - cellCentre.x) < cell * .8f && Mathf.Abs(c.z - cellCentre.z) < cell * .8f) { over = true; break; }
                if (!over) continue;
                r.forceRenderingOff = true; hiddenCeiling.Add(r);
            }
        var all = dungeon.GetComponentsInChildren<Renderer>(true);
        // Only the pieces merged away into chunks count: the reveal has every other renderer switched off for now.
        var revealed = new HashSet<Renderer>();
        foreach (var piece in pieces) revealed.Add(piece.renderer);
        int count = pieces.Count;
        for (int i = 0; i < count; i++)
        {
            var chunk = pieces[i].renderer;
            if (chunk == null || chunk.name != "Static chunk" || chunk.bounds.size.y < 1f || !chunk.bounds.Intersects(area)) continue;
            if (dungeon.Ceilings != null && chunk.transform.IsChildOf(dungeon.Ceilings)) continue;
            // A chunk that holds a piece of the near wall is hidden whole (only looked at, nothing is switched on).
            var box = chunk.bounds; box.Expand(.3f);
            bool near = false;
            foreach (var r in all)
            {
                if (r == null || r.enabled || revealed.Contains(r) || r.name == "Static chunk" || r.transform.parent != chunk.transform.parent || !box.Contains(r.bounds.center)) continue;
                if (r.bounds.size.y > 1f && NearWall(r.bounds)) { near = true; break; }
            }
            if (!near) continue;
            chunk.forceRenderingOff = true; splitChunks.Add(chunk);
        }
    }

    // The hidden chunks and ceiling show again.
    void ShowStartWall()
    {
        foreach (var r in splitChunks) if (r != null) r.forceRenderingOff = false;
        foreach (var r in hiddenCeiling) if (r != null) r.forceRenderingOff = false;
        splitChunks.Clear(); hiddenCeiling.Clear();
    }

    void Restore()
    {
        RestorePieces();
        ShowStartWall();
        foreach (var entry in handRenderers) if (entry.renderer != null) entry.renderer.forceRenderingOff = entry.forcedOff;
        handRenderers.Clear();
        foreach (var entry in roomRenderers) if (entry.renderer != null) entry.renderer.forceRenderingOff = entry.forcedOff;
        roomRenderers.Clear();
        skipWindow = false;
        if (skipInput != null) { skipInput.CancelPressed -= OnSkipInput; skipInput.SubmitPressed -= OnSkipInput; }
        skipInput = null;
        StopSound();
        if (blueprint != null) Destroy(blueprint);
        if (dust != null) Destroy(dust.gameObject);
        blueprint = null; dust = null;
        if (captured && output != null)
        {
            output.orthographic = orthographic; output.orthographicSize = size;
            if (!completed) output.fieldOfView = fov;
            output.ResetProjectionMatrix();
            output.nearClipPlane = near; output.farClipPlane = far;
            if (!completed) output.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
            if (brain != null) { brain.enabled = brainEnabled; if (!completed) brain.ResetState(); }
        }
        if (captured && PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind != PlayerKind.Table) PlayerManager.Instance.SwapToPlayerImmediately(PlayerKind.Table);
        RestoreCeilings();
        if (dungeon != null) dungeon.ShowCeilingsFor(PlayerManager.HasInstance ? PlayerManager.Instance.ActiveKind : PlayerKind.Table);
        pieces.Clear(); actors.Clear(); lights.Clear(); holes.Clear(); effects.Clear(); outlines.Clear(); captured = false; IsPlaying = false;
    }
    void OnDisable() => Restore();
}
