// A candle on each side of the character sheet on the hobby table, framing it (the user, 2026-11-16). Run as an eval
// with Room.unity open, outside Play (safe to run again: it replaces the two candles it made):
//   blender -b -P Tools/table_candlestick.py -- "Assets/Game/World/Art/Hobby table/Table candlestick.fbx" "Assets/Game/World/Art/Hobby table/Source~/Table candlestick.blend"
//   unity command eval_file --file Tools/sheet_candles.eval.cs
// The candlestick is brass in the make of the trophy wall's sconces, which hang right over the table, with their wax,
// brass, wick and flame materials (so their texel size: the model's UVs are one unit per UV as the sconce's are). Each
// is a prefab instance under the Hobby table (World/Art/Hobby table/Table candlestick.prefab: the model, its own small
// warm light as its child, and RoomGlow so the flame goes dark with the room), and each light is a room light
// (LightingManager sceneSources), so it dims and goes out with the room and stays out in the intro's dark.
const string Dir = "Assets/Game/World/Art/Hobby table";
const string Model = Dir + "/Table candlestick.fbx", Prefab = Dir + "/Table candlestick.prefab";
const string Wall = "Assets/Game/World/Art/Trophy wall/";
const float Glow = 16f, Range = 34f;
if (UnityEditor.EditorApplication.isPlaying) return "playing: stop first";
var sb = new System.Text.StringBuilder();
var table = UnityEngine.Object.FindAnyObjectByType<CharacterSheetTable>();
if (table == null) return "no hobby table in the open scene";
UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
var importer = UnityEditor.AssetImporter.GetAtPath(Model) as UnityEditor.ModelImporter;
if (importer == null) return "no model at " + Model + " (run Tools/table_candlestick.py first)";
// Imported as the sconce is: readable, no materials of its own, no animation.
if (importer.materialImportMode != UnityEditor.ModelImporterMaterialImportMode.None || !importer.isReadable || importer.importAnimation)
{
    importer.materialImportMode = UnityEditor.ModelImporterMaterialImportMode.None; importer.isReadable = true;
    importer.importAnimation = false; importer.animationType = UnityEditor.ModelImporterAnimationType.None; importer.importCameras = false; importer.importLights = false;
    importer.SaveAndReimport();
}
var model = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Model);
Material Mat(string name) { var m = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(Wall + name + ".mat"); if (m == null) throw new System.Exception("missing " + Wall + name + ".mat"); return m; }
UnityEngine.Color warm = new UnityEngine.Color(1f, .62f, .32f);

// ── The prefab ──
{
    var root = new GameObject("Table candlestick");
    try
    {
        var m = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(model, root.transform);
        m.name = "Model";
        float tip = 0f, top = 0f;
        foreach (var r in m.GetComponentsInChildren<MeshRenderer>())
        {
            r.sharedMaterial = r.name == "Brass" ? Mat("Sconce brass 6.4") : r.name == "Candle" ? Mat("Candle wax 6.4") : r.name == "Wick" ? Mat("Sconce wick 6.4") : Mat("Sconce flame");
            if (r.name == "Flame") { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; tip = r.bounds.max.y; top = r.bounds.min.y; }
        }
        // Its light: just over the flame, so the candle's own top, the stick and the table round it are lit.
        var lightGo = new GameObject("Candle light"); lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, tip + .4f, 0f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point; light.color = warm; light.intensity = Glow; light.range = Range; light.shadows = LightShadows.None;
        lightGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
        root.AddComponent<RoomGlow>();
        UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, Prefab);
        sb.AppendLine("prefab: flame " + top.ToString("0.00") + ".." + tip.ToString("0.00"));
    }
    finally { UnityEngine.Object.DestroyImmediate(root); }
}
var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);

// ── On the table: one each side of the sheet, a little behind its middle, clear of the figures and the books ──
var sheet = table.transform.Find("Character sheet");
float deskTop = 17.54f;
var desk = table.transform.Find("Desk"); if (desk != null && desk.TryGetComponent<Renderer>(out var deskR)) deskTop = table.transform.InverseTransformPoint(new Vector3(0, deskR.bounds.max.y, 0)).y;
float mid = sheet != null ? sheet.localPosition.x : -1f;
var lm = LightingManager.Instance;
UnityEditor.Undo.RecordObject(lm, "Sheet candles");
var sources = new System.Collections.Generic.List<LightingManager.Source>(lm.sceneSources);
foreach (var (name, side, turn) in new[] { ("Sheet candle left", 1f, 20f), ("Sheet candle right", -1f, 165f) })
{
    var old = table.transform.Find(name);
    if (old != null) { foreach (var l in old.GetComponentsInChildren<Light>(true)) sources.RemoveAll(x => x != null && x.light == l); UnityEngine.Object.DestroyImmediate(old.gameObject); }
    var go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, table.transform);
    go.name = name;
    go.transform.localPosition = new Vector3(mid + side * 8.55f, deskTop, -.8f);
    go.transform.localRotation = Quaternion.Euler(0f, turn, 0f);
    UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Sheet candles");
    var light = go.GetComponentInChildren<Light>(true);
    sources.Add(new LightingManager.Source { light = light, baseIntensity = Glow, baseColor = warm });
    sb.AppendLine(name + " at local " + go.transform.localPosition + " world " + go.transform.position);
}
sources.RemoveAll(x => x == null || x.light == null);
lm.sceneSources = sources.ToArray();
UnityEditor.EditorUtility.SetDirty(lm);
if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(lm)) UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(lm);
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
sb.AppendLine("room lights listed: " + lm.sceneSources.Length);
return sb.ToString();
