using UnityEngine;

// A humanoid enemy's fighting stance between swings (on the Humanoid enemy base). HumanoidController has two layers for it
// under the gestures: "Guard stance" (the whole body: knees bent, weight forward, weapon and off hand up, breathing) and
// "Guard arms" (arms and head only, so the walk and strafe legs keep playing under a raised guard). While the enemy is
// fighting the arms are always up and the stance comes in as it stands still; both drop at once when a swing, a hurt, a
// death or sleep starts on the base layer (the swing begins from the same pose, so nothing pops) and come back after.
// Made by EnemyGuardSetup.Build() (Characters/Animations/Editor), clips in Characters/Animations/ThirdPerson/Combat.
[RequireComponent(typeof(EnemyBrain))]
public class EnemyGuard : MonoBehaviour
{
    [Tooltip("Metres from its target inside which a chasing enemy raises its guard (it runs in with its arms swinging from farther).")]
    public float engageDistance = 6f;
    [Tooltip("Seconds to raise the guard.")] public float raiseSeconds = .3f;
    [Tooltip("Seconds to drop it as a hurt, a cast or a death starts.")] public float dropSeconds = .1f;
    [Tooltip("A swing takes the arms over from the guard across this share of its clip (its first frames hold the weapon low;"
        + " by then its own arms are rising into the windup), so the blade goes from the guard straight into the windup.")]
    [Range(0f, .5f)] public float swingHandover = .15f;
    [Tooltip("Moving slower than this (metres a second) it stands in the full stance; by Moving Speed the legs are the walk's"
        + " own and only the arms keep the guard (the stance holds the legs too, so any of it left while walking skates them).")]
    public float stillSpeed = .08f;
    public float movingSpeed = .3f;
    [Tooltip("How much of the arms the guard holds at a run (1 = all): below 1 the run's own arm swing comes through. At the"
        + " footwork's walking pace the guard holds them all (at half, circling read as arms hanging at its sides).")]
    [Range(0f, 1f)] public float movingArms = .5f;
    [Tooltip("Metres a second over which the arms go from the full guard (walking) to Moving Arms (running).")]
    public Vector2 runSpeed = new(1.4f, 2.6f);

    static readonly int AttackTag = AnimatorHelper.TagHash("Attack"), HurtTag = AnimatorHelper.TagHash("Hurt"),
        DeathTag = AnimatorHelper.TagHash("Death"), SleepTag = AnimatorHelper.TagHash("Sleep");

    EnemyBrain brain;
    Animator anim;
    int stanceLayer = -1, armsLayer = -1, gestureLayer = -1;
    float stance, arms, stillSince;
    [Tooltip("Seconds it must stand still before the whole-body stance comes back.")] public float settleSeconds = .25f;

    void Awake()
    {
        brain = GetComponent<EnemyBrain>();
        anim = GetComponentInChildren<Animator>();
        if (anim == null || anim.runtimeAnimatorController == null) return;
        stanceLayer = anim.GetLayerIndex("Guard stance");
        armsLayer = anim.GetLayerIndex("Guard arms");
        gestureLayer = anim.GetLayerIndex("FullBodyOverride");
    }

    void Update()
    {
        if (anim == null || stanceLayer < 0 || armsLayer < 0) return;
        var character = brain.Character;
        bool alive = character != null && character.IsAlive;
        // A swing on the base layer hands over from the guard over its first frames; anything else drops it at once.
        float swing = Swing();
        bool busy = !alive || swing < 0f && Busy(0) || gestureLayer >= 0 && Busy(gestureLayer) || Bool("HoldingBow") || Bool("Kneel") || Bool("Asleep");
        float want = Engaged && !busy ? 1f : 0f;
        Vector3 v = brain.Motor != null ? brain.Motor.Velocity : Vector3.zero; v.y = 0f;
        // Still is judged by the legs too: their blend eases after the body stops, so it is still mid-step for a moment.
        float legs = new Vector2(AnimatorHelper.GetFloat(anim, "MoveX"), AnimatorHelper.GetFloat(anim, "MoveZ")).magnitude;
        float still = 1f - Mathf.InverseLerp(stillSpeed, movingSpeed, Mathf.Max(v.magnitude, legs));
        if (swing >= 0f)
        {
            float left = 1f - Mathf.Clamp01(swing / Mathf.Max(.01f, swingHandover));
            arms = Mathf.Min(arms, left);
            stance = Mathf.Min(stance, left);
        }
        else
        {
            // Faster down than up: a flinch never plays under a half raised guard.
            float rate = want < arms ? 1f / Mathf.Max(.01f, dropSeconds) : 1f / Mathf.Max(.01f, raiseSeconds);
            // The stance pins its feet (foot IK): the moment the enemy moves it lets go of the legs at once (blended with
            // a starting step, the pinned feet fought the stepping ones and the legs kicked up), and comes back only once
            // it has stood still a moment.
            if (still < 1f) stillSince = Time.time;
            bool settled = still >= 1f && Time.time - stillSince > settleSeconds;
            float stanceWant = want > 0f && settled ? 1f : 0f;
            stance = stanceWant < stance ? Mathf.MoveTowards(stance, 0f, Time.deltaTime / .08f) : Mathf.MoveTowards(stance, stanceWant, rate * Time.deltaTime);
            // The arms layer adds the guard on top of the walk; the standing stance already holds the guard, so the two
            // share it (both at once would add the raise twice). At a run it eases toward Moving Arms.
            float run = Mathf.Lerp(1f, movingArms, Mathf.InverseLerp(runSpeed.x, runSpeed.y, v.magnitude));
            arms = Mathf.MoveTowards(arms, want * (1f - stance) * run, Time.deltaTime / .12f);
        }
        if (!alive) { arms = stance = 0f; }
        anim.SetLayerWeight(armsLayer, arms);
        anim.SetLayerWeight(stanceLayer, stance);
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
