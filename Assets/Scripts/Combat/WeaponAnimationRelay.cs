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
    // Raised by the PlaySwingAudio event every attack clip carries at the start of the swing.
    public event System.Action SwingStarted;

    void Awake()
    {
        owner = GetComponentInParent<Character>();
        Transform root = owner != null ? owner.transform : transform.root;
        hitboxes = root.GetComponentsInChildren<Hitbox>(true);
    }

    // ── Called by animation events ────────────────────────────────────

    public void EnableHitbox()
    {
        if(owner != null && owner.TryGetComponent<EnemyBrain>(out var brain) && !brain.CanOpenHitbox) return;
        foreach (var h in hitboxes) if (h.HitboxIndex == 0) h.EnableHitbox();
    }

    public void DisableHitbox()
    {
        foreach (var h in hitboxes) h.DisableHitbox();
    }

    public void EnableHitboxAt(int index)
    {
        if(owner != null && owner.TryGetComponent<EnemyBrain>(out var brain) && !brain.CanOpenHitbox) return;
        foreach (var h in hitboxes) if (h.HitboxIndex == index) h.EnableHitbox();
    }

    public void PlaySwingAudio()
    {
        foreach (var h in hitboxes) { h.PlaySwingAudio(); break; }
        SwingStarted?.Invoke();
    }

    public void AttackBegin() => AttackStarted?.Invoke();
    public void AttackEnd()   => AttackEnded?.Invoke();

    public Hitbox GetHitbox(int index)
    {
        foreach (var h in hitboxes) if (h.HitboxIndex == index) return h;
        return hitboxes.Length > 0 ? hitboxes[0] : null;
    }
}
