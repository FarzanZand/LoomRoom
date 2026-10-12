using UnityEngine;

// A block of the room's own stone: the building piece of hand-built rooms (pillars, platforms, steps, niches, arches,
// galleries). Put blocks inside a DungeonRoomTemplate (or a kit prefab placed in one) and size them; when the room is
// built the block becomes a box with the room style's materials (sides: its wall or trim, top: its floor), textured
// like the walls (one texture repeat per architecture tile, in world metres, so neighbouring blocks and the walls
// line up), with a collider the NavMesh walks on. In Prefab Mode it shows as a coloured box.
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

    // Built by DungeonGenerator.PlaceTemplate: roomHeight is the room's (metres), floorY its floor's world height.
    public void Build(DungeonRoomStyle style, TableLevelData level, float roomHeight, float floorY)
    {
        var s = size;
        if (fromCeiling) { var p = transform.position; p.y = floorY + roomHeight - ceilingGap - (standsOnPosition ? s.y : s.y * .5f); transform.position = p; }
        else if (toCeiling) s.y = Mathf.Max(.1f, roomHeight - ceilingGap - (standsOnPosition ? transform.position.y - floorY : 0f));
        var centre = standsOnPosition ? Vector3.up * s.y * .5f : Vector3.zero;
        float tile = Mathf.Max(.01f, level.architectureTileSize);
        var mesh = BoxMesh(transform, centre, s, tile);
        var filter = GetComponent<MeshFilter>(); if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
        var renderer = GetComponent<MeshRenderer>(); if (renderer == null) renderer = gameObject.AddComponent<MeshRenderer>();
        filter.sharedMesh = mesh;
        renderer.sharedMaterials = new[] { Material(style, level, sides), Material(style, level, top) };
        if (solid)
        {
            var box = GetComponent<BoxCollider>(); if (box == null) box = gameObject.AddComponent<BoxCollider>();
            box.center = centre; box.size = s;
        }
    }

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

    // Prefab Mode: the box in its surface's colour (sides), so a room can be laid out before it is ever generated.
    void OnDrawGizmos()
    {
        var c = sides switch { Surface.Trim => new Color(.75f, .65f, .45f), Surface.Floor => new Color(.5f, .55f, .6f), _ => new Color(.62f, .62f, .66f) };
        Gizmos.matrix = transform.localToWorldMatrix;
        var s = size; var centre = standsOnPosition ? Vector3.up * s.y * .5f : Vector3.zero;
        Gizmos.color = new Color(c.r, c.g, c.b, .55f); Gizmos.DrawCube(centre, s);
        Gizmos.color = c; Gizmos.DrawWireCube(centre, s);
    }
}
