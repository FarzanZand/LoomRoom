using UnityEngine;

// The IK half of EnemyGuard, on the enemy's Animator object (Unity calls OnAnimatorIK there): in the "Guard stance"
// layer's IK pass it turns the chest and head back toward the target, so the sword-and-shield moves keep their own
// stepping legs while the fighter faces the player (the clips stand bladed, side-on, and strafe turned further away).
// EnemyGuard adds it at start.
[RequireComponent(typeof(Animator))]
public class EnemyGuardAim : MonoBehaviour
{
    public EnemyGuard guard;
    Animator anim;

    void Awake() => anim = GetComponent<Animator>();

    void OnAnimatorIK(int layer)
    {
        if (guard != null && layer == guard.StanceLayer) guard.Aim(anim);
    }
}
