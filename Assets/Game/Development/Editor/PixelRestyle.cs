using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// The rat's look for Synty models, without Meshy: each model gets fresh UVs at a fixed texel density and a small
// texture painted texel by texel from its own surface. The colour is Synty's (read at the same spot through the
// model's original UVs), shading is baked ambient occlusion, and surface detail (wood grain, scratches, stains,
// weave) comes from 3D noise in the model's own space, so it runs across UV seams without a break.
// Emissive parts keep their glow in a separate emission texture. Results live in Assets/Game/Art/Synty Pixel.
public static partial class PixelRestyle
{
    public const string Root = "Assets/Game/Art/Synty Pixel";
    const int AoRays = 24, Super = 2, ProbeLayer = 31;

    // How much a painted restyle darkens its creases (1 = as always; a clock face set flat in its frame wants 0, or the
    // frame's shadow creeps across the dial).
    public static float PaintAoStrength = 1f;
    // Painted restyles pick each texel's view by its face's own facing instead of the smoothed vertex normal (room
    // furniture: smoothed normals on a box made a side face lean toward the front view and smear its edge column).
    public static bool PaintFaceNormals;

    public enum Kind { Auto = 0, Metal = 1, Stone = 2, Wood = 3, Bone = 4, Cloth = 5, Gold = 6, Painted = 7 }

