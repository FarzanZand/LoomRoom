using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// The bedroom bed (a Meshy model) with its cloth on its own UVs: Meshy's atlas cuts the duvet, sheet and pillows into
// hundreds of patches at different sizes and angles, so no pattern painted into it can keep one pixel size or line up.
// Here every cloth triangle is box-projected along the model's axis it faces most, at RoomTexelsPerUnit in the room,
// and drawn with a small hand-made tile (plaid, linen, cotton) that repeats with the same pixels everywhere. The
// wooden frame keeps Meshy's UVs and the cleaned texture.
// Triangle classes come from Temp/Paint/Room/bed_tricls.bin (0 duvet, 1 sheet, 2 pillow, 3 wood, -1 unknown).
public static class BedCloth
{
    const string Dir = "Assets/Game/World/Art/Bedroom/Bed";

    public static string Build(string scenePath)
    {
        var go = GameObject.Find(scenePath);
        var mf = go.GetComponentInChildren<MeshFilter>(); var mr = mf.GetComponent<MeshRenderer>();
        var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Meshy/Bed_A/Bed_A.fbx").GetComponentInChildren<MeshFilter>().sharedMesh;
        var v = src.vertices; var n = src.normals; var uv = src.uv; var t = src.triangles;
        int nt = t.Length / 3;
        var raw = File.ReadAllBytes("Temp/Paint/Room/bed_tricls.bin");
        var cls = new int[nt]; System.Buffer.BlockCopy(raw, 0, cls, 0, Mathf.Min(raw.Length, nt * 4));
        var scale = mf.transform.lossyScale; var rot = mf.transform.rotation;
        var cent = new Vector3[nt]; var fnorm = new Vector3[nt];
        for (int f = 0; f < nt; f++)
        {
            Vector3 a = v[t[f * 3]], b = v[t[f * 3 + 1]], c = v[t[f * 3 + 2]];
            cent[f] = Vector3.Scale((a + b + c) / 3f, scale);
            fnorm[f] = (n[t[f * 3]] + n[t[f * 3 + 1]] + n[t[f * 3 + 2]]).normalized;
        }
        // Triangles too small to have a texel take the class of the nearest triangle that has one.
        for (int f = 0; f < nt; f++)
        {
            if (cls[f] >= 0) continue;
            float best = float.MaxValue; int bc = 3;
            for (int o = 0; o < nt; o++) { if (cls[o] < 0) continue; float d = (cent[o] - cent[f]).sqrMagnitude; if (d < best) { best = d; bc = cls[o]; } }
            cls[f] = bc;
        }
        // Edge neighbours (shared corner positions), then cloth triangles whose neighbours mostly disagree flip.
        var key = new Dictionary<(int, int, int), int>(); var vid = new int[v.Length];
        for (int i = 0; i < v.Length; i++) { var k = (Mathf.RoundToInt(v[i].x * 1e6f), Mathf.RoundToInt(v[i].y * 1e6f), Mathf.RoundToInt(v[i].z * 1e6f)); if (!key.TryGetValue(k, out var id)) { id = key.Count; key[k] = id; } vid[i] = id; }
        var edges = new Dictionary<(int, int), List<int>>();
        for (int f = 0; f < nt; f++) for (int e = 0; e < 3; e++)
        {
            int p = vid[t[f * 3 + e]], q = vid[t[f * 3 + (e + 1) % 3]]; var k = p < q ? (p, q) : (q, p);
            if (!edges.TryGetValue(k, out var l)) edges[k] = l = new List<int>(); l.Add(f);
        }
        var nbs = new List<int>[nt]; for (int f = 0; f < nt; f++) nbs[f] = new List<int>();
        foreach (var l in edges.Values) for (int i = 0; i < l.Count; i++) for (int j = 0; j < l.Count; j++) if (i != j) nbs[l[i]].Add(l[j]);
        int flips = 0;
        for (int pass = 0; pass < 6; pass++)
        {
            var next = (int[])cls.Clone();
            for (int f = 0; f < nt; f++)
            {
                if (cls[f] == 3 || nbs[f].Count == 0) continue;
                var cnt = new int[4]; foreach (var o in nbs[f]) cnt[cls[o]]++;
                int m = 0; for (int c = 1; c < 3; c++) if (cnt[c] > cnt[m]) m = c;
                if (m != cls[f] && cnt[m] * 2 > nbs[f].Count && cnt[cls[f]] <= 1) { next[f] = m; flips++; }
            }
            cls = next;
        }
        // Patches: connected cloth triangles of one class. A small patch (a cluster the painting got wrong, a notch of
        // sheet in the duvet) takes the class most of its border touches.
        for (int pass = 0; pass < 3; pass++)
        {
            var comp = new int[nt]; for (int f = 0; f < nt; f++) comp[f] = -1;
            int nc = 0;
            for (int f0 = 0; f0 < nt; f0++)
            {
                if (comp[f0] >= 0 || cls[f0] == 3) continue;
                var members = new List<int> { f0 }; comp[f0] = nc; var border = new int[4];
                for (int i = 0; i < members.Count; i++)
                    foreach (var o in nbs[members[i]])
                    {
                        if (cls[o] == cls[f0]) { if (comp[o] < 0) { comp[o] = nc; members.Add(o); } }
                        else border[cls[o]]++;
                    }
                nc++;
                int m = 0; for (int c = 1; c < 3; c++) if (border[c] > border[m]) m = c;
                int total = border[0] + border[1] + border[2] + border[3];
                if (members.Count < 60 && total > 0 && border[m] * 10 >= total * 6 && m != cls[f0])
                    foreach (var f in members) { cls[f] = m; flips++; }
            }
        }
        var log = new System.Text.StringBuilder();
        // Pillows are separate pieces of the model: a connected piece that is mostly pillow-coloured is all pillow;
        // in every other piece pillow-coloured triangles are the sheet (the fold-over came out peach otherwise).
        var piece = new int[nt]; for (int f = 0; f < nt; f++) piece[f] = -1;
        var pieceLong = new List<int>();   // the world axis each piece is longest along
        var drop = new bool[nt];             // loose strips Meshy laid on the duvet (a "rolled cigar", a long ridge)
        {
            int np = 0;
            for (int f0 = 0; f0 < nt; f0++)
            {
                if (piece[f0] >= 0) continue;
                var members = new List<int> { f0 }; piece[f0] = np;
                for (int i = 0; i < members.Count; i++) foreach (var o in nbs[members[i]]) if (piece[o] < 0) { piece[o] = np; members.Add(o); }
                var cnt = new int[4]; foreach (var f in members) cnt[cls[f]]++;
                int cloth = cnt[0] + cnt[1] + cnt[2];
                var pb = new Bounds(cent[members[0]], Vector3.zero); foreach (var f in members) pb.Encapsulate(cent[f]);
                var wb = new Bounds(rot * cent[members[0]], Vector3.zero); foreach (var f in members) wb.Encapsulate(rot * cent[f]);
                pieceLong.Add(wb.size.x >= wb.size.y && wb.size.x >= wb.size.z ? 0 : wb.size.y >= wb.size.z ? 1 : 2);
                log.AppendLine($"piece {np}: {members.Count} tris, duvet {cnt[0]} sheet {cnt[1]} pillow {cnt[2]} wood {cnt[3]}, centre {pb.center:F1} size {pb.size:F1}");
                bool pillow = cloth > 0 && cnt[2] * 2 > cloth && cnt[3] * 4 < members.Count;
                if (cnt[0] * 10 > members.Count * 9 && members.Count < 300 && pb.size.y < 2f)
                    foreach (var f in members) drop[f] = true;
                foreach (var f in members) { if (cls[f] == 3) continue; int c = pillow ? 2 : (cls[f] == 2 ? 1 : cls[f]); if (c != cls[f]) { cls[f] = c; flips++; } }
                np++;
            }
        }
        // A new mesh: one vertex per triangle corner, submesh per class.
        var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nu = new List<Vector2>();
        var subs = new List<int>[4]; for (int c = 0; c < 4; c++) subs[c] = new List<int>();
        var tiles = new[] { 32f, 16f, 16f, 32f };
        for (int f = 0; f < nt; f++)
        {
            if (drop[f]) continue;
            int c = cls[f];
            // Facing in the room (the model is turned), projection along the dominant world axis.
            var wn = rot * fnorm[f]; var an = new Vector3(Mathf.Abs(wn.x), Mathf.Abs(wn.y), Mathf.Abs(wn.z));
            int axis = an.y >= an.x && an.y >= an.z ? 1 : an.x >= an.z ? 0 : 2;
            for (int e = 0; e < 3; e++)
            {
                int i = t[f * 3 + e];
                subs[c].Add(nv.Count); nv.Add(v[i]); nn.Add(n[i]);
                var w = rot * Vector3.Scale(v[i], scale) * PixelRestyle.RoomTexelsPerUnit / tiles[c];
                // u, v and the world axes they run along.
                Vector2 q = axis == 1 ? new Vector2(w.x, w.z) : axis == 0 ? new Vector2(w.z, w.y) : new Vector2(w.x, w.y);
                int uAxis = axis == 1 ? 0 : axis == 0 ? 2 : 0;
                // Wood: the tile's planks run along v, so a piece long along u is drawn turned (grain along the rail).
                if (c == 3 && pieceLong[piece[f]] == uAxis) q = new Vector2(q.y, q.x);
                nu.Add(q);
            }
        }
        var mesh = new Mesh { name = "Bed_A cloth", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(nv); mesh.SetNormals(nn); mesh.SetUVs(0, nu); mesh.subMeshCount = 4;
        for (int c = 0; c < 4; c++) mesh.SetTriangles(subs[c], c);
        mesh.RecalculateBounds(); mesh.RecalculateTangents();
        string meshPath = $"{Dir}/Bed cloth.asset"; AssetDatabase.DeleteAsset(meshPath); AssetDatabase.CreateAsset(mesh, meshPath);
        Material Tile(string name)
        {
            string tp = $"{Dir}/{name}.png"; AssetDatabase.ImportAsset(tp, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(tp);
            ti.filterMode = FilterMode.Point; ti.wrapMode = TextureWrapMode.Repeat; ti.npotScale = TextureImporterNPOTScale.None;
            ti.textureCompression = TextureImporterCompression.Uncompressed; ti.mipmapEnabled = true; ti.mipmapFilter = TextureImporterMipFilter.BoxFilter; ti.SaveAndReimport();
            string mp = $"{Dir}/{name}.mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, mp); }
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tp)); m.SetTextureScale("_BaseMap", Vector2.one); m.SetFloat("_Smoothness", 0f);
            EditorUtility.SetDirty(m); return m;
        }
        var mats = new[] { Tile("Duvet plaid"), Tile("Sheet linen"), Tile("Pillow cotton"), Tile("Walnut") };
        Undo.RecordObject(mf, "Bed cloth"); Undo.RecordObject(mr, "Bed cloth");
        mf.sharedMesh = mesh; mr.sharedMaterials = mats;
        PrefabUtility.RecordPrefabInstancePropertyModifications(mf); PrefabUtility.RecordPrefabInstancePropertyModifications(mr);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        AssetDatabase.SaveAssets();
        File.WriteAllText("Temp/Paint/Room/bed_pieces.txt", log.ToString());
        var counts = new int[4]; foreach (var c in cls) counts[c]++;
        return $"duvet {counts[0]}, sheet {counts[1]}, pillow {counts[2]}, wood {counts[3]}, flipped {flips}";
    }
}
