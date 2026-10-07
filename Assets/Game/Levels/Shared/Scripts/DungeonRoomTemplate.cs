using Sirenix.OdinInspector;
using UnityEngine;

// A hand-built room dropped into a generated dungeon: the generator still builds the walls,
// floor, ceiling and doors, then places this prefab at the room centre instead of rolling
// random props and containers.
//
// One prefab can hold the whole room: paint its outline here (Paint Footprint) and pick its
// style and height. Place wall torches with Wall Light sockets, like any other socket. Opened in
// Prefab Mode it shows the room's floor, walls and socket torches in that style (not saved), so
// props can be placed against the real walls. Children without DungeonTemplatePiece are always kept.
//
// Author the prefab around its pivot (the room centre). Keep the centre cross and the
// doorways walkable: any child with DungeonTemplatePiece that lands on a walkway is
// removed at placement. Put enemies, chests, breakables, the boss and the merchant on
// DungeonSocket children.
//
// Reference it from a DungeonRoomProfile (Room Template), a level milestone (Arena) or the
// level's Starting Room. Grown layouts build the room in its footprint, with this prefab's
// pivot on the footprint's centre cell. The room may be turned in quarter turns (never
// mirrored); the prefab turns with it.
[ExecuteAlways]
public class DungeonRoomTemplate : MonoBehaviour
{
    bool UsesAsset => !paintFootprint;

    [Title("Footprint")]
    [Tooltip("Paint the room's outline, pillars and door cells right here instead of referencing a Room Shape asset.")]
    public bool paintFootprint;
    [ShowIf(nameof(UsesAsset)), Tooltip("Grown layouts: the room's outline, pillars and door cells. Empty builds a rectangle of at least Minimum Cells.")]
    public DungeonRoomShape footprint;
    [ShowIf(nameof(paintFootprint)), Tooltip("The generator may turn the room in quarter turns. The prefab turns with it.")]
    public bool allowRotation = true;
    [HideInInspector] public int width = 7, height = 7;
    // rows[0] is the south (bottom) row: see DungeonShapeGrid.
    [HideInInspector] public string[] rows;
    [HideIf(nameof(HasFootprint)), Tooltip("Smallest room, in grid cells, this interior is built for. Smaller rooms still get it, with a warning.")]
    public Vector2Int minimumCells = new Vector2Int(6, 6);

    [Title("Architecture")]
    [Tooltip("Walls, floor and ceiling for this room. Empty keeps the profile's or the level's choice.")]
    public DungeonRoomStyle style;
    [Range(0, 3), Tooltip("Room height in wall tiles. 0 lets the generator choose.")]
    public int heightTiles;

    [Tooltip("A very large skylight over the whole room: only a thin frame of ceiling stays, so the room above shows through. The starting room uses it for the way in.")]
    public bool openCeiling;
    [ShowIf(nameof(openCeiling)), Range(.1f, 2f), Tooltip("Width of the ceiling frame left round the skylight, in world units.")]
    public float ceilingFrame = .35f;

    [Title("Contents")]
    [Tooltip("Skip the generator's random props, supply container and features in this room.")]
    public bool replaceGeneratedProps = true;
    [Tooltip("Spawn enemies only at Enemy sockets. Off also rolls the room's normal encounter.")]
    public bool replaceGeneratedEnemies = true;
    [Tooltip("Keep the generator's room light. Off when the template brings its own lights.")]
    public bool keepRoomLight = true;

    [Title("Lighting")]
    [Tooltip("No random wall lights in this room: only the ones placed with Wall Light sockets.")]
    public bool replaceGeneratedTorches;
    [Tooltip("Recolour every wall light in this room, over the biome's colours. A socket's own colour still wins.")]
    public bool overrideLightColor;
    [ShowIf(nameof(overrideLightColor))]
    public Color lightColor = new Color(1f, .6f, .3f);

    [Title("Preview")]
    [Tooltip("Level whose materials, cell size and wall torch the Prefab Mode preview uses.")]
    public TableLevelData previewLevel;
    [Tooltip("Grid spacing used by the gizmo when there is no preview level. Match the level's Cell Size.")]
    [Min(.5f)] public float previewCellSize = 2f;

    public bool HasFootprint => paintFootprint || footprint != null;

    // The outline as painted, before any turn.
    public DungeonShape Footprint() =>
        paintFootprint ? DungeonShapeGrid.ToShape(width, height, rows, name) : footprint != null ? footprint.ToShape() : null;

    // The outline as the generator places it: maybe turned, never mirrored.
    public DungeonShape FootprintShape(System.Random random) =>
        paintFootprint ? Footprint().Oriented(random, allowRotation, false) : footprint.ToShape(random, false);

