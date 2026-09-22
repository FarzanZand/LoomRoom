using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Explicit development check. Never runs on import or changes authored assets.
[InitializeOnLoad]
public static class LiveInventoryValidation
{
    const string RequestPath = "Temp/LiveInventory.request";
    const string ReportPath = "Temp/LiveInventoryReport.txt";
    static int phase;
    static double next;
    static float simulationTime, foodRemaining;
    static InventoryUI inventory;
    static Hitbox enemyHitbox, playerHitbox;
    static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    static LiveInventoryValidation() => EditorApplication.update += Tick;
    [MenuItem("Tools/Table Levels/Validate live inventory (enters Play mode)")]
    static void Request() => File.WriteAllText(RequestPath, "run");
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static Hitbox Probe(Character owner)
    {
        var go = new GameObject("Inventory validation hitbox");
        go.transform.SetParent(owner.transform, false);
        go.AddComponent<SphereCollider>().radius = .001f;
        var hitbox = go.AddComponent<Hitbox>();
        // The probe tests hitbox lifetime, without dealing accidental damage.
        typeof(Hitbox).GetField("hitMask", Private).SetValue(hitbox, (LayerMask)0);
        hitbox.EnableHitbox();
        return hitbox;
    }

    static void Tick()
    {
        try
        {
            if (File.Exists(RequestPath) && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                File.Delete(RequestPath);
                SessionState.SetBool("LiveInventoryValidation", true);
                EditorApplication.isPlaying = true;
            }
            if (!EditorApplication.isPlaying) return;
            if (phase == 0 && SessionState.GetBool("LiveInventoryValidation", false))
            {
                SessionState.SetBool("LiveInventoryValidation", false);
                phase = 1;
                next = EditorApplication.timeSinceStartup + 3;
            }
            if (phase == 0 || EditorApplication.timeSinceStartup < next) return;
            var loader = UnityEngine.Object.FindAnyObjectByType<TableLevelLoader>();
            if (loader == null) return;
            var player = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
            if (phase == 1)
            {
                foreach (var cutscene in UnityEngine.Object.FindObjectsByType<CutsceneController>())
                {
                    cutscene.Stop();
                    cutscene.StopAllCoroutines();
                }
                GameManager.Instance.PopAll();
                loader.Load(AssetDatabase.LoadAssetAtPath<TableLevelData>("Assets/Game/Levels/Dungeon1/Dungeon1.asset"));
                phase = 2;
                next = EditorApplication.timeSinceStartup + 8;
            }
            else if (phase == 2 && !loader.Busy)
            {
                inventory = UnityEngine.Object.FindAnyObjectByType<InventoryUI>();
                Check(inventory != null && player.IsActive, "Table inventory/player unavailable");
                inventory.Toggle();
                Check(inventory.IsOpen && GameManager.Instance.State == GameState.Inventory, "Inventory state missing");
                Check(GameManager.Instance.SimulationActive && !GameManager.Instance.GameplayActive, "Simulation/input state not separated");
                Check(Time.timeScale == 1f, "Inventory changed simulation speed");
                Check(Cursor.lockState == CursorLockMode.None, "Inventory cursor is locked");
                player.Stats.EatFood(1, 10);
                foodRemaining = player.Stats.FoodRemaining;
                simulationTime = Time.time;
                var enemy = loader.Dungeon.GetComponentsInChildren<EnemyBrain>().First();
                enemyHitbox = Probe(enemy.Character);
                playerHitbox = Probe(player);
                phase = 3;
                next = EditorApplication.timeSinceStartup + 1;
            }
            else if (phase == 3)
            {
                Check(Time.time > simulationTime + .1f, "Simulation stopped while inventory open");
                Check(player.Stats.FoodRemaining < foodRemaining, "Food timer stopped while inventory open");
                Check(enemyHitbox.Active, "Enemy hitbox was disabled by inventory");
                Check(!playerHitbox.Active, "Player hitbox remained active in inventory");
                inventory.Close();
                phase = 4;
                next = EditorApplication.timeSinceStartup + 1;
            }
            else if (phase == 4)
            {
                Check(!inventory.IsOpen && GameManager.Instance.GameplayActive, "Closing inventory failed to restore control");
                inventory.Toggle(); inventory.Close(); inventory.Toggle(); inventory.Close();
                phase = 5;
                next = EditorApplication.timeSinceStartup + 1;
            }
            else if (phase == 5)
            {
                Check(!GameManager.Instance.IsOpen(GameState.Inventory), "Rapid toggles left inventory state behind");
                Check(Time.timeScale == 1f, "Inventory close changed simulation speed");
                File.WriteAllText(ReportPath, "PASS: inventory releases cursor and disables player control, while simulation, food timers and enemy hitboxes continue. Player hitboxes stop. Closing and rapid toggles restore control without changing time scale.");
                phase = 0;
                EditorApplication.isPlaying = false;
            }
        }
        catch (Exception ex)
        {
            File.WriteAllText(ReportPath, "FAIL: " + ex);
            phase = 0;
            SessionState.SetBool("LiveInventoryValidation", false);
            EditorApplication.isPlaying = false;
        }
    }
}
