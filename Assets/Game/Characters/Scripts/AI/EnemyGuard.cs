using UnityEngine;

// A humanoid enemy's sword-and-shield moves while it fights (on the Humanoid enemy base). HumanoidController's "Guard
// stance" layer (whole body, under the gestures) holds the Mixamo moves (ThirdPerson/Combat/Sword and shield) as one
// blend: the shield-up block idle standing, walking in and back, strafing either way and running in, picked by CombatX
// and CombatZ, the body's velocity in its own frame per metre of its height (so a big body takes longer strides). While
// the enemy fights near its target at footwork pace the layer takes over the body (charging faster it runs as usual, Run
// Fade); it drops for a hurt, a cast, a bow, sleep or death,
// and a swing takes over from it across the swing's first frames (the swing starts from the block idle's pose).
// Made by EnemyGuardSetup.Build() (Characters/Animations/Editor).
[RequireComponent(typeof(EnemyBrain))]
public class EnemyGuard : MonoBehaviour
{
    [Tooltip("Metres from its target inside which a chasing enemy takes up its fighting moves (from farther it runs in as usual).")]
    public float engageDistance = 6f;
    [Tooltip("Seconds to take up the fighting moves.")] public float raiseSeconds = .3f;
    [Tooltip("Seconds to drop them as a hurt, a cast or a death starts (the body turns out of its bladed stance over it).")]
    public float dropSeconds = .15f;
    [Tooltip("A swing takes over from the guard across this share of its clip (it starts from the guard's own pose).")]
    [Range(0f, .5f)] public float swingHandover = .15f;
    [Tooltip("Seconds the moving blend eases over (quicker flips the legs between clips at every change of step).")]
    public float blendDamping = .15f;
    [Tooltip("Turning on the spot it steps sideways in place toward the turn, as much as a strafe of this speed (a metre"
        + " of height a second) at Turn Rate degrees a second, so its feet move instead of the whole body sliding round.")]
    public float turnStep = .6f;
    public float turnRate = 120f;
    [Tooltip("Metres a second below which it counts as standing (and its turning steps its feet).")]
    public float standingBelow = .2f;
    [Tooltip("Speeds (metres of height a second) over which the fighting moves give way to its own run: a charging enemy"
        + " runs in as usual and takes up its guard as it slows near its target (the sword-and-shield run twists the hips"
        + " side to side every stride while the head stays on the target, which reads as a wobble).")]
    public Vector2 runFade = new(2f, 3.5f);
    [Tooltip("How far the chest (Aim Body) and the head (Aim Head) turn back toward the target while the moves play (look-at"
        + " IK, 0-1; the clips stand side-on). Aim Clamp limits the turn (0 = free, 1 = none).")]
    [Range(0f, 1f)] public float aimBody = .8f, aimHead = 1f, aimClamp = .3f;

    static readonly int AttackTag = AnimatorHelper.TagHash("Attack"), HurtTag = AnimatorHelper.TagHash("Hurt"),
        DeathTag = AnimatorHelper.TagHash("Death"), SleepTag = AnimatorHelper.TagHash("Sleep");
    static readonly int CombatX = Animator.StringToHash("CombatX"), CombatZ = Animator.StringToHash("CombatZ"),
        GuardPace = Animator.StringToHash("GuardPace");

    // The moving clips' own speeds, metres a second per metre of the body's height (measured; their places in the blend,
    // EnemyGuardSetup): a walk in, a walk back, a strafe right and left, a run.
    public const float StrideForward = 1.549f, StrideBack = 1.177f, StrideRight = 1.291f, StrideLeft = 1.142f, RunForward = 4.232f;

    [Tooltip("Stride matching: moving slower than a clip's own speed it still takes whole strides, played that much slower "
        + "(GuardPace), so the planted foot stays put; a blend of a walk and the standing pose slid the feet. Below this share "
        + "of a stride's speed it eases back toward the standing pose.")]
    [Range(.05f, 1f)] public float strideMatchBelow = .3f;

    EnemyBrain brain;
    Animator anim;
    int stanceLayer = -1, gestureLayer = -1;
    float stance, lastYaw, yawRate;
    bool hasParams, hasPace;

    void Awake()
    {
        brain = GetComponent<EnemyBrain>();
        anim = GetComponentInChildren<Animator>();
        if (anim == null || anim.runtimeAnimatorController == null) return;
        stanceLayer = anim.GetLayerIndex("Guard stance");
        gestureLayer = anim.GetLayerIndex("FullBodyOverride");
        hasParams = Character.HasParameter(anim, "CombatX", AnimatorControllerParameterType.Float);
        hasPace = Character.HasParameter(anim, "GuardPace", AnimatorControllerParameterType.Float);
        lastYaw = transform.eulerAngles.y;
        if (stanceLayer >= 0)
        {
            var aim = anim.GetComponent<EnemyGuardAim>();
            if (aim == null) aim = anim.gameObject.AddComponent<EnemyGuardAim>();
            aim.guard = this;
        }
    }

    public int StanceLayer => stanceLayer;

    // Called in the stance layer's IK pass (EnemyGuardAim): the chest and head turn toward the target, level (the point
    // is at the enemy's own head height, so the body only turns, never bows).
    public void Aim(Animator a)
    {
        // Only a target it can see: the chest and head never follow a veiled player round.
        var target = brain.Perception != null && brain.Perception.TargetVisible ? brain.Perception.Target : null;
        var head = a.GetBoneTransform(HumanBodyBones.Head);
        Vector3 to = target != null ? target.transform.position - transform.position : Vector3.zero; to.y = 0f;
        if (stance <= 0f || head == null || to.sqrMagnitude < .01f) { a.SetLookAtWeight(0f); return; }
        a.SetLookAtWeight(1f, aimBody, aimHead, 0f, aimClamp);
        a.SetLookAtPosition(head.position + to.normalized * 5f);
    }

