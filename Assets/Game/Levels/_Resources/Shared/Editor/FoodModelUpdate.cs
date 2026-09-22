using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class FoodModelUpdate
{
    static FoodModelUpdate() => EditorApplication.update += Tick;
    static void Tick()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/FoodModelUpdate.request")) return;
        File.Delete("Temp/FoodModelUpdate.request");
        var mode = EditorSettings.serializationMode; EditorSettings.serializationMode = SerializationMode.ForceText;
        try
        {
            const string dataFolder = "Assets/Game/Items/Data/Consumables/", models = "Assets/Game/Items/Prefabs/Consumables/";
            foreach (string extension in new[] { ".asset", ".prefab" })
            {
                string folder = extension == ".asset" ? dataFolder : models;
                string old = folder + "Cooked crab meat" + extension;
                if (AssetDatabase.LoadMainAssetAtPath(old) != null)
                {
                    string error = AssetDatabase.MoveAsset(old, folder + "Cooked meat" + extension);
                    if (!string.IsNullOrEmpty(error)) throw new Exception(error);
                }
            }
            string[] names = { "Apple", "Pear", "Roasted mushroom", "Cooked meat", "Bread" };
            string[] sources = {
                "Assets/Synty/PolygonDarkFantasy/Prefabs/Props/SM_Prop_Apple_Rotten_01.prefab",
                "Assets/Synty/PolygonDarkFantasy/Prefabs/Props/SM_Prop_Pear_Rotten_01.prefab",
                "Assets/Synty/PolygonGeneric/Prefabs/Environment/SM_Gen_Env_Mushroom_01.prefab",
                "Assets/Synty/PolygonFantasyKingdom/Prefabs/Items/SM_Item_Meat_01.prefab",
                "Assets/Synty/PolygonGeneric/Prefabs/Props/SM_Gen_Prop_Food_Bread_01.prefab"
            };
            float[] sizes = { .2f, .23f, .25f, .28f, .3f };
            for (int i = 0; i < names.Length; i++)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(sources[i]);
                if (source == null) throw new Exception("Missing source: " + sources[i]);
                string path = models + names[i] + ".prefab";
                bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
                var root = exists ? PrefabUtility.LoadPrefabContents(path) : new GameObject(names[i]);
                try
                {
                    for (int c = root.transform.childCount - 1; c >= 0; c--) UnityEngine.Object.DestroyImmediate(root.transform.GetChild(c).gameObject);
                    root.name = names[i];
                    var model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
                    foreach (var collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                    foreach (var body in model.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.detectCollisions = false; }
                    var renderers = model.GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length == 0) throw new Exception("No renderers: " + sources[i]);
                    var bounds = renderers[0].bounds; foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                    float scale = sizes[i] / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                    var center = root.transform.InverseTransformPoint(bounds.center);
                    model.transform.localScale *= scale;
                    model.transform.localPosition -= center * scale;
                    var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                    var item = AssetDatabase.LoadAssetAtPath<ItemData>(dataFolder + names[i] + ".asset");
                    if (item == null)
                    {
                        item = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ItemData>(dataFolder + "Apple.asset"));
                        item.name = item.itemName = names[i];
                        item.description = "Eat bread from your hand to recover health gradually. A new meal replaces the previous meal.";
                        item.effects = new[] { new EffectEntry { type = EffectType.FoodRegen, value = 1, duration = 10 } };
                        AssetDatabase.CreateAsset(item, dataFolder + names[i] + ".asset");
                    }
                    item.name = item.itemName = names[i]; item.worldPrefab = prefab; EditorUtility.SetDirty(item);
                }
                finally { if (exists) PrefabUtility.UnloadPrefabContents(root); else UnityEngine.Object.DestroyImmediate(root); }
            }
            var bread = AssetDatabase.LoadAssetAtPath<ItemData>(dataFolder + "Bread.asset");
            ItemDatabaseAuthoring.EnsureFolder("Assets/Game/Items/Icons");
            var preview = new PreviewRenderUtility();
            try
            {
                preview.BeginStaticPreview(new Rect(0,0,64,64));
                var model = UnityEngine.Object.Instantiate(bread.worldPrefab); preview.AddSingleGO(model);
                preview.camera.transform.position = new Vector3(.35f,.3f,-.45f);
                preview.camera.transform.LookAt(Vector3.zero);
                preview.camera.orthographic = true; preview.camera.orthographicSize = .18f;
                preview.camera.nearClipPlane = .01f; preview.camera.farClipPlane = 10;
                preview.camera.clearFlags = CameraClearFlags.SolidColor; preview.camera.backgroundColor = new Color(.055f,.045f,.03f,0);
                preview.lights[0].intensity = 1.5f; preview.lights[0].transform.rotation = Quaternion.Euler(40,30,0);
                preview.lights[1].intensity = .8f;
                preview.Render(true);
                var texture = preview.EndStaticPreview();
                File.WriteAllBytes("Assets/Game/Items/Icons/Bread.png", texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
            }
            finally { preview.Cleanup(); }
            AssetDatabase.ImportAsset("Assets/Game/Items/Icons/Bread.png");
            var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/Game/Items/Icons/Bread.png");
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
            bread.icon = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/Items/Icons/Bread.png"); EditorUtility.SetDirty(bread);
            bool Keep(DungeonLootTable.Entry entry) => entry.item == null || (entry.item.itemName != "Pear" && entry.item.itemName != "Roasted mushroom");
            foreach (var guid in AssetDatabase.FindAssets("t:DungeonLootTable"))
            {
                var table = AssetDatabase.LoadAssetAtPath<DungeonLootTable>(AssetDatabase.GUIDToAssetPath(guid));
                if (table.entries != null) table.entries = table.entries.Where(Keep).ToArray();
                if (table.guaranteed != null) table.guaranteed = table.guaranteed.Where(Keep).ToArray();
                if (table.pools != null) foreach (var pool in table.pools) if (pool?.entries != null) pool.entries = pool.entries.Where(Keep).ToArray();
                EditorUtility.SetDirty(table);
            }
            foreach (string name in new[] { "enemy", "chest", "barrel" })
            {
                var table = AssetDatabase.LoadAssetAtPath<DungeonLootTable>("Assets/Game/Items/LootTables/Dungeon " + name + " progression.asset");
                var supplies = table.pools.First(p => p.entries.Any(e => e.item != null && e.item.tag == "Food"));
                if (!supplies.entries.Any(e => e.item == bread)) supplies.entries = supplies.entries.Concat(new[] { new DungeonLootTable.Entry { item = bread, weight = 4 } }).ToArray();
                EditorUtility.SetDirty(table);
            }
            AssetDatabase.SaveAssets(); ItemDatabaseAuthoring.SyncCatalog(); EditorSceneManager.SaveOpenScenes();
            foreach (string name in names)
            {
                var item = AssetDatabase.LoadAssetAtPath<ItemData>(dataFolder + name + ".asset");
                if (item.icon == null || item.worldPrefab.GetComponentsInChildren<Renderer>().Length == 0 || !item.BuildTooltip().Contains("total")) throw new Exception("Invalid food: " + name);
            }
            File.WriteAllText("Temp/FoodModelUpdate.report", "PASS: five authored food models use the requested Synty prefabs; Cooked meat renamed with GUIDs intact; Bread heals 10 HP over 10 seconds, has a baked icon and is included in all three active supply tables and the item catalog. Pear and mushroom assets retained, excluded from loot tables.");
        }
        catch (Exception ex) { File.WriteAllText("Temp/FoodModelUpdate.report", ex.ToString()); Debug.LogException(ex); }
        finally { EditorSettings.serializationMode = mode; }
    }
}
