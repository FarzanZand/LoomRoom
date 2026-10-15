using System;
using UnityEngine;

// Eating or drinking a consumable (ItemData.AnimationOnUse Eat or Drink): the item is shown in the right hand in place of
// whatever is held there (a weapon stays equipped, only hidden), the right arm plays the Eat or Drink clip
// (FirstPersonTable.controller, RightArm layer: UseItem trigger, UseItemType 1 eat / 2 drink, UseItemSpeed fits the clip
// to ItemData.eatDuration), the item takes effect near the end and the held item comes back. It waits for a swing or a
// spell in progress to finish before it starts; while it runs PlayerCombat and PlayerSpellcasting start nothing
// (Equipment.IsUsingItem). Owned by Equipment, which ticks it in Update.
public class ConsumableUse
{
    // How far through the clip the item takes effect: the last bite or the last swallow.
    const float UseAt = .72f;
    // A swing or a spell still going after this long: the request is dropped.
    const float WaitLimit = 2f;

    Equipment equipment;
    Player player;
    ItemData item;
    Action use;
    Animator arms;
    GameObject shown, hidden;
    float asked, started = -1f, length;
    bool used;

    public bool Busy => item != null;
    public ItemData Item => item;

    public bool Begin(Equipment owner, Player by, ItemData what, Action onUse)
    {
        if (Busy || owner == null || by == null || what == null) return false;
        equipment = owner; player = by; item = what; use = onUse;
        asked = Time.time; started = -1f; used = false;
        return true;
    }

    public void Tick()
    {
        if (!Busy) return;
        if (player == null || !player.IsAlive || !player.IsActive) { Cancel(); return; }
        if (started < 0f)
        {
            if (Waiting()) { if (Time.time - asked > WaitLimit) Cancel(); return; }
            Start();
            return;
        }
        float t = (Time.time - started) / length;
        if (!used && t >= UseAt) { used = true; use?.Invoke(); }
        if (t >= 1f) End();
    }

    // Stopped before the end (the player died or was put away): nothing is used, the held item comes back.
    public void Cancel() => End();

    bool Waiting()
    {
        if (player.Combat != null && player.Combat.IsAttacking) return true;
        var spells = player.GetComponent<PlayerSpellcasting>();
        return spells != null && spells.Busy;
    }

    void Start()
    {
        var anchor = equipment.GetAnchor(EquipmentSlot.RightHand);
        arms = anchor != null ? anchor.GetComponentInParent<Animator>() : null;
        // The first-person hands may have no animator of their own (they copy the body's pose): use the player's.
        if (arms == null && player.Combat != null) arms = player.Combat.ArmsAnimator;
        int type = item.AnimationOnUse == ItemUseAnimation.Drink ? 2 : 1;
        var clip = Clip(arms, type == 2 ? "Drink" : "Eat");
        // No arms to play it on (the room player): used at once.
        if (clip == null || !Character.HasParameter(arms, "UseItem", AnimatorControllerParameterType.Trigger))
        {
            use?.Invoke();
            End();
            return;
        }
        length = Mathf.Max(.2f, item.eatDuration);
        hidden = equipment.HeldObject(EquipmentSlot.RightHand);
        if (hidden != null) hidden.SetActive(false);
        if (item.worldPrefab != null)
        {
            shown = UnityEngine.Object.Instantiate(item.worldPrefab, anchor);
            shown.transform.SetLocalPositionAndRotation(item.heldPosition, Quaternion.Euler(item.heldRotation));
            shown.transform.localScale *= Mathf.Max(.01f, item.heldScale);
            foreach (var c in shown.GetComponentsInChildren<Collider>()) c.enabled = false;
            foreach (var rb in shown.GetComponentsInChildren<Rigidbody>()) UnityEngine.Object.Destroy(rb);
            foreach (var t in shown.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = anchor.gameObject.layer;
        }
        AnimatorHelper.SetInteger(arms, "UseItemType", type);
        AnimatorHelper.SetFloat(arms, "UseItemSpeed", clip.length / length);
        AnimatorHelper.SetTrigger(arms, "UseItem");
        started = Time.time;
    }

    static AnimationClip Clip(Animator animator, string name)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return null;
        foreach (var c in animator.runtimeAnimatorController.animationClips) if (c != null && c.name == name) return c;
        return null;
    }

    void End()
    {
        if (shown != null) UnityEngine.Object.Destroy(shown);
        if (hidden != null) hidden.SetActive(true);
        if (arms != null && started >= 0f && !used) AnimatorHelper.ResetTrigger(arms, "UseItem");
        shown = hidden = null; arms = null;
        item = null; use = null; player = null; equipment = null;
        started = -1f;
    }
}