    float CellSize => previewLevel != null ? previewLevel.cellSize : previewCellSize;

    void OnDrawGizmos()
    {
        float c = CellSize;
        var shape = HasFootprint ? Footprint() : null;
        if (shape != null)
        {
            // Pivot sits on the centre cell; draw each painted cell around it.
            Gizmos.matrix = transform.localToWorldMatrix;
            for (int x = 0; x < shape.width; x++) for (int y = 0; y < shape.height; y++)
            {
                byte cell = shape[x, y];
                if (cell == DungeonShape.Empty) continue;
                var p = new Vector3((x - shape.center.x) * c, 0, (y - shape.center.y) * c);
                Gizmos.color = cell == DungeonShape.Solid ? new Color(.2f, .2f, .2f, .8f) : shape.DoorAllowed(x, y) ? new Color(1f, .8f, .3f, .5f) : new Color(.4f, .8f, 1f, .25f);
                if (cell == DungeonShape.Solid) Gizmos.DrawCube(p + Vector3.up * c * .5f, new Vector3(c, c, c));
                else Gizmos.DrawCube(p + Vector3.up * .02f, new Vector3(c * .96f, .02f, c * .96f));
            }
            return;
        }
        var size = new Vector3(minimumCells.x * c, .05f, minimumCells.y * c);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(.4f, .8f, 1f, .6f);
        Gizmos.DrawWireCube(Vector3.zero, size);
        // The centre cross stays walkable: doorways connect through it.
        Gizmos.color = new Color(1f, .8f, .3f, .25f);
        Gizmos.DrawCube(Vector3.zero, new Vector3(size.x, .02f, c));
        Gizmos.DrawCube(Vector3.zero, new Vector3(c, .02f, size.z));
    }

    [Button("Snap children to grid"), PropertyTooltip("Rounds every socket and piece to the cell grid.")]
    void Snap()
    {
        float cellSize = CellSize;
        foreach (Transform child in GetComponentsInChildren<Transform>())
        {
            if (child == transform || (child.GetComponent<DungeonSocket>() == null && child.GetComponent<DungeonTemplatePiece>() == null)) continue;
            var p = child.localPosition;
            float half = cellSize * .5f;
            float Snap1(float v, int cells) => (cells % 2 == 0 ? Mathf.Round((v - half) / cellSize) * cellSize + half : Mathf.Round(v / cellSize) * cellSize);
            int cx = HasFootprint ? 1 : minimumCells.x, cz = HasFootprint ? 1 : minimumCells.y; // a footprint pivot sits on a cell centre
            child.localPosition = new Vector3(Snap1(p.x, cx), p.y, Snap1(p.z, cz));
        }
    }

#if UNITY_EDITOR
    // ── Footprint painter ─────────────────────────────────────────────

    [OnInspectorGUI, PropertyOrder(-1), ShowIf(nameof(paintFootprint))]
    void Painter()
    {
        if (DungeonShapeGrid.Painter(this, ref width, ref height, ref rows)) QueuePreview();
    }

    // ── Prefab Mode preview ───────────────────────────────────────────

    const string PreviewName = "Room preview (not saved)";
    // Same numbers as DungeonGenerator: walls sit just outside the cell edge, torches just inside.
    const float WallThickness = .22f;

    void OnEnable() { if (!Application.isPlaying) QueuePreview(); }
    // Moving, turning or retyping a socket rebuilds the preview.
    void Update()
    {
        if (Application.isPlaying || !InPrefabMode) return;
        bool dirty = false;
        foreach (var socket in GetComponentsInChildren<DungeonSocket>(true))
        {
            if (!socket.transform.hasChanged) continue;
            socket.transform.hasChanged = false;
            dirty = true;
        }
        if (dirty) RebuildPreview();
    }
    void OnDisable() { if (!Application.isPlaying) ClearPreview(); }
    void OnValidate()
    {
        if (paintFootprint && (rows == null || rows.Length != height)) rows = DungeonShapeGrid.Resized(rows, width, height);
        if (!Application.isPlaying) QueuePreview();
    }

    void QueuePreview()
    {
        UnityEditor.EditorApplication.delayCall -= RebuildPreview;
        UnityEditor.EditorApplication.delayCall += RebuildPreview;
    }

    bool InPrefabMode => this != null && UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(gameObject) != null;

    void ClearPreview()
    {
        if (this == null) return;
        for (int i = transform.childCount - 1; i >= 0; i--)
            if (transform.GetChild(i).name == PreviewName) DestroyImmediate(transform.GetChild(i).gameObject);
    }

