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
        // Edge neighbours (shared corner positions) give the model's separate pieces: frame boards, the mattress, the
        // duvet, the turned-down sheet, each pillow. The painting's colour per triangle bleeds across seams (green and
        // wood on the mattress, white on the headboard), so each piece takes one class: the one most of its triangles
        // have. A piece with no clear majority is the mattress block (its sides show under the sheet): sheet.
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
        var log = new System.Text.StringBuilder();
        var piece = new int[nt]; for (int f = 0; f < nt; f++) piece[f] = -1;
        var pieceLong = new List<int>();   // the world axis each piece is longest along
        var pieces = new List<List<int>>(); var pieceClass = new List<int>();
        int flips = 0;
        for (int f0 = 0; f0 < nt; f0++)
        {
            if (piece[f0] >= 0) continue;
            int np = pieces.Count;
            var members = new List<int> { f0 }; piece[f0] = np;
            for (int i = 0; i < members.Count; i++) foreach (var o in nbs[members[i]]) if (piece[o] < 0) { piece[o] = np; members.Add(o); }
            var cnt = new int[4]; int known = 0; foreach (var f in members) if (cls[f] >= 0) { cnt[cls[f]]++; known++; }
            int m = 3; for (int c = 0; c < 4; c++) if (cnt[c] > cnt[m]) m = c;
            if (known > 0 && cnt[m] * 10 < known * 6) m = 1;
            var pb = new Bounds(cent[members[0]], Vector3.zero); foreach (var f in members) pb.Encapsulate(cent[f]);
            var wb = new Bounds(rot * cent[members[0]], Vector3.zero); foreach (var f in members) wb.Encapsulate(rot * cent[f]);
            pieceLong.Add(wb.size.x >= wb.size.y && wb.size.x >= wb.size.z ? 0 : wb.size.y >= wb.size.z ? 1 : 2);
            log.AppendLine($"piece {np}: {members.Count} tris, duvet {cnt[0]} sheet {cnt[1]} pillow {cnt[2]} wood {cnt[3]} -> {m}, centre {pb.center:F1} size {pb.size:F1}");
            foreach (var f in members) { if (cls[f] != m) flips++; cls[f] = m; }
            pieces.Add(members); pieceClass.Add(m);
        }
        // Folds: Meshy laid loose duvet pieces over the duvet (a ridge by the sheet, a rolled strip) and drapes at the
        // foot corners. Only the biggest duvet piece is the duvet; the others are dropped.
        var drop = new bool[nt];
        int duvet = -1; for (int p = 0; p < pieces.Count; p++) if (pieceClass[p] == 0 && (duvet < 0 || pieces[p].Count > pieces[duvet].Count)) duvet = p;
        for (int p = 0; p < pieces.Count; p++) if (pieceClass[p] == 0 && p != duvet) { foreach (var f in pieces[p]) drop[f] = true; log.AppendLine($"piece {p} dropped (fold)"); }
        // The duvet's top has small bumps and ridges (the plaid bends over them). Its flat top corners are smoothed in
        // height toward their flat neighbours only (the rolled edges below would pull the middle down through the duvet's
        // underside), and their normals made again.
        var pos = (Vector3[])v.Clone(); var nrm = (Vector3[])n.Clone();
        if (duvet >= 0)
        {
            var faceUp = new Dictionary<int, bool>(); var links = new Dictionary<int, HashSet<int>>(); var corners = new Dictionary<int, List<int>>();
            foreach (var f in pieces[duvet])
                for (int e = 0; e < 3; e++)
                {
                    int w = vid[t[f * 3 + e]];
                    bool up = (rot * fnorm[f]).y > .8f;
                    faceUp[w] = faceUp.TryGetValue(w, out var u) ? u && up : up;
                    if (!links.TryGetValue(w, out var s)) links[w] = s = new HashSet<int>();
                    s.Add(vid[t[f * 3 + (e + 1) % 3]]); s.Add(vid[t[f * 3 + (e + 2) % 3]]);
                    if (!corners.TryGetValue(w, out var c)) corners[w] = c = new List<int>();
                    if (!c.Contains(t[f * 3 + e])) c.Add(t[f * 3 + e]);
                }
            var h = new Dictionary<int, float>(); foreach (var w in links.Keys) h[w] = v[corners[w][0]].y;
            int moved = 0;
            for (int it = 0; it < 40; it++)
            {
                var nh = new Dictionary<int, float>(h);
                foreach (var w in links.Keys)
                {
                    if (!faceUp[w]) continue;
                    float sum = h[w]; int k = 1; foreach (var o in links[w]) if (faceUp[o]) { sum += h[o]; k++; }
                    nh[w] = sum / k;
                }
                h = nh;
            }
            foreach (var w in links.Keys) if (faceUp[w]) { foreach (var i in corners[w]) pos[i].y = h[w]; moved++; }
            // Normals of the moved corners from the faces around them.
            var acc = new Dictionary<int, Vector3>();
            foreach (var f in pieces[duvet])
            {
                Vector3 fa = pos[t[f * 3]], fb = pos[t[f * 3 + 1]], fc = pos[t[f * 3 + 2]];
                var fn = Vector3.Cross(Vector3.Scale(fb - fa, scale), Vector3.Scale(fc - fa, scale));   // in mesh units the cross is too small to normalize
                if (Vector3.Dot(fn, fnorm[f]) < 0) fn = -fn;
                for (int e = 0; e < 3; e++) { int w = vid[t[f * 3 + e]]; acc[w] = (acc.TryGetValue(w, out var a) ? a : Vector3.zero) + fn; }
            }
            foreach (var w in links.Keys) if (faceUp[w]) foreach (var i in corners[w]) nrm[i] = acc[w].normalized;
            log.AppendLine($"duvet top: {moved} corners smoothed");
        }
        // A new mesh: one vertex per triangle corner, submesh per class.
        var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nu = new List<Vector2>();
        var subs = new List<int>[4]; for (int c = 0; c < 4; c++) subs[c] = new List<int>();
        var tiles = new[] { 32f, 16f, 16f, 32f };
        // The duvet is unfolded as cloth hanging over a box: the top is laid flat (x, z) and each side carries on from
        // its edge by how far the cloth has dropped below the top, so plaid lines run over the edge and down the side
        // without a break (a projection axis per triangle broke and bent them wherever the cloth curved). Near each
        // edge a short ramp moves from the top's mapping to the side's; the corners fold diagonally.
        Vector3 R(int i) => rot * Vector3.Scale(pos[i], scale);
        float cx = 0, cz = 0, hx = 1, hz = 1, top = 0;
        if (duvet >= 0)
        {
            var db = new Bounds(R(t[pieces[duvet][0] * 3]), Vector3.zero); float sum = 0; int count = 0;
            foreach (var f in pieces[duvet]) for (int e = 0; e < 3; e++)
            {
                int i = t[f * 3 + e]; var p = R(i); db.Encapsulate(p);
                if ((rot * nrm[i]).y > .9f) { sum += p.y; count++; }
            }
            cx = db.center.x; cz = db.center.z; hx = db.extents.x; hz = db.extents.z; top = count > 0 ? sum / count : db.max.y;
        }
        const float EdgeRamp = 1.5f;   // world units (about 7 cm) over which the mapping turns down an edge
        Vector2 DuvetUv(int i)
        {
            var p = R(i); float drop = Mathf.Max(0f, top - p.y);
            float ax = Mathf.Clamp01((Mathf.Abs(p.x - cx) - (hx - EdgeRamp)) / EdgeRamp), az = Mathf.Clamp01((Mathf.Abs(p.z - cz) - (hz - EdgeRamp)) / EdgeRamp);
            // Down a side only the drop counts (the edge's own place, not the cloth's bulges), so its lines run level.
            float sx = Mathf.Sign(p.x - cx), sz = Mathf.Sign(p.z - cz);
            float u = Mathf.Lerp(p.x, cx + sx * hx, ax) + sx * ax * drop, w = Mathf.Lerp(p.z, cz + sz * hz, az) + sz * az * drop;
            return new Vector2(u, w) * PixelRestyle.RoomTexelsPerUnit / tiles[0];
        }
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
                subs[c].Add(nv.Count); nv.Add(pos[i]); nn.Add(nrm[i]);
                var w = rot * Vector3.Scale(pos[i], scale) * PixelRestyle.RoomTexelsPerUnit / tiles[c];
                // u, v and the world axes they run along.
                Vector2 q = axis == 1 ? new Vector2(w.x, w.z) : axis == 0 ? new Vector2(w.z, w.y) : new Vector2(w.x, w.y);
                int uAxis = axis == 1 ? 0 : axis == 0 ? 2 : 0;
                // Wood: the tile's planks run along v, so a piece long along u is drawn turned (grain along the rail).
                if (c == 3 && pieceLong[piece[f]] == uAxis) q = new Vector2(q.y, q.x);
                nu.Add(c == 0 ? DuvetUv(i) : q);
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
