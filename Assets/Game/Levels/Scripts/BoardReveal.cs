using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// A board-game floor is laid out as it is explored. Everything behind a closed door (tiles, props,
// figures, the doors beyond) is off the table; when a door opens, the rooms and passages it leads to
// are dealt in: tiles drop into place from the door outwards with a little bounce, then props stand
// up and the figures are set down on them. Navigation was built for the whole floor beforehand.
public class BoardReveal : MonoBehaviour
{
    [Tooltip("Seconds between neighbouring tiles landing (by distance from the door, in cells).")]
    public float tileStagger = .032f;
    [Tooltip("Seconds a tile takes to drop into place.")]
    public float tileDrop = .2f;
    [Tooltip("How high above its place a tile starts (world units).")]
    public float dropHeight = 1.4f;
    [Tooltip("Seconds a prop or figure takes to stand up once its tile is down.")]
    public float popSeconds = .16f;
    [Tooltip("A soft light hung over the whole board, like a lamp over a gaming table: every room is lit without torches.")]
    public float boardLightHeight = 34f, boardLightIntensity = 2600f;
    public Color boardLightColor = new(1f, .86f, .68f);
    [Tooltip("Soft taps as tiles land: one every few tiles, quiet and short.")]
    public AudioClip[] tileTaps = new AudioClip[0];
    [Range(0, 1)] public float tapVolume = .22f;
    [Min(1)] public int tilesPerTap = 4;

    DungeonGenerator dungeon;
    Transform[] regions;
    readonly HashSet<int> shown = new();
    readonly Dictionary<int, List<GameObject>> actors = new();
    readonly List<(DungeonDoor door, int a, int b)> doors = new();
    ParticleSystem dust;

    public void Initialize(DungeonGenerator generated)
    {
        dungeon = generated;
        regions = generated.BoardRegions;
        var settings = WorldManager.HasInstance ? WorldManager.Instance.tableLevelReveal : null;
        if (settings != null) dust = settings.dustPrefab;
        if ((tileTaps == null || tileTaps.Length == 0) && generated.LevelData.boardTileTaps != null) tileTaps = generated.LevelData.boardTileTaps;
        var layout = generated.Layout;
        int RegionOf(Vector2Int c) => layout.InBounds(c) && layout.floor[c.x, c.y] ? layout.RegionIds[c.x, c.y] : -1;

        foreach (var (door, a, b) in generated.BoardDoors)
        {
            if (door == null) continue;
            doors.Add((door, RegionOf(a), RegionOf(b)));
            door.Opened += OnOpened;
        }
        // Everything else on the floor belongs to the region it stands in.
        var skip = new HashSet<Transform>();
        foreach (var r in regions) if (r != null) skip.Add(r.parent);
        foreach (var (door, _, _) in doors) skip.Add(door.transform);
        void Collect(Transform parent)
        {
            foreach (Transform child in parent)
            {
                if (skip.Contains(child) || child == generated.Ceilings) continue;
                if (child.name == "Architecture") { Collect(child); continue; }
                int region = NearestRegion(child.position);
                if (region < 0) continue;
                if (!actors.TryGetValue(region, out var list)) actors[region] = list = new List<GameObject>();
                list.Add(child.gameObject);
            }
        }
        Collect(transform);

        HangBoardLight();
        foreach (var region in Reachable()) shown.Add(region);
        for (int i = 0; i < regions.Length; i++)
        {
            bool on = shown.Contains(i);
            if (regions[i] != null) regions[i].gameObject.SetActive(on);
            if (actors.TryGetValue(i, out var list)) foreach (var go in list) if (go != null && !on) go.SetActive(false);
        }
        foreach (var (door, a, b) in doors) door.gameObject.SetActive(shown.Contains(a) || shown.Contains(b));
    }

    // A wide spot straight down over the middle of the board, reaching every corner of it.
    void HangBoardLight()
    {
        var data = dungeon.LevelData;
        float half = Mathf.Max(data.width, data.depth) * data.cellSize * .5f;
        var go = new GameObject("Board light"); go.transform.SetParent(transform, false);
        go.transform.position = transform.position + Vector3.up * boardLightHeight;
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var light = go.AddComponent<Light>();
        light.type = LightType.Spot;
        light.spotAngle = Mathf.Clamp(2f * Mathf.Atan(half * 1.15f / boardLightHeight) * Mathf.Rad2Deg, 30f, 150f);
        light.innerSpotAngle = light.spotAngle * .55f;
        light.range = boardLightHeight * 2.2f;
        light.intensity = boardLightIntensity;
        light.color = boardLightColor;
        light.shadows = LightShadows.Soft;
        FirstPersonLighting.SetLayers(light, uint.MaxValue);
    }

    void OnDestroy()
    {
        foreach (var (door, _, _) in doors) if (door != null) door.Opened -= OnOpened;
    }

    int NearestRegion(Vector3 world)
    {
        var layout = dungeon.Layout;
        var c = dungeon.CellOf(world);
        if (layout.InBounds(c) && layout.floor[c.x, c.y]) return layout.RegionIds[c.x, c.y];
        for (int r = 1; r <= 2; r++)
            for (int dx = -r; dx <= r; dx++) for (int dz = -r; dz <= r; dz++)
            {
                var n = c + new Vector2Int(dx, dz);
                if (layout.InBounds(n) && layout.floor[n.x, n.y]) return layout.RegionIds[n.x, n.y];
            }
        return -1;
    }

