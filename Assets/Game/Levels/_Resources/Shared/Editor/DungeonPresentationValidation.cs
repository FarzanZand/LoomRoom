using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class DungeonPresentationValidation
{
    static int phase;
    static double next;
    static EnemyWeaponLoadout enemy;
    static DungeonPresentationValidation() => EditorApplication.update += Tick;
    static void Tick()
    {
        try
        {
            if (File.Exists("Temp/DungeonPresentationValidation.request"))
            {
                File.Delete("Temp/DungeonPresentationValidation.request");
                SessionState.SetBool("DungeonPresentationValidation", true); EditorApplication.isPlaying = true;
            }
            if (!EditorApplication.isPlaying) return;
            if (phase == 0 && SessionState.GetBool("DungeonPresentationValidation", false))
            { SessionState.SetBool("DungeonPresentationValidation", false); phase = 1; next = EditorApplication.timeSinceStartup + 3; }
            if (phase == 0 || EditorApplication.timeSinceStartup < next) return;
            var loader = UnityEngine.Object.FindAnyObjectByType<TableLevelLoader>();
            if (loader == null) return;
            var player = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
            if (phase == 1)
            {
                foreach (var cutscene in UnityEngine.Object.FindObjectsByType<CutsceneController>()) { cutscene.Stop(); cutscene.StopAllCoroutines(); }
                GameManager.Instance.PopAll();
                var level = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<TableLevelData>("Assets/Game/Levels/Dungeon1/Dungeon1.asset"));
                level.fixedSeed = 713; loader.Load(level); phase = 2; next = EditorApplication.timeSinceStartup + 8;
            }
            else if (phase == 2 && !loader.Busy)
            {
                foreach (var brain in loader.Dungeon.GetComponentsInChildren<EnemyBrain>()) brain.enabled = false;
                if (loader.Dungeon.GetComponentsInChildren<WorldItem>().Any()) throw new Exception("Unexpected loose item at dungeon start");
                var roomPlayer = PlayerManager.Instance.GetPlayer(PlayerKind.Room);
                int roomNpcs = 0;
                foreach (var npc in UnityEngine.Object.FindObjectsByType<NpcBrain>(FindObjectsInactive.Include))
                {
                    if (!npc.CanInteract(roomPlayer)) continue;
                    roomNpcs++;
                    if (npc.CanInteract(player)) throw new Exception("Table player can talk to room NPC");
                    var trigger = npc.GetComponentInChildren<InteractableTrigger>(true);
                    if (trigger != null && trigger.CanInteract(player)) throw new Exception("Room NPC prompt available to table player");
                    var state = npc.State; npc.Interact(player);
                    if (npc.State != state) throw new Exception("Direct cross-player NPC interaction changed state");
                }
                if (roomNpcs == 0) throw new Exception("No interactable room NPC available to test");

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Levels/Dungeon1/Enemies/Crypt Warden.prefab");
                var go = UnityEngine.Object.Instantiate(prefab, loader.Dungeon.transform, false);
                var center = DungeonLayout.Center(loader.Dungeon.Layout.rooms[0]);
                var data = loader.Dungeon.LevelData;
                Vector3 localCenter = new Vector3((center.x-data.width*.5f)*data.cellSize, .12f, (center.y-data.depth*.5f)*data.cellSize);
                player.Motor.Controller.enabled = false;
                player.transform.position = loader.Dungeon.transform.TransformPoint(localCenter + Vector3.back * 2);
                player.Motor.Controller.enabled = true; player.Look.SetYaw(0);
                go.transform.position = loader.Dungeon.transform.TransformPoint(localCenter + Vector3.forward);
                go.transform.rotation = Quaternion.Euler(0,180,0);
                go.GetComponent<EnemyBrain>().enabled = false;
                go.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled = false;
                enemy = go.GetComponent<EnemyWeaponLoadout>();
                var toast = UnityEngine.Object.FindAnyObjectByType<NotificationUI>();
                var so = new SerializedObject(toast); so.FindProperty("hold").floatValue = 10; so.ApplyModifiedPropertiesWithoutUndo();
                InventoryManager.Instance.Pickup(AssetDatabase.LoadAssetAtPath<ItemData>("Assets/Game/Items/Data/Consumables/Apple.asset"), player);
                var library = new SerializedObject(AudioManager.Instance).FindProperty("sfxLibrary");
                AudioClip pickupClip = null; float pickupVolume = 0;
                for (int i = 0; i < library.arraySize; i++)
                {
                    var entry = library.GetArrayElementAtIndex(i);
                    if (entry.FindPropertyRelative("key").stringValue != "genericPickupSound") continue;
                    pickupClip = (AudioClip)entry.FindPropertyRelative("clip").objectReferenceValue;
                    pickupVolume = entry.FindPropertyRelative("volume").floatValue;
                }
                bool PickupSoundPlaying() => AudioManager.Instance.GetComponentsInChildren<AudioSource>().Any(a => a.clip == pickupClip && a.isPlaying && a.spatialBlend == 0 && Mathf.Approximately(a.volume,pickupVolume) && a.outputAudioMixerGroup != null && a.outputAudioMixerGroup.name == "SFX");
                if (!PickupSoundPlaying()) throw new Exception("Apple did not play shared pickup sound through SFX mixer");
                AudioManager.Instance.StopAllSFX();
                InventoryManager.Instance.Pickup(AssetDatabase.LoadAssetAtPath<ItemData>("Assets/Game/Items/Data/Consumables/Bread.asset"), player);
                if (!PickupSoundPlaying()) throw new Exception("Bread did not play shared pickup sound");

                phase = 3; next = EditorApplication.timeSinceStartup + 1;
            }
            else if (phase == 3)
            {
                if (enemy.Visual == null || enemy.Attachment.parent != enemy.GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.RightHand)) throw new Exception("Weapon not attached to humanoid right hand");
                ScreenCapture.CaptureScreenshot("Temp/EnemyGripAndToast.png");
                phase = 4; next = EditorApplication.timeSinceStartup + 1;
            }
            else if (phase == 4)
            {
                enemy.hasWeapon = false; enemy.RefreshWeapon();
                if (enemy.Visual != null) throw new Exception("Has Weapon off retained weapon");
                enemy.weapon = AssetDatabase.LoadAssetAtPath<ItemData>("Assets/Game/Items/Data/Weapons/Crypt sword.asset");
                enemy.hasWeapon = true; enemy.RefreshWeapon();
                if (enemy.Visual == null || enemy.Visual.transform.parent != enemy.Attachment) throw new Exception("Weapon swap lost shared grip");
                phase = 5; next = EditorApplication.timeSinceStartup + 1;
            }
            else if (phase == 5)
            {
                ScreenCapture.CaptureScreenshot("Temp/EnemySwordGrip.png");
                File.WriteAllText("Temp/DungeonPresentationValidation.report", "PASS: room NPC rejects table prompt and direct interaction while remaining available to room player; no loose starting pickup; Apple and Bread play genericPickupSound at configured volume through SFX mixer; weapon attaches to humanoid hand; Has Weapon removes visual; changing database weapon reuses shared grip. In-game captures created for mace, sword and centered pickup notification.");
                phase = 6; next = EditorApplication.timeSinceStartup + 1;
            }
            else if (phase == 6) { phase = 0; EditorApplication.isPlaying = false; }
        }
        catch (Exception ex) { File.WriteAllText("Temp/DungeonPresentationValidation.report", ex.ToString()); phase = 0; SessionState.SetBool("DungeonPresentationValidation", false); EditorApplication.isPlaying = false; }
    }
}
