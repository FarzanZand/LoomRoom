// The Z's over a sleeper's head (SleepZs, the user, 2026-11-13): import settings for Tools/sleep_zs.py's sprite sheet,
// its material, and the prefab Characters/Resources/Sleep Zs.prefab (SleepZs and its Letters particle system) that
// EnemyBrain.Sleep puts under every enemy that falls asleep. Safe to run again: the prefab keeps its SleepZs settings.
const string Dir = "Assets/Game/Characters/Enemies/Sleep";
const string Path = "Assets/Game/Characters/Resources/Sleep Zs.prefab";
UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);

var ti = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(Dir + "/Sleep Zs.png");
if (ti == null) return "no " + Dir + "/Sleep Zs.png: run python Tools/sleep_zs.py \"" + Dir + "\" first";
ti.textureType = UnityEditor.TextureImporterType.Default; ti.sRGBTexture = true; ti.alphaSource = UnityEditor.TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = true;
ti.filterMode = FilterMode.Point; ti.npotScale = UnityEditor.TextureImporterNPOTScale.None; ti.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
ti.mipmapEnabled = false; ti.wrapMode = TextureWrapMode.Clamp; ti.SaveAndReimport();
var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/Sleep Zs.png");
int tiles = tex.width / tex.height;

// Unlit and alpha blended: read in the dark, never lit white by a lamp beside it. The tone under white is the sprite's own.
var matPath = Dir + "/Sleep Zs.mat";
var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(matPath);
if (mat == null) { mat = new Material(shader); UnityEditor.AssetDatabase.CreateAsset(mat, matPath); }
mat.shader = shader;
mat.SetTexture("_BaseMap", tex); mat.SetColor("_BaseColor", Color.white);
mat.SetFloat("_Surface", 1); mat.SetFloat("_Blend", 0); mat.SetFloat("_Cull", 2); mat.SetFloat("_ZWrite", 0); mat.SetFloat("_ColorMode", 0);
mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One); mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
mat.SetOverrideTag("RenderType", "Transparent");
mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
UnityEditor.EditorUtility.SetDirty(mat);

bool exists = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Path) != null;
var prefab = exists ? UnityEditor.PrefabUtility.LoadPrefabContents(Path) : new GameObject("Sleep Zs");
try
{
    var old = prefab.transform.Find("Letters"); if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
    var go = new GameObject("Letters"); go.transform.SetParent(prefab.transform, false);
    var ps = go.AddComponent<ParticleSystem>();
    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    var main = ps.main;
    main.loop = true; main.playOnAwake = true; main.duration = 1; main.maxParticles = 24;
    main.simulationSpace = ParticleSystemSimulationSpace.World; main.scalingMode = ParticleSystemScalingMode.Local;
    main.startLifetime = 2.4f; main.startSpeed = 0; main.startSize = .1875f; main.startColor = Color.white; main.startRotation = 0; main.gravityModifier = 0;
    var em = ps.emission; em.enabled = true; em.rateOverTime = 0;      // SleepZs emits them one by one
    var shape = ps.shape; shape.enabled = false;
    var sheet = ps.textureSheetAnimation;
    sheet.enabled = true; sheet.numTilesX = tiles; sheet.numTilesY = 1; sheet.animation = ParticleSystemAnimationType.WholeSheet;
    sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0); sheet.startFrame = new ParticleSystem.MinMaxCurve(0); sheet.cycleCount = 1;
    // A letter pops out, holds, and fades in hard steps (pixel art: no smooth fade).
    var size = ps.sizeOverLifetime; size.enabled = true;
    size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, .5f), new Keyframe(.08f, 1.12f), new Keyframe(.16f, 1f), new Keyframe(1, 1f)));
    var colour = ps.colorOverLifetime; colour.enabled = true;
    var g = new Gradient { mode = GradientMode.Fixed };
    g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
              new[] { new GradientAlphaKey(1f, .62f), new GradientAlphaKey(.66f, .78f), new GradientAlphaKey(.33f, .92f), new GradientAlphaKey(0f, 1f) });
    colour.color = g;
    var r = go.GetComponent<ParticleSystemRenderer>();
    r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.Billboard; r.alignment = ParticleSystemRenderSpace.View;
    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; r.maxParticleSize = .2f;

    var zs = prefab.GetComponent<SleepZs>(); if (zs == null) zs = prefab.AddComponent<SleepZs>();
    // A prefab made before keeps what was tuned on it; a new one takes the script's own values.
    zs.letters = ps;
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(prefab, Path);
}
finally { if (exists) UnityEditor.PrefabUtility.UnloadPrefabContents(prefab); else UnityEngine.Object.DestroyImmediate(prefab); }
UnityEditor.AssetDatabase.SaveAssets();
return "Sleep Zs prefab made, " + tiles + " tiles";
