using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The Cascade build (CampaignManager > Level generator). In order: the floor's footprint shows as dark tiles; every
// piece is placed, from the floor's far end toward the entrance, each dropping from above a little large and tipped,
// squashing on its foot as it lands, in an uneven, patchy front, with every lamp out; a moment of stillness, then the
// entrance comes down hard (the view shakes, dust is thrown up); the floor's lamps come on one by one; the entrance's
// torches catch last; then the view flies in (TableLevelReveal.Play). While it runs the build's merged chunks are held
// off and the pieces they were made of drawn instead; at the end the chunks come back and the pieces go off again, so the
// floor is exactly as built. Everything is put back however it ends (Restore).
public sealed partial class TableLevelReveal
{
    // One falling thing: a wall or floor piece, a ceiling piece, or a prop (with its children).
    struct Tile
    {
        public Transform transform;
        public Vector3 position, scale, pivot, foot;   // foot: the middle of its underside, where it lands
        public Quaternion rotation, tilt;
        public float delay;            // seconds into the cascade that it starts to drop
        public float drop, seconds;    // its own drop height (a share of the setting) and drop time
        public bool entrance;
        public Renderer[] renderers;
        public bool[] wasEnabled, show;
        public bool shown, landed, resting;
    }
    readonly List<Tile> tiles = new();
    readonly List<(Renderer renderer, bool enabled)> heldChunks = new();
    readonly List<float> lightStrength = new();
    readonly List<AudioSource> clackSources = new();
    Mesh ghostMesh;
    Color[] ghostColors;
    float[] ghostAlpha;
    readonly List<(int vertex, float delay, bool entrance)> ghostCells = new();
    // Per lamp, flame and table hole: when it comes on, in seconds into the cascade.
    float[] lightAt, effectAt, holeAt;
    int startRoom = -1;
    Bounds entranceArea;

    static float Hash(Vector3 p) { float h = Mathf.Sin(p.x * 12.9898f + p.z * 78.233f + p.y * 37.719f) * 43758.5453f; return h - Mathf.Floor(h); }

    // Smooth noise over the table (patches a few cells across), -1..1: the front runs ahead or lags in patches.
    static float Patch(Vector3 p, float cell) => Mathf.PerlinNoise(p.x / (cell * 4f) + 31.7f, p.z / (cell * 4f) + 11.3f) * 2f - 1f;

    bool InEntrance(Vector3 at)
    {
        if (startRoom < 0 || dungeon.Layout == null) return false;
        float r = dungeon.LevelData.cellSize * .45f;
        foreach (var o in new[] { Vector3.zero, new Vector3(r, 0, 0), new Vector3(-r, 0, 0), new Vector3(0, 0, r), new Vector3(0, 0, -r) })
            if (dungeon.Layout.RoomAt(dungeon.CellOf(at + o)) == startRoom) return true;
        return false;
    }

    // Seconds into the cascade when the last piece of the floor has landed (worked out once their times are known), the
    // entrance comes down and lands, the floor's lamps start, the entrance's catch and it all ends.
    float placingEnd;
    float EntranceStart(TableLevelRevealSettings s) => placingEnd + s.entrancePause;
    float EntranceLands(TableLevelRevealSettings s) => EntranceStart(s) + s.entranceDropSeconds * .55f;
    float LightsStart(TableLevelRevealSettings s) => EntranceStart(s) + s.entranceDropSeconds + s.lightsPause;
    float EntranceLightsStart(TableLevelRevealSettings s) => LightsStart(s) + s.lightsSeconds + s.lightFadeSeconds + s.entranceLightsPause;
    float CascadeEnd(TableLevelRevealSettings s) => EntranceLightsStart(s) + s.lightFadeSeconds + s.litHold;

