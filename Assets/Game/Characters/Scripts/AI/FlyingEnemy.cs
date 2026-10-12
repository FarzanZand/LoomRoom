using UnityEngine;

// Heathen's flyers: the enemy cruises above head height, out of reach of a level swing, drops to its bite height
// while it attacks, and after the bite (or when it is hit) pulls back away from its target for a moment before it
// comes in again. Its path still follows the floor's NavMesh; only the model and collider leave the ground. The
// model's authored hover (its animation) is at designHeight; this moves the "Model" child to the wanted height.
[RequireComponent(typeof(EnemyBrain))]
public class FlyingEnemy : MonoBehaviour
{
    [SerializeField, Tooltip("Height the model's own animation hovers at (metres above the ground).")] float designHeight = 1.2f;
    [SerializeField, Tooltip("Height while it circles and closes in.")] float cruiseHeight = 2.2f;
    [SerializeField, Tooltip("Height while it bites (chest height).")] float diveHeight = 1.2f;
    [SerializeField, Tooltip("Metres the height wanders up and down while it flies.")] float bob = .18f;
    [SerializeField, Tooltip("Seconds it flies away after a bite or a hit.")] Vector2 retreatSeconds = new(.9f, 1.6f);
    [SerializeField, Range(.2f, 1.5f), Tooltip("Speed while pulling back, against its chase speed.")] float retreatSpeed = .9f;
    [SerializeField, Tooltip("Metres per second the height changes (diving is twice as fast).")] float climbSpeed = 2.4f;

    EnemyBrain brain;
    Character character;
    Transform model;
    CapsuleCollider body;
    float colliderOffset, height, retreatUntil, side = 1f, phase;
    bool wasSwinging;

    void Awake()
    {
        brain = GetComponent<EnemyBrain>();
        character = GetComponent<Character>();
        model = transform.Find("Model");
        body = GetComponent<CapsuleCollider>();
        if (body != null) colliderOffset = body.center.y - designHeight;
        height = cruiseHeight;
        phase = Random.value * 10f;
    }

    void OnEnable()
    {
        brain.MovementOverride = Retreating;
        character.Damaged += OnDamaged;
        character.Died += OnDied;
    }

    void OnDisable()
    {
        if (brain.MovementOverride == Retreating) brain.MovementOverride = null;
        character.Damaged -= OnDamaged;
        character.Died -= OnDied;
    }

    // The body falls as it is (the death clip and the throw take it from here).
    void OnDied() => enabled = false;

    void OnDamaged(DamageInfo info) { if (character.IsAlive) PullBack(); }

    void PullBack()
    {
        retreatUntil = Time.time + Random.Range(retreatSeconds.x, retreatSeconds.y);
        side = Random.value < .5f ? -1f : 1f;
    }

    // Away from the target and a little to one side, facing it.
    bool Retreating()
    {
        if (Time.time >= retreatUntil || brain.Perception == null || brain.Perception.Target == null) return false;
        var target = brain.Perception.Target.transform.position;
        Vector3 away = transform.position - target; away.y = 0f;
        if (away.sqrMagnitude < .01f) away = -transform.forward;
        away = away.normalized + Vector3.Cross(Vector3.up, away.normalized) * side * .6f;
        var speed = brain.Data != null && brain.Data.Behaviour.useChaseSpeed ? brain.Data.Behaviour.chaseSpeed : 4f;
        brain.Motor.Strafe(away, speed * retreatSpeed);
        brain.Motor.LookAt(target, 720f);
        return true;
    }

    void LateUpdate()
    {
        if (!character.IsAlive) return;   // also before it is set up
        bool swinging = brain.IsSwinging;
        if (wasSwinging && !swinging) PullBack();
        wasSwinging = swinging;

        float wanted = swinging ? diveHeight : cruiseHeight + Mathf.Sin(Time.time * 1.7f + phase) * bob;
        float rate = swinging ? climbSpeed * 2f : climbSpeed;
        height = Mathf.MoveTowards(height, wanted, rate * Time.deltaTime);
        if (model != null) { var p = model.localPosition; p.y = height - designHeight; model.localPosition = p; }
        if (body != null) { var c = body.center; c.y = height + colliderOffset; body.center = c; }
    }
}
