using Sirenix.OdinInspector;
using UnityEngine;

// Eyes and ears. Picks a hostile target (the active player, by faction), tracks
// whether it is visible and where it was last seen, and remembers the latest noise
// worth investigating. The brain reads; this never decides.
public class Perception : MonoBehaviour
{
    public EnemyBehaviourSettings Profile { get; set; }

    [ShowInInspector, ReadOnly] public Character Target                  { get; private set; }
    [ShowInInspector, ReadOnly] public bool      TargetVisible           { get; private set; }
    [ShowInInspector, ReadOnly] public Vector3   LastKnownTargetPosition { get; private set; }
    [ShowInInspector, ReadOnly] public bool      HasStimulus             { get; private set; }
    public Vector3 StimulusPosition { get; private set; }

    Character self;
    readonly RaycastHit[] sightHits = new RaycastHit[32];

    void Awake() => self = GetComponent<Character>();

    void OnEnable()  => NoiseEvents.Noise += OnNoise;
    void OnDisable() => NoiseEvents.Noise -= OnNoise;

    // Called by the brain once per frame.
    public void Tick(bool trackingTarget = false)
    {
        Target = ResolveTarget();
        TargetVisible = Target != null && CanSee(Target.transform, trackingTarget);
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

    public bool CanSee(Transform target, bool trackingTarget = false)
    {
        if (target == null || Profile == null) return false;

        Vector3 origin   = transform.position + Vector3.up * Profile.eyeHeight;
        Vector3 toTarget = target.position + Vector3.up * 0.9f - origin;
        float   dist     = toTarget.magnitude;

        if (dist > Profile.detectionRadius) return false;
        bool close = HorizontalDist(transform.position, target.position) <= Profile.closeDetectionRadius;
        if (!trackingTarget && !close && Vector3.Angle(transform.forward, toTarget) > Profile.fieldOfView * 0.5f) return false;

        // Close awareness bypasses the view cone, never walls. Ignore both character bodies.
        int count = Physics.RaycastNonAlloc(origin, toTarget.normalized, sightHits, dist, Profile.obstacleMask, QueryTriggerInteraction.Ignore);
        if (count == sightHits.Length) return false;
        for (int i = 0; i < count; i++)
        {
            var hit = sightHits[i].transform;
            if (hit.IsChildOf(transform) || hit.IsChildOf(target)) continue;
            return false;
        }
        return true;
    }

    void OnNoise(Vector3 position, float radius, Character source)
    {
        // Damage uses NotifyAttackedFrom separately; this toggle only controls sounds.
        if (Profile == null || !Profile.investigateNoise) return;
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
        // Being hit tells you where the attacker is, even if you cannot see them yet.
        LastKnownTargetPosition = position;
    }

    public void SetLastKnownTargetPosition(Vector3 position) => LastKnownTargetPosition = position;

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
