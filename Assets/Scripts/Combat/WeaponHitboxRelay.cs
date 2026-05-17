using UnityEngine;

// Place this on the same GameObject as the weapon collider.
// It forwards trigger events to the WeaponHitbox on a parent object.
[RequireComponent(typeof(Collider))]
public class WeaponHitboxRelay : MonoBehaviour
{
    WeaponHitbox hitbox;

    void Awake()
    {
        hitbox = GetComponentInParent<WeaponHitbox>();
    }

    void OnTriggerEnter(Collider other) => hitbox?.ProcessHit(other);
    void OnTriggerStay(Collider other)  => hitbox?.ProcessHit(other);
}
