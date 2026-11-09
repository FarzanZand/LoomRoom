using System.Collections.Generic;
using UnityEngine;

// Generates the realm's campaign map into the open scene (the terrain, its painted ground, the scatter, the places'
// props). CampaignMapBuilder.Build() turns the result into the editable prefab. Generated from the approved prototype
// ("The realm", pixel look); the numbers are kept as they were tuned.
public static partial class CampaignMapBuilder
{
    public class GenOut
    {
        public Vector3 C; public float HX, HZ; public int W, H; public float[] heights;
        public Dictionary<string, Vector2> pois;
        public readonly Dictionary<string, float> radii = new();
        public readonly List<(string a, string b, Vector2 mid)> legs = new();
        public int poiStart, poiEnd;
    }

    static GameObject Generate(GenOut o)
    {
        string example = "longroad"; string look = "pixel";
        var root = new UnityEngine.GameObject("Realm map (building)");
        var C = new UnityEngine.Vector3(-342.5f, 12.95f, 396.5f);
        float HX = 39f, HZ = 53f;
        var rng = new System.Random(example.Length * 7919 + 17);
        float R() => (float)rng.NextDouble();
        var lit = UnityEngine.Shader.Find("Universal Render Pipeline/Lit");
        UnityEngine.Material Mat(UnityEngine.Color c, float smooth = .1f) { var m = new UnityEngine.Material(lit); m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); return m; }
        bool parchment = look == "parchment", stylized = look == "stylized";

        // ── Lands ── 0 meadow, 1 pine forest, 2 autumn wood, 3 dead lands, 4 snow, 5 badlands, 6 swamp, 7 farmland
        // u: near (-1) to far (+1, the DM's side); v: the player's right (-1) to left (+1). Every world starts on the left.
        var seeds = new System.Collections.Generic.List<UnityEngine.Vector3>();
        void S(float u, float v, int b) => seeds.Add(new UnityEngine.Vector3(u, v, b));
        var ridges = new System.Collections.Generic.List<UnityEngine.Vector4>();   // mountain ridges: (u0, v0) to (u1, v1)
        var ridgeHeights = new System.Collections.Generic.List<float>();
        void Ridge(float u0, float v0, float u1, float v1, float h) { ridges.Add(new UnityEngine.Vector4(u0, v0, u1, v1)); ridgeHeights.Add(h); }
        var pois = new System.Collections.Generic.Dictionary<string, UnityEngine.Vector2>();
        string[][] routes;
        // Four acts spread out from the start, in the near-left corner (from where the player stands), toward the far-right
        // corner. A place is given by its distance from the start (0 home, 1 the far corner) and its angle (0 along the far
        // edge's way, 90 along the right).
        UnityEngine.Vector2 At(float r, float deg) { float a = deg * UnityEngine.Mathf.Deg2Rad; return new UnityEngine.Vector2(-.85f + r * 1.9f * UnityEngine.Mathf.Cos(a), .85f - r * 1.9f * UnityEngine.Mathf.Sin(a)); }
        float ActDist(float u, float v) => UnityEngine.Mathf.Sqrt((u + .85f) * (u + .85f) + (.85f - v) * (.85f - v)) / 1.9f;
        // lands per act: [act][0] main, [act][1] second
        int[][] acts;
        float passA = 40, pass2 = 50;
        if (example == "longroad")       // the realm: farms and woods, autumn hills, swamp and dead lands, frozen heights
            acts = new[] { new[] { 7, 1 }, new[] { 2, 0 }, new[] { 6, 3 }, new[] { 4, 3 } };
        else if (example == "pass")      // the frontier: green home, red badlands, dead lands, a black wasteland under the peaks
            { acts = new[] { new[] { 0, 1 }, new[] { 5, 2 }, new[] { 3, 5 }, new[] { 3, 4 } }; passA = 25; pass2 = 60; }
        else                              // the north: farms, taiga, snowfields, the frozen heart with dead woods
            { acts = new[] { new[] { 7, 0 }, new[] { 1, 2 }, new[] { 4, 1 }, new[] { 4, 3 } }; passA = 55; pass2 = 35; }
        float[] actEdge = { .3f, .55f, .8f };
        for (int k = 0; k < 90; k++)
        {
            float r = R() * 1.25f, a = R() * 90; var p = At(r, a);
            if (UnityEngine.Mathf.Abs(p.x) > 1.1f || UnityEngine.Mathf.Abs(p.y) > 1.1f) continue;
            int act = r < actEdge[0] ? 0 : r < actEdge[1] ? 1 : r < actEdge[2] ? 2 : 3;
            S(p.x, p.y, acts[act][R() < .62f ? 0 : 1]);
        }
        // Ridges between the acts, each with one pass the road goes through; the last act ringed by high peaks.
        void Arc(float r, float a0, float a1, float h) { for (float a = a0; a < a1 - 1; a += 8) { var p0 = At(r + (R() - .5f) * .03f, a); var p1 = At(r + (R() - .5f) * .03f, UnityEngine.Mathf.Min(a + 8, a1)); Ridge(p0.x, p0.y, p1.x, p1.y, h); } }
        Arc(actEdge[1], -5, passA - 7, 3.2f); Arc(actEdge[1], passA + 7, 95, 3.2f);
        Arc(actEdge[2], -5, pass2 - 6, 5f); Arc(actEdge[2], pass2 + 6, 95, 5f);
        // the last act's heights: spurs of mountains round the end, not a wall
        for (int k = 0; k < 5; k++) { float a = 8 + k * 18 + (R() - .5f) * 8; var p0 = At(.9f + R() * .08f, a); var p1 = At(1.2f, a + (R() - .5f) * 10); Ridge(p0.x, p0.y, p1.x, p1.y, 8.5f + R() * 2); }
        // Act 1: home, Dungeon1 and Forest1 close by. Act 2: the village and its tower, a camp. Act 3: ruins, a shrine. Act 4: the end.
        // each world places its stops a little differently within the same acts
        var lrng = new System.Random(example == "longroad" ? 11 : example == "pass" ? 23 : 37);
        UnityEngine.Vector2 Place(float r, float a, float jr = .05f, float ja = 14) => At(UnityEngine.Mathf.Max(.1f, r + ((float)lrng.NextDouble() - .5f) * 2 * jr), UnityEngine.Mathf.Clamp(a + ((float)lrng.NextDouble() - .5f) * 2 * ja, 4, 86));
        pois["start"] = At(.04f, 45); pois["forest"] = Place(.22f, 22); pois["crypt"] = Place(.25f, 66); pois["mill"] = Place(.15f, 84, .03f, 4);
        pois["camp"] = Place(.46f, 14); pois["village"] = Place(.46f, passA + 6, .03f, 6); pois["tower"] = Place(.48f, 78);
        pois["ruin"] = Place(.72f, pass2 - 20); pois["shrine"] = Place(.72f, pass2 + 22);
        pois["peak"] = Place(1.0f, 45, .03f, 12);
        routes = new[] { new[] { "start", "forest", "camp" }, new[] { "start", "crypt", "tower" }, new[] { "start", "mill" },
            new[] { "forest", "village" }, new[] { "crypt", "village" }, new[] { "village", "ruin", "peak" }, new[] { "village", "shrine", "peak" } };

