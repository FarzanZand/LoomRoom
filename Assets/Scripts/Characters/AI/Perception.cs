using Sirenix.OdinInspector;
using UnityEngine;

// Eyes and ears. Picks a hostile target (the active player, by faction), tracks
// whether it is visible and where it was last seen, and remembers the latest noise
// worth investigating. The brain reads; this never decides.
public class Perception : MonoBehaviour
{
    public EnemyBehaviourProfile Profile { get; set; }

    [ShowInInspector, ReadOnly] public Character Target                  { get; private set; }
    [ShowInInspector, ReadOnly] public bool      TargetVisible           { get; private set; }
    [ShowInInspector, ReadOnly] public Vector3   LastKnownTargetPosition { get; private set; }
    [ShowInInspector, ReadOnly] public bool      HasStimulus             { get; private set; }
    public Vector3 StimulusPosition { get; private set; }

    Character self;

    void Awake() => self = GetComponent<Character>();

    void OnEnable()  => NoiseEvents.Noise += OnNoise;
    void OnDisable() => NoiseEvents.Noise -= OnNoise;

    // Called by the brain once per frame.
    public void Tick()
    {
        Target = ResolveTarget();
        TargetVisible = Target != null && CanSee(Target.transform);
        if (TargetVisible) LastKnownTargetPosition = Target.transform.position;
    }

    Character ResolveTarget()
    {
        if (!PlayerManager.HasInstance) return null;
        var player = PlayerManager.Instance.Active;
        if (player == null || !player.IsAlive) return null;
        if (self != null && !FactionRules.IsHostile(self.Faction, player.Faction)) return null;
        return player;
    }

    public bool CanSee(Transform target)
    {
        if (target == null || Profile == null) return false;

        Vector3 origin   = transform.position + Vector3.up * Profile.eyeHeight;
        Vector3 toTarget = target.position + Vector3.up * 0.9f - origin;
        float   dist     = toTarget.magnitude;

        if (dist > Profile.detectionRadius) return false;
        if (HorizontalDist(transform.position, target.position) <= Profile.closeDetectionRadius) return true;
        if (Vector3.Angle(transform.forward, toTarget) > Profile.fieldOfView * 0.5f) return false;

        return !Physics.Raycast(origin, toTarget.normalized, dist, Profile.obstacleMask, QueryTriggerInteraction.Ignore);
    }

    void OnNoise(Vector3 position, float radius, Character source)
    {
        if (Profile == null) return;
        if (source != null && self != null && !FactionRules.IsHostile(self.Faction, source.Faction)) return;
        float reach = radius + Profile.hearingRadius;
        if ((position - transform.position).sqrMagnitude > reach * reach) return;
        HasStimulus      = true;
        StimulusPosition = position;
    }

    // Damage always counts as a stimulus from the attacker's position.
    public void NotifyAttackedFrom(Vector3 position)
    {
        HasStimulus      = true;
        StimulusPosition = position;
    }

    public void ConsumeStimulus() => HasStimulus = false;

    public static float HorizontalDist(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

    void OnDrawGizmosSelected()
    {
        if (Profile == null) return;
        Vector3 eye = transform.position + Vector3.up * Profile.eyeHeight;
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.1f);
        Gizmos.DrawSphere(transform.position, Profile.detectionRadius);
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, Profile.detectionRadius);
        Vector3 left  = Quaternion.Euler(0, -Profile.fieldOfView * 0.5f, 0) * transform.forward * Profile.detectionRadius;
        Vector3 right = Quaternion.Euler(0,  Profile.fieldOfView * 0.5f, 0) * transform.forward * Profile.detectionRadius;
        Gizmos.DrawLine(eye, eye + left);
        Gizmos.DrawLine(eye, eye + right);
        Gizmos.color = new Color(1f, 0.6f, 0f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, Profile.closeDetectionRadius);
        Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, Profile.hearingRadius);
    }
}
