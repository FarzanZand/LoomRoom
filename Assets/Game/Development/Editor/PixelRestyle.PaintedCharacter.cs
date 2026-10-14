using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Painted restyles for skinned characters (the Dungeon Master's robe), the character sibling of PixelRestyle.PaintedProps.
// The character is rendered from four sides in the pose it stands in, one large image per side, each repainted by the
// concept image model, and projected back onto fresh UVs. Painting positions come from that pose (Build's PaintPose),
// while the mesh keeps its bind pose, bone weights and bind poses, so it skins as before.
//   1. RenderCharacterViews: Temp/Paint/<name> front/right/back/left.png (flat lit, plain grey background).
//   2. Repaint each (Tools/meshy.py concept --ref ...) into Temp/Paint/<name> <view> painted.png.
//   3. BuildPaintedCharacter: projects the paintings at texelsPerMetre (per mesh unit; Synty characters are in metres)
//      and points the renderer at the result.
public static partial class PixelRestyle
{
    static readonly (string name, Vector3 from)[] CharacterViews =
        { ("front", Vector3.forward), ("right", Vector3.right), ("back", Vector3.back), ("left", Vector3.left) };

    // While set, Build paints each texel from this position and normal (one per source vertex) instead of the bind pose.
    static Vector3[] PaintPose, PaintPoseNormals;
    // The model's own colour at the texel being painted (Build sets it before each PaintSource call).
    static Color PaintBase;

    // Bare skin (pale, warm) or everything else (cloth, rope, the hood's dark inside).
    static bool IsSkin(Color c) => c.grayscale > .45f && c.r - c.b > .12f && c.r >= c.g;

    // For every pixel of a painting, the nearest painted pixel of the given part (skin or not), so a texel never takes
    // its colour from the other part where the painting's outline differs from the model's (a ragged hem over the legs).
    static int[] NearestPart(Texture2D tex, int[] painted, bool skin)
    {
        int w = tex.width, h = tex.height; var px = tex.GetPixels();
        var nearest = new int[w * h]; var queue = new Queue<int>();
        for (int i = 0; i < px.Length; i++)
        {
            bool ok = painted[i] == i && IsSkin(px[i]) == skin;
            nearest[i] = ok ? i : -1; if (ok) queue.Enqueue(i);
        }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue(), x = i % w, y = i / w;
            void Visit(int j) { if (nearest[j] < 0) { nearest[j] = nearest[i]; queue.Enqueue(j); } }
            if (x > 0) Visit(i - 1); if (x < w - 1) Visit(i + 1); if (y > 0) Visit(i - w); if (y < h - 1) Visit(i + w);
        }
        for (int i = 0; i < nearest.Length; i++) if (nearest[i] < 0) nearest[i] = painted[i];
        return nearest;
    }

    // The character in a fixed pose (a clip sampled at a time, on a hidden copy, so whatever the scene's copy is doing
    // doesn't matter), turned so it faces +z. The views must be rendered and projected in the same pose.
    static Mesh PosedMesh(SkinnedMeshRenderer smr, AnimationClip pose, float time)
    {
        var animator = smr.GetComponentInParent<Animator>();
        var copy = Object.Instantiate(animator.gameObject); copy.hideFlags = HideFlags.HideAndDontSave;
        copy.transform.SetPositionAndRotation(new Vector3(0, -15000, 0), Quaternion.identity);
        try
        {
            var ca = copy.GetComponent<Animator>();
            pose.SampleAnimation(copy, time);
            // Humanoid clips are applied through the Animator: sample them with AnimationMode as well.
            if (pose.humanMotion && ca != null)
            {
                AnimationMode.StartAnimationMode(); AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(copy, pose, time);
                AnimationMode.EndSampling();
            }
            var path = AnimationUtility.CalculateTransformPath(smr.transform, animator.transform);
            var csmr = copy.transform.Find(path).GetComponent<SkinnedMeshRenderer>();
            var baked = new Mesh(); csmr.BakeMesh(baked, true);
            var turn = Quaternion.Inverse(Quaternion.Euler(0, csmr.transform.eulerAngles.y, 0)) * csmr.transform.rotation;
            var v = baked.vertices; var n = baked.normals;
            for (int i = 0; i < v.Length; i++) { v[i] = turn * v[i]; if (i < n.Length) n[i] = turn * n[i]; }
            baked.vertices = v; baked.normals = n; baked.RecalculateBounds();
            return baked;
        }
        finally { if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode(); Object.DestroyImmediate(copy); }
    }

    static float ViewHalf(Bounds b) => Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z)) * 1.04f;

    public static string RenderCharacterViews(SkinnedMeshRenderer smr, string name, AnimationClip pose, float time, int size = 1024)
    {
        var posed = PosedMesh(smr, pose, time);
        Directory.CreateDirectory(Path.GetDirectoryName($"{PaintFolder}/{name}"));
        var root = new GameObject("paint character") { hideFlags = HideFlags.DontSave };
        root.transform.position = new Vector3(0, -900, 0);
        var model = new GameObject("model"); model.transform.SetParent(root.transform, false);
        model.AddComponent<MeshFilter>().sharedMesh = posed;
        // Flat lit with the texture at its full size, so the painter sees the cloth, not the pixel lighting's blocks.
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetTexture("_BaseMap", smr.sharedMaterial.GetTexture("_BaseMap")); mat.SetFloat("_Smoothness", 0f);
        model.AddComponent<MeshRenderer>().sharedMaterial = mat;
        var b = posed.bounds; var centre = root.transform.TransformPoint(b.center);
        var camGo = new GameObject("cam"); camGo.transform.SetParent(root.transform, false);
        var cam = camGo.AddComponent<Camera>(); cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = PaintBackground;
        cam.aspect = 1f; cam.nearClipPlane = .01f; cam.farClipPlane = b.size.magnitude * 4 + 10; cam.orthographicSize = ViewHalf(b);
        var light = new GameObject("light").AddComponent<Light>(); light.transform.SetParent(root.transform, false);
        light.type = LightType.Directional; light.intensity = 1.1f;
        var rt = new RenderTexture(size, size, 24); cam.targetTexture = rt;
        var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
        foreach (var (view, from) in CharacterViews)
        {
            camGo.transform.position = centre + from * (b.size.magnitude + 1f);
            camGo.transform.rotation = Quaternion.LookRotation(-from, Vector3.up);
            light.transform.rotation = Quaternion.LookRotation(camGo.transform.forward + camGo.transform.up * -.3f + camGo.transform.right * .3f);
            cam.Render();
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, size, size), 0, 0); tex.Apply();
            File.WriteAllBytes($"{PaintFolder}/{name} {view}.png", tex.EncodeToPNG());
        }
        RenderTexture.active = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); Object.DestroyImmediate(root);
        Object.DestroyImmediate(mat); Object.DestroyImmediate(posed);
        return $"{name}: four views rendered";
    }

    public static string BuildPaintedCharacter(SkinnedMeshRenderer smr, string name, string folder, float texelsPerMetre, AnimationClip pose, float time)
    {
        var posed = PosedMesh(smr, pose, time); var b = posed.bounds; float half = ViewHalf(b);
        int nv = CharacterViews.Length;
        var painted = new Texture2D[nv]; var nearest = new int[nv][]; var nearestSkin = new int[nv][]; var nearestCloth = new int[nv][]; var refBox = new RectInt[nv]; var paintBox = new RectInt[nv];
        for (int v = 0; v < nv; v++)
        {
            var reference = new Texture2D(2, 2); reference.LoadImage(File.ReadAllBytes($"{PaintFolder}/{name} {CharacterViews[v].name}.png"));
            refBox[v] = ObjectBox(reference, out _);
            painted[v] = new Texture2D(2, 2); painted[v].LoadImage(File.ReadAllBytes($"{PaintFolder}/{name} {CharacterViews[v].name} painted.png"));
            // The painting may come back at another size than the render: boxes are compared in each image's own pixels.
            paintBox[v] = ObjectBox(painted[v], out _);
            nearest[v] = NearestPainted(painted[v]);
            nearestSkin[v] = NearestPart(painted[v], nearest[v], true); nearestCloth[v] = NearestPart(painted[v], nearest[v], false);
            Object.DestroyImmediate(reference);
        }
        int refSize = 1024;
        // Occlusion probe: the posed body as a collider, so a face hidden from a view (the torso behind an arm) takes its
        // colour from a view that sees it.
        var probe = new GameObject("PixelRestyle character probe") { hideFlags = HideFlags.HideAndDontSave, layer = ProbeLayer };
        probe.transform.position = new Vector3(0, -14000, 0);
        probe.AddComponent<MeshCollider>().sharedMesh = posed;
        Physics.SyncTransforms();
        int mask = 1 << ProbeLayer;

        Color Sample(int v, Vector3 p)
        {
            var from = CharacterViews[v].from; var right = Vector3.Cross(Vector3.up, -from);
            var d = p - b.center;
            float rx = (Vector3.Dot(d, right) / (half * 2f) + .5f) * refSize, ry = (Vector3.Dot(d, Vector3.up) / (half * 2f) + .5f) * refSize;
            float u = Mathf.InverseLerp(refBox[v].xMin, refBox[v].xMax, rx), w = Mathf.InverseLerp(refBox[v].yMin, refBox[v].yMax, ry);
            var tex = painted[v];
            int x = Mathf.Clamp(Mathf.RoundToInt(paintBox[v].xMin + u * paintBox[v].width), 0, tex.width - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(paintBox[v].yMin + w * paintBox[v].height), 0, tex.height - 1);
            int k = (IsSkin(PaintBase) ? nearestSkin[v] : nearestCloth[v])[y * tex.width + x];
            return tex.GetPixel(k % tex.width, k / tex.width);
        }

        PaintPose = posed.vertices; PaintPoseNormals = posed.normals;
        PaintSource = (p, n) =>
        {
            int best = -1, fallback = 0; float bestDot = float.NegativeInfinity, fallbackDot = float.NegativeInfinity;
            for (int v = 0; v < nv; v++)
            {
                float dot = Vector3.Dot(n, CharacterViews[v].from);
                if (dot > fallbackDot) { fallbackDot = dot; fallback = v; }
                if (dot <= .3f || dot <= bestDot) continue;
                if (Physics.Raycast(probe.transform.position + p + n * .004f, CharacterViews[v].from, 20f, mask, QueryTriggerInteraction.Collide)) continue;
                best = v; bestDot = dot;
            }
            return Sample(best >= 0 ? best : fallback, p);
        };
        try
        {
            var src = smr.sharedMesh;
            AssetDatabase.DeleteAsset($"{folder}/{Path.GetFileName(name)} painted.asset");
            var built = Build(src, smr.sharedMaterials, "Characters", Path.GetFileName(name) + " painted", texelsPerMetre, folder);
            if (built.mesh == null) return name + ": build failed";
            built.mesh.name = Path.GetFileName(name) + " painted";
            var texPath = AssetDatabase.GetAssetPath(built.material.GetTexture("_BaseMap"));
            AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);
            Undo.RecordObject(smr, "Painted character");
            smr.sharedMesh = built.mesh; smr.sharedMaterials = new[] { built.material };
            PrefabUtility.RecordPrefabInstancePropertyModifications(smr);
            EditorUtility.SetDirty(smr);
            var t = built.material.GetTexture("_BaseMap");
            return $"{name}: {t.width}x{t.height} texture, {TexelsPerUnit(smr):0.#} texels per world unit";
        }
        finally
        {
            PaintSource = null; PaintPose = null; PaintPoseNormals = null;
            Object.DestroyImmediate(probe); Object.DestroyImmediate(posed);
            foreach (var t in painted) Object.DestroyImmediate(t);
        }
    }

    // The review render for a painted character, in the same fixed pose: the whole body, the upper and lower halves and
    // each hand, every one from eight sides, two high three-quarter views, straight down and from below
    // (<outPrefix> body.png, upper.png, lower.png, left hand.png, right hand.png).
    public static string RenderCharacterTurntable(SkinnedMeshRenderer smr, AnimationClip pose, float time, string outPrefix, int cell = 768)
    {
        bool asyncShaders = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
        var posed = PosedMesh(smr, pose, time);
        var root = new GameObject("character turntable") { hideFlags = HideFlags.DontSave };
        root.transform.position = new Vector3(0, -980, 0);
        var model = new GameObject("model"); model.transform.SetParent(root.transform, false);
        model.AddComponent<MeshFilter>().sharedMesh = posed;
        var mat = new Material(smr.sharedMaterial) { shader = Shader.Find("Universal Render Pipeline/Lit") };
        model.AddComponent<MeshRenderer>().sharedMaterial = mat;
        var b = posed.bounds;
        var camGo = new GameObject("cam"); camGo.transform.SetParent(root.transform, false);
        var cam = camGo.AddComponent<Camera>(); cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.18f, .17f, .16f);
        cam.aspect = 1f; cam.nearClipPlane = .01f; cam.farClipPlane = b.size.magnitude * 8 + 10;
        var light = new GameObject("light").AddComponent<Light>(); light.transform.SetParent(root.transform, false); light.type = LightType.Directional; light.intensity = 1.1f;
        var fill = new GameObject("fill").AddComponent<Light>(); fill.transform.SetParent(root.transform, false); fill.type = LightType.Directional; fill.intensity = .45f;
        var views = new List<(float pitch, float yaw)>();
        for (int i = 0; i < 8; i++) views.Add((8f, i * 45f));
        views.Add((35f, 35f)); views.Add((35f, 215f)); views.Add((89f, 0f)); views.Add((-25f, 135f));
        // The hands: the outermost vertices at hand height on each side.
        Vector3 Hand(int side)
        {
            Vector3 best = b.center; float bx = side < 0 ? float.MaxValue : float.MinValue;
            foreach (var v in posed.vertices)
            {
                if (v.y < b.center.y - b.extents.y * .35f || v.y > b.center.y + b.extents.y * .2f) continue;
                if (side < 0 ? v.x < bx : v.x > bx) { bx = v.x; best = v; }
            }
            return best - Vector3.right * side * .05f;
        }
        var shots = new (string name, Vector3 centre, float radius)[]
        {
            ("body", b.center, b.extents.magnitude),
            ("upper", b.center + Vector3.up * b.extents.y * .5f, b.extents.magnitude * .52f),
            ("lower", b.center - Vector3.up * b.extents.y * .5f, b.extents.magnitude * .52f),
            ("left hand", Hand(1), .14f), ("right hand", Hand(-1), .14f),
        };
        var rt = new RenderTexture(cell, cell, 24); cam.targetTexture = rt;
        var px = new Texture2D(cell, cell, TextureFormat.RGB24, false);
        foreach (var (shot, c, radius) in shots)
        {
            cam.orthographicSize = radius * 1.02f;
            var centre = root.transform.TransformPoint(c);
            var sheet = new Texture2D(cell * 4, cell * 3, TextureFormat.RGB24, false);
            for (int i = 0; i < views.Count; i++)
            {
                var dir = Quaternion.Euler(views[i].pitch, views[i].yaw, 0) * Vector3.forward;
                camGo.transform.position = centre - dir * (b.extents.magnitude * 3 + 2); camGo.transform.rotation = Quaternion.LookRotation(dir);
                light.transform.rotation = Quaternion.LookRotation(dir + camGo.transform.up * -.6f + camGo.transform.right * .5f);
                fill.transform.rotation = Quaternion.LookRotation(dir + camGo.transform.up * .3f - camGo.transform.right * .8f);
                cam.Render();
                RenderTexture.active = rt; px.ReadPixels(new Rect(0, 0, cell, cell), 0, 0); px.Apply();
                sheet.SetPixels((i % 4) * cell, (2 - i / 4) * cell, cell, cell, px.GetPixels());
            }
            sheet.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(outPrefix + " x"));
            File.WriteAllBytes($"{outPrefix} {shot}.png", sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
        }
        RenderTexture.active = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(px); Object.DestroyImmediate(root);
        Object.DestroyImmediate(mat); Object.DestroyImmediate(posed);
        ShaderUtil.allowAsyncCompilation = asyncShaders;
        return outPrefix;
    }
}