        float ActDist0(float u, float v) => UnityEngine.Mathf.Sqrt((u + .85f) * (u + .85f) + (.85f - v) * (.85f - v)) / 1.9f;
        float DistSeg(float u, float v, UnityEngine.Vector4 s)
        {
            var a = new UnityEngine.Vector2(s.x, s.y); var b = new UnityEngine.Vector2(s.z, s.w); var p = new UnityEngine.Vector2(u, v);
            float t = UnityEngine.Mathf.Clamp01(UnityEngine.Vector2.Dot(p - a, b - a) / (b - a).sqrMagnitude); return (a + (b - a) * t - p).magnitude;
        }
        float Height(float u, float v)
        {
            float n = UnityEngine.Mathf.PerlinNoise(u * 2.6f + 5, v * 2.6f + 9) * 2.2f + UnityEngine.Mathf.PerlinNoise(u * 7 + 1, v * 7 + 2) * .7f;
            float m = 0;
            for (int i = 0; i < ridges.Count; i++)
            {
                float d = DistSeg(u + (UnityEngine.Mathf.PerlinNoise(u * 4, v * 4) - .5f) * .12f, v, ridges[i]);
                float k = UnityEngine.Mathf.Clamp01(1 - d / .2f);
                float jag = .55f + .45f * (1 - UnityEngine.Mathf.Abs(UnityEngine.Mathf.PerlinNoise(u * 9 + 40, v * 9 + 40) * 2 - 1));
                m = UnityEngine.Mathf.Max(m, k * k * ridgeHeights[i] * jag);
            }
            // the pass: a saddle where the road crosses the range
            float edgeFall = UnityEngine.Mathf.SmoothStep(0, 1, UnityEngine.Mathf.Clamp01((1 - UnityEngine.Mathf.Max(UnityEngine.Mathf.Abs(u), UnityEngine.Mathf.Abs(v))) * 5f));
            float h = (n + m) * UnityEngine.Mathf.Lerp(.6f, 1f, edgeFall) + 1.6f;
            if (u > .4f && UnityEngine.Mathf.Abs(v) < .45f) { float k = UnityEngine.Mathf.Clamp01((u - .4f) / .2f) * UnityEngine.Mathf.Clamp01((.45f - UnityEngine.Mathf.Abs(v)) / .15f); h = UnityEngine.Mathf.Lerp(h, h > 3.1f ? 3.1f + (h - 3.1f) * .22f : h, k); }
            if (example == "pass") { float cd = UnityEngine.Mathf.Abs(ActDist0(u, v) - .66f + (UnityEngine.Mathf.PerlinNoise(u * 5, v * 5) - .5f) * .05f); float bridge = UnityEngine.Mathf.Abs(UnityEngine.Mathf.Atan2(.85f - v, u + .85f) * UnityEngine.Mathf.Rad2Deg - pass2); if (cd < .025f && bridge > 4) h -= (1 - cd / .025f) * 2.4f; }
            if (h > 3.2f && !parchment) { float step = .95f, t = (h - 3.2f) / step, f = t - UnityEngine.Mathf.Floor(t); h = 3.2f + (UnityEngine.Mathf.Floor(t) + UnityEngine.Mathf.SmoothStep(0, 1, UnityEngine.Mathf.Clamp01((f - .7f) / .3f))) * step; }
            return parchment ? .5f + (h - .5f) * .3f : h;   // the parchment lies nearly flat; its mountains are drawn and modelled
        }
        float HeightReal(float u, float v) { bool p = parchment; parchment = false; float h = Height(u, v); parchment = p; return h; }
        void BiomeAt(float u, float v, out int a, out int b, out float mix)
        {
            float wu = u + (UnityEngine.Mathf.PerlinNoise(u * 3 + 11, v * 3 + 3) - .5f) * .3f;
            float wv = v + (UnityEngine.Mathf.PerlinNoise(u * 3 + 31, v * 3 + 17) - .5f) * .3f;
            float d1 = 99, d2 = 99; a = 0; b = 0;
            foreach (var s in seeds) { float d = (s.x - wu) * (s.x - wu) * 1.3f + (s.y - wv) * (s.y - wv); if (d < d1) { d2 = d1; b = a; d1 = d; a = (int)s.z; } else if (d < d2) { d2 = d; b = (int)s.z; } }
            float edge = UnityEngine.Mathf.Sqrt(d2) - UnityEngine.Mathf.Sqrt(d1);
            mix = a == b ? 0 : UnityEngine.Mathf.Clamp01(.5f - edge / .14f);
            // high ground is snow whatever the land below says
            float h = HeightReal(u, v);
            if (h > 7.4f) { b = a; a = 4; mix = UnityEngine.Mathf.Clamp01((8.2f - h) / .8f) * .5f; }
        }

        // ── Palettes per look ──
        UnityEngine.Color[] ground, groundAlt;
        if (stylized)
        {
            ground = new[] { new UnityEngine.Color(.4f, .55f, .26f), new UnityEngine.Color(.2f, .42f, .25f), new UnityEngine.Color(.78f, .5f, .2f), new UnityEngine.Color(.42f, .38f, .45f), new UnityEngine.Color(.9f, .93f, 1f), new UnityEngine.Color(.8f, .5f, .3f), new UnityEngine.Color(.3f, .42f, .3f), new UnityEngine.Color(.75f, .7f, .35f) };
            groundAlt = new[] { new UnityEngine.Color(.58f, .72f, .32f), new UnityEngine.Color(.26f, .5f, .3f), new UnityEngine.Color(.88f, .62f, .25f), new UnityEngine.Color(.5f, .45f, .52f), new UnityEngine.Color(.8f, .86f, .95f), new UnityEngine.Color(.9f, .62f, .38f), new UnityEngine.Color(.36f, .5f, .34f), new UnityEngine.Color(.62f, .7f, .3f) };
        }
        else if (parchment)
        {
            // watercolour washes over paper
            ground = new[] { new UnityEngine.Color(.72f, .7f, .5f), new UnityEngine.Color(.55f, .6f, .45f), new UnityEngine.Color(.78f, .62f, .42f), new UnityEngine.Color(.62f, .56f, .5f), new UnityEngine.Color(.88f, .86f, .8f), new UnityEngine.Color(.8f, .6f, .45f), new UnityEngine.Color(.56f, .58f, .48f), new UnityEngine.Color(.8f, .74f, .5f) };
            groundAlt = ground;
        }
        else
        {
            ground = new[] { new UnityEngine.Color(.3f, .36f, .18f), new UnityEngine.Color(.17f, .24f, .13f), new UnityEngine.Color(.38f, .3f, .16f), new UnityEngine.Color(.32f, .26f, .26f), new UnityEngine.Color(.66f, .7f, .76f), new UnityEngine.Color(.5f, .33f, .21f), new UnityEngine.Color(.2f, .23f, .16f), new UnityEngine.Color(.42f, .4f, .2f) };
            groundAlt = new[] { new UnityEngine.Color(.37f, .4f, .2f), new UnityEngine.Color(.21f, .28f, .15f), new UnityEngine.Color(.45f, .33f, .18f), new UnityEngine.Color(.35f, .32f, .27f), new UnityEngine.Color(.64f, .67f, .7f), new UnityEngine.Color(.57f, .4f, .26f), new UnityEngine.Color(.25f, .27f, .18f), new UnityEngine.Color(.5f, .45f, .22f) };
        }

        // ── Roads ──
        var roadPts = new System.Collections.Generic.List<UnityEngine.Vector2>();
        var legs = o.legs;
        int ri = 0;
        foreach (var route in routes)
            for (int i = 0; i < route.Length - 1; i++, ri++)
            {
                var a = pois[route[i]]; var b = pois[route[i + 1]];
                var mid = (a + b) * .5f + new UnityEngine.Vector2(-(b - a).y, (b - a).x) * (UnityEngine.Mathf.PerlinNoise(ri * 3.1f, 2) - .5f) * .45f;
                legs.Add((route[i], route[i + 1], mid));
                for (int k = 0; k <= 70; k++) { float t = k / 70f; var p = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * mid + t * t * b; p += new UnityEngine.Vector2(UnityEngine.Mathf.PerlinNoise(t * 6, ri) - .5f, UnityEngine.Mathf.PerlinNoise(ri, t * 6) - .5f) * .03f; roadPts.Add(p); }
            }
        float RoadDist(float u, float v) { float best = 9; foreach (var p in roadPts) { float d = (p.x - u) * (p.x - u) + (p.y - v) * (p.y - v); if (d < best) best = d; } return UnityEngine.Mathf.Sqrt(best); }

