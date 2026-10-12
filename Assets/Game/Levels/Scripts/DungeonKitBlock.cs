using UnityEngine;

// A block of the room's own stone: the building piece of hand-built rooms (pillars, platforms, steps, niches, arches,
// galleries). Put blocks inside a DungeonRoomTemplate (or a kit prefab placed in one) and size them; when the room is
// built the block becomes a box with the room style's materials (sides: its wall or trim, top: its floor), textured
// like the walls (one texture repeat per architecture tile, in world metres, so neighbouring blocks and the walls
// line up), with a collider the NavMesh walks on. In the editor it shows its shape in the level's default stone (an
// EditorOnly "Kit preview" child, so prefab thumbnails show it too); the preview is removed when the room is built.
public class DungeonKitBlock : MonoBehaviour
{
    public enum Surface { Wall = 0, Trim = 1, Floor = 2, UpperWall = 3, Ceiling = 4 }

    [Tooltip("Width, height and depth in metres, centred on this object (the base sits at its y unless Centred).")]
    public Vector3 size = new(.8f, 3f, .8f);
    [Tooltip("The base is at this object's position (blocks stand on the floor). Off: the box is centred on it.")]
    public bool standsOnPosition = true;
    [Tooltip("Grow up to the room's ceiling (minus Ceiling Gap): pillars in rooms of any height.")]
    public bool toCeiling;
    [Tooltip("Hang under the room's ceiling (minus Ceiling Gap) instead of standing where it is: a pillar's capital, a beam.")]
    public bool fromCeiling;
    [Min(0f)] public float ceilingGap;
    public Surface sides = Surface.Wall;
    public Surface top = Surface.Floor;
    [Tooltip("Walkable and blocking (a platform, a step, a pillar). Off for trims high up.")]
    public bool solid = true;

    Mesh built;   // made per floor build, freed with the floor
    void OnDestroy() { if (built != null) Destroy(built); }

    // Built by DungeonGenerator.PlaceTemplate: roomHeight is the room's (metres), floorY its floor's world height.
    public void Build(DungeonRoomStyle style, TableLevelData level, float roomHeight, float floorY)
    {
        var preview = transform.Find(PreviewName);
        if (preview != null) DestroyImmediate(preview.gameObject);
        var s = size;
        if (fromCeiling) { var p = transform.position; p.y = floorY + roomHeight - ceilingGap - (standsOnPosition ? s.y : s.y * .5f); transform.position = p; }
        else if (toCeiling) s.y = Mathf.Max(.1f, roomHeight - ceilingGap - (standsOnPosition ? transform.position.y - floorY : 0f));
        var centre = standsOnPosition ? Vector3.up * s.y * .5f : Vector3.zero;
        float tile = Mathf.Max(.01f, level.architectureTileSize);
        var mesh = BoxMesh(transform, centre, s, tile);
        var filter = GetComponent<MeshFilter>(); if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
        var renderer = GetComponent<MeshRenderer>(); if (renderer == null) renderer = gameObject.AddComponent<MeshRenderer>();
        if (built != null) Destroy(built);
        filter.sharedMesh = built = mesh;
        renderer.sharedMaterials = new[] { Material(style, level, sides), Material(style, level, top) };
        if (solid)
        {
            var box = GetComponent<BoxCollider>(); if (box == null) box = gameObject.AddComponent<BoxCollider>();
            box.center = centre; box.size = s;
        }
    }

    public const string PreviewName = "Kit preview";

#if UNITY_EDITOR
    // ── Editor preview ──────────────────────────────────────────────

    const string PreviewMeshPath = "Assets/Game/Levels/Dungeon1/Rooms/Kit/Kit preview cube.asset";
    const string PreviewLevelPath = "Assets/Game/Levels/Dungeon1/Dungeon1.asset";

    void OnValidate()
    {
        if (Application.isPlaying || UnityEditor.PrefabUtility.IsPartOfPrefabAsset(this)) return;
        UnityEditor.EditorApplication.delayCall += () => { if (this != null && !Application.isPlaying) UpdatePreview(); };
    }

