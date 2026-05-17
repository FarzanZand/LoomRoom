using UnityEngine;

public class AttackAnimationEvents : MonoBehaviour
{
    [SerializeField] private WeaponHitbox hitbox;

    void Awake()
    {
        if (hitbox == null)
            hitbox = GetComponentInChildren<WeaponHitbox>(true);
    }

    public void EnableHitbox()  => hitbox?.EnableHitbox();
    public void DisableHitbox() => hitbox?.DisableHitbox();
}
