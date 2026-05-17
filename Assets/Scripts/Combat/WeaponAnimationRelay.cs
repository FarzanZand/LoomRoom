using UnityEngine;

// Attach to the same GameObject as the arms Animator.
// Forwards animation events to the WeaponHitbox found anywhere in the root hierarchy.
public class WeaponAnimationRelay : MonoBehaviour
{
    WeaponHitbox hitbox;

    void Awake()
    {
        hitbox = transform.root.GetComponentInChildren<WeaponHitbox>(true);
    }

    // Called by animation events
    public void EnableHitbox()  => hitbox?.EnableHitbox();
    public void DisableHitbox() => hitbox?.DisableHitbox();
}