        // ── Terrain ──
        int W = 156, H = 212;
        var verts = new UnityEngine.Vector3[(W + 1) * (H + 1)]; var uvs = new UnityEngine.Vector2[verts.Length];
        for (int z = 0; z <= H; z++) for (int x = 0; x <= W; x++) { float u = x / (float)W * 2 - 1, v = z / (float)H * 2 - 1; verts[z * (W + 1) + x] = new UnityEngine.Vector3(u * HX, Height(u, v), v * HZ); uvs[z * (W + 1) + x] = new UnityEngine.Vector2(x / (float)W, z / (float)H); }
        var tris = new int[W * H * 6]; int ti = 0;
        for (int z = 0; z < H; z++) for (int x = 0; x < W; x++) { int q = z * (W + 1) + x; tris[ti++] = q; tris[ti++] = q + W + 1; tris[ti++] = q + 1; tris[ti++] = q + 1; tris[ti++] = q + W + 1; tris[ti++] = q + W + 2; }
        o.W = W; o.H = H; o.heights = new float[verts.Length]; for (int hi = 0; hi < verts.Length; hi++) o.heights[hi] = verts[hi].y;
        var mesh = new UnityEngine.Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; mesh.vertices = verts; mesh.uv = uvs; mesh.triangles = tris; mesh.RecalculateNormals();
        int TW = parchment ? 624 : UnityEngine.Mathf.RoundToInt(HX * 2f * CampaignMapBuilder.MapTexelsPerUnit), TH = parchment ? 848 : UnityEngine.Mathf.RoundToInt(HZ * 2f * CampaignMapBuilder.MapTexelsPerUnit);
        var tex = new UnityEngine.Texture2D(TW, TH, UnityEngine.TextureFormat.RGBA32, false) { filterMode = stylized || parchment ? UnityEngine.FilterMode.Bilinear : UnityEngine.FilterMode.Point, wrapMode = UnityEngine.TextureWrapMode.Clamp };
        var px = new UnityEngine.Color[TW * TH];
        var road = new float[TW * TH];
        float rw = parchment ? 3.4f : 2.8f;
        for (int idx = 0; idx < 0; idx++)
        {
            var p = roadPts[idx];
            if (parchment && (idx / 4) % 2 == 1) continue;   // a dashed ink line
            int cx = (int)((p.x + 1) * .5f * TW), cy = (int)((p.y + 1) * .5f * TH);
            int rr = (int)UnityEngine.Mathf.Ceil(rw);
            for (int dy = -rr; dy <= rr; dy++) for (int dx = -rr; dx <= rr; dx++) { int x = cx + dx, y = cy + dy; if (x < 0 || y < 0 || x >= TW || y >= TH) continue; float d = UnityEngine.Mathf.Sqrt(dx * dx + dy * dy); road[y * TW + x] = UnityEngine.Mathf.Max(road[y * TW + x], d <= rw * .55f ? 1f : d <= rw ? .45f : 0); }
        }
        var biomeA = new int[TW * TH];
        var peakP = pois["peak"];
        // How thick the woods are at a place (the same rule the scatter below uses), so the forest floor can be darkened under them.
        float WoodAt(int bio, float u, float v)
        {
            float g = UnityEngine.Mathf.PerlinNoise(u * 6 + 50, v * 6 + 50);
            return bio == 1 ? UnityEngine.Mathf.Clamp01(.8f * g * g * 2.4f) : bio == 2 ? UnityEngine.Mathf.Clamp01(.6f * g * g * 2.2f) : bio == 4 ? .25f * g * 1.6f : bio == 6 ? .4f * g * 1.8f : bio == 0 ? .14f * g * g * 3 : 0;
        }
        // How far the end's corruption has spread here (1 at the fortress).
        float Rot(float u, float v) { float d = (new UnityEngine.Vector2(u, v) - peakP).magnitude; return UnityEngine.Mathf.Clamp01(1 - d / .42f + (UnityEngine.Mathf.PerlinNoise(u * 8 + 70, v * 8 + 70) - .5f) * .35f); }
        var lightDir = new UnityEngine.Vector3(.55f, .75f, .38f).normalized;   // from the near-left, above: the side the player stands on
        for (int y = 0; y < TH; y++) for (int x = 0; x < TW; x++)
        {
            float u = x / (float)TW * 2 - 1, v = y / (float)TH * 2 - 1;
            BiomeAt(u, v, out int a, out int b, out float mix);
            float n = UnityEngine.Mathf.PerlinNoise(x * .09f * (312f / TW), y * .09f * (312f / TW)), fine = UnityEngine.Mathf.PerlinNoise(x * .5f + 7, y * .5f + 3);
            var ca = UnityEngine.Color.Lerp(ground[a], groundAlt[a], n); var cb = UnityEngine.Color.Lerp(ground[b], groundAlt[b], n);
            UnityEngine.Color c = stylized || parchment ? UnityEngine.Color.Lerp(ca, cb, UnityEngine.Mathf.SmoothStep(0, 1, mix)) : (fine < mix ? cb : ca);
            int bio = mix > .5f ? b : a;
            biomeA[y * TW + x] = bio;
            float h = HeightReal(u, v);
            float hx = HeightReal(u + .006f, v) - HeightReal(u - .006f, v), hz = HeightReal(u, v + .006f) - HeightReal(u, v - .006f);
            // the slope's normal in world units (u spans 2*HX, v 2*HZ)
            var nrm = new UnityEngine.Vector3(-hx / (.012f * HX), 1, -hz / (.012f * HZ)).normalized;
            float shade = UnityEngine.Vector3.Dot(nrm, lightDir);
            float slope = 1 - nrm.y;
            float wood = UnityEngine.Mathf.Lerp(WoodAt(a, u, v), WoodAt(b, u, v), mix);
            float rot = Rot(u, v);
            if (!parchment)
            {
                bool inner = UnityEngine.Mathf.Max(UnityEngine.Mathf.Abs(u), UnityEngine.Mathf.Abs(v)) < .82f;
                if (slope > .25f && inner) c = UnityEngine.Color.Lerp(c, stylized ? new UnityEngine.Color(.62f, .56f, .52f) : new UnityEngine.Color(.43f, .4f, .37f), UnityEngine.Mathf.Clamp01((slope - .25f) * 3f));
                if (h > 4.2f && bio != 4) c = UnityEngine.Color.Lerp(c, new UnityEngine.Color(.46f, .44f, .42f), UnityEngine.Mathf.Clamp01((h - 4.2f) * .5f) * .8f);
                if (bio == 4) c = UnityEngine.Color.Lerp(new UnityEngine.Color(.93f, .95f, 1f), new UnityEngine.Color(.62f, .7f, .86f), UnityEngine.Mathf.Clamp01((.85f - shade) * 1.6f)) * (.97f + fine * .05f);   // bare rock above the tree line
                if (h > 7.2f && nrm.y > .55f) c = UnityEngine.Color.Lerp(c, new UnityEngine.Color(.92f, .94f, .98f), UnityEngine.Mathf.Clamp01((h - 7.2f) * 1.2f));   // snow caps on the flats of the peaks
                // forest floor: dark and mossy under thick woods
                c = UnityEngine.Color.Lerp(c, c * new UnityEngine.Color(.7f, .78f, .68f), UnityEngine.Mathf.Clamp01(wood * 1.2f));
                // detail per land: what a small painted ground shows up close
                float spk = ((x * 73856093) ^ (y * 19349663) ^ 0x5bd1e995) % 4096 / 4096f; if (spk < 0) spk = -spk;
                float spk2 = ((x * 19349663) ^ (y * 83492791)) % 4096 / 4096f; if (spk2 < 0) spk2 = -spk2;
                switch (bio)
                {
                    case 0: case 7:
                        if (spk > .996f) c = spk2 < .33f ? new UnityEngine.Color(.95f, .85f, .3f) : spk2 < .66f ? new UnityEngine.Color(.9f, .9f, .85f) : new UnityEngine.Color(.62f, .45f, .8f);
                        else if (spk > .95f) c *= 1.08f; else if (spk < .04f) c *= .88f; break;
                    case 1: if (spk > .95f) c *= .78f; else if (spk > .9f) c *= 1.1f; break;
                    case 2: if (spk > .95f) c = UnityEngine.Color.Lerp(c, spk2 < .5f ? new UnityEngine.Color(.75f, .32f, .12f) : new UnityEngine.Color(.85f, .6f, .2f), .45f); break;
                    case 3:
                        if (UnityEngine.Mathf.Abs(UnityEngine.Mathf.PerlinNoise(u * 38 + 2, v * 38 + 5) - .5f) < .014f) c *= .62f;
                        else if (spk > .998f) c = UnityEngine.Color.Lerp(c, new UnityEngine.Color(.84f, .8f, .7f), .5f);
                        else if (spk > .985f) c = UnityEngine.Color.Lerp(c, new UnityEngine.Color(.4f, .33f, .4f), .35f); break;
                    case 6:
                        if (UnityEngine.Mathf.PerlinNoise(u * 11 + 9, v * 11 + 4) > .63f) c = UnityEngine.Color.Lerp(c, new UnityEngine.Color(.11f, .14f, .1f), .75f);
                        else if (spk > .97f) c = UnityEngine.Color.Lerp(c, new UnityEngine.Color(.45f, .55f, .2f), .45f); break;
                    case 5: if (UnityEngine.Mathf.Sin(h * 5.5f + UnityEngine.Mathf.PerlinNoise(u * 6, v * 6) * 2) > .65f) c *= .84f; else if (spk > .985f) c *= 1.08f; break;
                    case 4: c *= new UnityEngine.Color(.96f, .98f, 1.04f); if (spk > .97f) c *= .93f; break;
                }
                // dry riverbeds wind through the dry lands: a dark channel with pale banks
                if (bio == 3 || bio == 5 || bio == 6)
                {
                    float rv = UnityEngine.Mathf.Abs(UnityEngine.Mathf.PerlinNoise(u * 2.4f + 61, v * 2.4f + 13) - .5f);
                    if (rv < .012f) c = UnityEngine.Color.Lerp(c, bio == 5 ? new UnityEngine.Color(.36f, .22f, .15f) : new UnityEngine.Color(.15f, .14f, .14f), .75f);
                    else if (rv < .02f) c = UnityEngine.Color.Lerp(c, bio == 5 ? new UnityEngine.Color(.82f, .62f, .44f) : new UnityEngine.Color(.5f, .47f, .44f), .45f);
                }
                // the end's corruption: ash, a purple dark, then glowing cracks right round the fortress
                if (rot > 0)
                {
                    c = UnityEngine.Color.Lerp(c, new UnityEngine.Color(.13f, .1f, .12f), UnityEngine.Mathf.Clamp01(rot * 1.1f));
                    float crack = UnityEngine.Mathf.Abs(UnityEngine.Mathf.PerlinNoise(u * 26 + 3, v * 26 + 9) - .5f);
                    if (rot > .3f && crack < .012f + rot * .02f) c = UnityEngine.Color.Lerp(c, new UnityEngine.Color(1f, .35f, .12f), .9f);
                }
                if (h > 3.2f) { float tt = (h - 3.2f) / .95f, ff = tt - UnityEngine.Mathf.Floor(tt); if (ff > .93f || ff < .03f) c *= 1.2f; }
                // valleys sit in shade, ridges catch light
                float around = (HeightReal(u + .02f, v) + HeightReal(u - .02f, v) + HeightReal(u, v + .02f) + HeightReal(u, v - .02f)) * .25f;
                float cav = UnityEngine.Mathf.Clamp((h - around) * .9f, -.25f, .15f);
                c *= 1 + cav;
                // the journey's mood: the further from home, the colder, darker and more washed out
                float jd = UnityEngine.Mathf.Clamp01(ActDist(u, v));
                float grey = (c.r + c.g + c.b) / 3f;
                c = UnityEngine.Color.Lerp(c, new UnityEngine.Color(grey, grey, grey), jd * jd * .15f);
                c *= UnityEngine.Color.Lerp(new UnityEngine.Color(1.06f, 1.02f, .94f), new UnityEngine.Color(.92f, .88f, 1.02f), jd);
                // light on the slopes: stepped bands, the way the pixel art is lit (three wider bands for the stylized look)
                float steps = stylized ? 3 : 6;
                float lightK = UnityEngine.Mathf.Round(UnityEngine.Mathf.Clamp01(shade * 1.35f - .25f) * steps) / steps;
                c *= UnityEngine.Mathf.Lerp(stylized ? .78f : .74f, stylized ? 1.12f : 1.16f, lightK);
                // roads with darker edges and a worn middle
                float rd = road[y * TW + x];
                c = UnityEngine.Color.Lerp(c, stylized ? new UnityEngine.Color(.86f, .74f, .52f) : (rd > .9f ? new UnityEngine.Color(.56f, .46f, .31f) : new UnityEngine.Color(.36f, .28f, .19f)), rd * .9f);
                px[y * TW + x] = c * (stylized ? 1f : (.94f + fine * .09f));
            }
            else
            {
                float stain = UnityEngine.Mathf.PerlinNoise(x * .012f + 3, y * .012f + 8);
                var paper = UnityEngine.Color.Lerp(new UnityEngine.Color(.94f, .87f, .72f), new UnityEngine.Color(.8f, .68f, .48f), stain * .8f) * (.96f + fine * .06f);
                c = UnityEngine.Color.Lerp(paper, c * paper, .5f);
                var ink = new UnityEngine.Color(.28f, .19f, .13f);
                // hatching on slopes facing away from the light, densest on the mountains
                if (shade < .82f && h > 2.2f) { float k = UnityEngine.Mathf.Clamp01((.82f - shade) * 3f); if ((x + y) % 4 == 0) c = UnityEngine.Color.Lerp(c, ink, .7f * k); }
                // stipple in the woods
                if (wood > .25f && ((x * 7 + y * 13) % 11 == 0) && fine > .45f) c = UnityEngine.Color.Lerp(c, new UnityEngine.Color(.22f, .3f, .2f), .6f);
                if (rot > 0) c = UnityEngine.Color.Lerp(c, new UnityEngine.Color(.35f, .25f, .25f), rot * .6f);
                c = UnityEngine.Color.Lerp(c, ink, road[y * TW + x] * .85f);
                float e = 1 - UnityEngine.Mathf.Max(UnityEngine.Mathf.Abs(u), UnityEngine.Mathf.Abs(v));
                c *= UnityEngine.Mathf.Lerp(.55f, 1f, UnityEngine.Mathf.Clamp01(e * 9f + UnityEngine.Mathf.PerlinNoise(x * .05f, y * .05f) * .3f));
                // a double ruled border
                float bu = UnityEngine.Mathf.Abs(u), bv = UnityEngine.Mathf.Abs(v);
                if (UnityEngine.Mathf.Abs(UnityEngine.Mathf.Max(bu, bv) - .955f) < .003f || UnityEngine.Mathf.Abs(UnityEngine.Mathf.Max(bu, bv) - .942f) < .0015f) c = UnityEngine.Color.Lerp(c, ink, .85f);
                px[y * TW + x] = c;
            }
        }
        if (parchment)
        {
            var ink = new UnityEngine.Color(.28f, .19f, .13f);
            for (int y = 1; y < TH - 1; y++) for (int x = 1; x < TW - 1; x++)
            {
                int i = y * TW + x;
                if (biomeA[i] != biomeA[i + 1] || biomeA[i] != biomeA[i + TW]) px[i] = UnityEngine.Color.Lerp(px[i], ink, .5f);
            }
            // mountain marks: little inked peaks drawn on high ground
            for (int y = 6; y < TH - 6; y += 9) for (int x = 6; x < TW - 6; x += 9)
            {
                float u = x / (float)TW * 2 - 1, v = y / (float)TH * 2 - 1;
                if (HeightReal(u, v) < 4.2f) continue;
                int jx = x + (int)((UnityEngine.Mathf.PerlinNoise(x, y) - .5f) * 6), sz = 4 + (int)(HeightReal(u, v) * .4f);
                for (int k = 0; k <= sz; k++) { foreach (var p in new[] { (jx - sz + k, y + k), (jx + sz - k, y + k) }) { int i = p.Item2 * TW + p.Item1; if (i >= 0 && i < px.Length) px[i] = UnityEngine.Color.Lerp(px[i], ink, .9f); } }
                for (int k = 1; k < sz; k++) { int i = (y + k + 1) * TW + jx + sz - k - 1; if (i >= 0 && i < px.Length && k % 2 == 0) px[i] = UnityEngine.Color.Lerp(px[i], ink, .5f); }
            }
            // a compass rose in the empty near-right corner
            var cc = new UnityEngine.Vector2(.08f * TW, .1f * TH); float cr = .055f * TW;
            for (int y = 0; y < TH; y++) for (int x = 0; x < TW; x++)
            {
                float dx = x - cc.x, dy = y - cc.y, d = UnityEngine.Mathf.Sqrt(dx * dx + dy * dy);
                if (d > cr * 1.15f) continue;
                int i = y * TW + x;
                if (UnityEngine.Mathf.Abs(d - cr) < .9f || UnityEngine.Mathf.Abs(d - cr * .82f) < .6f) { px[i] = UnityEngine.Color.Lerp(px[i], ink, .85f); continue; }
                float ang = UnityEngine.Mathf.Atan2(dy, dx); float star = UnityEngine.Mathf.Abs(UnityEngine.Mathf.Cos(ang * 2));
                float tri = 1 - UnityEngine.Mathf.Abs(((ang + 3.1416f) / 1.5708f) % 1 - .5f) * 2; float tri2 = 1 - UnityEngine.Mathf.Abs(((ang + 3.1416f + .7854f) / 1.5708f) % 1 - .5f) * 2; float limit = cr * UnityEngine.Mathf.Max(.15f + .85f * UnityEngine.Mathf.Pow(tri, 4), .1f + .5f * UnityEngine.Mathf.Pow(tri2, 4));
                if (d < limit) px[i] = UnityEngine.Color.Lerp(px[i], ((int)((ang + 3.1416f) / 0.7854f)) % 2 == 0 ? ink : new UnityEngine.Color(.75f, .6f, .4f), .8f);
            }
        }

