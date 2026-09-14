using UnityEngine;

// Attach to the same GameObject as the Animator that plays attacks.
// Forwards animation events to the Hitbox components found in the character's hierarchy.
// Event names are kept from the original clips: EnableHitbox / DisableHitbox / PlaySwingAudio.
public class WeaponAnimationRelay : MonoBehaviour
{
    Hitbox[] hitboxes;
    Character owner;

    public event System.Action AttackStarted;
    public event System.Action AttackEnded;

    void Awake()
    {
        owner = GetComponentInParent<Character>();
        Transform root = owner != null ? owner.transform : transform.root;
        hitboxes = root.GetComponentsInChildren<Hitbox>(true);
    }

    // ── Called by animation events ────────────────────────────────────

    public void EnableHitbox()
    {
        foreach (var h in hitboxes) if (h.HitboxIndex == 0) h.EnableHitbox();
    }

    public void DisableHitbox()
    {
        foreach (var h in hitboxes) h.DisableHitbox();
    }

    public void EnableHitboxAt(int index)
    {
        foreach (var h in hitboxes) if (h.HitboxIndex == index) h.EnableHitbox();
    }

    public void PlaySwingAudio()
    {
        foreach (var h in hitboxes) { h.PlaySwingAudio(); break; }
    }

    public void AttackBegin() => AttackStarted?.Invoke();
    public void AttackEnd()   => AttackEnded?.Invoke();

    public Hitbox GetHitbox(int index)
    {
        foreach (var h in hitboxes) if (h.HitboxIndex == index) return h;
        return hitboxes.Length > 0 ? hitboxes[0] : null;
    }
}