    // The block's shape as it would be in a room of the template's height, in the level's default materials. A block a
    // piece's model stands in for shows no preview (the model shows instead).
    public void UpdatePreview()
    {
        var existing = transform.Find(PreviewName);
        var piece = GetComponentInParent<DungeonKitPiece>(true);
        if (piece != null && piece.model != null && piece.Replaces(transform))
        {
            if (existing != null) DestroyImmediate(existing.gameObject);
            return;
        }
        var level = UnityEditor.AssetDatabase.LoadAssetAtPath<TableLevelData>(PreviewLevelPath);
        var mesh = PreviewMesh();
        if (level == null || mesh == null) return;
        var template = GetComponentInParent<DungeonRoomTemplate>(true);
        float tile = Mathf.Max(.01f, level.architectureTileSize);
        float roomHeight = tile * Mathf.Max(1, template != null ? template.heightTiles : 1);
        float floorY = template != null ? template.transform.position.y : transform.root.position.y;
        var s = size; float lift = 0f;
        if (fromCeiling) lift = floorY + roomHeight - ceilingGap - (standsOnPosition ? s.y : s.y * .5f) - transform.position.y;
        else if (toCeiling) s.y = Mathf.Max(.1f, roomHeight - ceilingGap - (standsOnPosition ? transform.position.y - floorY : 0f));
        var go = existing != null ? existing.gameObject : new GameObject(PreviewName, typeof(MeshFilter), typeof(MeshRenderer));
        go.tag = "EditorOnly";
        go.transform.SetParent(transform, false);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = new Vector3(s.x / Mathf.Max(.0001f, 1f), s.y, s.z);
        go.transform.localPosition = (standsOnPosition ? Vector3.up * s.y * .5f : Vector3.zero) + Vector3.up * lift;
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterials = new[] { Material(null, level, sides), Material(null, level, top) };
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // A unit cube with the block's two submeshes (sides, top and bottom), saved once next to the kit.
    static Mesh PreviewMesh()
    {
        var mesh = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>(PreviewMeshPath);
        if (mesh != null) return mesh;
        var holder = new GameObject("tmp") { hideFlags = HideFlags.HideAndDontSave };
        mesh = BoxMesh(holder.transform, Vector3.zero, Vector3.one, 1f);
        DestroyImmediate(holder);
        // Per face 0..1, not world tiles: the cube is scaled per block.
        var uv = new Vector2[mesh.vertexCount];
        for (int i = 0; i < uv.Length; i += 4) { uv[i] = new Vector2(0, 0); uv[i + 1] = new Vector2(1, 0); uv[i + 2] = new Vector2(1, 1); uv[i + 3] = new Vector2(0, 1); }
        mesh.uv = uv; mesh.name = "Kit preview cube";
        UnityEditor.AssetDatabase.CreateAsset(mesh, PreviewMeshPath);
        return mesh;
    }
#endif

    static Material Material(DungeonRoomStyle style, TableLevelData level, Surface surface) => surface switch
    {
        Surface.Trim => DungeonRoomStyle.Resolve(style != null ? style.trim : null, level.trimMaterial),
        Surface.Floor => DungeonRoomStyle.Resolve(style != null ? style.floor : null, level.floorMaterial),
        Surface.UpperWall => DungeonRoomStyle.Resolve(style != null ? style.upperWall : null, DungeonRoomStyle.Resolve(level.upperWallMaterial, level.wallMaterial)),
        Surface.Ceiling => DungeonRoomStyle.Resolve(style != null ? style.ceiling : null, level.ceilingMaterial),
        _ => DungeonRoomStyle.Resolve(style != null ? style.bottomWall : null, level.wallMaterial),
    };

    // A box with two submeshes (sides, then top and bottom); each face's UVs are its world position in tiles, so the
    // texture runs on across neighbouring blocks like across wall segments.
    static Mesh BoxMesh(Transform t, Vector3 centre, Vector3 size, float tile)
    {
        var v = new System.Collections.Generic.List<Vector3>(); var n = new System.Collections.Generic.List<Vector3>(); var uv = new System.Collections.Generic.List<Vector2>();
        var sideTris = new System.Collections.Generic.List<int>(); var capTris = new System.Collections.Generic.List<int>();
        var h = size * .5f;
        void Face(Vector3 normal, Vector3 right, Vector3 up, float w, float hgt, bool cap)
        {
            int start = v.Count;
            var mid = centre + Vector3.Scale(normal, h);
            for (int i = 0; i < 4; i++)
            {
                float a = i == 1 || i == 2 ? 1 : -1, b = i >= 2 ? 1 : -1;
                var local = mid + right * (a * w * .5f) + up * (b * hgt * .5f);
                v.Add(local); n.Add(normal);
                var world = t.TransformPoint(local); var wn = t.TransformDirection(normal);
                // World-axis projection: the face's two other world axes, in tiles.
                Vector2 p = Mathf.Abs(wn.y) > .5f ? new Vector2(world.x, world.z) : Mathf.Abs(wn.x) > .5f ? new Vector2(world.z * Mathf.Sign(wn.x), world.y) : new Vector2(-world.x * Mathf.Sign(wn.z), world.y);
                uv.Add(p / tile);
            }
            var list = cap ? capTris : sideTris;
            list.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
        }
        Face(Vector3.forward, Vector3.left, Vector3.up, size.x, size.y, false);
        Face(Vector3.back, Vector3.right, Vector3.up, size.x, size.y, false);
        Face(Vector3.right, Vector3.forward, Vector3.up, size.z, size.y, false);
        Face(Vector3.left, Vector3.back, Vector3.up, size.z, size.y, false);
        Face(Vector3.up, Vector3.right, Vector3.forward, size.x, size.z, true);
        Face(Vector3.down, Vector3.left, Vector3.forward, size.x, size.z, true);
        var mesh = new Mesh { name = "Kit block" };
        mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv);
        mesh.subMeshCount = 2; mesh.SetTriangles(sideTris, 0); mesh.SetTriangles(capTris, 1);
        mesh.RecalculateBounds(); mesh.RecalculateTangents();
        return mesh;
    }

    // Selected: its authored size as a wire box (the preview shows the shape).
    void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        var s = size; var centre = standsOnPosition ? Vector3.up * s.y * .5f : Vector3.zero;
        Gizmos.color = new Color(1f, .8f, .3f); Gizmos.DrawWireCube(centre, s);
    }
}
