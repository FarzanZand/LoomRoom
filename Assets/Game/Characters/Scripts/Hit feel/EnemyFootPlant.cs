using UnityEngine;

// Foot locking for humanoid enemies, so feet step instead of skating. While something would slide them (a hit's
// knockback slide, EnemyMotor.IsKnockedBack; the guard fading in or out while the enemy stands, EnemyGuard's "Guard
// stance" layer; the enemy's own swing, played on the spot, whose lunge dragged the back foot across the floor) each
// foot is held where it stood the frame before. A held foot the animation pulls further than a short reach away takes a
// real step there (eased in and out, lifted a little, never below the spot it was held on) and is held again; one foot
// steps at a time. When nothing holds them any more each foot steps to where the animation has it, the one with
// furthest to go first and the other once it has nearly landed, and the IK lets go.
// While the enemy walks, runs or circles (nothing of the above holding it) a foot the animation has on the ground is
// pinned where it touched down, and handed back with a quick catch-up as the animation lifts it into its swing: the
// clips' strides don't match every body's proportions, and the body turns as it circles the player, so the stance foot
// slid at up to 40% of the body's speed. A foot is pinned once its ankle is within ContactOn of the lowest the animation
// has had it over this floor, and let go once it is ContactOff above it.
// The IK weight stays at 1 while a foot is held or stepping (Unity blends a part weight by turning the joints, which
// swings the foot through the floor) and the held spot itself travels. Where the animation has a foot is read in the
// first IK pass of each frame, before anything is set: in a later layer's pass Unity returns the goal set in the earlier
// one. On the Animator object (Unity calls OnAnimatorIK there, in every layer with an IK pass); EnemyBrain adds it to
// humanoids at start. Tuning on CombatManager > Hits > Impact (plantFeetInKnockback, footPlantReach, footPlantRelease).
[RequireComponent(typeof(Animator))]
public class EnemyFootPlant : MonoBehaviour
{
    // How far along the first stepping foot must be before the other lifts (both in the air read as a hop).
    const float FirstLanding = .85f;
    // Metres a second a stepping foot never goes past through the middle of its step, and how high it lifts.
    const float StepSpeed = 2.5f, StepLift = .06f;
    // Seconds the IK takes to let go once a foot is home.
    const float LetGo = .06f;
    // Metres a second the body may walk for the guard's fade to be held (walking, its own steps carry the change).
    const float SettleBelow = .3f;
    // Metres a held foot may lag behind the animation during a swing or the guard's fade before it steps.
    const float SwingReach = .22f;
    // Walking: metres a second the body moves for stance locking; metres above the lowest ankle a foot is pinned below
    // (on the ground) and let go above (clearly lifted: the heel rises first while the toes still scrape); metres a pinned
    // foot may fall behind before it lets go anyway; seconds the catch-up takes, in the air.
    const float WalkingAbove = .12f, ContactOn = .02f, ContactOff = .06f, StanceReach = .3f, CatchUp = .25f;

    enum State { Free, Held, Stepping, Letting }

    class Foot
    {
        public AvatarIKGoal goal;
        public State state;
        public Vector3 at, wants, before, from, offset;   // held spot; animation's spot now and last frame; where a step began; a catch-up's start offset
        public Quaternion turn, wantsTurn, turnBefore;
        public float weight, done, length, lift, groundY;
        public bool home;                        // this step ends the lock: let go on landing
        public bool stance, quick;               // pinned by walking; a quick catch-up with no lift
    }

    Animator anim;
    EnemyMotor motor;
    EnemyBrain brain;
    Character character;
    Transform hips;
    int guardLayer = -1, readFrame = -1;
    float lastGuard, lowestAnkle = float.MaxValue;
    bool locked, plantPending;
    readonly Foot left = new Foot { goal = AvatarIKGoal.LeftFoot }, right = new Foot { goal = AvatarIKGoal.RightFoot };

    static CombatManager Tuning => CombatManager.HasInstance ? CombatManager.Instance : null;

    void Awake()
    {
        anim = GetComponent<Animator>();
        motor = GetComponentInParent<EnemyMotor>();
        brain = GetComponentInParent<EnemyBrain>();
        character = GetComponentInParent<Character>();
        if (anim != null && anim.isHuman) hips = anim.GetBoneTransform(HumanBodyBones.Hips);
        if (anim != null && anim.runtimeAnimatorController != null) guardLayer = anim.GetLayerIndex("Guard stance");
    }