    // Gathers what falls. Characters stay hidden as before (actors), until the entrance has landed.
    void CollectTiles(TableLevelRevealSettings settings)
    {
        var spawn = dungeon.SpawnPoint;
        startRoom = dungeon.Layout != null ? dungeon.Layout.RoomAt(dungeon.CellOf(spawn)) : -1;
        float cell = dungeon.LevelData.cellSize;
        entranceArea = new Bounds(spawn, Vector3.one * cell);
        if (startRoom >= 0)
            foreach (var c in dungeon.Layout.RoomCells(startRoom)) entranceArea.Encapsulate(new Bounds(dungeon.Cell(c), new Vector3(cell, .1f, cell)));
        var geometry = dungeon.GetComponent<DungeonStaticGeometry>();
        var merged = geometry != null ? new HashSet<Renderer>(geometry.MergedPieces) : new HashSet<Renderer>();
        if (geometry != null)
            foreach (var chunk in geometry.BuildChunks)
                if (chunk != null && chunk.gameObject.activeInHierarchy) { heldChunks.Add((chunk, chunk.enabled)); chunk.enabled = false; }
        var architecture = dungeon.transform.Find("Architecture");
        var ceilings = dungeon.Ceilings;
        var candidates = new List<(Transform t, int stage)>();
        foreach (Transform child in dungeon.transform)
        {
            if (!child.gameObject.activeInHierarchy) continue;
            if (child == architecture || child == ceilings)
            {
                foreach (Transform piece in child)
                    if (piece.name != "Static chunk" && piece.gameObject.activeInHierarchy && piece.TryGetComponent<Renderer>(out _))
                        candidates.Add((piece, child == ceilings ? 3 : -1));
                continue;
            }
            AddUnit(child, candidates, 0);
        }
        float scale = Mathf.Abs(dungeon.transform.lossyScale.y);
        var found = new List<(Tile tile, float distance, int stage)>();
        foreach (var (t, wanted) in candidates)
        {
            var renderers = new List<Renderer>();
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                if (!(r is ParticleSystemRenderer) && !(r is LineRenderer) && r.GetComponentInParent<Canvas>() == null && r.GetComponentInParent<Character>() == null) renderers.Add(r);
            if (renderers.Count == 0) continue;
            var tile = new Tile { transform = t, position = t.position, rotation = t.rotation, scale = t.localScale, renderers = renderers.ToArray() };
            tile.wasEnabled = new bool[renderers.Count]; tile.show = new bool[renderers.Count];
            Bounds? bounds = null;
            for (int i = 0; i < renderers.Count; i++)
            {
                var r = renderers[i];
                tile.wasEnabled[i] = r.enabled;
                tile.show[i] = r.enabled || merged.Contains(r);
                if (!tile.show[i]) continue;
                if (bounds is { } b) { b.Encapsulate(r.bounds); bounds = b; } else bounds = r.bounds;
            }
            if (bounds == null) continue;
            tile.pivot = bounds.Value.center;
            tile.foot = new Vector3(tile.pivot.x, bounds.Value.min.y, tile.pivot.z);
            tile.entrance = InEntrance(tile.pivot);
            // Floors first at any spot, then walls, then props, then ceilings.
            int stage = wanted >= 0 ? wanted : bounds.Value.size.y < .5f * scale ? 0 : 1;
            var flat = tile.pivot - spawn; flat.y = 0f;
            float distance = flat.magnitude;
            float h = Hash(tile.pivot), h2 = Hash(tile.pivot * 1.7f + Vector3.one), h3 = Hash(tile.pivot * 2.3f);
            var axis = new Vector3(Mathf.Cos(h * 40f), 0f, Mathf.Sin(h * 40f));
            tile.tilt = Quaternion.AngleAxis((h - .5f) * 2f * settings.tileTilt * (1f + settings.variety * (h3 - .5f)), axis);
            tile.drop = 1f + settings.variety * (h2 * 2f - 1f);
            tile.seconds = Mathf.Max(.05f, settings.tileDropSeconds * (1f + settings.variety * .6f * (h3 * 2f - 1f)));
            foreach (var r in tile.renderers) r.enabled = false;
            found.Add((tile, distance, stage));
        }
        // From the far end toward the entrance at an even pace: a piece's place in the order is the share of pieces
        // farther from the entrance than it (the entrance is on the floor's edge, so raw distance would leave the last
        // stretch nearly empty). The front is patchy (Warp), each piece leads or lags a little (Jitter), and walls and
        // props come a moment after the floor under them.
        var sorted = new List<float>();
        foreach (var f in found) if (!f.tile.entrance) sorted.Add(f.distance);
        sorted.Sort();
        paceDistances = sorted.ToArray();
        float span = Mathf.Max(.3f, 1f - settings.waveJitter - settings.waveWarp - 3f * settings.wallLag);
        float Placed(Vector3 at, float distance, int stage) =>
            settings.ghostSeconds + settings.cascadeSeconds * Mathf.Clamp01(Pace(distance) * span
                + (Patch(at, cell) * .5f + .5f) * settings.waveWarp + Hash(at * 3.1f) * settings.waveJitter + stage * settings.wallLag);
        var rng = new System.Random(unchecked((int)(Hash(spawn) * 100000)));
        // The floor first, so the entrance can come a set pause after its last piece has landed.
        placingEnd = settings.ghostSeconds;
        foreach (var (tile, distance, stage) in found)
        {
            if (tile.entrance) continue;
            var t = tile;
            t.delay = Placed(t.pivot, distance, stage);
            placingEnd = Mathf.Max(placingEnd, t.delay + t.seconds);
            tiles.Add(t);
        }
        foreach (var (tile, _, _) in found)
        {
            if (!tile.entrance) continue;
            var t = tile;
            t.delay = EntranceStart(settings) + (float)rng.NextDouble() * .06f; t.drop = 1f + .25f * (float)rng.NextDouble(); t.seconds = settings.entranceDropSeconds;
            tiles.Add(t);
        }
        // Lamps and flames: all out until the entrance has landed, then on one by one; the entrance's last.
        lightAt = new float[lights.Count];
        for (int i = 0; i < lights.Count; i++)
        {
            var light = lights[i].light;
            lightStrength.Add(light != null ? light.intensity : 0f);
            bool atEntrance = light != null && InEntrance(light.transform.position);
            lightAt[i] = atEntrance ? EntranceLightsStart(settings) + (float)rng.NextDouble() * .08f : LightsStart(settings) + (float)rng.NextDouble() * settings.lightsSeconds;
        }
        effectAt = new float[effects.Count];
        for (int i = 0; i < effects.Count; i++)
        {
            var go = effects[i].effect;
            var at = go != null ? go.transform.position : spawn;
            // A flame catches with the lamp nearest it, so torch and flame come on together.
            float best = float.MaxValue, when = InEntrance(at) ? EntranceLightsStart(settings) : LightsStart(settings) + Hash(at) * settings.lightsSeconds;
            for (int l = 0; l < lights.Count; l++)
            {
                if (lights[l].light == null) continue;
                float d = (lights[l].light.transform.position - at).sqrMagnitude;
                if (d < best && d < cell * cell) { best = d; when = lightAt[l]; }
            }
            effectAt[i] = when;
        }
        holeAt = new float[holes.Count];
        for (int i = 0; i < holes.Count; i++)
        {
            var at = new Vector3(holes[i].hole.area.center.x, spawn.y, holes[i].hole.area.center.y);
            var flat = at - spawn; flat.y = 0f;
            holeAt[i] = InEntrance(at) ? EntranceStart(settings) : Placed(at, flat.magnitude, 0);
        }
        BuildGhost(settings, spawn, span, cell);
    }

