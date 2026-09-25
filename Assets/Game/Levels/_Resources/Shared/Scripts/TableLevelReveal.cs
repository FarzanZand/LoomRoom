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
    readonly List<LineRenderer> outlines = new();
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
        output = PlayerManager.Instance.OutputCamera;
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
            dungeon.ShowCeilings(false);
            foreach (var character in dungeon.GetComponentsInChildren<Character>())
            { actors.Add((character.gameObject, character.gameObject.activeSelf)); character.gameObject.SetActive(false); }
            float range = Mathf.Max(1, new Vector2(width, depth).magnitude);
            foreach (var renderer in dungeon.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || renderer is ParticleSystemRenderer || renderer.GetComponentInParent<Canvas>() != null) continue;
                float distance = Vector3.Distance(renderer.bounds.center, dungeon.SpawnPoint) / range;
                float stage = renderer.bounds.size.y < .5f ? 0 : .08f;
                pieces.Add(new Piece { renderer = renderer, position = renderer.transform.position, delay = Mathf.Clamp01(distance + stage) * .72f });
                renderer.enabled = false;
            }
            foreach (var light in dungeon.GetComponentsInChildren<Light>())
            { lights.Add((light, light.enabled, Vector3.Distance(light.transform.position, dungeon.SpawnPoint) / range * .72f)); light.enabled = false; }
            BuildBlueprint(settings);
            if (settings.dustPrefab != null)
            {
                dust = Instantiate(settings.dustPrefab, dungeon.transform.position + Vector3.up * .2f, Quaternion.identity, transform);
                var shape = dust.shape; shape.scale = new Vector3(width, .1f, depth);
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
                foreach (var line in outlines)
                {
                    var color = settings.blueprintColor;
                    float lineDelay = Vector3.Distance(line.GetPosition(0), dungeon.SpawnPoint) / range * .7f;
                    color.a *= Mathf.Clamp01((elapsed / Mathf.Max(.1f, settings.blueprintSeconds) - lineDelay) * 4) * (1 - Mathf.Clamp01(build));
                    line.startColor = line.endColor = color;
                }
                if (!started && build >= 0) { started = true; PlaySound(settings.buildSound, settings.volume); }
                if (!settled && build >= 1) { settled = true; PlaySound(settings.settleSound, settings.volume); foreach (var actor in actors) if (actor.target != null) actor.target.SetActive(actor.active); }
                elapsed += Time.unscaledDeltaTime;
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
            dungeon.ShowCeilings(false);
            if (tablePlayer.ViewPresentation != null)
                foreach (var renderer in tablePlayer.ViewPresentation.GetComponentsInChildren<Renderer>(true))
                { handRenderers.Add((renderer, renderer.forceRenderingOff)); renderer.forceRenderingOff = true; }
            yield return null;
            tablePlayer.Combat?.PrepareEquippedPose();
            entryHands = tablePlayer.ViewPresentation;
            entryHands?.FollowTransitionCamera(output, 0);
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
            dungeon.ShowCeilings(true);
            // The loadout is ready, but belongs only to the dungeon POV. Reveal it at
            // the destination, never attached to the room camera during the flight.
            entryHands?.FollowTransitionCamera(output, 0);
            foreach (var entry in handRenderers) if (entry.renderer != null) entry.renderer.forceRenderingOff = entry.forcedOff;
            // Discard residual smoothing before handing control back.
            tablePlayer.Look?.ResetInput();
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
                entryTarget.InternalUpdateCameraState(Vector3.up, Time.unscaledDeltaTime);
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
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        if (entryTarget != null)
        {
            entryTarget.InternalUpdateCameraState(Vector3.up, Time.unscaledDeltaTime);
            var live = entryTarget.State;
            destination = live.GetFinalPosition(); rotation = live.GetFinalOrientation();
            projection = Matrix4x4.Perspective(live.Lens.FieldOfView, output.aspect, live.Lens.NearClipPlane, live.Lens.FarClipPlane);
        }
        output.transform.SetPositionAndRotation(destination, rotation); output.projectionMatrix = projection;
    }

    void RestorePieces()
    {
        foreach (var piece in pieces) if (piece.renderer != null) { piece.renderer.transform.position = piece.position; piece.renderer.enabled = true; }
        foreach (var entry in lights) if (entry.light != null) entry.light.enabled = entry.enabled;
        foreach (var actor in actors) if (actor.target != null) actor.target.SetActive(actor.active);
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
        var directions = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
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
    void Restore()
    {
        RestorePieces();
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
        if (dungeon != null) dungeon.ShowCeilings(true);
        pieces.Clear(); actors.Clear(); lights.Clear(); outlines.Clear(); captured = false; IsPlaying = false;
    }
    void OnDisable() => Restore();
}
