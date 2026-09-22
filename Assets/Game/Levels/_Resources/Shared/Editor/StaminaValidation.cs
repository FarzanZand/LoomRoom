using System;
using System.IO;
using System.Reflection;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class StaminaValidation
{
    static int phase;
    static double next, deadline;
    static float depleted;
    static StaminaValidation() => EditorApplication.update += Tick;
    static void Check(bool pass, string message) { if (!pass) throw new Exception(message); }
    static void Field(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    static void Input(string name, object value) => typeof(InputManager).GetProperty(name).SetValue(InputManager.Instance, value);
    static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    static void Tick()
    {
        try
        {
            if (File.Exists("Temp/StaminaValidation.request") && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                File.Delete("Temp/StaminaValidation.request");
                SessionState.SetBool("StaminaValidation", true); EditorApplication.isPlaying = true;
            }
            if (!EditorApplication.isPlaying) return;
            if (phase == 0 && SessionState.GetBool("StaminaValidation", false))
            { SessionState.SetBool("StaminaValidation", false); phase = 1; next = EditorApplication.timeSinceStartup + 3; deadline = next + 35; }
            if (phase == 0 || EditorApplication.timeSinceStartup < next) return;
            Check(EditorApplication.timeSinceStartup < deadline, "Test timed out");
            var loader = Object.FindAnyObjectByType<TableLevelLoader>();
            if (loader == null) return;
            var p = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
            if (phase == 1)
            {
                foreach (var c in Object.FindObjectsByType<CutsceneController>()) { c.Stop(); c.StopAllCoroutines(); }
                GameManager.Instance.PopAll();
                WorldManager.Instance.tableLevelReveal = null;
                loader.Load(AssetDatabase.LoadAssetAtPath<TableLevelData>("Assets/Game/Levels/Dungeon1/Dungeon1.asset"));
                phase = 2; next = EditorApplication.timeSinceStartup + 3;
            }
            else if (phase == 2 && !loader.Busy)
            {
                foreach (var brain in loader.Dungeon.GetComponentsInChildren<EnemyBrain>()) brain.enabled = false;
                p.Stats.Revive();
                Check(p.Stats.MaxStamina == 30 && p.Stats.CurrentMana == 20, "Independent stamina/mana pools");
                Check(p.Stats.GetFinal(StatType.Armor) == 0, "Shield gives passive armor");
                Input("SecondaryHeld", true);
                float shieldArmor = p.Equipment.Get(EquipmentSlot.LeftHand).statModifiers.Where(m => m.stat == StatType.Armor).Sum(m => m.value);
                Check(p.Combat.IsGuarding && Mathf.Approximately(p.Stats.GetFinal(StatType.Armor), shieldArmor), "Shield armor not active while guarding");
                var hit = DamageInfo.Simple(10); hit.Direction = -p.Look.YawTransform.forward;
                p.Stats.TakeDamage(hit);
                Check(Mathf.Abs(p.Stats.CurrentHealth - (30 - Mathf.Max(0, 10 - shieldArmor) * (1 - CombatManager.Instance.blockDamageReduction))) < .01f && Mathf.Abs(p.Stats.CurrentStamina - 24) < .01f, "Frontal block damage/cost incorrect");
                p.Stats.Revive(); hit.Direction = p.Look.YawTransform.forward; p.Stats.TakeDamage(hit);
                Check(p.Stats.CurrentHealth == 20 && p.Stats.CurrentStamina == 30, "Rear hit received shield armor or spent stamina");
                p.Stats.Revive(); float before = p.Stats.CurrentStamina; Call(p.Combat, "Update");
                Check(p.Stats.CurrentStamina < before, "Raised shield did not drain stamina");
                Input("SecondaryHeld", false);
                p.Stats.Revive(); Input("SprintHeld", true); Input("Move", Vector2.up);
                typeof(PlayerMotor).GetProperty("IsSprinting").SetValue(p.Motor, true);
                Call(p.Motor, "ApplyMovement", Vector2.up);
                Check(p.Stats.CurrentStamina < 30 && p.Motor.Speed > p.data.walkSpeed, "Sprint did not drain stamina/use sprint speed");
                Input("SprintHeld", false); Input("Move", Vector2.zero);
                p.Stats.Revive();
                var animator = (Animator)typeof(PlayerCombat).GetField("armsAnimator", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(p.Combat);
                animator.Play("Attack_Hold", animator.GetLayerIndex("RightArm"), .2f); animator.Update(0);
                Check(p.Combat.IsAttacking, "Heavy test failed to enter attack pose");
                Field(p.Combat, "charging", true); Field(p.Combat, "pressedAt", Time.time - 5);
                Call(p.Combat, "OnPrimaryReleased");
                Check(p.Combat.HeavySwing && Mathf.Abs(p.Stats.CurrentStamina - 22) < .01f, "Heavy release did not spend 8 stamina");
                p.Stats.ExhaustStamina();
                Check(!p.Stats.TryUseStamina(8) && !p.Stats.DrainStamina(6), "Exhaustion permits spending");
                p.Stats.RestoreStamina(3); Check(p.Stats.IsExhausted, "Exhaustion cleared before recovery threshold");
                p.Stats.RestoreStamina(5); Check(!p.Stats.IsExhausted, "Exhaustion did not recover at 25 percent");
                p.Stats.ExhaustStamina(); Field(p.Combat, "charging", true); Field(p.Combat, "pressedAt", Time.time - 5);
                Call(p.Combat, "OnPrimaryReleased"); Check(!p.Combat.HeavySwing, "Free heavy attack when exhausted");
                Check(p.Stats.CurrentMana == 20, "Physical actions spent mana");
                p.Stats.Revive(); p.Stats.TryUseStamina(8, 1); depleted = p.Stats.CurrentStamina;
                phase = 3; next = EditorApplication.timeSinceStartup + .4;
            }
            else if (phase == 3)
            {
                Check(Mathf.Abs(p.Stats.CurrentStamina - depleted) < .01f, "Regen ignored delay");
                phase = 4; next = EditorApplication.timeSinceStartup + 1.2;
            }
            else if (phase == 4)
            {
                Check(p.Stats.CurrentStamina > depleted, "Stamina did not regenerate");
                var ui = Object.FindAnyObjectByType<PlayerVitalsUI>(); var so = new SerializedObject(ui);
                Check(so.FindProperty("staminaBarRoot").objectReferenceValue != null && so.FindProperty("manaRoot").objectReferenceValue != null, "Missing stamina/mana HUD references");
                p.Stats.ExhaustStamina(); p.Stats.TryUseStamina(8);
                phase = 8; next = EditorApplication.timeSinceStartup + .25;
            }
            else if (phase == 8)
            {
                p.Stats.TryUseStamina(8);
                var label = (TMPro.TMP_Text)typeof(NotificationUI).GetField("label", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(NotificationUI.Instance);
                Check(label.text == "Not enough stamina" && label.color.a > .5f, "Stamina warning missing or spam restarted its fade");
                ScreenCapture.CaptureScreenshot("Temp/StaminaHUD.png");
                File.WriteAllText("Temp/StaminaValidation.report", "PASS: stamina and mana separate; sprint drain; shield hold/hit costs; frontal-only shield armor; heavy release cost and exhausted fallback; 25% recovery threshold; regen delay and recovery; HUD references; visible throttled stamina warning.");
                phase = 5; next = EditorApplication.timeSinceStartup + .3;
            }
            else if (phase == 5)
            {
                Object.FindAnyObjectByType<InventoryUI>(FindObjectsInactive.Include).Toggle();
                phase = 6; next = EditorApplication.timeSinceStartup + .4;
            }
            else if (phase == 6)
            {
                var panel = Object.FindAnyObjectByType<EquipmentPanelUI>();
                Check(panel != null && panel.stamina != null && panel.stamina.text.Contains("STAMINA"), "Stamina missing from character panel");
                ScreenCapture.CaptureScreenshot("Temp/StaminaInventory.png");
                phase = 7; next = EditorApplication.timeSinceStartup + .3;
            }
            else if (phase == 7) { phase = 0; EditorApplication.isPlaying = false; }
        }
        catch (Exception ex) { File.WriteAllText("Temp/StaminaValidation.report", ex.ToString()); phase = 0; EditorApplication.isPlaying = false; }
    }
}