    [Button("Refresh preview"), PropertyOrder(100)]
    void RebuildPreview()
    {
        if (this == null || Application.isPlaying) return;
        ClearPreview();
        if (!InPrefabMode || !HasFootprint) return;
        if (previewLevel == null)
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:TableLevelData"))
            {
                var level = UnityEditor.AssetDatabase.LoadAssetAtPath<TableLevelData>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (level != null && level.kind == TableLevelKind.Dungeon) { previewLevel = level; UnityEditor.EditorUtility.SetDirty(this); break; }
            }
        if (previewLevel == null) return;

        var shape = Footprint();
        var lvl = previewLevel;
        float c = lvl.cellSize, tile = lvl.architectureTileSize, roomHeight = tile * Mathf.Max(1, heightTiles);
        var root = new GameObject(PreviewName) { hideFlags = HideFlags.DontSave };
        root.transform.SetParent(transform, false);
        UnityEditor.SceneVisibilityManager.instance.DisablePicking(root, true);

        var floorMat = DungeonRoomStyle.Resolve(style != null ? style.floor : null, lvl.floorMaterial);
        Material WallMat(float bottom)
        {
            var fallback = bottom < tile - .001f ? lvl.wallMaterial : DungeonRoomStyle.Resolve(lvl.upperWallMaterial, lvl.wallMaterial);
            return style != null ? style.Wall(bottom, tile, fallback) : fallback;
        }
        Vector3 Local(Vector2Int p) => new Vector3((p.x - shape.center.x) * c, 0, (p.y - shape.center.y) * c);
        bool Floor(Vector2Int p) => shape.IsFloor(p.x, p.y);
        // A door cell opens onto the corridor outside it.
        bool Open(Vector2Int cell, Vector2Int d) => Floor(cell + d) || (shape.DoorAllowed(cell.x, cell.y) && shape[cell.x + d.x, cell.y + d.y] == DungeonShape.Empty);
        var dirs = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

        for (int x = 0; x < shape.width; x++) for (int y = 0; y < shape.height; y++)
        {
            var cell = new Vector2Int(x, y);
            if (!Floor(cell)) continue;
            var p = Local(cell);
            Box(root.transform, p + Vector3.down * .12f, new Vector3(c, .24f, c), floorMat);
            foreach (var d in dirs)
            {
                if (Open(cell, d)) continue;
                var outward = new Vector3(d.x, 0, d.y);
                var size = d.x != 0 ? new Vector3(WallThickness, tile, c + WallThickness * 2) : new Vector3(c + WallThickness * 2, tile, WallThickness);
                for (float b = 0; b < roomHeight - .01f; b += tile)
                    Box(root.transform, p + outward * (c * .5f + WallThickness * .5f) + Vector3.up * (b + tile * .5f), size, WallMat(b));
            }
        }

        // Wall Light sockets, snapped to the wall they face as the generator does.
        foreach (var socket in GetComponentsInChildren<DungeonSocket>(true))
        {
            if (socket.type != DungeonSocketType.WallLight) continue;
            var entry = socket.overridePrefab != null ? null : DungeonWallLight.First(lvl.lightingPrefabs);
            var prefab = entry != null ? entry.prefab : socket.overridePrefab;
            if (prefab == null) continue;
            Color? tint = entry?.Tint;
            if (overrideLightColor) tint = lightColor;
            if (socket.overrideLightColor) tint = socket.lightColor;
            var lp = transform.InverseTransformPoint(socket.transform.position);
            var cell = new Vector2Int(Mathf.RoundToInt(lp.x / c) + shape.center.x, Mathf.RoundToInt(lp.z / c) + shape.center.y);
            if (!Floor(cell)) continue;
            var f = transform.InverseTransformDirection(socket.transform.forward);
            var facing = Mathf.Abs(f.x) >= Mathf.Abs(f.z) ? new Vector2Int(f.x >= 0 ? 1 : -1, 0) : new Vector2Int(0, f.z >= 0 ? 1 : -1);
            foreach (var d in new[] { facing, new Vector2Int(-facing.y, facing.x), new Vector2Int(facing.y, -facing.x), -facing })
            {
                if (Open(cell, d)) continue;
                var outward = new Vector3(d.x, 0, d.y);
                var torch = (GameObject)Instantiate(prefab, root.transform);
                torch.transform.localPosition = Local(cell) + outward * (c * .5f - WallThickness * .5f);
                torch.transform.localRotation = Quaternion.LookRotation(-outward);
                if (tint.HasValue) DungeonWallLight.Apply(torch, tint.Value);
                break;
            }
        }

        foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
    }

    static void Box(Transform parent, Vector3 localPos, Vector3 size, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
    }
#endif
}
