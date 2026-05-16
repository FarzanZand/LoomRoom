using UnityEngine;

public class EnemyAnimationEvents : MonoBehaviour
{
    HumanoidEnemy enemy;

    void Awake()
    {
        enemy = GetComponentInParent<HumanoidEnemy>();
    }

    public void OnAttackHit()
    {
        enemy?.OnAttackHit();
    }
}
