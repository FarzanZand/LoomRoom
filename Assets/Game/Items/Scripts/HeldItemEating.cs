using System;
using UnityEngine;

// Runtime-only "eat" presentation for a held consumable: moves the holding hand so the
// item travels to the mouth, then runs the completion. Owned by Equipment, which calls
// RestorePose in Update (before the Animator) and Tick in LateUpdate (after it).
public class HeldItemEating
{
    Transform hand, heldItem;
    Player player;
    ItemData item;
    Action complete;
    Vector3 handPosition;
    Quaternion handRotation;
    float started;
    bool poseApplied;

    public bool     IsPlaying => item != null;
    public ItemData Item      => item;

    public bool Play(Player player, ItemData item, Transform anchor, GameObject held, Action complete)
    {
        if (IsPlaying || player == null || !player.IsActive ||
            !PlayerManager.HasInstance || PlayerManager.Instance.OutputCamera == null ||
            held == null || anchor == null || anchor.parent == null) return false;
        hand = anchor.parent;
        heldItem = held.transform;
        this.player = player;
        this.item = item;
        this.complete = complete;
        started = Time.time;
        return true;
    }

    public void Tick()
    {
        if (!IsPlaying) return;
        if (hand == null || heldItem == null || !player.IsActive || !player.IsAlive ||
            !PlayerManager.HasInstance || PlayerManager.Instance.OutputCamera == null)
        { Clear(); return; }
        float progress = (Time.time - started) / Mathf.Max(.1f, item.eatDuration);
        if (progress >= 1)
        {
            var done = complete;
            Clear();
            done?.Invoke();
            return;
        }
        var camera = PlayerManager.Instance.OutputCamera.transform;
        handPosition = hand.localPosition;
        handRotation = hand.localRotation;
        poseApplied = true;
        float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01(progress / .8f));
        var targetRotation = camera.rotation * Quaternion.Euler(item.eatMouthRotation);
        var rotation = Quaternion.Slerp(Quaternion.identity, targetRotation * Quaternion.Inverse(heldItem.rotation), t);
        var targetPosition = Vector3.Lerp(heldItem.position, camera.TransformPoint(item.eatMouthPosition), t);
        hand.rotation = rotation * hand.rotation;
        hand.position += targetPosition - heldItem.position;
    }

    public void RestorePose()
    {
        if (poseApplied && hand != null)
            hand.SetLocalPositionAndRotation(handPosition, handRotation);
        poseApplied = false;
    }

    public void Clear()
    {
        RestorePose();
        item = null;
        complete = null;
        hand = heldItem = null;
        player = null;
    }
}
