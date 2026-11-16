using System.Collections.Generic;
using UnityEngine;

// Cuts a model into closed pieces along planes: a stone pillar into a stump and loose rubble (BossCoverPillar). Each cut
// closes both halves with a broken face, drawn with a patch of the model's own texture (its flat top) at the model's own
// texel size, so a piece is a solid lump of the same stone and never an open shell. Works on triangles kept loose (three
// corners each): every corner a cut makes on an edge two triangles share comes out the same to the last bit (the edge's
// ends are always taken in one order), which is what lets the cut's outline be chained into loops and filled.
public static class MeshShatter
{
    public struct Vertex { public Vector3 p, n; public Vector2 uv; }

    // A piece: per submesh of the model its triangles (three corners each), and last the broken faces.
    public class Solid
    {
        public List<Vertex>[] subs;
        public int Broken => subs.Length - 1;
        public int Corners { get { int n = 0; foreach (var s in subs) n += s.Count; return n; } }
        public Bounds Bounds
        {
            get
            {
                bool any = false; var b = new Bounds();
                foreach (var s in subs) foreach (var v in s) { if (any) b.Encapsulate(v.p); else { b = new Bounds(v.p, Vector3.zero); any = true; } }
                return b;
            }
        }
        // How much stone it holds, cubic metres (a closed piece: the sum of the wedges its triangles make with a point).
        public float Volume
        {
            get
            {
                double sum = 0;
                foreach (var s in subs) for (int i = 0; i + 2 < s.Count; i += 3) sum += Vector3.Dot(s[i].p, Vector3.Cross(s[i + 1].p, s[i + 2].p));
                return Mathf.Abs((float)(sum / 6.0));
            }
        }
        // A sliver: it holds far less stone than a lump of its length would (a plate, a splinter, a face's shaving).
        // Loose in the air or lying on the floor such a piece is a sheet of card, not a stone.
        public bool Sliver { get { var size = Bounds.size; float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z)); return Volume < .14f * longest * longest * longest; } }
        public Solid Empty() { var e = new Solid { subs = new List<Vertex>[subs.Length] }; for (int i = 0; i < subs.Length; i++) e.subs[i] = Rent(); return e; }
    }

    // A break makes a few hundred solids, each a handful of lists, most of them dead again after the next cut: the
    // lists go round (Release hands a dead solid's back, Empty takes them), and Trim lets them go when the job is done.
    // Without it one pillar's cutting left 17 MB for the garbage collector.
    static readonly Stack<List<Vertex>> spare = new();
    static List<Vertex> Rent() { if (spare.Count == 0) return new List<Vertex>(128); var l = spare.Pop(); l.Clear(); return l; }
    public static void Release(Solid solid)
    {
        if (solid == null || solid.subs == null) return;
        foreach (var l in solid.subs) if (l != null) spare.Push(l);
        solid.subs = null;
    }
    public static void Trim() => spare.Clear();

    // Where on the model's texture a broken face is drawn: the model's flat top (plain stone, the widest flat patch a
    // pillar has), and how the texture runs there (texture distance per metre along the face's two axes), so the broken
    // faces keep the model's texel size. Without a flat top a broken face takes its outline's own texels.
    public struct Patch
    {
        public bool valid; public int submesh;
        public Vector2 centre, min, max, perX, perZ;
        public Vector2 At(float a, float b)
        {
            var uv = centre + perX * a + perZ * b;
            return new Vector2(Mathf.Clamp(uv.x, min.x, max.x), Mathf.Clamp(uv.y, min.y, max.y));
        }
    }

    // The mesh's triangles under a transform, moved so origin is zero (small numbers cut cleaner than world ones).
    public static Solid From(Mesh mesh, Matrix4x4 toWorld, Vector3 origin)
    {
        var solid = new Solid { subs = new List<Vertex>[mesh.subMeshCount + 1] };
        var points = mesh.vertices; var normals = mesh.normals; var uvs = mesh.uv;
        bool flipped = toWorld.determinant < 0f;
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var list = solid.subs[s] = Rent();
            var tris = mesh.GetTriangles(s);
            for (int i = 0; i + 2 < tris.Length; i += 3)
                for (int k = 0; k < 3; k++)
                {
                    int index = tris[i + (flipped ? 2 - k : k)];
                    list.Add(new Vertex
                    {
                        p = toWorld.MultiplyPoint3x4(points[index]) - origin,
                        n = index < normals.Length ? toWorld.MultiplyVector(normals[index]).normalized : Vector3.up,
                        uv = index < uvs.Length ? uvs[index] : Vector2.zero,
                    });
                }
        }
        solid.subs[mesh.subMeshCount] = Rent();
        return solid;
    }

    // The model's flat top: its highest faces that look straight up.
    public static Patch TopPatch(Solid solid)
    {
        var patch = new Patch();
        float top = float.MinValue;
        for (int s = 0; s < solid.Broken; s++)
        {
            var list = solid.subs[s];
            for (int i = 0; i + 2 < list.Count; i += 3)
                if (FaceNormal(list[i].p, list[i + 1].p, list[i + 2].p).y > .95f) top = Mathf.Max(top, list[i].p.y);
        }
        if (top == float.MinValue) return patch;
        float best = 0f; int bestSub = 0, bestAt = -1;
        var min = new Vector2(float.MaxValue, float.MaxValue); var max = -min;
        for (int s = 0; s < solid.Broken; s++)
        {
            var list = solid.subs[s];
            for (int i = 0; i + 2 < list.Count; i += 3)
            {
                Vector3 a = list[i].p, b = list[i + 1].p, c = list[i + 2].p;
                var n = Vector3.Cross(b - a, c - a);
                if (n.magnitude < 1e-8f || FaceNormal(a, b, c).y <= .95f || a.y < top - .02f) continue;
                for (int k = 0; k < 3; k++) { min = Vector2.Min(min, list[i + k].uv); max = Vector2.Max(max, list[i + k].uv); }
                if (n.magnitude > best) { best = n.magnitude; bestSub = s; bestAt = i; }
            }
        }
        if (bestAt < 0) return patch;
        // The texture's run across that face: (x, z) in metres to texture distance, from its biggest triangle.
        var t = solid.subs[bestSub];
        Vector2 e1 = new(t[bestAt + 1].p.x - t[bestAt].p.x, t[bestAt + 1].p.z - t[bestAt].p.z), e2 = new(t[bestAt + 2].p.x - t[bestAt].p.x, t[bestAt + 2].p.z - t[bestAt].p.z);
        Vector2 d1 = t[bestAt + 1].uv - t[bestAt].uv, d2 = t[bestAt + 2].uv - t[bestAt].uv;
        float det = e1.x * e2.y - e1.y * e2.x;
        if (Mathf.Abs(det) < 1e-8f) return patch;
        patch.perX = (d1 * e2.y - d2 * e1.y) / det;
        patch.perZ = (d2 * e1.x - d1 * e2.x) / det;
        // A texel in from the patch's rim, so a face as wide as the top never takes the neighbouring island's texels.
        var inset = (max - min) * .03f;
        patch.min = min + inset; patch.max = max - inset; patch.centre = (min + max) * .5f;
        patch.submesh = bestSub; patch.valid = true;
        return patch;
    }

    static Vector3 FaceNormal(Vector3 a, Vector3 b, Vector3 c) => Vector3.Cross(b - a, c - a).normalized;

    // Both halves of a solid cut by the plane through point with this normal: above (the normal's side) and below, each
    // closed with a broken face. fitted: the lower half's face meets every corner of the model's side along its rim
    // (Fill), for a piece that stays and is looked at (a stump). The upper half's face, and both without it, are a few
    // big triangles, which is all a lump in the air needs, and later cuts through them stay cheap. bothFitted: the upper
    // half's face is fitted too (a pier split into columns that both stay standing).
    public static void Split(Solid solid, Vector3 point, Vector3 normal, Patch patch, out Solid above, out Solid below, bool fitted = true, bool bothFitted = false)
    {
        normal.Normalize();
        above = solid.Empty(); below = solid.Empty();
        var segments = new List<(Vertex a, Vertex b)>();
        var poly = new List<Vertex>(6); var cuts = new List<Vertex>(2);
        for (int s = 0; s < solid.subs.Length; s++)
        {
            var list = solid.subs[s];
            for (int i = 0; i + 2 < list.Count; i += 3)
            {
                Vertex a = list[i], b = list[i + 1], c = list[i + 2];
                float da = Side(a.p, point, normal), db = Side(b.p, point, normal), dc = Side(c.p, point, normal);
                if (da >= 0f && db >= 0f && dc >= 0f) { above.subs[s].Add(a); above.subs[s].Add(b); above.subs[s].Add(c); continue; }
                if (da < 0f && db < 0f && dc < 0f) { below.subs[s].Add(a); below.subs[s].Add(b); below.subs[s].Add(c); continue; }
                cuts.Clear();
                Clip(a, b, c, da, db, dc, true, poly, cuts); Fan(poly, above.subs[s]);
                if (cuts.Count == 2 && (cuts[0].p - cuts[1].p).sqrMagnitude > 1e-12f) segments.Add((cuts[0], cuts[1]));
                Clip(a, b, c, da, db, dc, false, poly, null); Fan(poly, below.subs[s]);
            }
        }
        if (segments.Count >= 3) Close(segments, normal, patch, above.subs[above.Broken], below.subs[below.Broken], fitted, fitted && bothFitted);
    }

    static float Side(Vector3 p, Vector3 point, Vector3 normal)
    {
        float d = Vector3.Dot(p - point, normal);
        return Mathf.Abs(d) < 1e-5f ? 0f : d;
    }

    // The part of a triangle on one side of the plane (its corners in order), and where its edges cross.
    static void Clip(Vertex a, Vertex b, Vertex c, float da, float db, float dc, bool upper, List<Vertex> poly, List<Vertex> cuts)
    {
        poly.Clear();
        Edge(a, b, da, db, upper, poly, cuts); Edge(b, c, db, dc, upper, poly, cuts); Edge(c, a, dc, da, upper, poly, cuts);
    }

    static void Edge(Vertex from, Vertex to, float df, float dt, bool upper, List<Vertex> poly, List<Vertex> cuts)
    {
        bool fromIn = upper ? df >= 0f : df < 0f, toIn = upper ? dt >= 0f : dt < 0f;
        if (fromIn) poly.Add(from);
        if (fromIn == toIn) return;
        var cut = Cut(from, to, df, dt);
        poly.Add(cut);
        cuts?.Add(cut);
    }

    // Where an edge meets the plane. Its ends are taken in one fixed order whichever way the triangle runs, so the two
    // triangles that share the edge get the very same point.
    static Vertex Cut(Vertex a, Vertex b, float da, float db)
    {
        if (Before(b.p, a.p)) { (a, b) = (b, a); (da, db) = (db, da); }
        float t = da / (da - db);
        return new Vertex { p = Vector3.LerpUnclamped(a.p, b.p, t), n = Vector3.LerpUnclamped(a.n, b.n, t).normalized, uv = Vector2.LerpUnclamped(a.uv, b.uv, t) };
    }

    static bool Before(Vector3 a, Vector3 b) => a.x != b.x ? a.x < b.x : a.y != b.y ? a.y < b.y : a.z < b.z;

    static void Fan(List<Vertex> poly, List<Vertex> into)
    {
        for (int i = 1; i + 1 < poly.Count; i++)
        {
            if (Vector3.Cross(poly[i].p - poly[0].p, poly[i + 1].p - poly[0].p).sqrMagnitude < 1e-14f) continue;
            into.Add(poly[0]); into.Add(poly[i]); into.Add(poly[i + 1]);
        }
    }

    // The cut's outline chained into loops, each filled: a face looking up the normal for the lower half, down it for
    // the upper. A loop wholly inside another is the same stone twice (a model built of overlapping parts): only the
    // outer one is filled, since two faces in one plane flicker.
    static void Close(List<(Vertex a, Vertex b)> segments, Vector3 normal, Patch patch, List<Vertex> upperFace, List<Vertex> lowerFace, bool fitted, bool upperFitted)
    {
        var ids = new Dictionary<Vector3, int>(); var corners = new List<Vertex>();
        int Id(Vertex v) { if (!ids.TryGetValue(v.p, out int id)) { id = corners.Count; ids.Add(v.p, id); corners.Add(v); } return id; }
        var ends = new List<(int a, int b)>(segments.Count);
        var at = new Dictionary<int, List<int>>();
        foreach (var (a, b) in segments)
        {
            int ia = Id(a), ib = Id(b);
            if (ia == ib) continue;
            int index = ends.Count; ends.Add((ia, ib));
            if (!at.TryGetValue(ia, out var la)) at.Add(ia, la = new List<int>()); la.Add(index);
            if (!at.TryGetValue(ib, out var lb)) at.Add(ib, lb = new List<int>()); lb.Add(index);
        }
        var used = new bool[ends.Count];
        var e1 = Vector3.Cross(normal, Mathf.Abs(normal.y) < .9f ? Vector3.up : Vector3.right).normalized;
        var e2 = Vector3.Cross(normal, e1);
        var loops = new List<(List<int> ring, List<Vector2> flat, float area)>();
        for (int start = 0; start < ends.Count; start++)
        {
            if (used[start]) continue;
            used[start] = true;
            var ring = new List<int> { ends[start].a, ends[start].b };
            // On from the last corner along any outline piece not yet walked, until back at the start or at a loose end.
            for (int guard = 0; guard < ends.Count + 2; guard++)
            {
                int tip = ring[ring.Count - 1], next = -1;
                foreach (int s in at[tip]) if (!used[s]) { used[s] = true; next = ends[s].a == tip ? ends[s].b : ends[s].a; break; }
                if (next < 0 || next == ring[0]) break;
                ring.Add(next);
            }
            if (ring.Count < 3) continue;
            var flat = new List<Vector2>(ring.Count);
            foreach (int id in ring) flat.Add(new Vector2(Vector3.Dot(corners[id].p, e1), Vector3.Dot(corners[id].p, e2)));
            float area = Area(flat);
            if (Mathf.Abs(area) < 1e-6f) continue;
            if (area < 0f) { ring.Reverse(); flat.Reverse(); area = -area; }
            loops.Add((ring, flat, area));
        }
        loops.Sort((x, y) => y.area.CompareTo(x.area));
        var tris = new List<int>(); var coarse = new List<int>();
        for (int l = 0; l < loops.Count; l++)
        {
            var (ring, flat, _) = loops[l];
            bool inner = false;
            for (int o = 0; o < l && !inner; o++) inner = Inside(loops[o].flat, flat[0]);
            if (inner) continue;
            tris.Clear(); coarse.Clear(); Fill(flat, tris, coarse, fitted);
            Vector2 lo = flat[0], hi = flat[0];
            foreach (var f in flat) { lo = Vector2.Min(lo, f); hi = Vector2.Max(hi, f); }
            var mid = (lo + hi) * .5f;
            Vertex Corner(int k, Vector3 n) => new Vertex { p = corners[ring[k]].p, n = n, uv = patch.valid ? patch.At(flat[k].x - mid.x, flat[k].y - mid.y) : corners[ring[k]].uv };
            for (int i = 0; i + 2 < tris.Count; i += 3) Add(lowerFace, Corner(tris[i], normal), Corner(tris[i + 1], normal), Corner(tris[i + 2], normal), normal);
            var upper = upperFitted ? tris : coarse;
            for (int i = 0; i + 2 < upper.Count; i += 3) Add(upperFace, Corner(upper[i], -normal), Corner(upper[i + 1], -normal), Corner(upper[i + 2], -normal), -normal);
        }
    }

    // A triangle wound so it is seen from the side its normal looks to.
    static void Add(List<Vertex> into, Vertex a, Vertex b, Vertex c, Vector3 facing)
    {
        if (Vector3.Dot(Vector3.Cross(b.p - a.p, c.p - a.p), facing) < 0f) (b, c) = (c, b);
        into.Add(a); into.Add(b); into.Add(c);
    }

    static float Area(List<Vector2> p)
    {
        float sum = 0f;
        for (int i = 0; i < p.Count; i++) { var a = p[i]; var b = p[(i + 1) % p.Count]; sum += a.x * b.y - b.x * a.y; }
        return sum * .5f;
    }

    static bool Inside(List<Vector2> poly, Vector2 p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            if (poly[i].y > p.y != poly[j].y > p.y && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) inside = !inside;
        return inside;
    }

    static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

    // An outline (counter-clockwise) filled with triangles. A cut through a painted model has a point wherever it
    // crossed a triangle's edge, a hundred or more along a few straight sides: only its corners go through the ear
    // cutting (which slows with the cube of the count), and the points on the straight runs are then put back by
    // splitting the triangle on each run, so the face still meets every corner of the model's side along its rim (no
    // hairline gaps where a long edge would pass corners it does not share).
    static readonly List<int> cornersOnly = new();
    static void Fill(List<Vector2> p, List<int> tris, List<int> coarse, bool fitted)
    {
        int n = p.Count;
        var straight = new bool[n];
        float scale = 1e-6f;
        for (int i = 1; i < n; i++) scale = Mathf.Max(scale, (p[i] - p[0]).sqrMagnitude);
        // A point is on a straight run when it lies on the line from the last point kept to the next point, between them.
        cornersOnly.Clear();
        int prev = n - 1;
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            if (i < n - 1 && Mathf.Abs(Cross(p[prev], p[i], p[next])) <= 1e-7f * scale && Vector2.Dot(p[i] - p[prev], p[next] - p[i]) > 0f) { straight[i] = true; continue; }
            cornersOnly.Add(i); prev = i;
        }
        if (cornersOnly.Count < 3)
        {
            cornersOnly.Clear();
            for (int i = 0; i < n; i++) { cornersOnly.Add(i); straight[i] = false; }
        }
        Triangulate(p, cornersOnly, coarse);
        if (!fitted) { tris.AddRange(coarse); return; }
        for (int i = 0; i + 2 < coarse.Count; i += 3) Refine(coarse[i], coarse[i + 1], coarse[i + 2], straight, n, tris, 0);
    }

    // A triangle of the outline's points; where one of its sides runs along the rim past points left out as straight,
    // it is split at the first of them (from the corner opposite), and so on along the run.
    static void Refine(int a, int b, int c, bool[] straight, int n, List<int> tris, int depth)
    {
        if (depth < 2000)
        {
            if (Run(a, b, straight, n)) { int m = (a + 1) % n; Refine(a, m, c, straight, n, tris, depth + 1); Refine(m, b, c, straight, n, tris, depth + 1); return; }
            if (Run(b, c, straight, n)) { int m = (b + 1) % n; Refine(b, m, a, straight, n, tris, depth + 1); Refine(m, c, a, straight, n, tris, depth + 1); return; }
            if (Run(c, a, straight, n)) { int m = (c + 1) % n; Refine(c, m, b, straight, n, tris, depth + 1); Refine(m, a, b, straight, n, tris, depth + 1); return; }
        }
        tris.Add(a); tris.Add(b); tris.Add(c);
    }

    // From 'from' on round the rim to 'to' there are only points left out as straight, and at least one.
    static bool Run(int from, int to, bool[] straight, int n)
    {
        int i = (from + 1) % n;
        if (i == to) return false;
        for (int guard = 0; guard < n; guard++, i = (i + 1) % n)
        {
            if (i == to) return true;
            if (!straight[i]) return false;
        }
        return false;
    }

    // The outline's points listed in left (counter-clockwise; emptied) cut into triangles, ear by ear; what can't be cut
    // that way is fanned.
    static void Triangulate(List<Vector2> p, List<int> left, List<int> tris)
    {
        for (int guard = 0; left.Count > 3 && guard < 4000; guard++)
        {
            bool cut = false;
            for (int i = 0; i < left.Count && !cut; i++)
            {
                int i0 = left[(i + left.Count - 1) % left.Count], i1 = left[i], i2 = left[(i + 1) % left.Count];
                float turn = Cross(p[i0], p[i1], p[i2]);
                if (Mathf.Abs(turn) < 1e-9f) { left.RemoveAt(i); cut = true; break; }   // a corner on a straight run: nothing to fill
                if (turn < 0f) continue;
                bool ear = true;
                for (int k = 0; k < left.Count && ear; k++)
                {
                    int j = left[k];
                    if (j == i0 || j == i1 || j == i2) continue;
                    if (Cross(p[i0], p[i1], p[j]) > 0f && Cross(p[i1], p[i2], p[j]) > 0f && Cross(p[i2], p[i0], p[j]) > 0f) ear = false;
                }
                if (!ear) continue;
                tris.Add(i0); tris.Add(i1); tris.Add(i2);
                left.RemoveAt(i); cut = true;
            }
            if (!cut) break;
        }
        for (int i = 1; i + 1 < left.Count; i++) { tris.Add(left[0]); tris.Add(left[i]); tris.Add(left[i + 1]); }
    }

    // A solid broken into lumps no longer than size: cut across its longest side near its middle, the cut tipped a
    // little (a cut tipped far made long thin wedges and planks, which read as broken board, not stone), and each half
    // again, so no two lumps are alike.
    // In steps (an enumerator: after a cut it stops whenever due says the frame's time is up), so the work can be spread
    // over frames; run to its end in one go it does the whole job. The same lumps either way.
    public static System.Collections.IEnumerator Shatter(Solid solid, float size, System.Random random, Patch patch, List<Solid> into, System.Func<bool> due = null)
    {
        var waiting = new Stack<(Solid solid, int depth)>();
        waiting.Push((solid, 0));
        while (waiting.Count > 0)
        {
            var (piece, depth) = waiting.Pop();
            if (piece.Corners < 9) { Release(piece); continue; }
            var b = piece.Bounds;
            float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            // Each lump its own size, from two thirds of the size to all of it.
            if (depth >= 8 || longest <= size * (.66f + .34f * (float)random.NextDouble())) { into.Add(piece); continue; }
            var axis = longest == b.size.y ? Vector3.up : longest == b.size.x ? Vector3.right : Vector3.forward;
            var tip = new Vector3((float)random.NextDouble() - .5f, (float)random.NextDouble() - .5f, (float)random.NextDouble() - .5f) * .4f;
            var at = b.center + axis * (((float)random.NextDouble() - .5f) * .3f * longest);
            Split(piece, at, axis + tip, patch, out var one, out var other, false);
            // A cut that took nothing off (a sliver's worth): the lump stays whole.
            if (one.Corners < 9 || other.Corners < 9) { Release(one); Release(other); into.Add(piece); continue; }
            Release(piece);
            waiting.Push((other, depth + 1)); waiting.Push((one, depth + 1));
            if (due != null && due()) yield return null;
        }
    }

    // The solid as a mesh about its own middle (returned as centre), one submesh per entry of used (the solid's
    // submeshes that have triangles, the broken faces last).
    public static Mesh ToMesh(Solid solid, out Vector3 centre, List<int> used)
    {
        centre = solid.Bounds.center;
        var points = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>();
        var mesh = new Mesh { name = "Broken stone" };
        var tris = new List<List<int>>();
        used.Clear();
        for (int s = 0; s < solid.subs.Length; s++)
        {
            if (solid.subs[s].Count == 0) continue;
            var list = new List<int>(solid.subs[s].Count);
            foreach (var v in solid.subs[s]) { list.Add(points.Count); points.Add(v.p - centre); normals.Add(v.n); uvs.Add(v.uv); }
            tris.Add(list); used.Add(s);
        }
        mesh.SetVertices(points); mesh.SetNormals(normals); mesh.SetUVs(0, uvs);
        mesh.subMeshCount = tris.Count;
        for (int i = 0; i < tris.Count; i++) mesh.SetTriangles(tris[i], i);
        mesh.RecalculateBounds(); mesh.RecalculateTangents();
        return mesh;
    }
}
