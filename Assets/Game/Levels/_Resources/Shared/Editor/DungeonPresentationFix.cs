using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class DungeonPresentationFix
{
    static DungeonPresentationFix() => EditorApplication.update += Tick;
    static void Tick()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/DungeonPresentationFix.request")) return;
        File.Delete("Temp/DungeonPresentationFix.request");
        var mode = EditorSettings.serializationMode; EditorSettings.serializationMode = SerializationMode.ForceText;
        try
        {
            const string gripPath = "Assets/Game/Characters/Resources/Synty humanoid weapon grip.asset";
            ItemDatabaseAuthoring.EnsureFolder("Assets/Game/Characters/Resources");
            var profile = AssetDatabase.LoadAssetAtPath<EnemyWeaponGrip>(gripPath);
            if (profile == null) { profile = ScriptableObject.CreateInstance<EnemyWeaponGrip>(); AssetDatabase.CreateAsset(profile, gripPath); }
            foreach (string name in new[] { "Crypt Soldier", "Crypt Warden", "HumanoidEnemy" })
            {
                string path = name == "HumanoidEnemy" ? "Assets/Game/Characters/Enemies/HumanoidEnemy.prefab" : "Assets/Game/Levels/Dungeon1/Enemies/" + name + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var loadout = root.GetComponent<EnemyWeaponLoadout>();
                    if (loadout == null)
                    {
                        var animator = root.GetComponentInChildren<Animator>(true);
                        var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                        if (hand == null) throw new Exception(name + " has no right hand");
                        foreach (var mesh in hand.GetComponentsInChildren<MeshFilter>(true).Where(m => m.name.StartsWith("SM_Wep")).ToArray())
                            UnityEngine.Object.DestroyImmediate(mesh.gameObject);
                        var oldGrip = hand.Find("Weapon grip - right hand");
                        if (oldGrip != null) UnityEngine.Object.DestroyImmediate(oldGrip.gameObject);
                        loadout = root.AddComponent<EnemyWeaponLoadout>();
                        loadout.grip = profile;
                        loadout.hasWeapon = name != "HumanoidEnemy";
                        loadout.weapon = AssetDatabase.LoadAssetAtPath<ItemData>("Assets/Game/Items/Data/Weapons/" + (name == "Crypt Warden" ? "Grave mace" : "Crypt sword") + ".asset");
                        loadout.RefreshWeapon();
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            const string messagePath = "Assets/Game/UI/Prefabs/Dungeon/Dungeon message.prefab";
            var messageRoot = PrefabUtility.LoadPrefabContents(messagePath);
            try
            {
                var messageRect = messageRoot.GetComponent<RectTransform>();
                messageRect.anchorMin = messageRect.anchorMax = new Vector2(.5f, .35f);
                messageRect.anchoredPosition = Vector2.zero;
                PrefabUtility.SaveAsPrefabAsset(messageRoot, messagePath);
            }
            finally { PrefabUtility.UnloadPrefabContents(messageRoot); }
            var toast = UnityEngine.Object.FindAnyObjectByType<NotificationUI>(FindObjectsInactive.Include);
            if (toast == null) throw new Exception("NotificationUI not found in the loaded scene");
            var rect = toast.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .35f);
            rect.anchoredPosition = Vector2.zero; rect.sizeDelta = new Vector2(640, 70);
            var label = toast.GetComponent<TMPro.TextMeshProUGUI>();
            label.alignment = TMPro.TextAlignmentOptions.Center; label.raycastTarget = false;
            label.fontSize = 26; label.enableAutoSizing = true; label.fontSizeMin = 20; label.fontSizeMax = 26;
            toast.transform.SetAsLastSibling();
            ItemDatabaseAuthoring.EnsureFolder("Assets/Game/UI/Prefabs/Feedback");
            const string toastPath = "Assets/Game/UI/Prefabs/Feedback/Pickup notification.prefab";
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(toastPath))
                PrefabUtility.SaveAsPrefabAssetAndConnect(toast.gameObject, toastPath, InteractionMode.AutomatedAction);
            EditorUtility.SetDirty(toast); EditorSceneManager.MarkSceneDirty(toast.gameObject.scene);
            AssetDatabase.SaveAssets(); EditorSceneManager.SaveOpenScenes();
            File.WriteAllText("Temp/DungeonPresentationFix.report", "PASS: both enemy weapons now use an offset, rotated hand grip; pickup notification anchored at 35% screen height, prefab authored and scene saved.");
        }
        catch (Exception ex) { File.WriteAllText("Temp/DungeonPresentationFix.report", ex.ToString()); Debug.LogException(ex); }
        finally { EditorSettings.serializationMode = mode; }
    }
}
