using UnityEngine;

public class AttackAnimationEvents : MonoBehaviour
{
    [SerializeField] private WeaponHitbox hitbox;

    public void EnableHitbox()  => hitbox.EnableHitbox();
    public void DisableHitbox() => hitbox.DisableHitbox();
}
