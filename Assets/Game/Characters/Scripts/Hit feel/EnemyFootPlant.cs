using UnityEngine;

// While a hit slides a humanoid enemy (EnemyMotor.IsKnockedBack), its feet stay where they stood instead of skating
// along with the body: foot IK holds each foot on the spot (and turned the way) the animation had it when the slide
// began. When the slide ends, or the body has slid more than CombatManager.footPlantReach from its feet, they step back
// under the body one after the other, the one with furthest to go first: each held spot travels to where the animation
// has the foot now (a guard raised meanwhile can have widened the stance) in about footPlantRelease, never faster than
// StepSpeed, lifting a little on the way, and the IK lets go once it is there, so the animation takes over with nothing
// left to jump. The IK weight stays at 1 throughout: Unity blends a part weight by turning the joints, which swings the
// foot through the floor on its way.
// Where the animation has a foot is read in the first IK pass of each frame, before anything is set: in a later layer's
// pass Unity returns the goal set in the earlier one. On the Animator object (Unity calls OnAnimatorIK there, in every
// layer with an IK pass); EnemyBrain adds it to humanoids at start.
[RequireComponent(typeof(Animator))]
public class EnemyFootPlant : MonoBehaviour
{
    // Seconds the second foot waits after the first starts its step back.
    const float SecondStep = .1f;
    // Metres a second a stepping foot never goes past (a walking step's swing), and how high it lifts.
    const float StepSpeed = 2.5f, StepLift = .06f;
    // Seconds the IK takes to let go once a foot has arrived.
    const float LetGo = .06f;

    Animator anim;
    EnemyMotor motor;
    Character character;
    Transform hips;
    bool planted, needPlant, wasSliding, released, leftFirst;
    float releasedAt;
    int readFrame = -1;
    Vector3 hipsAtPlant;
    readonly Foot left = new Foot { goal = AvatarIKGoal.LeftFoot }, right = new Foot { goal = AvatarIKGoal.RightFoot };

    class Foot
    {
        public AvatarIKGoal goal;
        public Vector3 at, wants, before;    // the held spot; where the animation has the foot this frame, and had it last frame
        public Quaternion turn, wantsTurn, turnBefore;
        public float weight, stepLength, lift;
        public bool stepping;
    }

    static CombatManager Tuning => CombatManager.HasInstance ? CombatManager.Instance : null;

    void Awake()
    {
        anim = GetComponent<Animator>();
        motor = GetComponentInParent<EnemyMotor>();
        character = GetComponentInParent<Character>();
        if (anim != null && anim.isHuman) hips = anim.GetBoneTransform(HumanBodyBones.Hips);
    }

    void Update()
    {
        if (hips == null) return;
        var tuning = Tuning;
        bool sliding = motor != null && motor.IsKnockedBack && tuning != null && tuning.plantFeetInKnockback
            && (character == null || character.IsAlive);
        // A new slide plants the feet where the animation has them (a second hit during the step back too).
        if (sliding && !wasSliding)
        {
            needPlant = planted = true; released = false;
            left.weight = right.weight = 1f; left.stepping = right.stepping = false; left.lift = right.lift = 0f;
            hipsAtPlant = hips.position;
        }
        wasSliding = sliding;
        if (!planted || needPlant) return;
        // The step back starts when the slide ends, or once the body has slid too far for the legs to reach.
        float slid = Flat(hips.position - hipsAtPlant).magnitude;
        if (!released && (!sliding || slid > (tuning != null ? tuning.footPlantReach : .4f)))
        {
            released = true; releasedAt = Time.time;
            leftFirst = (left.wants - left.at).sqrMagnitude >= (right.wants - right.at).sqrMagnitude;
        }
        if (!released) return;
        float release = tuning != null ? tuning.footPlantRelease : .2f;
        float since = Time.time - releasedAt;
        if (since >= (leftFirst ? 0f : SecondStep)) Step(left, release);
        if (since >= (leftFirst ? SecondStep : 0f)) Step(right, release);
        if (left.weight <= 0f && right.weight <= 0f) planted = false;
    }

    // The held spot travels to where the animation has the foot, lifting on the way, and the IK lets go there.
    void Step(Foot foot, float release)
    {
        if (foot.weight <= 0f) return;
        if (!foot.stepping) { foot.stepping = true; foot.stepLength = Vector3.Distance(foot.at, foot.wants); }
        float remaining = Vector3.Distance(foot.at, foot.wants);
        if (remaining > .01f)
        {
            float speed = Mathf.Clamp(foot.stepLength / Mathf.Max(.05f, release), .5f, StepSpeed);
            foot.at = Vector3.MoveTowards(foot.at, foot.wants, speed * Time.deltaTime);
            float done = foot.stepLength > .01f ? Mathf.Clamp01(1f - Vector3.Distance(foot.at, foot.wants) / foot.stepLength) : 1f;
            foot.lift = Mathf.Sin(done * Mathf.PI) * Mathf.Min(StepLift, foot.stepLength * .5f);
            foot.turn = Quaternion.Slerp(foot.turn, foot.wantsTurn, done);
        }
        else { foot.at = foot.wants; foot.turn = foot.wantsTurn; foot.lift = 0f; foot.weight = Mathf.MoveTowards(foot.weight, 0f, Time.deltaTime / LetGo); }
    }

    void OnAnimatorIK(int layer)
    {
        if (hips == null) return;
        // The first pass of the frame: where the animation has the feet, before anything is set (read every frame, so a
        // slide plants them where they stood the frame before the blow: the blow's own frame already starts its stagger
        // or raises the guard).
        if (readFrame != Time.frameCount)
        {
            bool fresh = readFrame == Time.frameCount - 1;
            readFrame = Time.frameCount;
            Read(left); Read(right);
            if (needPlant) { Plant(left, fresh); Plant(right, fresh); needPlant = false; }
        }
        if (!planted) return;
        Hold(left);
        Hold(right);
    }

    void Read(Foot foot)
    {
        foot.before = foot.wants; foot.turnBefore = foot.wantsTurn;
        foot.wants = anim.GetIKPosition(foot.goal); foot.wantsTurn = anim.GetIKRotation(foot.goal);
    }

    void Plant(Foot foot, bool fresh)
    {
        foot.at = fresh ? foot.before : foot.wants;
        foot.turn = fresh ? foot.turnBefore : foot.wantsTurn;
    }

    void Hold(Foot foot)
    {
        anim.SetIKPosition(foot.goal, foot.at + Vector3.up * foot.lift); anim.SetIKPositionWeight(foot.goal, foot.weight);
        anim.SetIKRotation(foot.goal, foot.turn); anim.SetIKRotationWeight(foot.goal, foot.weight);
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