    // 0 for the piece farthest from the entrance .. 1 for the nearest: the share of pieces farther away than distance.
    float[] paceDistances;
    float Pace(float distance)
    {
        if (paceDistances == null || paceDistances.Length == 0) return 0f;
        int i = System.Array.BinarySearch(paceDistances, distance);
        if (i < 0) i = ~i;
        return 1f - (float)i / paceDistances.Length;
    }

    // A room template or prop group is a container: its children fall one by one. A thing with a renderer, a body or a
    // collider of its own falls whole.
    void AddUnit(Transform t, List<(Transform, int)> into, int depth)
    {
        if (t.GetComponent<Character>() != null) return;
        bool whole = depth >= 2 || t.childCount == 0 || t.GetComponent<Renderer>() != null || t.GetComponent<Rigidbody>() != null || t.GetComponent<Collider>() != null;
        // A thing that carries a character (the keeper's stall) stays where it is: he is hidden until the entrance lands.
        if (whole) { if (t.GetComponentInChildren<Character>(true) == null) into.Add((t, 2)); return; }
        foreach (Transform child in t) if (child.gameObject.activeInHierarchy) AddUnit(child, into, depth + 1);
    }

    // The start room's near wall is hidden chunk by chunk (HideStartWall): its pieces stay hidden too.
    void KeepHiddenWithChunks(List<Renderer> hidden)
    {
        if (hidden.Count == 0) return;
        for (int i = 0; i < tiles.Count; i++)
        {
            var tile = tiles[i];
            for (int r = 0; r < tile.renderers.Length; r++)
            {
                var piece = tile.renderers[r];
                if (piece == null || !tile.show[r]) continue;
                foreach (var chunk in hidden)
                    if (chunk != null && piece.transform.parent == chunk.transform.parent && piece.sharedMaterial == chunk.sharedMaterial && chunk.bounds.Contains(piece.bounds.center))
                    { tile.show[r] = false; break; }
            }
        }
    }

