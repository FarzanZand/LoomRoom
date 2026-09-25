using System;
using Sirenix.OdinInspector;
using UnityEngine;

// Serialized by integer — never renumber. Investigate and Dead were appended.
public enum EnemyState { Idle = 0, Wander = 1, Patrol = 2, Chase = 3, Attack = 4, ReturnToPost = 5, Investigate = 6, Dead = 7 }

public enum AggressionMode { Aggressive = 0, AggressiveWhenHit = 1, Passive = 2 }

// The actual tuning knobs. A plain serializable class so the same fields can live in
// a shared EnemyBehaviourProfile asset OR inline on an EnemyBrain that overrides it.
// Adding a knob is one field here and one read in the brain.
[Serializable]
public class EnemyBehaviourSettings
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
    [Tooltip("Investigate nearby sounds regardless of the default state, including Idle. Being attacked still provokes a reaction when this is off.")]
    public bool investigateNoise = true;
    [TabGroup(Tabs, "Perception")]
    [Tooltip("Added to a noise's own radius when noise investigation is enabled.")]
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

    [TabGroup(Tabs, "Chase")]
    [Tooltip("How quickly the agent reaches its speed. High values feel snappy; low values slide.")]
    public float acceleration = 30f;

    // ── Spacing (in range, attack recharging) ─────────────────────────
    [TabGroup(Tabs, "Spacing")]
    [Tooltip("Fraction of the attack's max range the enemy tries to hold while its attack recharges.")]
    [Range(0.3f, 1f)] public float preferredRangeFraction = 0.7f;
    [TabGroup(Tabs, "Spacing")]
    [Tooltip("Circle sideways around the target instead of standing still while the attack recharges.")]
    public bool circleTarget = true;
    [TabGroup(Tabs, "Spacing"), ShowIf("circleTarget")]
    [Tooltip("Circling speed as a fraction of chase speed.")]
    [Range(0f, 1f)] public float circleSpeedFraction = 0.35f;
    [TabGroup(Tabs, "Spacing"), ShowIf("circleTarget")]
    [Tooltip("Seconds between random direction changes while circling (min / max).")]
    public Vector2 circleSwitchInterval = new Vector2(0.9f, 2f);
    [TabGroup(Tabs, "Spacing")]
    [Tooltip("Speed fraction used to step in or back off to the preferred range.")]
    [Range(0f, 1f)] public float spacingSpeedFraction = 0.8f;

    // ── Rotation ──────────────────────────────────────────────────────
    [TabGroup(Tabs, "Rotation")]
    [Tooltip("Turn speed (deg/s) while wandering or patrolling.")]
    public float passiveAngularSpeed = 360f;
    [TabGroup(Tabs, "Rotation")]
    [Tooltip("Turn speed (deg/s) toward the movement direction while chasing.")]
    public float chaseAngularSpeed   = 720f;
    [TabGroup(Tabs, "Rotation")]
    [Tooltip("Degrees per second the enemy turns toward the target while in attack range.")]
    public float attackFaceSpeed     = 900f;
    [TabGroup(Tabs, "Rotation")]
    [Tooltip("Degrees per second the enemy keeps tracking the target during the attack windup, before the swing commits. 0 = fully planted.")]
    public float windupTrackSpeed    = 240f;

    // Used when an EnemyBrain starts overriding: it begins from the shared values.
    public void CopyFrom(EnemyBehaviourSettings other)
    {
        if (other == null) return;
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(other), this);
    }
}

// Shareable tuning asset. Assign on CharacterData.behaviour; EnemyBrain reads it live.
[CreateAssetMenu(fileName = "NewEnemyBehaviour", menuName = "Characters/Enemy Behaviour Profile")]
public class EnemyBehaviourProfile : ScriptableObject
{
    [HideLabel, InlineProperty]
    public EnemyBehaviourSettings settings = new();
}
