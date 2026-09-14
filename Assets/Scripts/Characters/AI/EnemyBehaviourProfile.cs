using Sirenix.OdinInspector;
using UnityEngine;

// Serialized by integer — never renumber. Investigate and Dead were appended.
public enum EnemyState { Idle = 0, Wander = 1, Patrol = 2, Chase = 3, Attack = 4, ReturnToPost = 5, Investigate = 6, Dead = 7 }

public enum AggressionMode { Aggressive = 0, AggressiveWhenHit = 1, Passive = 2 }

// Shareable tuning for how an enemy perceives, moves and chases. Assign on
// CharacterData.behaviour; EnemyBrain reads it live, so adding a knob is one field
// here and one read in the brain. Attacks are authored on CharacterData.
[CreateAssetMenu(fileName = "NewEnemyBehaviour", menuName = "Characters/Enemy Behaviour Profile")]
public class EnemyBehaviourProfile : ScriptableObject
{
    const string Tabs = "Tabs";

    // ── Behaviour ─────────────────────────────────────────────────────
    [TabGroup(Tabs, "Behaviour")]
    public EnemyState     defaultState   = EnemyState.Idle;
    [TabGroup(Tabs, "Behaviour")]
    public AggressionMode aggressionMode = AggressionMode.Aggressive;

    [TabGroup(Tabs, "Behaviour"), Title("Wander")]
    public float wanderRadius      = 8f;
    [TabGroup(Tabs, "Behaviour")]
    public float minWanderDistance = 2f;
    [TabGroup(Tabs, "Behaviour")]
    public float minIdleTime       = 2f;
    [TabGroup(Tabs, "Behaviour")]
    public float maxIdleTime       = 6f;
    [TabGroup(Tabs, "Behaviour")]
    [Tooltip("Hard cap on distance from the wander zone centre. 0 = only wanderRadius applies.")]
    public float wanderZoneRadius  = 0f;

    // ── Perception ────────────────────────────────────────────────────
    [TabGroup(Tabs, "Perception")]
    public float detectionRadius = 12f;
    [TabGroup(Tabs, "Perception"), Range(0f, 360f)]
    public float fieldOfView = 120f;
    [TabGroup(Tabs, "Perception")]
    public float eyeHeight = 1.6f;
    [TabGroup(Tabs, "Perception")]
    [Tooltip("Inside this radius the target is noticed regardless of facing.")]
    public float closeDetectionRadius = 2f;
    [TabGroup(Tabs, "Perception")]
    public LayerMask obstacleMask = ~0;
    [TabGroup(Tabs, "Perception")]
    [Tooltip("Added to a noise's own radius when deciding whether this enemy heard it.")]
    public float hearingRadius = 6f;
    [TabGroup(Tabs, "Perception")]
    [Tooltip("Seconds spent looking around at a noise or last-seen spot before giving up.")]
    public float investigateTime = 4f;

    // ── Chase ─────────────────────────────────────────────────────────
    [TabGroup(Tabs, "Chase")]
    [Tooltip("Seconds the target can be out of sight before the enemy starts searching.")]
    public float loseSightGracePeriod = 3f;
    [TabGroup(Tabs, "Chase")]
    [Tooltip("Seconds of searching the last known position before returning to post.")]
    public float maxSearchTime = 5f;
    [TabGroup(Tabs, "Chase")]
    public bool  useChaseSpeed;
    [TabGroup(Tabs, "Chase"), ShowIf("useChaseSpeed")]
    public float chaseSpeed = 5f;
    [TabGroup(Tabs, "Chase")]
    public bool  useWanderSpeed;
    [TabGroup(Tabs, "Chase"), ShowIf("useWanderSpeed")]
    public float wanderSpeed = 2f;

    // ── Rotation ──────────────────────────────────────────────────────
    [TabGroup(Tabs, "Rotation")]
    public float passiveAngularSpeed = 120f;
    [TabGroup(Tabs, "Rotation")]
    public float chaseAngularSpeed   = 540f;
    [TabGroup(Tabs, "Rotation")]
    [Tooltip("Degrees per second the enemy turns toward the target while in attack range.")]
    public float attackFaceSpeed     = 720f;
}
