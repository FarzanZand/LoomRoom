using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Builds the main menu's tabletop ruin in the house pixel style: broken walls as solid slabs whose tops step down
// course by course, wrapped in the painted Cellar3 brick wall (Levels/Shared/Room styles/HighRes Materials), loose stone
// chunks cut from the same painting, and the painted Kit pillar (Art/Synty Pixel/Props/SM_Bld_Pillar_02) standing and
// fallen. Walls and rubble take the pillar's texel size, so every pixel on the ruin is the same size.
// Tools > LoomRoom > UI > Rebuild Main Menu Ruins rebuilds the layout under "Ruins" in the open scene from its seeds
// (meshes and the stone material in UI/Main Menu/Ruins).
public static class MainMenuRuins
{
    const string Folder = "Assets/Game/UI/Main Menu/Ruins";
    const string WallTexturePath = "Assets/Game/Levels/Shared/Room styles/HighRes Materials/Cellar3/Cellar3 wall.png";
    const string PillarMeshPath = "Assets/Game/Art/Synty Pixel/Props/SM_Bld_Pillar_02/SM_Bld_Pillar_02 painted.asset";
    const string PillarMaterialPath = "Assets/Game/Art/Synty Pixel/Props/SM_Bld_Pillar_02/SM_Bld_Pillar_02 painted.mat";
    // The pillar's scale on the ruin; its painting is 128 texels per unit at scale 1.
    const float PillarScale = 1.1f;
    static float Texels => 128f / PillarScale;
    // The wall painting: 256 x 192, six courses of 32 texels.
    const float TextureWidth = 256f, TextureHeight = 192f, CourseTexels = 32f;
    static float Course => CourseTexels / Texels;

    static void RebuildMenu()
    {
        var set = GameObject.Find("Set");
        if (set == null) { Debug.LogWarning("Open Scenes/Main Menu first: no Set object."); return; }
        var ruins = set.transform.Find("Ruins");
        if (ruins == null) { ruins = new GameObject("Ruins").transform; ruins.SetParent(set.transform, false); }
        Layout(ruins);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(set.scene);
    }

    // The ruin as in the menu: two broken corners either side of the Dungeon Master's hand, columns, fallen columns
    // and rubble. Local +X runs to the right of the screen, +Z away from the camera; the root sits on the table top.
    public static void Layout(Transform root)
    {
        // Two broken corners, each turned so both of its walls show.
        var left = Group(root, "Left corner", new Vector3(-8.5f, 0f, .5f), 28f);
        Place(Wall(left, "Left front wall", 14, 13, .75f, 11), left, new Vector3(-3.6f, 0f, 0f), 0f);
        Place(Wall(left, "Left side wall", 10, 13, .65f, 12), left, new Vector3(-3.6f, 0f, 0f), -90f);
        var right = Group(root, "Right corner", new Vector3(8.5f, 0f, 1f), -32f);
        Place(Wall(right, "Right front wall", 14, 16, .8f, 13), right, new Vector3(3.6f, 0f, 0f), 180f);
        Place(Wall(right, "Right side wall", 10, 16, .55f, 14), right, new Vector3(3.6f, 0f, 0f), -90f);
        Place(Rubble(root, "Rubble", 55, 7f, 21), root, new Vector3(0f, 0f, -2f), 0f);
        Place(Rubble(root, "Rubble left", 24, 3f, 22), root, new Vector3(-7f, 0f, -2f), 0f);
        Place(Rubble(root, "Rubble right", 24, 3f, 23), root, new Vector3(7f, 0f, -1f), 0f);
        Pillar(root, "Column left", new Vector3(-3.6f, 0f, 1.5f), Quaternion.identity);
        Pillar(root, "Column right", new Vector3(3.6f, 0f, 2f), Quaternion.Euler(0f, 40f, 0f));
        Pillar(root, "Fallen column", new Vector3(-6f, .8f, 1.5f), Quaternion.Euler(0f, 70f, 90f));
        Pillar(root, "Fallen column right", new Vector3(5.5f, .8f, 3f), Quaternion.Euler(0f, -60f, 90f));
    }

