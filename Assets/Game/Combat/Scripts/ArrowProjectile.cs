using UnityEngine;

// An arrow in flight: a fast line with a little drop. Hurts the first hostile character it
// meets, flies past friends and sticks in whatever else it hits. Fired by EnemyAttackRunner
// for archers (EnemyArchery); lives on Combat/Arrows/Arrow. The art child points along +Z.
public class ArrowProjectile : MonoBehaviour
{
    public LayerMask collisionMask = ~0;
    [Min(.01f)] public float collisionRadius = .06f;
    [Tooltip("Downward pull in m/s². Kept small so long shots stay readable.")]
    [Min(0)] public float gravity = 1.5f;
    [Min(.1f)] public float lifetime = 4f;
    [Tooltip("Seconds an arrow stays stuck in a wall or prop.")]
    [Min(0)] public float stickTime = 8f;
    [Tooltip("How far the arrow sinks into what it hits. The pivot is the tip.")]
    [Min(0)] public float penetration = .08f;
    [Tooltip("Played when it sticks in something that is not a character. Character hits use the shot's HitProfile.")]
    public AudioData impactAudio;

    Character owner;
    HitProfile profile;
    float damage, age;
    Vector3 velocity;
    bool done;

    public void Launch(Character source, Vector3 direction, float speed, float amount, HitProfile hit)
    {
        owner = source; damage = amount; profile = hit ?? HitProfile.Default;
        velocity = direction.normalized * speed;
        transform.rotation = Quaternion.LookRotation(velocity);
    }

    void Update()
    {
        if (done) return;
        if (GameManager.HasInstance && !GameManager.Instance.SimulationActive) return;
        float dt = Time.deltaTime;
        age += dt;
        if (age >= lifetime) { Destroy(gameObject); return; }

        velocity += Vector3.down * gravity * dt;
        Vector3 step = velocity * dt;
        float distance = step.magnitude;
        if (distance < .0001f) return;

        var hits = Physics.SphereCastAll(transform.position, collisionRadius, step / distance, distance, collisionMask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.transform.IsChildOf(transform)) continue;
            if (owner != null && hit.transform.IsChildOf(owner.transform)) continue;
            var point = hit.distance <= 0 ? transform.position : hit.point;

            var victim = hit.collider.GetComponentInParent<Character>();
            if (victim != null)
            {
                // Friends and corpses are flown past.
                if (!victim.IsAlive || (owner != null && !FactionRules.IsHostile(owner.Faction, victim.Faction))) continue;
                Strike(victim.GetComponent<IDamageable>(), point);
                Destroy(gameObject);
                return;
            }

            // Crates, barrels and doors take the hit; the arrow stays in them either way.
            var damageable = hit.collider.GetComponentInParent<IDamageable>();
            if (damageable != null) Strike(damageable, point);
            Stick(point);
            return;
        }

        transform.position += step;
        transform.rotation = Quaternion.LookRotation(velocity);
    }

    void Strike(IDamageable target, Vector3 point)
    {
        if (target == null) return;
        Vector3 flat = velocity; flat.y = 0;
        float force = profile.knockbackForce >= 0f ? profile.knockbackForce : 1f;
        if (CombatManager.HasInstance) force = CombatManager.Instance.ScaleKnockback(force);
        target.TakeDamage(new DamageInfo
        {
            Profile        = profile,
            Amount         = damage,
            Source         = owner,
            HitPoint       = point,
            Direction      = flat.sqrMagnitude > .0001f ? flat.normalized : transform.forward,
            KnockbackForce = force,
        });
    }

    void Stick(Vector3 point)
    {
        done = true;
        transform.position = point + velocity.normalized * penetration;
        if (impactAudio != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFXData(impactAudio, point);
        Destroy(gameObject, stickTime);
    }
}