    void Update()
    {
        if (hips == null) return;
        var tuning = Tuning;
        bool alive = character == null || character.IsAlive;
        bool on = tuning != null && tuning.plantFeetInKnockback && alive;
        // What holds the feet this frame.
        bool sliding = motor != null && motor.IsKnockedBack;
        float guard = guardLayer >= 0 ? anim.GetLayerWeight(guardLayer) : 0f;
        bool guardFading = guardLayer >= 0 && Mathf.Abs(guard - lastGuard) > 1e-4f;
        lastGuard = guard;
        bool still = motor == null || Flat(motor.LegVelocity).magnitude < SettleBelow;
        bool swinging = brain != null && brain.IsSwinging;
        bool hold = on && (sliding || swinging || guardFading && still);
        float reach = sliding ? (tuning != null ? tuning.footPlantReach : .4f) : SwingReach;
        float release = tuning != null ? tuning.footPlantRelease : .2f;

        if (hold && !locked)
        {
            // The lock starts: free feet are held where they stood the frame before (that frame's pose had nothing of
            // the blow, the fade or the swing yet); a foot still stepping or held keeps where it is.
            locked = true; plantPending = true;
        }
        else if (!hold && locked)
        {
            // The lock ends: every held foot goes home, the furthest first.
            locked = false;
            var first = Gap(left) >= Gap(right) ? left : right;
            GoHome(first, false); GoHome(first == left ? right : left, true);
        }
        if (!alive) { Free(left); Free(right); return; }
        if (plantPending) return;   // planted in the next IK pass

        // Walking: the stance foot is pinned while the animation has it down, and caught up as it lifts.
        bool walking = on && !locked && motor != null && Flat(motor.LegVelocity).magnitude > WalkingAbove;
        float floor = motor != null ? motor.transform.position.y : transform.position.y;
        Stance(left, walking, floor); Stance(right, walking, floor);

        // A held foot left too far behind steps after the body (one foot at a time).
        if (locked)
        {
            var lagging = Gap(left) >= Gap(right) ? left : right;
            var other = lagging == left ? right : left;
            if (lagging.state == State.Held && Gap(lagging) > reach && other.state != State.Stepping) BeginStep(lagging, false);
        }
        Advance(left, right, release);
        Advance(right, left, release);
    }

    void Stance(Foot foot, bool walking, float floor)
    {
        if (locked) return;
        float ankle = foot.wants.y - floor;
        // The lowest the animation has had an ankle, drifting back up slowly (a slope or stairs can't leave it low).
        if (readFrame >= 0 && ankle > -.5f) lowestAnkle = Mathf.Min(lowestAnkle + .02f * Time.deltaTime, ankle);
        if (foot.state == State.Held && foot.stance && (!walking || ankle > lowestAnkle + ContactOff || Gap(foot) > StanceReach)) BeginStep(foot, true, true);
        else if (walking && ankle < lowestAnkle + ContactOn && foot.state == State.Free)
        {
            foot.at = foot.wants; foot.turn = foot.wantsTurn; foot.groundY = foot.at.y;
            foot.state = State.Held; foot.weight = 1f; foot.lift = 0f; foot.home = false; foot.stance = true;
        }
    }

    float Gap(Foot foot) => foot.state == State.Free ? 0f : Flat(foot.wants - foot.at).magnitude;

    // Sends a foot home; second: it waits for the other to have nearly landed (done < 0 marks the wait).
    void GoHome(Foot foot, bool second)
    {
        if (foot.state == State.Held) { BeginStep(foot, true); if (second) foot.done = -1f; }
        else if (foot.state == State.Stepping) foot.home = true;
    }

    void BeginStep(Foot foot, bool home, bool quick = false)
    {
        foot.state = State.Stepping; foot.home = home; foot.done = 0f; foot.quick = quick; foot.stance = false;
        foot.from = foot.at; foot.length = Vector3.Distance(foot.at, foot.wants); foot.offset = foot.at - foot.wants;
    }