    // Regions that can be walked to from the start without passing a closed door.
    HashSet<int> Reachable()
    {
        var layout = dungeon.Layout;
        var closed = new HashSet<(Vector2Int, Vector2Int)>();
        foreach (var (door, a, b) in dungeon.BoardDoors)
            if (door != null && !door.IsOpen) { closed.Add((a, b)); closed.Add((b, a)); }
        var seen = new HashSet<Vector2Int> { layout.Start };
        var queue = new Queue<Vector2Int>(); queue.Enqueue(layout.Start);
        var result = new HashSet<int>();
        var dirs = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            result.Add(layout.RegionIds[c.x, c.y]);
            foreach (var d in dirs)
            {
                var n = c + d;
                if (!layout.InBounds(n) || !layout.floor[n.x, n.y] || seen.Contains(n) || closed.Contains((c, n))) continue;
                seen.Add(n); queue.Enqueue(n);
            }
        }
        return result;
    }

    void OnOpened(DungeonDoor door)
    {
        var origin = door.transform.position;
        foreach (var region in Reachable())
            if (shown.Add(region)) StartCoroutine(Reveal(region, origin));
        foreach (var (d, a, b) in doors)
            if (d != null && !d.gameObject.activeSelf && (shown.Contains(a) || shown.Contains(b))) StartCoroutine(Pop(d.gameObject, .35f));
    }

    // Tiles fall into place outward from the door; what stands on them follows.
    IEnumerator Reveal(int region, Vector3 origin)
    {
        var root = regions[region];
        if (root == null) yield break;
        root.gameObject.SetActive(true);
        float cell = Mathf.Max(.1f, dungeon.LevelData.cellSize);
        int index = 0;
        foreach (Transform tile in root)
        {
            float delay = Vector3.Distance(Flat(tile.position), Flat(origin)) / cell * tileStagger;
            StartCoroutine(Drop(tile, delay, index % 5 == 0, index % Mathf.Max(1, tilesPerTap) == 0));
            index++;
        }
        if (!actors.TryGetValue(region, out var list)) yield break;
        foreach (var go in list)
        {
            if (go == null) continue;
            float delay = Vector3.Distance(Flat(go.transform.position), Flat(origin)) / cell * tileStagger + tileDrop + .05f;
            StartCoroutine(Pop(go, delay));
        }
    }

    static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

    IEnumerator Drop(Transform tile, float delay, bool puff, bool tap)
    {
        var place = tile.position;
        var rotation = tile.rotation;
        var tilt = Quaternion.Euler(Random.Range(-10f, 10f), Random.Range(-6f, 6f), Random.Range(-10f, 10f));
        tile.position = place + Vector3.up * dropHeight; tile.rotation = rotation * tilt;
        SetVisible(tile, false);
        if (delay > 0f) yield return new WaitForSeconds(delay);
        SetVisible(tile, true);
        for (float t = 0f; t < tileDrop; t += Time.deltaTime)
        {
            float k = t / tileDrop;
            tile.position = place + Vector3.up * (dropHeight * (1f - k * k));
            tile.rotation = Quaternion.Slerp(rotation * tilt, rotation, k);
            yield return null;
        }
        // A small bounce as it lands.
        const float bounce = .07f;
        for (float t = 0f; t < bounce; t += Time.deltaTime)
        {
            tile.position = place + Vector3.up * (Mathf.Sin(t / bounce * Mathf.PI) * .05f);
            yield return null;
        }
        tile.SetPositionAndRotation(place, rotation);
        if (tap && tileTaps != null && tileTaps.Length > 0 && AudioManager.HasInstance)
            AudioManager.Instance.PlaySFX(tileTaps[Random.Range(0, tileTaps.Length)], place, tapVolume, .12f);
        if (puff && dust != null)
        {
            var fx = Instantiate(dust, place + Vector3.up * .2f, Quaternion.identity);
            fx.transform.localScale *= .5f;
            Destroy(fx.gameObject, 2f);
        }
    }

    static void SetVisible(Transform root, bool on)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
    }

    // Props stand up from flat; figures are set down with a little drop.
    IEnumerator Pop(GameObject go, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        if (go == null) yield break;
        var t = go.transform;
        var scale = t.localScale;
        bool figure = go.GetComponent<Character>() != null;
        t.localScale = figure ? scale * .2f : new Vector3(scale.x, scale.y * .05f, scale.z);
        go.SetActive(true);
        for (float time = 0f; time < popSeconds; time += Time.deltaTime)
        {
            if (t == null) yield break;
            float k = time / popSeconds;
            float over = 1f + Mathf.Sin(k * Mathf.PI) * .12f; // overshoot, then settle
            t.localScale = figure ? scale * Mathf.Lerp(.2f, 1f, k) * over : new Vector3(scale.x, scale.y * Mathf.Lerp(.05f, 1f, k) * over, scale.z);
            yield return null;
        }
        if (t != null) t.localScale = scale;
    }
}