    void Update()
    {
        if (anim == null || stanceLayer < 0) return;
        var character = brain.Character;
        bool alive = character != null && character.IsAlive;
        // A swing on the base layer hands over from the guard over its first frames; anything else drops it.
        float swing = Swing();
        bool busy = !alive || swing < 0f && Busy(0) || gestureLayer >= 0 && Busy(gestureLayer) || Bool("HoldingBow") || Bool("Kneel") || Bool("Asleep");
        float want = Engaged && !busy ? 1f : 0f;

        Vector3 v = brain.Motor != null ? brain.Motor.LegVelocity : Vector3.zero; v.y = 0f;
        float size = Mathf.Max(.3f, transform.lossyScale.y) * Mathf.Max(.3f, anim.humanScale);
        want *= 1f - Mathf.InverseLerp(runFade.x, runFade.y, v.magnitude / size);
        if (hasParams)
        {
            Vector3 local = transform.InverseTransformDirection(v) / size;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f), yaw = transform.eulerAngles.y;
            yawRate = Mathf.Lerp(yawRate, Mathf.DeltaAngle(lastYaw, yaw) / dt, Mathf.Clamp01(dt / .1f));
            lastYaw = yaw;
            if (v.magnitude < standingBelow) local.x += Mathf.Clamp(yawRate / Mathf.Max(1f, turnRate), -1f, 1f) * turnStep;
            var move = new Vector2(local.x, local.z);
            float pace = hasPace ? StrideMatch(ref move) : 1f;
            anim.SetFloat(CombatX, move.x, blendDamping, Time.deltaTime);
            anim.SetFloat(CombatZ, move.y, blendDamping, Time.deltaTime);
            if (hasPace) anim.SetFloat(GuardPace, pace, blendDamping, Time.deltaTime);
        }

        if (swing >= 0f) stance = Mathf.Min(stance, 1f - Mathf.Clamp01(swing / Mathf.Max(.01f, swingHandover)));
        else
        {
            // Faster down than up: a flinch never plays under a half raised guard.
            float rate = want < stance ? 1f / Mathf.Max(.01f, dropSeconds) : 1f / Mathf.Max(.01f, raiseSeconds);
            stance = Mathf.MoveTowards(stance, want, rate * Time.deltaTime);
        }
        anim.SetLayerWeight(stanceLayer, stance);
    }

    // Slower than a clip's own speed the blend takes the whole stride in that direction and returns how much slower to play
    // it: the clips' places make an ellipse (StrideForward and StrideBack along z, StrideRight and StrideLeft along x).
    // Faster than that it blends on toward the run as before.
    float StrideMatch(ref Vector2 move)
    {
        float speed = move.magnitude;
        if (speed < 1e-4f) return 1f;
        var dir = move / speed;
        float rx = dir.x >= 0f ? StrideRight : StrideLeft, rz = dir.y >= 0f ? StrideForward : StrideBack;
        float stride = 1f / Mathf.Sqrt(dir.x * dir.x / (rx * rx) + dir.y * dir.y / (rz * rz));
        if (speed >= stride) return 1f;
        float share = speed / stride, low = strideMatchBelow;
        if (share >= low) { move = dir * stride; return share; }
        // Barely moving: from the standing pose toward a whole stride played at the lowest pace.
        float t = share / low;
        move = dir * stride * t;
        return Mathf.Lerp(1f, low, t);
    }

    // Fighting: in range of its target, or chasing it from close by; never asleep, entranced (the dancers, Not a witch
    // kneeling) or dead.
    bool Engaged
    {
        get
        {
            if (brain.Asleep || brain.Entranced) return false;
            if (brain.State == EnemyState.Attack) return true;
            if (brain.State != EnemyState.Chase) return false;
            var target = brain.Perception != null ? brain.Perception.Target : null;
            return target != null && (target.transform.position - transform.position).sqrMagnitude < engageDistance * engageDistance;
        }
    }

    // How far into a swing (an Attack-tagged state on the base layer) the enemy is, 0 to 1, or -1 when it isn't swinging.
    float Swing()
    {
        var now = anim.GetCurrentAnimatorStateInfo(0);
        if (anim.IsInTransition(0))
        {
            var next = anim.GetNextAnimatorStateInfo(0);
            if (AnimatorHelper.HasTag(next, AttackTag)) return next.normalizedTime;
        }
        return AnimatorHelper.HasTag(now, AttackTag) ? now.normalizedTime : -1f;
    }

    bool Bool(string name) => Character.HasParameter(anim, name, AnimatorControllerParameterType.Bool) && anim.GetBool(name);

    // The layer is in (or blending into) a swing, a hurt, a death or sleep.
    bool Busy(int layer)
    {
        if (AnimatorHelper.HasTag(anim.GetCurrentAnimatorStateInfo(layer), AttackTag, HurtTag, DeathTag, SleepTag)) return true;
        return anim.IsInTransition(layer) && AnimatorHelper.HasTag(anim.GetNextAnimatorStateInfo(layer), AttackTag, HurtTag, DeathTag, SleepTag);
    }
}