    void Advance(Foot foot, Foot other, float release)
    {
        switch (foot.state)
        {
            case State.Stepping:
                if (foot.done < 0f)
                {
                    if (other.state == State.Stepping && other.done >= 0f && other.done < FirstLanding) return;
                    BeginStep(foot, true);
                }
                if (foot.length <= .01f) { Land(foot); return; }
                if (foot.quick)
                {
                    // A walking catch-up follows the animation's own swing from the start and fades the offset it was
                    // pinned at out over CatchUp: lerping toward the swinging foot lagged it, then rushed.
                    foot.done = Mathf.Min(1f, foot.done + Time.deltaTime / CatchUp);
                    foot.at = foot.wants + foot.offset * (1f - Mathf.SmoothStep(0f, 1f, foot.done));
                    foot.turn = Quaternion.Slerp(foot.turn, foot.wantsTurn, foot.done);
                    if (foot.done >= 1f) Land(foot);
                    break;
                }
                // Eased in and out, so the foot leaves and lands without sliding; StepSpeed at most through the middle
                // (the eased curve's peak is 1.5 times its average).
                float speed = foot.quick ? foot.length / CatchUp : Mathf.Clamp(foot.length / Mathf.Max(.05f, release), .5f, StepSpeed / 1.5f);
                foot.done = Mathf.Min(1f, foot.done + speed * Time.deltaTime / foot.length);
                var target = foot.wants; target.y = Mathf.Max(target.y, foot.groundY);
                foot.at = Vector3.Lerp(foot.from, target, Mathf.SmoothStep(0f, 1f, foot.done));
                foot.lift = foot.quick ? 0f : Mathf.Sin(foot.done * Mathf.PI) * Mathf.Min(StepLift, foot.length * .5f);
                foot.turn = Quaternion.Slerp(foot.turn, foot.wantsTurn, foot.done);
                if (foot.done >= 1f) Land(foot);
                break;
            case State.Letting:
                // It follows the animation while the IK lets go: held on the spot it landed on, the swinging foot it
                // handed back to was up to 20 cm on by the time the weight reached 0, and jumped there.
                foot.at = foot.wants; foot.turn = foot.wantsTurn;
                foot.weight = Mathf.MoveTowards(foot.weight, 0f, Time.deltaTime / LetGo);
                if (foot.weight <= 0f) Free(foot);
                break;
        }
    }

    void Land(Foot foot)
    {
        foot.lift = 0f;
        if (foot.home) { foot.at = foot.wants; foot.turn = foot.wantsTurn; foot.state = State.Letting; }
        else foot.state = State.Held;
    }

    static void Free(Foot foot) { foot.state = State.Free; foot.weight = 0f; foot.lift = 0f; foot.home = false; foot.stance = false; foot.quick = false; }

    void OnAnimatorIK(int layer)
    {
        if (hips == null) return;
        // The first pass of the frame: where the animation has the feet, before anything is set (read every frame, so a
        // lock can plant them where they stood the frame before).
        if (readFrame != Time.frameCount)
        {
            bool fresh = readFrame == Time.frameCount - 1;
            readFrame = Time.frameCount;
            Read(left); Read(right);
            if (plantPending) { Plant(left, fresh); Plant(right, fresh); plantPending = false; }
        }
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
        // A foot pinned by walking comes under this lock where it is.
        if (foot.state == State.Held) { foot.stance = false; return; }
        if (foot.state == State.Stepping && !foot.home) return;
        if (foot.state == State.Free)
        {
            foot.at = fresh ? foot.before : foot.wants;
            foot.turn = fresh ? foot.turnBefore : foot.wantsTurn;
            foot.groundY = foot.at.y;
        }
        else foot.at += Vector3.up * foot.lift;   // caught mid-step home or letting go: held where it is
        foot.state = State.Held; foot.weight = 1f; foot.lift = 0f; foot.home = false; foot.stance = false; foot.quick = false;
    }

    void Hold(Foot foot)
    {
        if (foot.state == State.Free) return;
        anim.SetIKPosition(foot.goal, foot.at + Vector3.up * foot.lift); anim.SetIKPositionWeight(foot.goal, foot.weight);
        anim.SetIKRotation(foot.goal, foot.turn); anim.SetIKRotationWeight(foot.goal, foot.weight);
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