    static Transform Group(Transform root, string name, Vector3 position, float yaw)
    {
        var t = root.Find(name);
        if (t == null) { t = new GameObject(name).transform; t.SetParent(root, false); Undo.RegisterCreatedObjectUndo(t.gameObject, "Ruins"); }
        t.localPosition = position; t.localRotation = Quaternion.Euler(0f, yaw, 0f); t.localScale = Vector3.one;
        return t;
    }

    static void Place(GameObject go, Transform root, Vector3 position, float yaw)
    {
        go.transform.SetParent(root, false);
        go.transform.localPosition = position;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = Vector3.one;
    }

    static void Pillar(Transform root, string name, Vector3 position, Quaternion rotation)
    {
        var t = root.Find(name);
        if (t != null && t.GetComponent<MeshFilter>() == null) { Object.DestroyImmediate(t.gameObject); t = null; }   // an old prefab instance
        if (t == null)
        {
            t = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)).transform;
            t.SetParent(root, false);
            Undo.RegisterCreatedObjectUndo(t.gameObject, "Ruins");
        }
        t.GetComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(PillarMeshPath);
        t.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(PillarMaterialPath);
        t.localPosition = position; t.localRotation = rotation; t.localScale = Vector3.one * PillarScale;
    }

    // A straight broken wall along local +X from the origin, `columns` half bricks long and `courses` high at its
    // start, the top falling toward the +X end by `fall` (0-1) in ragged steps of whole courses. One solid slab with
    // the painted brick wall on every face, so the steps follow the painted courses.
    public static GameObject Wall(Transform parent, string name, int columns, int courses, float fall, int seed)
    {
        var random = new System.Random(seed);
        float colWidth = Course;                 // half a brick: 32 texels
        float depth = Course * 1.5f;
        int[] top = new int[columns];
        for (int c = 0; c < columns; c++)
        {
            float t = c / (float)Mathf.Max(1, columns - 1);
            float h = courses * (1f - fall * Mathf.SmoothStep(0f, 1f, t)) + (float)(random.NextDouble() * 2.4 - 1.2);
            top[c] = Mathf.Clamp(Mathf.RoundToInt(h), 1, courses);
        }
        var mesh = new MeshBuilder();
        float z0 = -depth * .5f, z1 = depth * .5f;
        for (int c = 0; c < columns; c++)
        {
            float x0 = c * colWidth, x1 = x0 + colWidth, y = top[c] * Course;
            mesh.Quad(new Vector3(x0, 0, z0), new Vector3(x1, 0, z0), new Vector3(x1, y, z0), new Vector3(x0, y, z0), Vector3.back);
            mesh.Quad(new Vector3(x1, 0, z1), new Vector3(x0, 0, z1), new Vector3(x0, y, z1), new Vector3(x1, y, z1), Vector3.forward);
            mesh.Quad(new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1), Vector3.up);
            // The step down to the next column (or the wall's end), and up from the one before.
            float yNext = c + 1 < columns ? top[c + 1] * Course : 0f;
            if (yNext < y) mesh.Quad(new Vector3(x1, yNext, z0), new Vector3(x1, yNext, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0), Vector3.right);
            float yPrev = c > 0 ? top[c - 1] * Course : 0f;
            if (yPrev < y) mesh.Quad(new Vector3(x0, yPrev, z1), new Vector3(x0, yPrev, z0), new Vector3(x0, y, z0), new Vector3(x0, y, z1), Vector3.left);
        }
        return Build(parent, name, mesh, true);
    }

    // Loose stone chunks scattered over a disc of `radius`, denser toward its centre: small knocked-about blocks with
    // their corners pushed in and out, so no two are the same box.
    public static GameObject Rubble(Transform parent, string name, int count, float radius, int seed)
    {
        var random = new System.Random(seed);
        float R() => (float)random.NextDouble();
        var mesh = new MeshBuilder();
        for (int i = 0; i < count; i++)
        {
            float a = R() * Mathf.PI * 2, r = radius * Mathf.Sqrt(R());
            float s = Course * Mathf.Lerp(.6f, 2.2f, R() * R());
            var size = new Vector3(s * Mathf.Lerp(1f, 1.8f, R()), s * Mathf.Lerp(.55f, 1f, R()), s);
            var rotation = Quaternion.Euler((R() - .5f) * 30f, R() * 360f, (R() - .5f) * 30f);
            var centre = new Vector3(Mathf.Cos(a) * r, size.y * .4f, Mathf.Sin(a) * r);
            var corner = new Vector3[8];
            for (int k = 0; k < 8; k++)
            {
                var c = new Vector3((k & 1) == 0 ? -.5f : .5f, (k & 2) == 0 ? -.5f : .5f, (k & 4) == 0 ? -.5f : .5f);
                c += new Vector3(R() - .5f, R() - .5f, R() - .5f) * .3f;
                corner[k] = centre + rotation * Vector3.Scale(c, size);
            }
            mesh.uvOffset = new Vector2(R() * 8f, R() * 6f);
            mesh.Quad(corner[0], corner[1], corner[3], corner[2], rotation * Vector3.back);
            mesh.Quad(corner[5], corner[4], corner[6], corner[7], rotation * Vector3.forward);
            mesh.Quad(corner[4], corner[0], corner[2], corner[6], rotation * Vector3.left);
            mesh.Quad(corner[1], corner[5], corner[7], corner[3], rotation * Vector3.right);
            mesh.Quad(corner[2], corner[3], corner[7], corner[6], rotation * Vector3.up);
            mesh.Quad(corner[4], corner[5], corner[1], corner[0], rotation * Vector3.down);
        }
        return Build(parent, name, mesh, false);
    }

    // Quads with the brick painting projected on at the ruin's texel size: wall faces in wall space (u along the face,
    // v up) so the courses carry on round corners; tops and bottoms from above.
    class MeshBuilder
    {
        public readonly List<Vector3> vertices = new();
        public readonly List<Vector2> uvs = new();
        public readonly List<int> triangles = new();
        public Vector2 uvOffset;

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            int start = vertices.Count;
            foreach (var p in new[] { a, b, c, d })
            {
                vertices.Add(p);
                Vector2 uv;
                if (Mathf.Abs(normal.y) > .7f) uv = new Vector2(p.x, p.z);
                else if (Mathf.Abs(normal.x) > Mathf.Abs(normal.z)) uv = new Vector2(p.z, p.y);
                else uv = new Vector2(p.x, p.y);
                uvs.Add(new Vector2(uv.x * Texels / TextureWidth, uv.y * Texels / TextureHeight) + uvOffset);
            }
            // Wind both triangles so they face along the normal (Unity's front faces).
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) > 0f) triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            else triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
        }
    }

    static GameObject Build(Transform parent, string name, MeshBuilder built, bool castShadows)
    {
        var mesh = new Mesh { name = name };
        mesh.SetVertices(built.vertices); mesh.SetUVs(0, built.uvs); mesh.SetTriangles(built.triangles, 0);
        mesh.RecalculateNormals();    // flat: no vertex is shared between faces
        mesh.RecalculateBounds();
        System.IO.Directory.CreateDirectory(Folder);
        string path = Folder + "/" + name + ".asset";
        // A new asset each time: a mesh overwritten in place keeps drawing its old data.
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);

        var t = parent.Find(name);
        var go = t != null ? t.gameObject : new GameObject(name);
        if (t == null) { go.transform.SetParent(parent, false); Undo.RegisterCreatedObjectUndo(go, "Ruins"); }
        var filter = go.GetComponent<MeshFilter>();
        if (filter == null) filter = go.AddComponent<MeshFilter>();
        var renderer = go.GetComponent<MeshRenderer>();
        if (renderer == null) renderer = go.AddComponent<MeshRenderer>();
        filter.sharedMesh = mesh;
        renderer.sharedMaterial = StoneMaterial();
        renderer.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    // The ruin's stone: the painted brick wall, as it is.
    public static Material StoneMaterial()
    {
        string path = Folder + "/Ruin stone.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            System.IO.Directory.CreateDirectory(Folder);
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(WallTexturePath));
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Smoothness", .05f);
        m.SetFloat("_EnvironmentReflections", 0f);
        m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        return m;
    }
}