    // texelsPerUnit: props ~40, hand-held items ~110 (a sword ~170 texels long).
    public static (Mesh mesh, Material material) Build(Mesh src, Material[] mats, string category, string name, float texelsPerUnit, string folder = null)
    {
        // Fresh UVs on a copy (vertex attributes, bone weights and bind poses come along).
        var mesh = Object.Instantiate(src); mesh.name = src.name + " (Synty Pixel)";
        var origUv = new List<Vector2>(); src.GetUVs(0, origUv);
        // Unwrapping splits vertices but keeps per-vertex attributes; remember which source vertex each came from via uv7.
        var tag = new List<Vector4>(); for (int i = 0; i < src.vertexCount; i++) tag.Add(new Vector4(i, 0, 0, 0));
        mesh.SetUVs(7, tag);
        float area = 0; var sv = src.vertices; var st = src.triangles;
        for (int i = 0; i < st.Length; i += 3) area += Vector3.Cross(sv[st[i + 1]] - sv[st[i]], sv[st[i + 2]] - sv[st[i]]).magnitude * .5f;
        // A first guess; the real size comes from the UVs below. The pack margin is fixed so a re-bake keeps the layout.
        int W = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(area / .5f) * texelsPerUnit), 32, 2048);
        var up = new UnwrapParam(); UnwrapParam.SetDefaults(out up);
        up.packMargin = 1f / 128f; up.hardAngle = 70; up.angleError = .08f; up.areaError = .15f;
        Unwrapping.GenerateSecondaryUVSet(mesh, up);
        var newUv = new List<Vector2>(); mesh.GetUVs(1, newUv);
        // Exactly texelsPerUnit texels per mesh unit, whatever share of the square the unwrap used: every model gets
        // the same pixel size (no power-of-two rounding or size cap, which made pixels up to twice as big on some props).
        if (newUv.Count == mesh.vertexCount)
        {
            float uvArea = 0f; var tri = mesh.triangles;
            for (int i = 0; i < tri.Length; i += 3)
            {
                Vector2 a0 = newUv[tri[i]], a1 = newUv[tri[i + 1]], a2 = newUv[tri[i + 2]];
                uvArea += Mathf.Abs((a1.x - a0.x) * (a2.y - a0.y) - (a2.x - a0.x) * (a1.y - a0.y)) * .5f;
            }
            if (uvArea > 1e-6f) W = Mathf.Clamp(Mathf.RoundToInt(texelsPerUnit * Mathf.Sqrt(area / uvArea)), 16, 4096);
        }
        var tags = new List<Vector4>(); mesh.GetUVs(7, tags);
        if (newUv.Count != mesh.vertexCount || tags.Count != mesh.vertexCount)
        {
            Debug.LogWarning($"[PixelRestyle] {src.name}: Unity could not unwrap it (needs Read/Write on the model); skipped.");
            Object.DestroyImmediate(mesh); return (null, null);
        }
        mesh.SetUVs(0, newUv); mesh.SetUVs(1, (List<Vector2>)null); mesh.SetUVs(7, (List<Vector4>)null);

        // Per-triangle source material (submesh) and palette lookups.
        var mv = mesh.vertices; var mn = mesh.normals; var palettes = new Dictionary<Texture, Texture2D>();
        var bounds = mesh.bounds; float size = bounds.size.magnitude;
        Kind kindHint = KindFromName(src.name);
        bool modern = category == "Room";
        // Unpainted tabletop miniatures are cast pewter.
        if (mats != null && System.Array.Exists(mats, m => m != null && m.name.ToLowerInvariant().Contains("pewter"))) kindHint = Kind.Metal;

        // AO probe: the model alone, on its own layer, far from the scene.
        var probe = new GameObject("PixelRestyle probe") { hideFlags = HideFlags.HideAndDontSave, layer = ProbeLayer };
        probe.transform.position = new Vector3(0, -10000, 0);
        var mc = probe.AddComponent<MeshCollider>(); var colMesh = Object.Instantiate(src); mc.sharedMesh = colMesh;
        Physics.SyncTransforms();
        int SW = W * Super; var col = new Color[SW * SW]; var emi = new Color[SW * SW]; var cov = new bool[SW * SW]; bool anyEmission = false;
        var rng = new System.Random(src.name.GetHashCode());
        var dirs = new Vector3[AoRays];
        for (int i = 0; i < AoRays; i++) { float u = (float)rng.NextDouble(), v = (float)rng.NextDouble(); float th = 2 * Mathf.PI * u, ph = Mathf.Acos(2 * v - 1); dirs[i] = new Vector3(Mathf.Sin(ph) * Mathf.Cos(th), Mathf.Cos(ph), Mathf.Sin(ph) * Mathf.Sin(th)); }
        float aoDist = Mathf.Clamp(size * .08f, .015f, .3f);

        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            var mat = mats != null && mats.Length > 0 ? mats[Mathf.Min(sub, mats.Length - 1)] : null;
            var pal = Palette(mat, "_BaseMap", "_Albedo_Map", "_MainTex", palettes);
            var emap = Palette(mat, "_EmissionMap", "_Emission_Map", null, palettes);
            Color tint = mat != null && mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;
            Color etint = mat != null && mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black;
            if (mat != null && mat.HasProperty("_Emission_Color")) etint = mat.GetColor("_Emission_Color");
            bool emissive = emap != null && etint.maxColorComponent > .01f || (mat != null && mat.IsKeywordEnabled("_EMISSION") && etint.maxColorComponent > .01f);
            // Synty's shaders have their own switch: with it off the emission map is never drawn (it still points at the
            // atlas's emissive sheet, which baked stray glowing patches onto the DM's faces).
            if (mat != null && mat.HasProperty("_Enable_Emission") && mat.GetFloat("_Enable_Emission") < .5f) emissive = false;
            var t = mesh.GetTriangles(sub);
            for (int f = 0; f < t.Length; f += 3)
            {
                int i0 = t[f], i1 = t[f + 1], i2 = t[f + 2];
                Vector2 a = newUv[i0] * SW, b = newUv[i1] * SW, c = newUv[i2] * SW;
                Vector2 o0 = origUv.Count > 0 ? origUv[(int)tags[i0].x] : Vector2.zero, o1 = origUv.Count > 0 ? origUv[(int)tags[i1].x] : Vector2.zero, o2 = origUv.Count > 0 ? origUv[(int)tags[i2].x] : Vector2.zero;
                // Synty atlases are flat swatches: one colour per face (sampled at its centre) never bleeds into the next swatch.
                var oc = (o0 + o1 + o2) / 3f;
                Color baseC = (pal != null ? Sample(pal, oc) : Color.white) * tint; // untextured materials are their colour
                // The face's own colour, averaged over its corners and centre (steadier than one grainy texel).
                Color faceC = pal != null ? (Sample(pal, o0) + Sample(pal, o1) + Sample(pal, o2) + Sample(pal, oc) * 3f) / 6f * tint : baseC;
                Color emC = emissive ? (emap != null ? Sample(emap, oc) : Color.white) * etint : Color.black;
                if (emC.maxColorComponent < .15f) emC = Color.black; else anyEmission = true;
                Kind kind = kindHint != Kind.Auto ? Refine(kindHint, baseC) : Classify(baseC);
                // Modern rooms: pale, unsaturated surfaces are paint, plastic or enamel (a fridge, a door), not rusty iron or bone.
                if (modern && kindHint == Kind.Auto && (kind == Kind.Metal || kind == Kind.Bone)) kind = Kind.Painted;
                var fn = Vector3.Cross(mv[i1] - mv[i0], mv[i2] - mv[i0]).normalized;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - 1)), x1 = Mathf.Min(SW - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) + 1));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)) - 1)), y1 = Mathf.Min(SW - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y)) + 1));
                float den = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y); if (Mathf.Abs(den) < 1e-9f) continue;
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                {
                    float px = x + .5f, py = y + .5f;
                    float w1 = ((b.y - c.y) * (px - c.x) + (c.x - b.x) * (py - c.y)) / den, w2 = ((c.y - a.y) * (px - c.x) + (a.x - c.x) * (py - c.y)) / den, w3 = 1 - w1 - w2;
                    if (w1 < -.01f || w2 < -.01f || w3 < -.01f) continue;
                    int idx = y * SW + x; if (cov[idx]) continue;
                    var p = mv[i0] * w1 + mv[i1] * w2 + mv[i2] * w3;
                    var n = (mn.Length > 0 ? (mn[i0] * w1 + mn[i1] * w2 + mn[i2] * w3) : fn).normalized;
                    float ao = AmbientOcclusion(probe.transform.position + p, n, dirs, aoDist);
                    // Painted detail inside a face (book labels, sigils, rug patterns) comes from the Synty texture at this
                    // exact spot; the weights are pulled a little toward the face centre so edge texels never read the
                    // neighbouring swatch.
                    Color texelC = baseC;
                    if (pal != null && origUv.Count > 0)
                    {
                        float k1 = Mathf.Lerp(Mathf.Clamp01(w1), 1f / 3f, .12f), k2 = Mathf.Lerp(Mathf.Clamp01(w2), 1f / 3f, .12f), k3 = Mathf.Lerp(Mathf.Clamp01(w3), 1f / 3f, .12f);
                        float ks = k1 + k2 + k3; texelC = Sample(pal, (o0 * k1 + o1 * k2 + o2 * k3) / ks) * tint;
                    }
                    // A painted restyle takes its colour from the painting; only the creases darken a little.
                    if (PaintSource != null)
                    {
                        // A skinned model is painted in the pose it was rendered in, not its bind pose.
                        if (PaintPose != null)
                        {
                            int s0 = (int)tags[i0].x, s1 = (int)tags[i1].x, s2 = (int)tags[i2].x;
                            p = PaintPose[s0] * w1 + PaintPose[s1] * w2 + PaintPose[s2] * w3;
                            n = (PaintPoseNormals[s0] * w1 + PaintPoseNormals[s1] * w2 + PaintPoseNormals[s2] * w3).normalized;
                        }
                        PaintBase = faceC;
                        // The face's own facing from its winding (the plain cross product is outward here, checked with a
                        // colour-coded sheet; smoothed normals made box sides lean toward the front view).
                        if (PaintFaceNormals && PaintPose == null) n = fn;
                        var painted = PaintSource(p, n); col[idx] = Color.Lerp(painted * (1f - .55f * PaintAoStrength), painted, .5f + .5f * Mathf.Clamp01(ao)); col[idx].a = 1f;
                    }
                    else col[idx] = Shade(texelC, kind, p, n, ao, size);
                    emi[idx] = emC; cov[idx] = true;
                }
            }
        }
        Object.DestroyImmediate(probe); Object.DestroyImmediate(colMesh);
        foreach (var copy in palettes.Values) Object.DestroyImmediate(copy);   // readable copies of the source atlases

        // Down to the final size (each texel the average of its Super x Super area), then pad the islands.
        var outC = new Color[W * W]; var outE = new Color[W * W]; var outCov = new bool[W * W];
        for (int y = 0; y < W; y++) for (int x = 0; x < W; x++)
        {
            Color s = Color.clear, e = Color.clear; int nC = 0;
            for (int dy = 0; dy < Super; dy++) for (int dx = 0; dx < Super; dx++) { int i = (y * Super + dy) * SW + x * Super + dx; if (!cov[i]) continue; s += col[i]; e += emi[i]; nC++; }
            if (nC > 0) { outC[y * W + x] = s / nC; outE[y * W + x] = e / nC; outCov[y * W + x] = true; }
        }
        Pad(outC, outE, outCov, W, 6);

        string dir = $"{Root}/{category}/{src.name}";
        // Two different models can share a name (the arms-only hunter and the full hunter): each source file gets its own folder.
        var srcFile = System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(src));
        if (src.bindposes.Length > 0) dir = $"{Root}/{category}/{src.name} ({srcFile})";
        if (folder != null) dir = folder;
        System.IO.Directory.CreateDirectory(dir);
        string texPath = $"{dir}/{name}.png"; Save(outC, W, texPath, false);
        string emiPath = $"{dir}/{name}_emission.png"; if (anyEmission) Save(outE, W, emiPath, true);
        string matPath = $"{dir}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, matPath); }
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath)); material.SetColor("_BaseColor", Color.white); material.SetFloat("_Smoothness", 0f);
        if (anyEmission)
        {
            material.EnableKeyword("_EMISSION"); material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(emiPath)); material.SetColor("_EmissionColor", Color.white);
        }
        else material.DisableKeyword("_EMISSION");
        EditorUtility.SetDirty(material);
        // One material now, so the submeshes merge into one (their triangles already index the unwrapped vertices).
        var merged = new List<int>(); for (int s2 = 0; s2 < mesh.subMeshCount; s2++) merged.AddRange(mesh.GetTriangles(s2));
        mesh.subMeshCount = 1; mesh.SetTriangles(merged, 0); mesh.RecalculateBounds();
        string meshPath = $"{dir}/{name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (existing != null) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); mesh = existing; }
        else AssetDatabase.CreateAsset(mesh, meshPath);
        MarkReadable(mesh);
        AssetDatabase.SaveAssets();
        return (mesh, material);
    }

    // Restyled meshes stay readable (Synty's sources aren't): mesh colliders on props feed the NavMesh built when a floor
    // is generated, which in a build can only read readable meshes.
    public static void MarkReadable(Mesh mesh)
    {
        if (mesh == null || mesh.isReadable) return;
        var so = new SerializedObject(mesh);
        var p = so.FindProperty("m_IsReadable");
        if (p == null) return;
        p.boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(mesh);
    }

    // ── Shading ───────────────────────────────────────────────────────────

    static float AmbientOcclusion(Vector3 p, Vector3 n, Vector3[] dirs, float dist)
    {
        float open = 0, total = 0; var origin = p + n * (dist * .02f + .0015f);
        foreach (var d0 in dirs)
        {
            var d = Vector3.Dot(d0, n) < 0 ? -d0 : d0; float w = Vector3.Dot(d, n); total += w;
            if (!Physics.Raycast(origin, d, out var hit, dist, 1 << ProbeLayer, QueryTriggerInteraction.Ignore)) open += w;
            else open += w * Mathf.Clamp01(hit.distance / dist);
        }
        return total > 0 ? open / total : 1f;
    }

    static Color Shade(Color baseC, Kind kind, Vector3 p, Vector3 n, float ao, float size)
    {
        float s = 1f / Mathf.Max(.15f, size);
        float grime = Mathf.Pow(Mathf.Clamp01(ao), 1.2f);
        float mottle = Fbm(p * 6f * s * 4f, 3) - .5f;           // large soft blotches
        float fine = Hash(Mathf.FloorToInt(p.x * 140f), Mathf.FloorToInt(p.y * 140f), Mathf.FloorToInt(p.z * 140f)) - .5f; // per-texel speckle
        float m = 1f;
        switch (kind)
        {
            case Kind.Wood:
            {
                // Grain along the object's longest axis: stretched noise.
                var q = new Vector3(p.x * 4f, p.y * 60f, p.z * 60f); if (Mathf.Abs(n.y) > .7f) q = new Vector3(p.x * 60f, p.y * 4f, p.z * 60f);
                float grain = Fbm(q, 3) - .5f; m = 1f + grain * .8f + fine * .22f + mottle * .4f; break;
            }
            case Kind.Metal:
            {
                float scratch = Mathf.Pow(Mathf.Abs(Fbm(new Vector3(p.x * 90f + p.y * 20f, p.y * 8f, p.z * 90f), 2) - .5f) * 2f, 6f);
                m = 1f + fine * .3f + mottle * .55f + scratch * .5f;
                float rust = Mathf.Clamp01((Fbm(p * 14f, 3) - .62f) * 5f) * (1f - grime * .5f);
                baseC = Color.Lerp(baseC, new Color(.36f, .2f, .12f), rust * .6f); break;
            }
            case Kind.Gold: m = 1f + fine * .3f + mottle * .45f; break;
            case Kind.Painted: m = 1f + fine * .12f + mottle * .22f; break;
            case Kind.Stone: m = 1f + mottle * .7f + fine * .35f; break;
            case Kind.Bone:
            {
                float stain = Mathf.Clamp01((Fbm(p * 10f, 3) - .45f) * 3f);
                baseC = Color.Lerp(baseC, baseC * new Color(.7f, .58f, .4f), stain * .8f); m = 1f + fine * .25f + mottle * .4f; break;
            }
            case Kind.Cloth:
            {
                float weave = (Mathf.Sin(p.x * 900f) * Mathf.Sin(p.y * 900f + p.z * 900f)) * .5f;
                m = 1f + weave * .12f + fine * .3f + mottle * .55f; break;
            }
        }
        // Dirt settles in the creases: darker and a little brown where little light gets in.
        var dirt = new Color(.16f, .12f, .09f);
        var c = baseC * Mathf.Clamp(m, .45f, 1.5f);
        c = Color.Lerp(dirt, c, .3f + .7f * grime);
        c.r = Mathf.Clamp01(c.r); c.g = Mathf.Clamp01(c.g); c.b = Mathf.Clamp01(c.b); c.a = 1f;
        return c;
    }

    static Kind KindFromName(string n)
    {
        n = n.ToLowerInvariant();
        // Painted modern fittings first: kitchen units, house doors, appliances.
        if (n.Contains("kitchen") || n.Contains("house_door") || n.Contains("fridge") || n.Contains("toaster")) return Kind.Painted;
        if (n.Contains("skeleton") || n.Contains("bone") || n.Contains("skull")) return Kind.Bone;
        if (n.Contains("wep_") || n.Contains("shield") || n.Contains("ring") || n.Contains("latch") || n.Contains("torch") || n.Contains("brazier") || n.Contains("lantern") || n.Contains("armor")) return Kind.Metal;
        if (n.Contains("bld_") || n.Contains("pillar") || n.Contains("tomb") || n.Contains("fountain") || n.Contains("gargoyle") || n.Contains("altar")) return Kind.Stone;
        if (n.Contains("barrel") || n.Contains("crate") || n.Contains("chest") || n.Contains("shelf") || n.Contains("table") || n.Contains("chair") || n.Contains("counter") || n.Contains("rack")) return Kind.Wood;
        if (n.Contains("sack") || n.Contains("rug")) return Kind.Cloth;
        return Kind.Auto;
    }

    // A name says what most of the model is; the colour decides each face (a sword's leather grip, a chest's iron bands).
    static Kind Refine(Kind hint, Color c)
    {
        var k = Classify(c);
        if (k == Kind.Gold || k == Kind.Cloth) return k;
        if (hint == Kind.Bone && k != Kind.Bone) return k == Kind.Metal ? Kind.Metal : k;
        if (hint == Kind.Wood && k == Kind.Metal) return Kind.Metal;
        if (hint == Kind.Metal && k == Kind.Wood) return Kind.Wood;
        return hint;
    }

    static Kind Classify(Color c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v); h *= 360f;
        if (s < .16f) return v > .62f ? Kind.Bone : Kind.Metal;
        if (h >= 32 && h <= 58 && s > .45f && v > .45f) return Kind.Gold;
        if (h >= 10 && h <= 50 && v < .62f) return Kind.Wood;
        if (h >= 25 && h <= 55 && s < .4f && v >= .55f) return Kind.Bone;
        return Kind.Cloth;
    }

    // ── Noise ─────────────────────────────────────────────────────────────

    static float Hash(int x, int y, int z) { unchecked { int h = x * 374761393 + y * 668265263 + z * 1274126177; h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xffffff) / (float)0xffffff; } }

    static float Noise(Vector3 p)
    {
        int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
        float fx = p.x - x, fy = p.y - y, fz = p.z - z;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz);
        float L(int a, int b, int c) => Hash(x + a, y + b, z + c);
        float x00 = Mathf.Lerp(L(0, 0, 0), L(1, 0, 0), fx), x10 = Mathf.Lerp(L(0, 1, 0), L(1, 1, 0), fx);
        float x01 = Mathf.Lerp(L(0, 0, 1), L(1, 0, 1), fx), x11 = Mathf.Lerp(L(0, 1, 1), L(1, 1, 1), fx);
        return Mathf.Lerp(Mathf.Lerp(x00, x10, fy), Mathf.Lerp(x01, x11, fy), fz);
    }

    static float Fbm(Vector3 p, int octaves) { float s = 0, a = .5f, t = 0; for (int i = 0; i < octaves; i++) { s += Noise(p) * a; t += a; p = p * 2.03f + new Vector3(17.1f, 3.3f, 9.7f); a *= .5f; } return s / t; }

    // ── Helpers ───────────────────────────────────────────────────────────

    static Color Sample(Texture2D t, Vector2 uv)
    {
        int x = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(uv.x, 1f) * t.width), 0, t.width - 1), y = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(uv.y, 1f) * t.height), 0, t.height - 1);
        return t.GetPixel(x, y);
    }

    static Texture2D Palette(Material mat, string p1, string p2, string p3, Dictionary<Texture, Texture2D> cache)
    {
        if (mat == null) return null;
        Texture tex = null;
        foreach (var p in new[] { p1, p2, p3 }) if (p != null && tex == null && mat.HasProperty(p)) tex = mat.GetTexture(p);
        if (tex == null) return null;
        if (cache.TryGetValue(tex, out var t2)) return t2;
        var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(tex, rt); var prev = RenderTexture.active; RenderTexture.active = rt;
        t2 = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false); t2.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0); t2.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        cache[tex] = t2; return t2;
    }

    static void Pad(Color[] c, Color[] e, bool[] cov, int W, int passes)
    {
        var offs = new[] { 1, -1, W, -W };
        for (int pass = 0; pass < passes; pass++)
        {
            var next = (bool[])cov.Clone();
            for (int y = 1; y < W - 1; y++) for (int x = 1; x < W - 1; x++)
            {
                int i = y * W + x; if (cov[i]) continue;
                foreach (var o in offs) if (cov[i + o]) { c[i] = c[i + o]; e[i] = e[i + o]; next[i] = true; break; }
            }
            System.Array.Copy(next, cov, cov.Length);
        }
    }

    static void Save(Color[] px, int W, string path, bool emission)
    {
        var tex = new Texture2D(W, W, TextureFormat.RGBA32, false); tex.SetPixels(px); tex.Apply();
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.filterMode = FilterMode.Point; ti.mipmapEnabled = true; ti.mipmapFilter = TextureImporterMipFilter.BoxFilter; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.maxTextureSize = 4096; ti.wrapMode = TextureWrapMode.Clamp; ti.npotScale = TextureImporterNPOTScale.None; // exact sizes: every model the same pixel size
        ti.SaveAndReimport();
    }
}
