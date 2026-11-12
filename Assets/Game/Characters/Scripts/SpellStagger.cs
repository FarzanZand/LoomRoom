using System.Collections.Generic;
using UnityEngine;

// A spell the table player throws up out of the dungeon that strikes the Dungeon Master, or the room player's own body
// standing at the table, makes him flinch: a short recoil away from the hit, then back to whatever he was doing (the
// user, 2026-11-12). The motion is the clips `Spell stagger back/left/right` (Characters/Animations/ThirdPerson/
// Gestures, edit them freely) on an additive layer "Spell stagger" in HumanoidController (the DM) and ThirdPersonRoom
// (the room body), made by SpellStaggerSetup.Build(): additive, so it plays over his pose whatever it is (seated,
// standing by his chair, leaning over the table) and leaves it as it was. This only picks the side and fires it:
// StaggerSide is the way the body recoils, -1 to its left, 0 straight back, 1 to its right (the way the spell flew).
// Called from SpellProjectile where it finds those hits.
public static class SpellStagger
{
    public const string TriggerName = "SpellStagger", SideName = "StaggerSide";
    // A second hit within this long (a piercing spell, a volley) doesn't start it over.
    const float Gap = .25f;

    static readonly Dictionary<Animator, float> last = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => last.Clear();

    // The Dungeon Master struck by a spell flying along `travel`.
    public static void HitDungeonMaster(Vector3 travel)
    {
        if (!TableManager.HasInstance || TableManager.Instance.DM == null) return;
        foreach (var animator in TableManager.Instance.DM.GetComponentsInChildren<Animator>()) Play(animator, travel);
    }

    // The room player's body at the table struck by the table player's own spell flying along `travel`.
    public static void HitRoomSelf(Vector3 travel)
    {
        var room = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Room) : null;
        if (room == null) return;
        foreach (var animator in room.ActivationRoot.GetComponentsInChildren<Animator>()) Play(animator, travel);
    }

    static void Play(Animator animator, Vector3 travel)
    {
        if (animator == null || !animator.isActiveAndEnabled || !Character.HasParameter(animator, TriggerName, AnimatorControllerParameterType.Trigger)) return;
        if (last.TryGetValue(animator, out float at) && Time.time - at < Gap) return;
        last[animator] = Time.time;
        // Only the level part of its flight picks the side: a spell coming straight at him (or straight up) throws him back.
        var flat = Vector3.ProjectOnPlane(travel, Vector3.up);
        float side = flat.sqrMagnitude < 1e-4f ? 0f : Mathf.Clamp(Vector3.Dot(flat.normalized, animator.transform.right) * 1.4f, -1f, 1f);
        AnimatorHelper.SetFloat(animator, SideName, side);
        animator.SetTrigger(TriggerName);
    }
}