    // The footprint: one dark tile per floor cell (a darker rim round a lighter face), each going as its floor lands.
    void BuildGhost(TableLevelRevealSettings settings, Vector3 spawn, float span, float cell)
    {
        if (settings.lineMaterial == null || dungeon.Layout == null) return;
        var layout = dungeon.Layout;
        float outer = cell * settings.ghostTileSize * .5f, inner = outer * .82f;
        var vertices = new List<Vector3>(); var triangles = new List<int>();
        // Vertex colours are drawn as linear values: converted, the tiles show the colour picked in the inspector.
        var rim = settings.ghostColor.linear; var face = Color.Lerp(settings.ghostColor, Color.white, .12f).linear;
        rim.a = face.a = settings.ghostColor.a;
        var colors = new List<Color>();
        void Quad(Vector3 c, float half, float lift, Color color)
        {
            int v = vertices.Count;
            vertices.Add(c + new Vector3(-half, lift, -half)); vertices.Add(c + new Vector3(-half, lift, half));
            vertices.Add(c + new Vector3(half, lift, half)); vertices.Add(c + new Vector3(half, lift, -half));
            for (int i = 0; i < 4; i++) colors.Add(color);
            triangles.AddRange(new[] { v, v + 1, v + 2, v, v + 2, v + 3 });
        }
        for (int x = 0; x < layout.floor.GetLength(0); x++) for (int y = 0; y < layout.floor.GetLength(1); y++)
        {
            if (!layout.floor[x, y]) continue;
            var c = dungeon.Cell(new Vector2Int(x, y));
            var flat = c - spawn; flat.y = 0f;
            bool entrance = startRoom >= 0 && layout.RoomAt(new Vector2Int(x, y)) == startRoom;
            float delay = entrance ? EntranceStart(settings) : settings.ghostSeconds + settings.cascadeSeconds * Mathf.Clamp01(Pace(flat.magnitude) * span
                + (Patch(c, cell) * .5f + .5f) * settings.waveWarp + Hash(c * 3.1f) * settings.waveJitter);
            ghostCells.Add((vertices.Count, delay, entrance));
            Quad(c, outer, .03f, rim);
            Quad(c, inner, .04f, face);
        }
        if (blueprint == null) { blueprint = new GameObject("Dungeon blueprint"); blueprint.transform.SetParent(transform, false); }
        var go = new GameObject("Floor footprint", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(blueprint.transform, false);
        // The cells are in world space; the blueprint hangs under the loader, wherever that stands.
        for (int i = 0; i < vertices.Count; i++) vertices[i] = go.transform.InverseTransformPoint(vertices[i]);
        ghostMesh = new Mesh { name = "Floor footprint", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        ghostMesh.SetVertices(vertices); ghostMesh.SetColors(colors); ghostMesh.SetTriangles(triangles, 0);
        ghostColors = colors.ToArray();
        ghostAlpha = new float[ghostColors.Length];
        for (int i = 0; i < ghostColors.Length; i++) { ghostAlpha[i] = ghostColors[i].a; ghostColors[i].a = 0f; }
        ghostMesh.colors = ghostColors;
        go.GetComponent<MeshFilter>().sharedMesh = ghostMesh;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = settings.lineMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
    }

    IEnumerator Cascade(TableLevelRevealSettings settings)
    {
        float cellHeight = dungeon.LevelData.cellSize * Mathf.Abs(dungeon.transform.lossyScale.y);
        float end = CascadeEnd(settings), landsAt = EntranceLands(settings), entranceLightsAt = EntranceLightsStart(settings);
        float elapsed = 0f, lastClack = -1f, lastTorch = -1f;
        int waiting = 0;
        bool landed = false, entranceLit = false;
        var lightOn = new bool[lights.Count];
        var view = output.transform; var viewRotation = view.rotation;
        foreach (var (light, _, _) in lights) if (light != null) light.enabled = false;
        foreach (var (effect, _, _) in effects) if (effect != null) effect.SetActive(false);
        if (ghostMesh != null) PlaySound(settings.footprintSound, settings.footprintVolume);
        while (elapsed < end)
        {
            if (settings.allowSkip && SkipRequested) break;
            for (int i = 0; i < tiles.Count; i++)
            {
                var tile = tiles[i];
                if (tile.resting || tile.transform == null) continue;
                float u = (elapsed - tile.delay) / tile.seconds;
                if (u <= 0f) continue;
                if (!tile.shown)
                {
                    tile.shown = true;
                    for (int r = 0; r < tile.renderers.Length; r++) if (tile.renderers[r] != null) tile.renderers[r].enabled = tile.show[r];
                }
                if (!tile.landed && u >= .55f) { tile.landed = true; if (!tile.entrance) waiting++; }
                if (u >= 1f)
                {
                    tile.resting = true;
                    tile.transform.SetPositionAndRotation(tile.position, tile.rotation); tile.transform.localScale = tile.scale;
                }
                else Pose(tile, u, settings, (tile.entrance ? settings.entranceDropHeight : settings.tileDropHeight) * tile.drop * cellHeight);
                tiles[i] = tile;
            }
            // The footprint fades in, then each of its tiles goes as the floor lands on it.
            if (ghostMesh != null)
            {
                float fadeIn = settings.ghostSeconds > 0f ? Mathf.Clamp01(elapsed / Mathf.Min(.25f, settings.ghostSeconds)) : 1f;
                foreach (var (vertex, delay, entrance) in ghostCells)
                {
                    float drop = entrance ? settings.entranceDropSeconds : settings.tileDropSeconds;
                    float gone = Mathf.Clamp01((elapsed - delay - drop * .5f) / (drop * .4f));
                    for (int v = vertex; v < vertex + 8; v++) ghostColors[v].a = ghostAlpha[v] * fadeIn * (1f - gone);
                }
                ghostMesh.colors = ghostColors;
            }
            for (int i = 0; i < holes.Count; i++) holes[i].hole.Open = elapsed > holeAt[i];
            // The landings: never closer than the gap, louder while many land together.
            if (waiting > 0 && elapsed - lastClack >= settings.clackGap)
            {
                PlayOneOf(settings.tileSounds, settings.clackVolume * Mathf.Clamp01(.5f + .1f * waiting), Random.Range(settings.clackPitch.x, settings.clackPitch.y));
                lastClack = elapsed; waiting = 0;
            }
            // The entrance lands: the slam, its dust, a shake; the characters come out.
            if (!landed && elapsed >= landsAt)
            {
                landed = true;
                PlayOneShot(settings.entranceSlamSound, settings.entranceSlamVolume, 1f);
                ThrowDust(settings);
                flash = MakeFlash(settings);
                foreach (var actor in actors) if (actor.target != null) actor.target.SetActive(actor.active);
            }
            if (flash != null)
            {
                float f = Mathf.Clamp01((elapsed - landsAt) / .5f);
                flash.intensity = flashPeak * (1f - f) * (1f - f);
                if (f >= 1f) { Destroy(flash.gameObject); flash = null; }
            }
            if (landed && settings.entranceShake > 0f)
            {
                float s = Mathf.Clamp01((elapsed - landsAt) / Mathf.Max(.05f, settings.entranceShakeSeconds));
                float amount = settings.entranceShake * (1f - s) * (1f - s);
                float n = elapsed * 38f;
                view.rotation = viewRotation * Quaternion.Euler((Mathf.PerlinNoise(n, 1.3f) - .5f) * 2f * amount, (Mathf.PerlinNoise(2.7f, n) - .5f) * 2f * amount, (Mathf.PerlinNoise(n, 5.1f) - .5f) * amount);
            }
            // The lamps, one by one, each easing up to its strength; its flame catches with it.
            for (int i = 0; i < lights.Count; i++)
            {
                var (light, enabled, _) = lights[i];
                if (light == null || !enabled) continue;
                float f = Mathf.Clamp01((elapsed - lightAt[i]) / settings.lightFadeSeconds);
                light.enabled = f > 0f;
                light.intensity = lightStrength[i] * f * f * (3f - 2f * f);
                if (f > 0f && !lightOn[i])
                {
                    lightOn[i] = true;
                    if (lightAt[i] < entranceLightsAt && elapsed - lastTorch >= settings.torchGap)
                    { PlayOneOf(settings.torchSounds, settings.torchVolume, Random.Range(.8f, 1f)); lastTorch = elapsed; }
                }
            }
            for (int i = 0; i < effects.Count; i++)
            {
                var (effect, active, _) = effects[i];
                bool on = active && elapsed >= effectAt[i];
                if (effect != null && effect.activeSelf != on) effect.SetActive(on);
            }
            if (!entranceLit && elapsed >= entranceLightsAt) { entranceLit = true; PlayOneShot(settings.entranceTorchSound, settings.entranceTorchVolume, 1f); }
            elapsed += GameManager.CutsceneDelta;
            yield return null;
        }
        view.rotation = viewRotation;
        if (flash != null) { Destroy(flash.gameObject); flash = null; }
    }

    Light flash;
    float flashPeak;

    // A short warm flash over the entrance as it lands: a point light like the floor's own lamps (their rendering
    // layers), Entrance Flash times as strong as the strongest of them.
    Light MakeFlash(TableLevelRevealSettings settings)
    {
        if (settings.entranceFlash <= 0f) return null;
        Light template = null; float strongest = 0f;
        for (int i = 0; i < lights.Count; i++)
            if (lights[i].light != null && lights[i].light.type == LightType.Point && lightStrength[i] > strongest) { strongest = lightStrength[i]; template = lights[i].light; }
        if (template == null) return null;
        var go = Instantiate(template.gameObject, new Vector3(entranceArea.center.x, entranceArea.min.y + entranceArea.extents.magnitude * .5f, entranceArea.center.z), Quaternion.identity, transform);
        foreach (var c in go.GetComponentsInChildren<Component>())
            if (c is not Transform && c is not Light && c is not UnityEngine.Rendering.Universal.UniversalAdditionalLightData) Destroy(c);
        for (int i = go.transform.childCount - 1; i >= 0; i--) Destroy(go.transform.GetChild(i).gameObject);
        var light = go.GetComponent<Light>();
        light.enabled = true;
        light.color = new Color(1f, .78f, .55f);
        light.range = entranceArea.extents.magnitude * 2.2f;
        light.shadows = LightShadows.None;
        flashPeak = strongest * settings.entranceFlash;
        light.intensity = flashPeak;
        return light;
    }

    // Placed: it drops from above, a little large and tipped, straightening as it falls (gravity: slow at first, fast
    // at the end), and squashes flat on its foot as it lands, then springs back. Everything is scaled about the foot, so
    // nothing ever dips below where it rests.
    static void Pose(Tile tile, float u, TableLevelRevealSettings settings, float dropHeight)
    {
        const float fall = .55f;
        float f = Mathf.Clamp01(u / fall);
        float y = dropHeight * (1f - f * f);
        float straight = 1f - (1f - f) * (1f - f);
        float k = Mathf.Lerp(settings.tilePop, 1f, f * f);
        float squash = 0f;
        if (u > fall) { float b = (u - fall) / (1f - fall); squash = settings.tileSquash * (tile.entrance ? 1.5f : 1f) * Mathf.Sin(b * Mathf.PI) * (1f - b * .6f); }
        var q = Quaternion.Slerp(tile.tilt, Quaternion.identity, straight);
        var stretch = new Vector3(k * (1f + squash * .5f), k * (1f - squash), k * (1f + squash * .5f));
        tile.transform.SetPositionAndRotation(tile.foot + Vector3.up * y + q * Vector3.Scale(tile.position - tile.foot, stretch), q * tile.rotation);
        tile.transform.localScale = Vector3.Scale(tile.scale, stretch);
    }

    // Dust blown out from under the entrance as it lands: puffs along its edges, pushed outward and a little up,
    // spreading and thinning; it clears on its own.
    void ThrowDust(TableLevelRevealSettings settings)
    {
        if (settings.entranceDustMaterial == null || settings.entranceDustCount <= 0) return;
        float cell = dungeon.LevelData.cellSize;
        var go = new GameObject("Entrance dust");
        go.transform.SetParent(transform, false);
        var puff = go.AddComponent<ParticleSystem>();
        puff.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = puff.main;
        main.loop = false; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = settings.entranceDustCount + 10;
        main.gravityModifier = .02f;
        var emission = puff.emission; emission.enabled = false;
        var shape = puff.shape; shape.enabled = false;
        var grow = puff.sizeOverLifetime; grow.enabled = true; grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, .6f, 1f, 1.8f));
        var slow = puff.limitVelocityOverLifetime; slow.enabled = true; slow.limit = cell * .4f; slow.dampen = .12f;
        var fade = puff.colorOverLifetime; fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .06f), new GradientAlphaKey(.7f, .45f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = settings.entranceDustMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        puff.Play();
        var b = entranceArea; float floorY = b.min.y;
        var p = new ParticleSystem.EmitParams();
        for (int i = 0; i < settings.entranceDustCount; i++)
        {
            // A point on the room's edge, and outward from its middle.
            float side = Random.value, along = Random.value;
            var at = side < .5f ? new Vector3(side < .25f ? b.min.x : b.max.x, floorY, Mathf.Lerp(b.min.z, b.max.z, along))
                                : new Vector3(Mathf.Lerp(b.min.x, b.max.x, along), floorY, side < .75f ? b.min.z : b.max.z);
            var outward = at - b.center; outward.y = 0f; outward = outward.normalized;
            p.position = at + Vector3.up * Random.Range(cell * .35f, cell * .9f);
            p.velocity = outward * Random.Range(1.5f, 4f) * cell + Vector3.up * Random.Range(.2f, 1f) * cell;
            p.startSize = Random.Range(2f, 3.6f) * cell;
            p.startLifetime = Random.Range(1.3f, 2.2f);
            p.startColor = Color.Lerp(new Color(1f, 1f, 1f, 1f), new Color(.85f, .8f, .75f, .85f), Random.value);
            p.rotation = Random.Range(0f, 360f);
            puff.Emit(p, 1);
        }
        Destroy(go, 3.5f);
    }

