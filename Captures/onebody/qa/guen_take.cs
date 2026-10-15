// Guen's one-body rows, same method as qa_take.cs: a fresh Debug session per action, the same file in both editors.
// Replace __ACTION__ with one of: lightA, lightB, heavy, tension0, tension50, tension100, hurtrelease, fireball, heal,
// strain, run, crouch, jump. Output: Temp/FirstPerson/gt_<action>. Takes 3.5 s; movement input is cleared after it.
var action = "__ACTION__";
// "<action>_nolegs": the same take with the one body's Legs layer switched off (proves the legs never reach first person).
var take = action;
bool noLegs = action.EndsWith("_nolegs");
if (noLegs) action = action.Substring(0, action.Length - 7);
var p = PlayerManager.Instance.Active;
if (p == null || p.kind != PlayerKind.Table) return "no table player";
var im = InventoryManager.Instance;
var input = InputManager.Instance;
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
System.Action<string, object> setProp = (name, value) => typeof(InputManager).GetProperty(name).GetSetMethod(true).Invoke(input, new object[] { value });
System.Action<string> fire = name => (typeof(InputManager).GetField(name, flags).GetValue(input) as System.Action)?.Invoke();
setProp("Move", UnityEngine.Vector2.zero); setProp("SprintHeld", false); setProp("CrouchHeld", false);
p.Stats.Revive();
p.Look.SetYaw(0f); p.Look.SetPitch(0f);
CombatManager.Instance.chargeShake = 0f;
// The capture starts first (it seeds Random, fixes the frame rate and restarts the arms), then the action, as in qa_take.cs.
FirstPersonCapture.Run(3.5f, "gt_" + take, 1234, FirstPersonCapture.View.Front, true);
// Movement input stops after the take (game time, so the same frame in both editors).
DG.Tweening.DOVirtual.DelayedCall(3.6f, () => { setProp("Move", UnityEngine.Vector2.zero); setProp("SprintHeld", false); setProp("CrouchHeld", false); }, false);
string result = take;
if (noLegs)
{
    var body = UnityEngine.Object.FindAnyObjectByType<OneBodyRig>().animator;
    int legs = body.GetLayerIndex("Legs");
    body.SetLayerWeight(legs, 0f);
    result += " legs layer " + legs + " off";
}
switch (action)
{
    case "lightA": CombatTestBot.Run("attack; wait 1.5"); break;
    case "lightB": CombatTestBot.Run("attack; wait .75; attack; wait 1.5"); break;
    case "heavy": CombatTestBot.Run("hold 1.6; wait 1.2"); break;
    case "tension0": CombatManager.Instance.chargeTension = 0f; CombatTestBot.Run("hold 1.6; wait 1.2"); break;
    case "tension50": CombatManager.Instance.chargeTension = .5f; CombatTestBot.Run("hold 1.6; wait 1.2"); break;
    case "tension100": CombatManager.Instance.chargeTension = 1f; CombatTestBot.Run("hold 1.6; wait 1.2"); break;
    case "hurtrelease":
    {
        // A hit the moment the swing is released (the arms skip the hurt trigger while attacking).
        var relay = p.GetComponentInChildren<WeaponAnimationRelay>(true);
        System.Action hit = null;
        hit = () => { relay.SwingStarted -= hit; p.Stats.TakeDamage(new DamageInfo { Amount = 2f, Direction = p.transform.forward, HitPoint = p.transform.position + UnityEngine.Vector3.up * 1.5f }); };
        relay.SwingStarted += hit;
        CombatTestBot.Run("attack; wait 1.5");
        break;
    }
    // A plain hit, standing (as qa_take.cs's hurt).
    case "hurt": p.Stats.TakeDamage(new DamageInfo { Amount = 2f, Direction = p.transform.forward, HitPoint = p.transform.position + UnityEngine.Vector3.up * 1.5f }); break;
    case "fireball":
    case "heal":
    {
        var tome = im.FindItem(action == "fireball" ? "Fireball Tome" : "Heal Tome");
        if (tome == null) return "no tome";
        if (action == "heal") p.Stats.TakeDamage(new DamageInfo { Amount = 10f, Magic = true, Direction = p.transform.forward, HitPoint = p.transform.position + UnityEngine.Vector3.up * 1.5f });
        // A hand item is equipped from the hotbar, so the tome goes there first.
        if (p.Hotbar.IndexOf(tome) < 0 && p.Bag.IndexOf(tome) < 0) p.Hotbar.TryAdd(tome);
        bool ok = p.Equipment.Equip(tome, false);
        // The tome takes a moment to come up; the cast is pressed once it has (secondary = cast with a tome in the left hand).
        CombatTestBot.Run("wait .8; blockon; wait .1; blockoff; wait 2");
        result += " equipped " + ok;
        break;
    }
    // The shield held against the beam (BlockStrain), set as the beam sets it, between .5 s and 2.5 s of a held block.
    case "strain":
        CombatTestBot.Run("blockon; wait 3; blockoff");
        DG.Tweening.DOVirtual.DelayedCall(.5f, () => p.Combat.SetShieldStrain(true), false);
        DG.Tweening.DOVirtual.DelayedCall(2.5f, () => p.Combat.SetShieldStrain(false), false);
        break;
    case "run": setProp("Move", new UnityEngine.Vector2(0, 1)); setProp("SprintHeld", true); fire("SprintPressed"); break;
    case "crouch": setProp("CrouchHeld", true); fire("CrouchPressed"); DG.Tweening.DOVirtual.DelayedCall(.6f, () => setProp("Move", new UnityEngine.Vector2(0, 1)), false); break;
    case "jump": fire("JumpPressed"); break;
    // Outside-look takes (the one body's legs and lean): plain walking each way, and looking up and down.
    case "walkF": setProp("Move", new UnityEngine.Vector2(0, 1)); break;
    case "walkB": setProp("Move", new UnityEngine.Vector2(0, -1)); break;
    case "walkR": setProp("Move", new UnityEngine.Vector2(1, 0)); break;
    case "walkL": setProp("Move", new UnityEngine.Vector2(-1, 0)); break;
    case "lookUp": DG.Tweening.DOVirtual.DelayedCall(.3f, () => p.Look.SetPitch(-70f), false); break;
    case "lookDown": DG.Tweening.DOVirtual.DelayedCall(.3f, () => p.Look.SetPitch(70f), false); break;
    default: return "unknown action " + action;
}
return result;
