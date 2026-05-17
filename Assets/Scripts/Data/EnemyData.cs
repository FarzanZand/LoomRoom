using UnityEngine;

[CreateAssetMenu(fileName = "NewEnemyData", menuName = "Game/Enemy Data")]
public class EnemyData : ScriptableObject
{
    [Header("Identity")]
    public string   enemyName;
    public GameObject prefab;
    public Faction  faction = Faction.Enemy;

    [Header("Stats")]
    public StatProfile statProfile;

    [Header("Default Behaviour")]
    public EnemyState    defaultState    = EnemyState.Idle;
    public AggressionMode aggressionMode = AggressionMode.Aggressive;

    [Header("Wander")]
    public float wanderRadius      = 8f;
    public float minWanderDistance = 2f;
    public float minIdleTime       = 2f;
    public float maxIdleTime       = 6f;
    public float wanderZoneRadius  = 0f;

    [Header("Movement Speeds")]
    public bool  useWanderSpeed;
    public float wanderSpeed = 2f;
    public bool  useChaseSpeed;
    public float chaseSpeed  = 5f;

    [Header("Detection")]
    public float detectionRadius    = 12f;
    [Range(0f, 360f)]
    public float fieldOfView        = 120f;
    public float eyeHeight          = 1.6f;
    public float closeDetectionRadius = 2f;
    public LayerMask obstacleMask   = ~0;

    [Header("Chase")]
    public float loseSightGracePeriod = 3f;
    public float maxSearchTime        = 5f;

    [Header("Rotation")]
    public float passiveAngularSpeed = 120f;
    public float chaseAngularSpeed   = 540f;
    public float attackFaceSpeed     = 720f;

    [Header("Attack")]
    public float attackRange          = 1.8f;
    public float attackCooldown       = 1.5f;
    [Range(0f, 180f)]
    public float attackAngleThreshold = 45f;
    [Tooltip("Seconds after the attack trigger fires before OnAttackHit is called. Set to -1 to rely solely on animation events.")]
    public float attackHitDelay       = 0.4f;

    [Header("Animation Triggers")]
    [Tooltip("Leave empty to use the default 'Attack' trigger.")]
    public string attackTrigger;
    [Tooltip("Leave empty to use CharacterBase's default 'Death' trigger.")]
    public string deathTrigger;
    [Tooltip("Leave empty to use CharacterBase's default 'Hurt' trigger.")]
    public string hurtTrigger;
}
