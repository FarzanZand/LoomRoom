var action = "__ACTION__";
var p = PlayerManager.Instance.Active;
if (p == null || p.kind != PlayerKind.Table) return "no table player";
ProgressionManager.Instance.SetFlag("meta.trophy.blocking.15", 3);
p.Stats.Revive();
FirstPersonCapture.Run(3.5f, "zq_" + action, 1234, FirstPersonCapture.View.Front, true);
var im = InventoryManager.Instance;
switch (action) {
  case "eat": { var it = im.FindItem("Apple"); bool ok = p.Equipment.Consume(it, () => { }); return "eat " + (it != null) + " started " + ok; }
  case "drink": { var it = im.FindItem("Crimson tonic"); bool ok = p.Equipment.Consume(it, () => { }); return "drink " + (it != null) + " started " + ok; }
  case "hurt": { p.Stats.TakeDamage(new DamageInfo { Amount = 2f, Direction = p.transform.forward, HitPoint = p.transform.position + UnityEngine.Vector3.up * 1.5f }); return "hurt hp " + p.Stats.CurrentHealth; }
  case "bash": { CombatTestBot.Run("pitch 0; blockon; wait .4; attack; wait 1.6; blockoff"); return "bash queued"; }
  case "walk": { var fld = typeof(InputManager).GetField("<Move>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic); fld.SetValue(InputManager.Instance, new UnityEngine.Vector2(0, 1)); return "walk on"; }
}
return "unknown";