    // Its own few sources (the pool keeps one clip to three at once and spaces them out): round robin.
    AudioSource NextSource()
    {
        if (!AudioManager.HasInstance) return null;
        if (clackSources.Count == 0)
            for (int i = 0; i < 6; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false; source.spatialBlend = 0f; source.outputAudioMixerGroup = AudioManager.Instance.SfxGroup;
                clackSources.Add(source);
            }
        var next = clackSources[0]; clackSources.RemoveAt(0); clackSources.Add(next);
        return next;
    }

    void PlayOneOf(AudioClip[] clips, float volume, float pitch)
    {
        if (clips == null || clips.Length == 0) return;
        PlayOneShot(clips[Random.Range(0, clips.Length)], volume, pitch);
    }

    void PlayOneShot(AudioClip clip, float volume, float pitch)
    {
        if (clip == null) return;
        var source = NextSource();
        if (source == null) return;
        source.pitch = pitch;
        source.PlayOneShot(clip, volume);
    }

    void DestroyClackSources()
    {
        foreach (var source in clackSources) if (source != null) Destroy(source);
        clackSources.Clear();
    }

    // Everything back as built: pieces in place and drawn as before, the chunks on, lamps at their strength.
    void RestoreTiles()
    {
        foreach (var tile in tiles)
        {
            if (tile.transform == null) continue;
            tile.transform.SetPositionAndRotation(tile.position, tile.rotation); tile.transform.localScale = tile.scale;
            for (int r = 0; r < tile.renderers.Length; r++) if (tile.renderers[r] != null) tile.renderers[r].enabled = tile.wasEnabled[r];
        }
        tiles.Clear();
        foreach (var (chunk, enabled) in heldChunks) if (chunk != null) chunk.enabled = enabled;
        heldChunks.Clear();
        for (int i = 0; i < lightStrength.Count && i < lights.Count; i++) if (lights[i].light != null) lights[i].light.intensity = lightStrength[i];
        lightStrength.Clear();
        if (ghostMesh != null) Destroy(ghostMesh);
        ghostMesh = null; ghostCells.Clear(); ghostColors = null; ghostAlpha = null;
        lightAt = effectAt = holeAt = null; paceDistances = null;
    }
}