        tex.SetPixels(px); tex.Apply();
        var gm = Mat(UnityEngine.Color.white, parchment ? .02f : .05f); gm.SetTexture("_BaseMap", tex);
        var terrain = new UnityEngine.GameObject("terrain"); terrain.transform.SetParent(root.transform); terrain.transform.position = C;
        terrain.AddComponent<UnityEngine.MeshFilter>().sharedMesh = mesh; terrain.AddComponent<UnityEngine.MeshRenderer>().sharedMaterial = gm;
        {
            // strata: a grass lip, topsoil, clay, then bands of rock, point-filtered like the ground
            // The strata repeat every 8 segments (about 7.1 units) along and span 6 units down: at the map's density.
            int SW = UnityEngine.Mathf.RoundToInt(106f / 120f * 8f * CampaignMapBuilder.MapTexelsPerUnit), SH = UnityEngine.Mathf.RoundToInt(6f * CampaignMapBuilder.MapTexelsPerUnit);
            var st = new UnityEngine.Texture2D(SW, SH, UnityEngine.TextureFormat.RGBA32, false) { filterMode = UnityEngine.FilterMode.Point, wrapMode = UnityEngine.TextureWrapMode.Repeat };
            for (int y = 0; y < SH; y++) for (int x = 0; x < SW; x++)
            {
                float t = y / (SH - 1f), w = UnityEngine.Mathf.PerlinNoise(x * 12f / SW, y * 7f / SH) * .08f;
                UnityEngine.Color c2 = t > .94f - w ? new UnityEngine.Color(.3f, .38f, .18f) : t > .78f - w ? new UnityEngine.Color(.3f, .22f, .15f) : t > .6f - w ? new UnityEngine.Color(.42f, .3f, .2f) : (UnityEngine.Mathf.Sin(t * 40 + UnityEngine.Mathf.PerlinNoise(x * .1f, t * 3) * 3) > .2f ? new UnityEngine.Color(.4f, .38f, .36f) : new UnityEngine.Color(.32f, .3f, .29f));
                float sp = ((x * 92821) ^ (y * 68917)) % 97 / 97f; if (sp > .93f) c2 *= .8f;
                st.SetPixel(x, y, c2);
            }
            st.Apply();
            var sm = Mat(UnityEngine.Color.white, .05f); sm.SetTexture("_BaseMap", st);
            var sv = new System.Collections.Generic.List<UnityEngine.Vector3>(); var suv = new System.Collections.Generic.List<UnityEngine.Vector2>(); var stri = new System.Collections.Generic.List<int>();
            void Side(System.Func<float, UnityEngine.Vector2> edge, int n, bool flip)
            {
                int b0 = sv.Count;
                for (int k = 0; k <= n; k++)
                {
                    float t = k / (float)n; var e = edge(t); float hh = Height(e.x, e.y);
                    sv.Add(new UnityEngine.Vector3(e.x * HX, hh, e.y * HZ)); sv.Add(new UnityEngine.Vector3(e.x * HX, .5f, e.y * HZ));
                    suv.Add(new UnityEngine.Vector2(t * n / 8f, 1)); suv.Add(new UnityEngine.Vector2(t * n / 8f, 1 - (hh - .5f) / 6f));
                    if (k > 0) { int q = b0 + (k - 1) * 2; if (flip) stri.AddRange(new[] { q, q + 2, q + 1, q + 1, q + 2, q + 3 }); else stri.AddRange(new[] { q, q + 1, q + 2, q + 1, q + 3, q + 2 }); }
                }
            }
            Side(t => new UnityEngine.Vector2(-1, t * 2 - 1), 120, false); Side(t => new UnityEngine.Vector2(1, t * 2 - 1), 120, true);
            Side(t => new UnityEngine.Vector2(t * 2 - 1, -1), 90, true); Side(t => new UnityEngine.Vector2(t * 2 - 1, 1), 90, false);
            var skirt = new UnityEngine.Mesh(); skirt.SetVertices(sv); skirt.SetUVs(0, suv); skirt.SetTriangles(stri, 0); skirt.RecalculateNormals();
            var sg = new UnityEngine.GameObject("cut edge"); sg.transform.SetParent(root.transform); sg.transform.position = C;
            sg.AddComponent<UnityEngine.MeshFilter>().sharedMesh = skirt; var smr = sg.AddComponent<UnityEngine.MeshRenderer>(); smr.sharedMaterial = sm;
        }
        var board = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cube); UnityEngine.Object.DestroyImmediate(board.GetComponent<UnityEngine.Collider>());
        board.transform.SetParent(root.transform); board.transform.position = C + new UnityEngine.Vector3(0, .25f, 0); board.transform.localScale = new UnityEngine.Vector3(HX * 2 + 1.6f, .5f, HZ * 2 + 1.6f);
        board.GetComponent<UnityEngine.Renderer>().sharedMaterial = parchment ? Mat(new UnityEngine.Color(.55f, .42f, .28f), .1f) : Mat(new UnityEngine.Color(.2f, .14f, .1f), .3f);

        var plinths = new System.Collections.Generic.List<UnityEngine.Vector4>();   // (u, v, radius in world units, top height)
        UnityEngine.Vector3 W3(float u, float v, float lift = 0)
        {
            float h = Height(u, v);
            foreach (var pl in plinths) { float dx = (u - pl.x) * HX, dz = (v - pl.y) * HZ; if (dx * dx + dz * dz < pl.z * pl.z) h = pl.w; }
            return C + new UnityEngine.Vector3(u * HX, h + lift, v * HZ);
        }
        var block = new UnityEngine.MaterialPropertyBlock();
        var prefabs = new System.Collections.Generic.Dictionary<string, UnityEngine.GameObject>();
        UnityEngine.Color? Grade(UnityEngine.Color? tint) => stylized ? (UnityEngine.Color?)((tint ?? UnityEngine.Color.white) * new UnityEngine.Color(1.04f, 1.02f, .96f)) : tint;
        UnityEngine.GameObject Prop(string path, UnityEngine.Vector3 pos, float yaw, float scale, UnityEngine.Color? tint = null)
        {
            if (!prefabs.TryGetValue(path, out var pf)) prefabs[path] = pf = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Synty/" + path + ".prefab");
            if (pf == null) return null;
            var g = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(pf, root.transform);
            g.transform.SetPositionAndRotation(pos, UnityEngine.Quaternion.Euler(0, yaw, 0));
            g.transform.localScale = UnityEngine.Vector3.one * scale;
            foreach (var col in g.GetComponentsInChildren<UnityEngine.Collider>()) UnityEngine.Object.DestroyImmediate(col);
            var t = Grade(tint);
            if (t.HasValue) Tint(g, t.Value);
            return g;
        }
        const string FK = "PolygonFantasyKingdom/Prefabs/Environments/", DF = "PolygonDarkFantasy/Prefabs/Environment/", B = "PolygonFantasyKingdom/Prefabs/Buildings/Presets/", FP = "PolygonFantasyKingdom/Prefabs/Props/";
        string[] pines = { FK + "SM_Env_Tree_Pine_Cluster_01", FK + "SM_Env_Tree_Pine_Cluster_02", FK + "SM_Env_Tree_Pine_Cluster_04", FK + "SM_Env_Tree_Pine_Cluster_05" };
        string[] rounds = { FK + "SM_Env_Tree_Round_01", FK + "SM_Env_Tree_Round_02", FK + "SM_Env_Tree_Round_03", FK + "SM_Env_Tree_Round_04" };
        string[] thins = { FK + "SM_Env_Tree_Thin_01", FK + "SM_Env_Tree_Thin_03", FK + "SM_Env_Tree_Thin_05" };
        string[] deads = { FK + "SM_Env_Tree_Dead_01", FK + "SM_Env_Tree_Dead_02", FK + "SM_Env_Tree_Dead_03", FK + "SM_Env_Tree_Dead_04", FK + "SM_Env_Tree_Burnt_01" };
        string[] rocks = { DF + "SM_Env_Rock_01", DF + "SM_Env_Rock_02", DF + "SM_Env_Rock_03" };
        string[] bushes = { FK + "SM_Env_Bush_Cluster_01", FK + "SM_Env_Bush_Cluster_03", FK + "SM_Env_Bush_04" };
        string[] houses = { B + "SM_Bld_Preset_House_01_A_Optimized", B + "SM_Bld_Preset_House_03_Optimized", B + "SM_Bld_Preset_House_05_Optimized", B + "SM_Bld_Preset_House_07_Optimized", B + "SM_Bld_Preset_Hut_01_Optimized", B + "SM_Bld_Preset_Tavern_01_Optimized", B + "SM_Bld_Preset_Stables_01_Optimized", B + "SM_Bld_Preset_Blacksmith_01_Optimized" };
        bool NearPoi(float u, float v, float r) { foreach (var p in pois.Values) if ((p.x - u) * (p.x - u) + (p.y - v) * (p.y - v) < r * r) return true; return false; }

        // ── Scatter: what grows where. On parchment the trees stand thinner, as markers of the woods ──
        float s0 = .17f;
        float density = parchment ? .35f : 1f;
        int placed = 0;
        for (int i = 0; i < 11000; i++)
        {
            float u = R() * 1.94f - .97f, v = R() * 1.94f - .97f;
            if (NearPoi(u, v, .1f) || RoadDist(u, v) < .022f) continue;
            if (R() > density) continue;
            BiomeAt(u, v, out int a, out int b, out float mix);
            int bio = R() < mix * .9f ? b : a;
            float h = HeightReal(u, v);
            float k = R(), grove = UnityEngine.Mathf.PerlinNoise(u * 6 + 50, v * 6 + 50);
            if (h > 7.2f) { if (k < .12f) Prop(rocks[rng.Next(3)], W3(u, v), R() * 360, s0 * (.5f + R() * .8f), new UnityEngine.Color(.95f, .96f, 1f)); continue; }
            switch (bio)
            {
                case 0: if (k < .14f * grove * grove * 3) Prop(rounds[rng.Next(4)], W3(u, v), R() * 360, s0 * (.8f + R() * .5f)); else if (k < .17f) Prop(bushes[rng.Next(3)], W3(u, v), R() * 360, s0 * 1.2f); else continue; break;
                case 1: if (grove > .38f && k < .85f * (grove - .38f) * 3.6f) if (example == "kingdom" && R() < .25f) Prop(thins[rng.Next(3)], W3(u, v), R() * 360, s0 * (.7f + R() * .4f), new UnityEngine.Color(1f, .95f, .7f)); else Prop(pines[rng.Next(4)], W3(u, v, -.1f), R() * 360, s0 * (.7f + R() * .5f)); else continue; break;
                case 2: if (k < .6f * grove * grove * 2.2f) Prop(rounds[rng.Next(4)], W3(u, v), R() * 360, s0 * (.8f + R() * .5f), UnityEngine.Color.Lerp(new UnityEngine.Color(1f, .55f, .2f), new UnityEngine.Color(1f, .82f, .3f), R())); else continue; break;
                case 3: if (k < .2f) Prop(deads[rng.Next(5)], W3(u, v), R() * 360, s0 * (.7f + R() * .6f)); else if (k < .26f) Prop(rocks[rng.Next(3)], W3(u, v), R() * 360, s0 * (.5f + R() * .5f)); else if (k < .275f) Prop(FP + "SM_Prop_Gravestone_0" + (1 + rng.Next(5)), W3(u, v), R() * 360, s0 * 1.5f); else continue; break;
                case 4: if (grove > .55f && k < .4f * (grove - .55f) * 3f) Prop(pines[rng.Next(4)], W3(u, v, -.1f), R() * 360, s0 * (.6f + R() * .5f), new UnityEngine.Color(.78f, .86f, .9f)); else if (k < .3f) Prop(rocks[rng.Next(3)], W3(u, v), R() * 360, s0 * (.5f + R() * .7f), new UnityEngine.Color(.9f, .92f, .95f)); else continue; break;
                case 5: if (k < .09f) Prop(rocks[rng.Next(3)], W3(u, v), R() * 360, s0 * (.6f + R() * .9f), new UnityEngine.Color(1f, .7f, .55f)); else if (k < .11f) Prop(deads[rng.Next(5)], W3(u, v), R() * 360, s0 * .8f, new UnityEngine.Color(.85f, .7f, .6f)); else continue; break;
                case 6: if (k < .4f * grove * 1.8f) Prop(thins[rng.Next(3)], W3(u, v), R() * 360, s0 * (.7f + R() * .5f), new UnityEngine.Color(.6f, .7f, .5f)); else if (k < .3f) Prop(deads[rng.Next(5)], W3(u, v), R() * 360, s0 * .7f, new UnityEngine.Color(.6f, .65f, .55f)); else continue; break;
                case 7: if (k < .03f) Prop(rounds[rng.Next(4)], W3(u, v), R() * 360, s0 * .9f); else continue; break;
            }
            placed++;
        }
        // Farm fields round the farmland: rows of crops in neat patches.
        for (int f = 0; f < 26; f++)
        {
            float u = R() * 1.8f - .9f, v = R() * 1.8f - .9f;
            BiomeAt(u, v, out int a, out int b, out float mix);
            if (a != 7 || NearPoi(u, v, .07f) || RoadDist(u, v) < .03f) continue;
            float yaw = rng.Next(4) * 90 + R() * 10;
            for (int k = 0; k < 3; k++) Prop(FK + "SM_Env_Ground_Farm_Row_0" + (1 + rng.Next(5)), W3(u + k * .012f, v, -.05f), yaw, s0 * 1.4f);
        }
        // Fences beside the roads at home
        for (int i = 0; i < roadPts.Count; i += 9)
        {
            var p = roadPts[i]; if (ActDist(p.x, p.y) > actEdge[0] || NearPoi(p.x, p.y, .06f)) continue;
            var q = roadPts[UnityEngine.Mathf.Min(i + 1, roadPts.Count - 1)]; var d = (q - p); if (d.sqrMagnitude < 1e-8f) continue; d.Normalize();
            var side = new UnityEngine.Vector2(-d.y, d.x) * .014f;
            Prop("PolygonTown/Prefabs/Environment/SM_Env_Fence_Wood_Straight_01", W3(p.x + side.x, p.y + side.y), UnityEngine.Mathf.Atan2(d.x * HX, d.y * HZ) * UnityEngine.Mathf.Rad2Deg + 90, s0 * 1.1f);
        }
        // Mesas in the badlands: flat-topped red rocks standing in groups
        for (int i = 0; i < 400; i++)
        {
            float u = R() * 1.9f - .95f, v = R() * 1.9f - .95f;
            BiomeAt(u, v, out int a, out int b, out float mix);
            if (a != 5 || NearPoi(u, v, .08f) || RoadDist(u, v) < .03f || R() > .25f) continue;
            Prop(FK + "SM_Env_Rock_Cliff_0" + (1 + rng.Next(5)), W3(u, v, -.3f), R() * 360, s0 * (.35f + R() * .35f), new UnityEngine.Color(1f, .62f, .45f));
        }
        // Hamlets along the roads: a few houses here and there.
        for (int hmt = 0; hmt < 16; hmt++)
        {
            var p = roadPts[rng.Next(roadPts.Count)];
            if (NearPoi(p.x, p.y, .16f)) continue;
            BiomeAt(p.x, p.y, out int a, out int b, out float mix);
            if (a == 3 || a == 4 || a == 5 || a == 6) continue;
            for (int k = 0; k < 3; k++) Prop(houses[rng.Next(houses.Length)], W3(p.x + (R() - .5f) * .05f + .03f, p.y + (R() - .5f) * .05f), R() * 360, .55f * .22f);
        }
        // Old ruins and standing stones across the dark lands.
        for (int k = 0; k < 18; k++)
        {
            float u = R() * 1.8f - .9f, v = R() * 1.8f - .9f;
            BiomeAt(u, v, out int a, out int b, out float mix);
            if (a != 3 || NearPoi(u, v, .08f)) continue;
            Prop(R() < .5f ? "PolygonDarkFantasy/Prefabs/Building/SM_Bld_Wall_Ruin_0" + (1 + rng.Next(3)) : "PolygonDarkFantasy/Prefabs/Props/SM_Prop_Statue_01", W3(u, v), R() * 360, .55f * (R() < .5f ? .25f : .5f));
        }

        // ── Points of interest, oversized so they read ──
        float big = .55f;
        // every place stands on a low round plinth with a brass rim, like a game piece: it reads as something to pick
        var plinthMat = Mat(new UnityEngine.Color(.36f, .3f, .22f), .15f); var rimMat = Mat(new UnityEngine.Color(.7f, .55f, .3f), .6f);
        foreach (var kv in pois)
        {
            var p = kv.Value; float rad = kv.Key == "peak" ? 4.2f : kv.Key == "village" || kv.Key == "start" ? 3.6f : 2.4f;
            float hMin = 99; for (int k = 0; k < 12; k++) { float a = k / 12f * 6.28f; hMin = UnityEngine.Mathf.Min(hMin, Height(p.x + UnityEngine.Mathf.Cos(a) * rad / HX, p.y + UnityEngine.Mathf.Sin(a) * rad / HZ)); }
            float top = UnityEngine.Mathf.Max(Height(p.x, p.y), hMin) + .25f;
            plinths.Add(new UnityEngine.Vector4(p.x, p.y, rad, top));
            o.radii[kv.Key] = rad;
        }
        const string DFP = "PolygonDarkFantasy/Prefabs/Props/", DFB = "PolygonDarkFantasy/Prefabs/Building/";
        void Glow(UnityEngine.Vector3 pos, UnityEngine.Color c, float intensity, float height = 9f, float range = 16f)
        {
            var g = new UnityEngine.GameObject("poi light"); g.transform.SetParent(root.transform); g.transform.position = pos + UnityEngine.Vector3.up * height;
            var l = g.AddComponent<UnityEngine.Light>(); l.type = UnityEngine.LightType.Point; l.color = c; l.range = range; l.intensity = parchment ? intensity * .2f : intensity;
        }
        UnityEngine.Vector2 Around(UnityEngine.Vector2 p, float a, float ru, float rv) => new(p.x + UnityEngine.Mathf.Cos(a) * ru, p.y + UnityEngine.Mathf.Sin(a) * rv);
        float FaceTo(UnityEngine.Vector2 from, UnityEngine.Vector2 to) { var d = new UnityEngine.Vector3((to.x - from.x) * HX, 0, (to.y - from.y) * HZ); return UnityEngine.Quaternion.LookRotation(d).eulerAngles.y; }
        // Fields round a town: patches of crop rows at right angles to each other.
        void Fields(UnityEngine.Vector2 at, int count, float spread)
        {
            for (int f = 0; f < count; f++)
            {
                var p = Around(at, R() * 6.28f, spread * (.6f + R() * .5f), spread * (.6f + R() * .5f) * .75f);
                if (RoadDist(p.x, p.y) < .02f) continue;
                float yaw = rng.Next(2) * 90;
                for (int k = 0; k < 3; k++) Prop(FK + "SM_Env_Ground_Farm_Row_0" + (1 + rng.Next(5)), W3(p.x + (yaw == 0 ? k * .011f : 0), p.y + (yaw == 0 ? 0 : k * .009f), -.05f), yaw, s0 * 1.3f);
            }
        }

        o.poiStart = root.transform.childCount;
        // Act 1 · home: a little town with its tavern, well and fields
        var ps = pois["start"];
        for (int k = 0; k < 8; k++) { var p = Around(ps, k / 8f * 6.28f + R() * .3f, .035f + R() * .025f, .03f + R() * .02f); Prop(houses[rng.Next(houses.Length)], W3(p.x, p.y), FaceTo(p, ps) + 180, big * .3f); }
        Prop(B + "SM_Bld_Preset_Tavern_01_Optimized", W3(ps.x + .015f, ps.y - .01f), 120, big * .34f);
        Prop(DFP + "SM_Prop_Well_01", W3(ps.x - .012f, ps.y + .012f), 0, big * .6f);
        Prop(DFP + "SM_Prop_Flag_Hero_01", W3(ps.x + .03f, ps.y + .03f), 90, big * .5f);
        Fields(ps, 9, .1f);
        Glow(W3(ps.x, ps.y), new UnityEngine.Color(1f, .85f, .55f), 120);
        // Act 1 · Dungeon1: a fenced graveyard round the crypt, lanterns at its gate
        var pc = pois["crypt"];
        Prop(DFP + "SM_Prop_Crypt_01", W3(pc.x, pc.y), FaceTo(pc, ps), big * 1.1f);
        for (int k = 0; k < 12; k++) { float a = k / 12f * 6.28f; var p = Around(pc, a, .055f, .042f); if (k == 3) continue; Prop(DFB + "SM_Bld_Fence_Cemetary_01", W3(p.x, p.y), a * UnityEngine.Mathf.Rad2Deg + 90, big * .55f); }
        for (int k = 0; k < 9; k++) { var p = Around(pc, R() * 6.28f, .02f + R() * .025f, .016f + R() * .02f); Prop(FP + "SM_Prop_Gravestone_0" + (1 + k % 5), W3(p.x, p.y), R() * 360, big * .8f); }
        { var g = Around(pc, 3 / 12f * 6.28f, .055f, .042f); Prop(DFP + "SM_Prop_Lantern_01", W3(g.x + .008f, g.y), 0, big * .9f); Prop(DFP + "SM_Prop_Lantern_01", W3(g.x - .008f, g.y), 0, big * .9f); }
        Prop(DF + "SM_Env_Tree_Dead_02", W3(pc.x + .03f, pc.y + .03f), 30, big * .6f);
        Glow(W3(pc.x, pc.y), new UnityEngine.Color(.55f, .75f, 1f), 180);
        // Act 1 · Forest1: the great tree in a clearing ringed by old stones
        var pf2 = pois["forest"]; Prop(FK + "SM_Env_Big_Tree_01", W3(pf2.x, pf2.y), 40, big * .38f);
        for (int k = 0; k < 7; k++) { var p = Around(pf2, k / 7f * 6.28f, .045f, .035f); Prop(rocks[k % 3], W3(p.x, p.y), R() * 360, big * .45f, new UnityEngine.Color(.8f, .85f, .8f)); }
        Glow(W3(pf2.x, pf2.y), new UnityEngine.Color(.7f, 1f, .6f), 140);
        var pm = pois["mill"]; Prop(B + "SM_Bld_Preset_House_Windmill_01_Optimized", W3(pm.x, pm.y), 200, big * .4f); Fields(pm, 6, .07f);
        // The passes between acts: a watchtower with a flag guards each
        foreach (var (r, a) in new[] { (actEdge[1], passA), (actEdge[2], pass2) })
        {
            var p = At(r, a + 7);
            Prop("PolygonFantasyKingdom/Prefabs/Buildings/SM_Bld_Wooden_Tower_01", W3(p.x, p.y), R() * 360, big * .35f);
            Prop(DFP + (r < .7f ? "SM_Prop_Flag_Light_01" : "SM_Prop_Flag_Dark_01"), W3(p.x + .015f, p.y), 0, big * .5f);
            Glow(W3(p.x, p.y), new UnityEngine.Color(1f, .75f, .45f), 70, 6f, 10f);
        }
        // Act 2: the market village with its church and fields, the hill tower, a soldiers' camp
        var pv = pois["village"];
        for (int k = 0; k < 11; k++) { var p = Around(pv, k / 11f * 6.28f + R() * .2f, .03f + R() * .045f, .025f + R() * .035f); Prop(houses[rng.Next(houses.Length)], W3(p.x, p.y), FaceTo(p, pv) + 180, big * .3f); }
        Prop(B + "SM_Bld_Preset_Church_01_A_Optimized", W3(pv.x, pv.y), 90, big * .36f); Fields(pv, 7, .11f);
        var pt = pois["tower"]; Prop(B + "SM_Bld_Preset_Tower_01_Optimized", W3(pt.x, pt.y), 30, big * .45f); Prop(DFP + "SM_Prop_Flag_Light_02", W3(pt.x + .02f, pt.y), 0, big * .5f);
        var pcamp = pois["camp"];
        for (int k = 0; k < 5; k++) { var p = Around(pcamp, k / 5f * 6.28f, .03f, .025f); Prop(k % 2 == 0 ? "PolygonFantasyKingdom/Prefabs/Buildings/SM_Bld_Tent_Round_01" : "PolygonFantasyKingdom/Prefabs/Buildings/SM_Bld_Tent_Single_01", W3(p.x, p.y), FaceTo(p, pcamp), big * .3f); }
        Prop(DFP + "SM_Prop_Campfire_Setup_01", W3(pcamp.x, pcamp.y), 0, big * .8f); Glow(W3(pcamp.x, pcamp.y), new UnityEngine.Color(1f, .6f, .3f), 110, 5f, 10f);
        // Act 3: a broken keep, and a shrine in a ring of stones with its braziers lit
        var pr = pois["ruin"]; Prop(DFB + "SM_Bld_Ruin_02", W3(pr.x, pr.y), 90, big * .8f);
        for (int k = 0; k < 6; k++) { var p = Around(pr, k / 6f * 6.28f, .05f, .04f); Prop(DFB + "SM_Bld_Wall_Ruin_0" + (1 + k % 3), W3(p.x, p.y), k / 6f * 360 + 90, big * .3f); }
        var psh = pois["shrine"]; Prop(DFP + "SM_Prop_Statue_01", W3(psh.x, psh.y), 200, big * 1.3f);
        for (int k = 0; k < 6; k++) { var p = Around(psh, k / 6f * 6.28f, .035f, .03f); Prop(k % 2 == 0 ? DFP + "SM_Prop_Brazier_01" : rocks[k % 3], W3(p.x, p.y), R() * 360, big * (k % 2 == 0 ? .9f : .5f)); }
        Glow(W3(psh.x, psh.y), new UnityEngine.Color(1f, .55f, .25f), 150, 5f, 11f);
        // Act 4: the end. Dark towers inside broken walls, banners, braziers, a red glow over the ash.
        var pk = pois["peak"];
        var dark = new UnityEngine.Color(.32f, .28f, .34f);
        Prop(B + "SM_Bld_Preset_Tower_01_Optimized", W3(pk.x, pk.y), 210, big * .7f, dark);
        Prop(B + "SM_Bld_Preset_Tower_01_Optimized", W3(pk.x - .035f, pk.y + .03f), 120, big * .5f, dark);
        Prop(B + "SM_Bld_Preset_Tower_01_Optimized", W3(pk.x + .03f, pk.y + .035f), 40, big * .45f, dark);
        for (int k = 0; k < 10; k++) { float a = k / 10f * 6.28f; var p = Around(pk, a, .07f, .055f); Prop(DFB + "SM_Bld_Wall_Ruin_0" + (1 + k % 3), W3(p.x, p.y), a * UnityEngine.Mathf.Rad2Deg + 90, big * .32f, dark); }
        for (int k = 0; k < 4; k++) { var p = Around(pk, k / 4f * 6.28f + .4f, .05f, .04f); Prop(DFP + "SM_Prop_Flag_Dark_0" + (1 + k % 3), W3(p.x, p.y), R() * 360, big * .55f); Prop(DFP + "SM_Prop_Brazier_01", W3(p.x + .01f, p.y), 0, big * .8f); }
        Prop(DFP + "SM_Prop_Gargoyle_Statue_01", W3(pk.x - .06f, pk.y - .05f), FaceTo(pk, ps) + 180, big * 1.3f, dark);
        Glow(W3(pk.x, pk.y), new UnityEngine.Color(1f, .3f, .18f), 320, 10f, 22f);
        o.poiEnd = root.transform.childCount;
        // Forest edges: bushes and young trees where the woods meet open ground, so woods don't end in a hard line
        for (int i = 0; i < 9000; i++)
        {
            float u = R() * 1.9f - .95f, v = R() * 1.9f - .95f;
            if (NearPoi(u, v, .08f) || RoadDist(u, v) < .02f) continue;
            BiomeAt(u, v, out int a, out int b, out float mix);
            if (a != 1 && a != 2 && b != 1 && b != 2) continue;
            float g = UnityEngine.Mathf.PerlinNoise(u * 6 + 50, v * 6 + 50);
            if (g < .42f || g > .52f) continue;   // the band round the edge of each grove
            if (R() < .5f) Prop(bushes[rng.Next(3)], W3(u, v), R() * 360, s0 * (1f + R() * .5f), a == 2 || b == 2 ? new UnityEngine.Color(1f, .75f, .45f) : (UnityEngine.Color?)null);
            else Prop(a == 2 || b == 2 ? rounds[rng.Next(4)] : pines[rng.Next(4)], W3(u, v), R() * 360, s0 * (.45f + R() * .3f), a == 2 || b == 2 ? new UnityEngine.Color(1f, .6f, .25f) : (UnityEngine.Color?)null);
        }
        // Lone crags on the high ground, so the mountains have rock as well as snow
        for (int i = 0; i < 2500; i++)
        {
            float u = R() * 1.9f - .95f, v = R() * 1.9f - .95f;
            float h = HeightReal(u, v); if (h < 5f || NearPoi(u, v, .07f)) continue;
            if (R() < .35f) Prop(rocks[rng.Next(3)], W3(u, v, -.1f), R() * 360, s0 * (.8f + R() * 1.2f), new UnityEngine.Color(.85f, .85f, .9f));
        }
        // Black spires round the end
        for (int i = 0; i < 40; i++)
        {
            float a = R() * 6.28f, r = .07f + R() * .2f; var p = Around(pois["peak"], a, r, r * .8f);
            if (UnityEngine.Mathf.Abs(p.x) > .95f || UnityEngine.Mathf.Abs(p.y) > .95f || RoadDist(p.x, p.y) < .03f) continue;
            Prop(FK + "SM_Env_Rock_Cliff_0" + (1 + rng.Next(5)), W3(p.x, p.y, -.3f), R() * 360, s0 * (.4f + R() * .5f), new UnityEngine.Color(.25f, .2f, .24f));
        }
        // Act 3 landmarks: a fallen giant's statue in the realm, a rope bridge over the frontier's chasm, an ice-bound wreck of a keep in the north
        {
            var lm = At(.66f, 30);
            if (example == "longroad") { Prop(DFP + "SM_Prop_Statue_People_Damaged_01", W3(lm.x, lm.y), 70, big * 2.2f, new UnityEngine.Color(.8f, .78f, .72f)); Prop(DFP + "SM_Prop_Statue_People_Damaged_02", W3(lm.x + .03f, lm.y - .02f), 200, big * 1.6f, new UnityEngine.Color(.8f, .78f, .72f)); }
            else if (example == "pass") { var bp = At(.66f, pass2); Prop("PolygonFantasyKingdom/Prefabs/Castle/SM_Bld_Bridge_01", W3(bp.x, bp.y, .2f), 45 + 90, big * .35f); }
            else { Prop(DFB + "SM_Bld_Ruin_03", W3(lm.x, lm.y), 20, big * .9f, new UnityEngine.Color(.75f, .82f, .92f)); for (int k = 0; k < 6; k++) { var q = Around(lm, k, .04f, .03f); Prop(rocks[k % 3], W3(q.x, q.y), R() * 360, big * .45f, new UnityEngine.Color(.85f, .9f, 1f)); } }
        }
        // Lanterns along the far roads: small warm lights in the dark lands, so the way on reads
        for (int i = 0; i < roadPts.Count; i += 23)
        {
            var p = roadPts[i]; float jd = ActDist(p.x, p.y); if (jd < actEdge[0] + .05f || NearPoi(p.x, p.y, .06f)) continue;
            var q = roadPts[UnityEngine.Mathf.Min(i + 1, roadPts.Count - 1)]; var d = q - p; if (d.sqrMagnitude < 1e-8f) continue; d.Normalize();
            var sp = p + new UnityEngine.Vector2(-d.y, d.x) * .016f;
            Prop(DFP + "SM_Prop_Lantern_01", W3(sp.x, sp.y), 0, big * .55f);
            var lg = new UnityEngine.GameObject("lantern light"); lg.transform.SetParent(root.transform); lg.transform.position = W3(sp.x, sp.y, 1.4f);
            var ll = lg.AddComponent<UnityEngine.Light>(); ll.type = UnityEngine.LightType.Point; ll.color = new UnityEngine.Color(1f, .7f, .35f); ll.range = 5f; ll.intensity = 18f;
        }
        // Old battlefields in act 3: tattered banners and cairns where armies met
        for (int f = 0; f < 3; f++)
        {
            var at = At(.6f + R() * .12f, 15 + R() * 60);
            if (NearPoi(at.x, at.y, .1f)) continue;
            for (int k = 0; k < 9; k++)
            {
                var p = Around(at, R() * 6.28f, R() * .06f, R() * .05f);
                if (RoadDist(p.x, p.y) < .02f) continue;
                Prop(k % 3 == 0 ? DFP + "SM_Prop_Flag_" + (R() < .5f ? "Hero" : "Dark") + "_Damaged_0" + (1 + rng.Next(4)) : FP + "SM_Prop_Gravestone_Broken_01", W3(p.x, p.y), R() * 360, big * (k % 3 == 0 ? .45f : .7f));
            }
        }
        // Act 2 outposts: a watch tower with a tent or two beside the roads
        for (int oi = 0; oi < 5; oi++)
        {
            var p = roadPts[rng.Next(roadPts.Count)]; float jd = ActDist(p.x, p.y);
            if (jd < actEdge[0] || jd > actEdge[1] || NearPoi(p.x, p.y, .1f)) continue;
            var q = p + new UnityEngine.Vector2(.025f, -.02f);
            Prop("PolygonFantasyKingdom/Prefabs/Buildings/SM_Bld_Wooden_Tower_02", W3(q.x, q.y), R() * 360, big * .25f);
            Prop("PolygonFantasyKingdom/Prefabs/Buildings/SM_Bld_Tent_Single_01", W3(q.x + .015f, q.y + .012f), R() * 360, big * .22f);
        }
        // Act 3: hamlets that were burnt and left
        for (int oi = 0; oi < 6; oi++)
        {
            var at = At(actEdge[1] + R() * (actEdge[2] - actEdge[1]), 5 + R() * 80);
            if (NearPoi(at.x, at.y, .1f) || RoadDist(at.x, at.y) < .03f) continue;
            for (int k = 0; k < 4; k++) { var p = Around(at, k * 1.6f + R(), .02f + R() * .015f, .016f + R() * .012f); Prop(DFB + "SM_Bld_Wall_Ruin_0" + (1 + rng.Next(3)), W3(p.x, p.y), R() * 360, big * .22f); }
            Prop(FK + "SM_Env_Tree_Burnt_01", W3(at.x + .02f, at.y), R() * 360, s0 * 1.1f);
        }
        // Flowers in the green lands near home
        for (int i = 0; i < 900; i++)
        {
            float u = R() * 1.9f - .95f, v = R() * 1.9f - .95f;
            BiomeAt(u, v, out int a, out int b, out float mix);
            if ((a != 0 && a != 7) || NearPoi(u, v, .06f) || RoadDist(u, v) < .02f || ActDist(u, v) > actEdge[1]) continue;
            Prop(FK + "SM_Env_Flowers_0" + (1 + rng.Next(4)), W3(u, v), R() * 360, s0 * 1.6f);
        }

        var keyGo = new UnityEngine.GameObject("table light"); keyGo.transform.SetParent(root.transform); keyGo.transform.position = C + new UnityEngine.Vector3(-55, 80, 45);
        keyGo.transform.LookAt(C); var key = keyGo.AddComponent<UnityEngine.Light>(); key.type = UnityEngine.LightType.Spot; key.spotAngle = 95; key.range = 240;
        key.intensity = stylized ? 7800 : 13000;
        var fillGo = new UnityEngine.GameObject("fill light"); fillGo.transform.SetParent(root.transform); fillGo.transform.position = C + new UnityEngine.Vector3(70, 60, -70);
        fillGo.transform.LookAt(C); var fill = fillGo.AddComponent<UnityEngine.Light>(); fill.type = UnityEngine.LightType.Spot; fill.spotAngle = 80; fill.range = 240; fill.intensity = 2600; fill.color = new UnityEngine.Color(.6f, .72f, 1f); key.color = stylized ? new UnityEngine.Color(1f, .96f, .9f) : new UnityEngine.Color(1f, .93f, .82f); key.shadows = UnityEngine.LightShadows.Soft;
        o.C = C; o.HX = HX; o.HZ = HZ; o.pois = pois;
        return root;

    }
}
