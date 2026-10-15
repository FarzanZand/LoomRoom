using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Room furniture in the room's own style: plank textures made like the bedroom's tabletop (Tools/furniture_wood.py:
// real painted planks laid in rows; walnut and oak), the door's pale trim, brass and iron, box-projected at the room's
// texel size (PixelRestyle.RoomTexelsPerUnit, 128 per metre), so every piece has the room's pixels. Each triangle takes a tile by its colour on the Synty original
// (dark wood walnut, mid wood oak, light wood the pale trim, gold brass, grey iron; anything else a plain tile of its own
// colour), and wood grain runs along each piece's long side. Painted AI textures are not used.
// Run on scene objects: their meshes and materials become prefab overrides, like the room restyles.
public static class RoomTiles
{
    public const string Dir = "Assets/Game/World/Art/Bedroom/Furniture";
    enum Tile { Walnut, Oak, Pale, Brass, Iron, Plain }
    // Texture, size in texels, and whether its grain runs along u (the plank rows) rather than v (the door trim).
    static readonly Dictionary<Tile, (string texture, int size, bool grainAlongU)> Tiles = new()
    {
        { Tile.Pale, ("Assets/Game/World/Art/Bedroom/Door trim.png", 64, false) },
        { Tile.Brass, ($"{Dir}/Brass.png", 16, false) },
        { Tile.Iron, ($"{Dir}/Iron.png", 16, false) },
    };
    const int PlainSize = 16;

    // Each piece of furniture its own wood (Tools/furniture_wood.py), so the room's pieces don't all look alike; doors
    // and drawers take the same wood's lighter "fronts" grade. Matched by the start of the Synty mesh name.
    static readonly (string mesh, string wood)[] Woods =
    {
        ("SM_Prop_Wardrobe_02", "walnut"),
        ("SM_Prop_Cabinet_02", "cherry"),
        ("SM_Prop_Cabinet_03", "cherry"),
        ("SM_Prop_Side_Table_02", "cherry"),
        ("SM_Prop_Desk_01", "smoked oak"),
        ("SM_Prop_DiningChair_01", "mahogany"),
        ("SM_Prop_Animal_Head_Backing_05", "mahogany"),
    };
    const string DefaultWood = "walnut";
    static string wood = DefaultWood;
    static string WoodFor(string baseName)
    {
        foreach (var (mesh, w) in Woods) if (baseName.StartsWith(mesh)) return w;
        return DefaultWood;
    }

    // Every renderer under the object.
    public static string Apply(GameObject target)
    {
        if (target == null) return "nothing";
        var log = new System.Text.StringBuilder();
        foreach (var r in target.GetComponentsInChildren<MeshRenderer>(true)) log.Append(ApplyTo(r)).Append("; ");
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(target.scene);
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    static string BaseName(Mesh m)
    {
        // Synty mesh names have no spaces: everything after the first one is ours (" painted", " (Synty Pixel)",
        // " PolygonTown_01_A", the saved asset's "<object> <mesh>" name).
        string n = m.name; int cut = n.IndexOf(' ');
        return cut > 0 ? n.Substring(0, cut) : n;
    }

    public static string ApplyTo(MeshRenderer r)
    {
        var mf = r.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return r.name + ": no mesh";
        string baseName = BaseName(mf.sharedMesh);
        wood = WoodFor(baseName);
        if (!Source(baseName, mf.sharedMesh.triangles.Length / 3, out var src, out var srcMats)) return r.name + ": no Synty source for " + baseName;
        var v = src.vertices; var n = src.normals; var t = src.triangles; int nt = t.Length / 3;
        var colours = PixelRestyle.SyntyFaceColours(src, srcMats, nt);
        if (colours == null) return r.name + ": no Synty colours";
        var rot = r.transform.rotation; var scale = r.transform.lossyScale;
        Vector3 W(int i) => rot * Vector3.Scale(v[i], scale);

        // Tile per flat face: triangles sharing an edge in one plane are one face and take the tile most of its area
        // has (Synty colours a quad's two triangles from different spots of its atlas; a panel came out half walnut,
        // half oak, split corner to corner).
        var tile = new Tile[nt]; var faceNormal = new Vector3[nt];
        {
            var parentF = new int[nt]; var area = new float[nt]; var fnrm = new Vector3[nt];
            for (int f = 0; f < nt; f++)
            {
                parentF[f] = f;
                var c = Vector3.Cross(v[t[f * 3 + 1]] - v[t[f * 3]], v[t[f * 3 + 2]] - v[t[f * 3]]);
                area[f] = c.magnitude; fnrm[f] = area[f] > 0 ? c / area[f] : Vector3.zero;
            }
            int FindF(int x) { while (parentF[x] != x) x = parentF[x] = parentF[parentF[x]]; return x; }
            // Triangles in one plane whose bounds touch are one face. Synty's panels are not always welded (the two
            // halves of a side meet without sharing corners), so shared edges alone left them split.
            var tb = new Bounds[nt]; float size = 0f;
            for (int f = 0; f < nt; f++)
            {
                tb[f] = new Bounds(v[t[f * 3]], Vector3.zero); tb[f].Encapsulate(v[t[f * 3 + 1]]); tb[f].Encapsulate(v[t[f * 3 + 2]]);
                size = Mathf.Max(size, tb[f].size.magnitude);
            }
            float eps = size * .01f;
            for (int f = 0; f < nt; f++)
            {
                if (area[f] <= 0) continue;
                float df = Vector3.Dot(fnrm[f], v[t[f * 3]]);
                var bf = tb[f]; bf.Expand(eps);
                for (int o = f + 1; o < nt; o++)
                {
                    if (area[o] <= 0 || Vector3.Dot(fnrm[f], fnrm[o]) < .97f) continue;
                    if (Mathf.Abs(Vector3.Dot(fnrm[f], v[t[o * 3]]) - df) > eps * 3f) continue;
                    if (!bf.Intersects(tb[o])) continue;
                    parentF[FindF(f)] = FindF(o);
                }
            }
            var votes = new Dictionary<int, float[]>();
            for (int f = 0; f < nt; f++)
            {
                int g = FindF(f); if (!votes.TryGetValue(g, out var vt)) votes[g] = vt = new float[6];
                vt[(int)Classify(colours[f])] += area[f];
            }
            // One normal per flat face (area-weighted): Synty's panels are two triangles a few degrees apart, and shaded
            // apart they showed a dark diagonal across every cabinet side.
            var sum = new Dictionary<int, Vector3>();
            for (int f = 0; f < nt; f++) { int g = FindF(f); sum[g] = (sum.TryGetValue(g, out var a) ? a : Vector3.zero) + fnrm[f] * area[f]; }
            for (int f = 0; f < nt; f++)
            {
                var vt = votes[FindF(f)]; int best = 0; for (int i = 1; i < 6; i++) if (vt[i] > vt[best]) best = i;
                tile[f] = (Tile)best;
                // Box sides are often folded a few degrees corner to corner: a normal close to a world axis is
                // snapped to it, so the whole side lights as one.
                var wn = rot * sum[FindF(f)].normalized; var aw = new Vector3(Mathf.Abs(wn.x), Mathf.Abs(wn.y), Mathf.Abs(wn.z));
                float most = Mathf.Max(aw.x, Mathf.Max(aw.y, aw.z));
                if (most > .9f) wn = aw.x == most ? new Vector3(Mathf.Sign(wn.x), 0, 0) : aw.y == most ? new Vector3(0, Mathf.Sign(wn.y), 0) : new Vector3(0, 0, Mathf.Sign(wn.z));
                faceNormal[f] = (Quaternion.Inverse(rot) * wn).normalized;
            }
        }

        // Pieces: triangles of one tile joined by shared corners; each knows the world axis it is longest along.
        var key = new Dictionary<Vector3Int, int>(); var vid = new int[v.Length];
        for (int i = 0; i < v.Length; i++) { var k = Vector3Int.RoundToInt(v[i] * 1e5f); if (!key.TryGetValue(k, out var id)) { id = key.Count; key[k] = id; } vid[i] = id; }
        var parent = new int[key.Count]; for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        var cornerTile = new Dictionary<int, Tile>();
        for (int f = 0; f < nt; f++)
            for (int e = 1; e < 3; e++) { int a = Find(vid[t[f * 3]]), b = Find(vid[t[f * 3 + e]]); if (a != b) parent[a] = b; }
        var pieceBounds = new Dictionary<int, Bounds>();
        for (int f = 0; f < nt; f++)
        {
            int p = Find(vid[t[f * 3]]);
            for (int e = 0; e < 3; e++) { var w = W(t[f * 3 + e]); if (pieceBounds.TryGetValue(p, out var b)) { b.Encapsulate(w); pieceBounds[p] = b; } else pieceBounds[p] = new Bounds(w, Vector3.zero); }
        }
        int LongAxis(int p) { var s = pieceBounds[p].size; return s.x >= s.y && s.x >= s.z ? 0 : s.y >= s.z ? 1 : 2; }

        // One wood per piece of furniture: Synty mixes dark and mid wood across a panel and its recessed half, and
        // two plank textures side by side read as a diagonal split. All wood takes the piece's wood (WoodFor).
        {
            // Walnut here means the piece's body wood, Oak its lighter fronts grade: doors and drawers read as panels.
            var grade = baseName.Contains("_Door") || baseName.Contains("_Drawer") ? Tile.Oak : Tile.Walnut;
            for (int f = 0; f < nt; f++) if (tile[f] == Tile.Walnut || tile[f] == Tile.Oak) tile[f] = grade;
        }

        // The room is lightmapped: the baked light was laid out on the restyled mesh's UVs (its second set when it has
        // one), so each corner keeps its coordinates there as the new mesh's second UV set. Without them the lightmap was
        // read through the plank UVs and every cabinet side showed a dark diagonal.
        Mesh lit = AssetDatabase.LoadAssetAtPath<Mesh>($"{PixelRestyle.Root}/Room/{baseName}/{baseName} painted.asset")
                   ?? AssetDatabase.LoadAssetAtPath<Mesh>($"{PixelRestyle.Root}/Room/{baseName}/{baseName}.asset");
        List<Vector2> litUv = null; int[] litTris = null;
        if (lit != null && lit.triangles.Length == t.Length)
        {
            litUv = new List<Vector2>(); lit.GetUVs(1, litUv); if (litUv.Count == 0) lit.GetUVs(0, litUv);
            litTris = lit.triangles;
        }
        var nl = new List<Vector2>();
        // One vertex per triangle corner, one submesh per material.
        var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nu = new List<Vector2>();
        var subs = new List<List<int>>(); var mats = new List<Material>(); var matIndex = new Dictionary<Material, int>();
        for (int f = 0; f < nt; f++)
        {
            var mat = MaterialFor(tile[f], colours[f], out int size, out bool grainU);
            if (!matIndex.TryGetValue(mat, out int sub)) { sub = mats.Count; matIndex[mat] = sub; mats.Add(mat); subs.Add(new List<int>()); }
            Vector3 a = W(t[f * 3]), b = W(t[f * 3 + 1]), c = W(t[f * 3 + 2]);
            var fn = Vector3.Cross(b - a, c - a); var an = new Vector3(Mathf.Abs(fn.x), Mathf.Abs(fn.y), Mathf.Abs(fn.z));
            int axis = an.y >= an.x && an.y >= an.z ? 1 : an.x >= an.z ? 0 : 2;
            int uAxis = axis == 1 ? 0 : axis == 0 ? 2 : 0, vAxis = axis == 1 ? 2 : 1;
            int longAxis = LongAxis(Find(vid[t[f * 3]]));
            // The grain follows the piece's long side: a tile whose grain runs along u is turned when the piece is long
            // along v, and the other way round.
            bool turn = grainU ? longAxis == vAxis : longAxis == uAxis;
            for (int e = 0; e < 3; e++)
            {
                int i = t[f * 3 + e]; var w = W(i) * PixelRestyle.RoomTexelsPerUnit / size;
                var q = new Vector2(w[uAxis], w[vAxis]);
                subs[sub].Add(nv.Count); nv.Add(v[i]); nn.Add(faceNormal[f]);
                if (litUv != null && litUv.Count > 0) nl.Add(litUv[litTris[f * 3 + e]]);
                nu.Add(turn ? new Vector2(q.y, q.x) : q);
            }
        }
        var mesh = new Mesh { name = baseName + " room tiles", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(nv); mesh.SetNormals(nn); mesh.SetUVs(0, nu); if (nl.Count == nv.Count) mesh.SetUVs(1, nl); mesh.subMeshCount = subs.Count;
        for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s);
        mesh.RecalculateBounds(); mesh.RecalculateTangents();
        Directory.CreateDirectory(Dir);
        string meshPath = $"{Dir}/{r.name} {baseName}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (existing != null) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); mesh = existing; }
        else AssetDatabase.CreateAsset(mesh, meshPath);
        PixelRestyle.MarkReadable(mesh);

        Undo.RecordObject(mf, "Room tiles"); Undo.RecordObject(r, "Room tiles");
        mf.sharedMesh = mesh; r.sharedMaterials = mats.ToArray();
        foreach (var mc in r.GetComponents<MeshCollider>()) { Undo.RecordObject(mc, "Room tiles"); mc.sharedMesh = mesh; if (PrefabUtility.IsPartOfPrefabInstance(mc)) PrefabUtility.RecordPrefabInstancePropertyModifications(mc); }
        if (PrefabUtility.IsPartOfPrefabInstance(mf)) { PrefabUtility.RecordPrefabInstancePropertyModifications(mf); PrefabUtility.RecordPrefabInstancePropertyModifications(r); }
        var counts = new int[6]; foreach (var x in tile) counts[(int)x]++;
        return $"{r.name}: walnut {counts[0]} oak {counts[1]} pale {counts[2]} brass {counts[3]} iron {counts[4]} plain {counts[5]}";
    }

    // The Synty mesh and materials a restyled mesh was made from. A cabinet's doors and drawers are meshes inside the
    // cabinet's model file with no prefab of their own: found by name and triangle count, with the cabinet's materials.
    static bool Source(string baseName, int triangles, out Mesh mesh, out Material[] mats)
    {
        if (PixelRestyle.SyntySource(baseName, out mesh, out mats) && mesh != null && mesh.triangles.Length / 3 == triangles) return true;
        mesh = null; mats = null;
        foreach (var g in AssetDatabase.FindAssets(baseName + " t:Mesh", new[] { "Assets/Synty" }))
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)))
                if (a is Mesh m && m.name == baseName && m.triangles.Length / 3 == triangles) { mesh = m; break; }
        if (mesh == null) return false;
        // The owner's materials: the name with its last "_Part_N" taken off.
        string owner = System.Text.RegularExpressions.Regex.Replace(baseName, @"_[A-Za-z]+_\d+$", "");
        return PixelRestyle.SyntySource(owner, out _, out mats) && mats != null;
    }

    // Wood by how dark it is, gold as brass, greys as iron, anything else its own colour.
    static Tile Classify(Color c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float val);
        float deg = h * 360f;
        if (s < .18f) return val > .2f && val < .75f ? Tile.Iron : Tile.Plain;
        if (deg >= 36f && deg <= 60f && s > .35f && val > .55f) return Tile.Brass;
        if ((deg <= 45f || deg >= 345f) && s > .2f) return val < .33f ? Tile.Walnut : val < .6f ? Tile.Oak : Tile.Pale;
        return Tile.Plain;
    }

    static readonly Dictionary<string, Material> cache = new();
    static Material MaterialFor(Tile tile, Color colour, out int size, out bool grainU)
    {
        string name; string texPath; grainU = false;
        if (tile == Tile.Plain)
        {
            size = PlainSize;
            string hex = ColorUtility.ToHtmlStringRGB(colour);
            name = "Plain " + hex; texPath = $"{Dir}/{name}.png";
            if (!File.Exists(texPath)) File.WriteAllBytes(texPath, PlainTile(colour, hex.GetHashCode()));
        }
        else if (tile == Tile.Walnut || tile == Tile.Oak)
        {
            name = $"Room wood {wood}{(tile == Tile.Oak ? " fronts" : "")}"; texPath = $"{Dir}/Wood {wood}{(tile == Tile.Oak ? " fronts" : "")}.png";
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath); size = t != null ? t.width : 119; grainU = true;
        }
        else { (texPath, size, grainU) = Tiles[tile]; name = "Room " + tile.ToString().ToLowerInvariant(); }
        string matPath = $"{Dir}/{name}.mat";
        if (cache.TryGetValue(matPath, out var cached) && cached != null) return cached;
        AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
        if (ti != null && (ti.filterMode != FilterMode.Point || ti.npotScale != TextureImporterNPOTScale.None || ti.wrapMode != TextureWrapMode.Repeat))
        {
            ti.filterMode = FilterMode.Point; ti.wrapMode = TextureWrapMode.Repeat; ti.npotScale = TextureImporterNPOTScale.None;
            ti.textureCompression = TextureImporterCompression.Uncompressed; ti.mipmapEnabled = true; ti.mipmapFilter = TextureImporterMipFilter.BoxFilter; ti.SaveAndReimport();
        }
        var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, matPath); }
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath)); m.SetTextureScale("_BaseMap", Vector2.one);
        m.SetColor("_BaseColor", Color.white); m.SetFloat("_Smoothness", 0f);
        EditorUtility.SetDirty(m);
        cache[matPath] = m;
        return m;
    }

    // A small tile of one colour with a little texel-to-texel variation, so plain parts still read as pixels.
    static byte[] PlainTile(Color c, int seed)
    {
        var rng = new System.Random(seed);
        var tex = new Texture2D(PlainSize, PlainSize, TextureFormat.RGB24, false);
        for (int y = 0; y < PlainSize; y++) for (int x = 0; x < PlainSize; x++)
        {
            float k = 1f + ((float)rng.NextDouble() - .5f) * .08f;
            tex.SetPixel(x, y, new Color(c.r * k, c.g * k, c.b * k));
        }
        var bytes = tex.EncodeToPNG(); Object.DestroyImmediate(tex);
        return bytes;
    }
}
