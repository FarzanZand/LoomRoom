using System.Collections.Generic;
using UnityEngine;

// The light-up dance floor over a boss stage (BossFight > Phase two > Dance Floor, made by BossStageEncounter in the
// second phase): a 1 m tile on every stretch of the stage's top (not over the stair's cut or past its edge), 1.2 cm over
// the floor, one mesh. Each beat of the music the tiles are dealt again: about half light up, each in one of the light
// show's colours, flashing bright on the beat and settling before the next; the rest glow faintly. It fades in as it
// starts and goes dark at once when the party is over (Off).
public class BossDanceFloor : MonoBehaviour
{
    [Min(.25f)] public float tileSize = 1f;
    [Range(0, 1), Tooltip("Share of the tiles lit each beat.")] public float litShare = .34f;
    [Min(0), Tooltip("A lit tile's brightness on the beat, and what it settles to before the next.")] public float flash = 1.5f, settle = .45f;
    [Min(0), Tooltip("How fast a beat's flash settles (higher: a sharper flash).")] public float falloff = 3.5f;
    [Min(0), Tooltip("Unlit tiles' glow.")] public float unlit = 0f;
    [Min(0)] public float fadeInSeconds = 1.2f, fadeOutSeconds = .15f;

    Mesh mesh;
    Color[] palette, colours;
    readonly List<int> tileColour = new();
    readonly List<bool> tileLit = new();
    int tiles, step = int.MinValue;
    float beat = -1f, level, target = 1f;

    public static BossDanceFloor Build(DungeonGenerator dungeon, Material material, Color[] colours)
    {
        if (dungeon == null || dungeon.Stage == null || dungeon.StageRoom < 0 || material == null) return null;
        var go = new GameObject("Dance floor");
        go.transform.SetParent(dungeon.transform, false);
        var floor = go.AddComponent<BossDanceFloor>();
        floor.palette = colours != null && colours.Length > 0 ? colours : new[] { Color.white };
        floor.Make(dungeon, material);
        return floor;
    }

    void Make(DungeonGenerator dungeon, Material material)
    {
        float cell = dungeon.LevelData.cellSize, top = dungeon.StageTop;
        int split = Mathf.Max(1, Mathf.RoundToInt(cell / tileSize));
        float size = cell / split, half = size * .5f;
        var vertices = new List<Vector3>(); var uvs = new List<Vector2>(); var triangles = new List<int>();
        foreach (var p in dungeon.Layout.RoomCells(dungeon.StageRoom))
        {
            var c = dungeon.Cell(p);
            for (int x = 0; x < split; x++)
                for (int z = 0; z < split; z++)
                {
                    var centre = new Vector3(c.x - cell * .5f + half + x * size, top, c.z - cell * .5f + half + z * size);
                    // Only where the stage's top is there under the whole tile: not over the stair's cut, nor past an edge.
                    if (!Physics.Raycast(centre + Vector3.up * .45f, Vector3.down, out var hit, 1.2f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (Mathf.Abs(hit.point.y - top) > .2f || hit.collider.GetComponentInParent<Character>() != null) continue;
                    bool whole = true;
                    foreach (var corner in new[] { new Vector3(-.45f, 0, -.45f), new Vector3(-.45f, 0, .45f), new Vector3(.45f, 0, .45f), new Vector3(.45f, 0, -.45f) })
                        if (!Physics.Raycast(centre + corner * size + Vector3.up * .45f, Vector3.down, out var under, 1.2f, ~0, QueryTriggerInteraction.Ignore) || Mathf.Abs(under.point.y - hit.point.y) > .05f) { whole = false; break; }
                    if (!whole) continue;
                    float y = hit.point.y + .012f;
                    int v = vertices.Count;
                    // A hair inside its square, so neighbouring tiles never share an edge.
                    float h = half - .004f;
                    foreach (var corner in new[] { new Vector3(-h, 0, -h), new Vector3(-h, 0, h), new Vector3(h, 0, h), new Vector3(h, 0, -h) })
                        vertices.Add(transform.InverseTransformPoint(new Vector3(centre.x, y, centre.z) + corner));
                    uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(1, 0));
                    triangles.AddRange(new[] { v, v + 1, v + 2, v, v + 2, v + 3 });
                    tileColour.Add(0); tileLit.Add(false);
                }
        }
        tiles = tileColour.Count;
        mesh = new Mesh { name = "Dance floor" };
        mesh.SetVertices(vertices); mesh.SetUVs(0, uvs); mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        colours = new Color[vertices.Count];
        mesh.colors = colours;
        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // The music's beat now (fractional), or below 0 with no music (the floor then steps twice a second).
    public void Beat(float beats) => beat = beats;

    public void Off() => target = 0f;

    void Update()
    {
        if (mesh == null) return;
        level = Mathf.MoveTowards(level, target, Time.deltaTime / Mathf.Max(.01f, target > level ? fadeInSeconds : fadeOutSeconds));
        float steps = beat >= 0f ? beat : Time.time * 2f;
        int now = Mathf.FloorToInt(steps);
        if (now != step)
        {
            step = now;
            // Dealt again: never the same colour twice running on a tile, and no tile lit two beats in a row more than by chance.
            for (int i = 0; i < tiles; i++)
            {
                tileLit[i] = Random.value < litShare;
                int k = Random.Range(0, palette.Length - 1);
                tileColour[i] = palette.Length > 1 && k >= tileColour[i] ? k + 1 : k;
            }
        }
        float frac = steps - now;
        float lit = settle + (flash - settle) * Mathf.Exp(-frac * falloff);
        for (int i = 0; i < tiles; i++)
        {
            var c = palette[tileColour[i] % palette.Length] * (tileLit[i] ? lit : unlit) * level;
            c.a = 1f;
            for (int v = 0; v < 4; v++) colours[i * 4 + v] = c;
        }
        mesh.colors = colours;
    }

    void OnDestroy() { if (mesh != null) Destroy(mesh); }
}
