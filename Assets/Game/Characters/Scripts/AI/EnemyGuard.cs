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
    [Tooltip("Moving slower than this (metres a second) it stands in the full stance; faster than Moving Speed only the arms stay up.")]
    public float stillSpeed = .3f;
    public float movingSpeed = 1f;
    [Tooltip("How much of the arms the guard holds while moving (1 = all): below 1 the walk's own arm swing and torso turn"
        + " come through under the raised weapon, so it doesn't move like a figure on a pole.")]
    [Range(0f, 1f)] public float movingArms = .5f;

    static readonly int AttackTag = AnimatorHelper.TagHash("Attack"), HurtTag = AnimatorHelper.TagHash("Hurt"),
        DeathTag = AnimatorHelper.TagHash("Death"), SleepTag = AnimatorHelper.TagHash("Sleep");

    EnemyBrain brain;
    Animator anim;
    int stanceLayer = -1, armsLayer = -1, gestureLayer = -1;
    float stance, arms;

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
        float still = 1f - Mathf.InverseLerp(stillSpeed, movingSpeed, v.magnitude);
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
            arms = Mathf.MoveTowards(arms, want * Mathf.Lerp(movingArms, 1f, still), rate * Time.deltaTime);
            stance = Mathf.MoveTowards(stance, want * still, rate * Time.deltaTime);
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
