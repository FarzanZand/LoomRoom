using System.Collections.Generic;
using System.Collections;
using UnityEngine;

// The Dungeon Master's hands in a boss fight (BossStageEncounter): his right hand carries a figure from above the table
// down onto the stage and lets it go, his left hand points his energy beam (DungeonMasterBeam) at the player, he leans
// over the table to reach and his head follows what he is doing. Animator IK on top of whatever his body plays (the
// controller's Base Layer has IK Pass on); with nothing to do it sets no weights and he moves as before.
[RequireComponent(typeof(Animator))]
[DefaultExecutionOrder(1000)]   // after everything else has moved the carried figure this frame
public class DungeonMasterHand : MonoBehaviour
{
    [Tooltip("Degrees he leans over the table while he reaches for the stage.")]
    [Range(0, 60)] public float reachLean = 32f;
    [Range(.2f, 1f), Tooltip("How far his arm stretches toward the player while he aims, as a share of its length.")]
    public float aimReach = .42f;
    [Min(0), Tooltip("Least distance (world units, level) his aiming hand keeps from the player.")]
    public float aimKeepAway = 9f;
    [Tooltip("Degrees he leans while he aims the beam.")]
    [Range(0, 50)] public float beamLean = 10f;
    [Tooltip("Seconds his weights take to blend in and out.")]
    [Min(.05f)] public float blend = .35f;
    [Tooltip("Where the figure hangs below his pinch: a share of its own height (a little over 1: the top of its head just under his fingers, never in them).")]
    [Range(.5f, 1.2f)] public float grip = 1.04f;
    [Range(0, 1), Tooltip("How far above the floor (a share of its height) he lets go of it: it drops the rest of the way.")]
    public float dropHeight = .25f;
    [Range(-.5f, 1f), Tooltip("How high above his shoulder (a share of his arm's length) he raises his hand to show the figure, where it appears.")]
    public float raiseShown = .2f;
    [Tooltip("How high (world units) above the spot he carries the figure before he lowers it.")]
    [Min(0)] public float carryHeight = 7f;
    [Tooltip("Light on the figure while it is in his hand.")]
    [Min(0)] public float carryLight = 40f;

    Animator animator;
    Vector3 rightTarget, leftTarget, leftHint, lookTarget;
    Quaternion rightRotation, leftRotation;
    float rightWeight, leftWeight, lookWeight, lean;
    float rightGoal, leftGoal, lookGoal, leanGoal;
    // The figure in his hand, placed every frame after the animator and its IK have moved the hand.
    Transform hanging; float hangHeight; Quaternion hangFacing;

    public bool Busy { get; private set; }
    // Standing up straight with both hands back in his own pose (a lid can come down over the room without meeting him).
    public bool Upright => lean < 1f && rightWeight <= 0f && leftWeight <= 0f;
    // Where the last figure he set down ended up (Place).
    public Vector3 LastPlacedAt { get; private set; }
    public Transform RightHand => animator != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
    public Transform LeftHand => animator != null ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;

    void Awake()
    {
        animator = GetComponent<Animator>();
        // The rig's rest pose from its avatar: fingers straight (an open hand), as the model was made. (The prefab is
        // saved seated, with its fingers curled, so its own pose is no use.)
        if (animator == null || animator.avatar == null) return;
        var rest = new Dictionary<string, Quaternion>();
        foreach (var bone in animator.avatar.humanDescription.skeleton) rest[bone.name] = bone.rotation;
        for (var b = HumanBodyBones.LeftThumbProximal; b <= HumanBodyBones.LeftLittleDistal; b++)
        {
            var t = animator.GetBoneTransform(b);
            if (t != null && rest.TryGetValue(t.name, out var open)) openFingers.Add((t, open));
        }
        for (var b = HumanBodyBones.RightThumbProximal; b <= HumanBodyBones.RightLittleDistal; b++)
        {
            var t = animator.GetBoneTransform(b);
            if (t != null && rest.TryGetValue(t.name, out var open)) rightRest[t] = open;
        }
    }

    [Header("Open palm (beam)")]
    [Range(0, 1), Tooltip("How far his left fingers open while he aims the beam (1: straight, as the model rests).")]
    public float palmOpen = .9f;
    [Tooltip("Turn the left hand so its palm faces the player and its fingers point up while he aims.")]
    public bool palmFacesTarget = true;
    [Tooltip("Flip which side of the hand counts as the palm (if the back of the hand faces the player).")]
    public bool flipPalm = true;
    readonly List<(Transform bone, Quaternion open)> openFingers = new();
    Vector3 palmTarget;

    void Update()
    {
        float step = Time.deltaTime / blend;
        rightWeight = Mathf.MoveTowards(rightWeight, rightGoal, step);
        leftWeight = Mathf.MoveTowards(leftWeight, leftGoal, step);
        lookWeight = Mathf.MoveTowards(lookWeight, lookGoal, step);
        lean = Mathf.MoveTowards(lean, leanGoal, step * 40f);
    }

    void LateUpdate()
    {
        OpenPalm();
        FlickPose();
        var hand = RightHand;
        if (hanging != null && hand != null)
            hanging.SetPositionAndRotation(Pinch() - Vector3.up * (hangHeight * grip), hangFacing);
        if (carriedLight != null && hanging != null && Camera.main != null)
        {
            var middle = hanging.position + Vector3.up * carriedHeight * .45f;
            carriedLight.transform.position = middle + (Camera.main.transform.position - middle).normalized * carriedHeight * .8f;
        }
    }

    Light carriedLight; float carriedHeight;

    // Where his fingers pinch the figure: between the pads of his thumb and index finger (the figure is smaller than
    // his hand; hung from the wrist it would be inside his fist, from one fingertip it dangles off a nail).
    Vector3 Pinch()
    {
        var hand = RightHand;
        Vector3 Pad(HumanBodyBones bone)
        {
            var t = animator.GetBoneTransform(bone);
            if (t == null) return hand.position;
            var tip = t.childCount > 0 ? t.GetChild(0).position : t.position + (t.position - t.parent.position) * .8f;
            return Vector3.Lerp(t.position, tip, .5f);
        }
        return Vector3.Lerp(Pad(HumanBodyBones.RightThumbDistal), Pad(HumanBodyBones.RightIndexDistal), .5f);
    }

    void OnAnimatorIK(int layer)
    {
        if (layer != 0 || animator == null) return;
        if (lean > .01f) animator.bodyRotation = Quaternion.AngleAxis(lean, transform.right) * animator.bodyRotation;
        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, rightWeight);
        animator.SetIKRotationWeight(AvatarIKGoal.RightHand, rightWeight);
        if (rightWeight > 0)
        {
            bool flick = !float.IsNaN(curl);
            animator.SetIKPosition(AvatarIKGoal.RightHand, flick ? tipTarget + tipFix : rightTarget);
            animator.SetIKRotation(AvatarIKGoal.RightHand, flick ? handFix * Quaternion.LookRotation(flickAim, Vector3.up) : rightRotation);
        }
        // A finger flick comes down from above: the elbow held up over the hand, so the forearm stands up out of the room
        // through its open roof instead of reaching in sideways through its walls.
        bool fromAbove = !float.IsNaN(curl) && flickFromAbove;
        animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, fromAbove ? rightWeight : 0f);
        if (fromAbove)
        {
            float forearm = ArmLength(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, HumanBodyBones.RightHand);
            var level = flickAim; level.y = 0f; level = level.sqrMagnitude > 1e-4f ? level.normalized : transform.forward;
            // Up and out to his right of the hand, never toward his head.
            animator.SetIKHintPosition(AvatarIKHint.RightElbow, tipTarget + tipFix + Vector3.up * forearm * 1.2f + transform.right * forearm * .9f - level * forearm * .3f);
        }
        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, leftWeight);
        animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, leftWeight * .7f);
        if (leftWeight > 0) { animator.SetIKPosition(AvatarIKGoal.LeftHand, leftTarget); animator.SetIKRotation(AvatarIKGoal.LeftHand, leftRotation); }
        animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, leftWeight);
        if (leftWeight > 0) animator.SetIKHintPosition(AvatarIKHint.LeftElbow, leftHint);
        animator.SetLookAtWeight(lookWeight, .25f, .8f, 1f, .6f);
        if (lookWeight > 0) animator.SetLookAtPosition(lookTarget);
    }

    // Sets a figure (already spawned, its brain off) down on spot over seconds, then calls landed. Read as one motion:
    // his hand comes up from below the table's edge pinching the figure by the head and raises it beside his face; he
    // holds it out toward the player for a moment, carries it out over the stage, brings it low over the spot and lets go: it
    // drops the last stretch and lands (landed is called the moment its feet touch) while his hand rises away.
    // tableTop: the world height of the table, below which his hand is hidden.
    public IEnumerator Place(Transform figure, Vector3 spot, float seconds, float tableTop, System.Action landed)
    {
        Busy = true;
        var hand = RightHand;
        float height = FigureHeight(figure);
        var toSpot = spot - transform.position; toSpot.y = 0;
        var outward = toSpot.sqrMagnitude > .01f ? toSpot.normalized : transform.forward;
        // It faces away from him, toward the rest of the stage (the player's side), the whole time.
        var facing = Quaternion.LookRotation(outward);
        var shoulder = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightUpperArm) : null;
        var shoulderAt = shoulder != null ? shoulder.position : transform.position + Vector3.up * 26f;
        float reach = ArmLength(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
        var from = hand != null ? hand.position : shoulderAt;
        // Raised out to his right beside his face, clear of his body (in front of his chest it is lost against his robe).
        var right = Vector3.Cross(Vector3.up, outward);
        var shown = shoulderAt + right * reach * .35f + outward * reach * .45f + Vector3.up * reach * raiseShown;
        rightRotation = Quaternion.LookRotation(Vector3.down, outward);
        lookTarget = spot; lookGoal = 1f; rightGoal = 1f;
        // The wrist goes where it puts the pinch (the figure's head) at p.
        Vector3 Wrist(Vector3 p) => p + (hand.position - Pinch());

        float dip = seconds * .14f, rise = seconds * .2f, carry = seconds * .22f, lower = seconds * .2f;

        // He has it from the start: pinched in his fingers where his hand rests, below the table's edge, and it comes up
        // with his hand.
        hanging = figure; hangHeight = height; hangFacing = facing;
        // Lit from the player's side (LateUpdate keeps the light between it and the camera): the room is dark by now.
        var lightGo = new GameObject("Carried light");
        var carried = lightGo.AddComponent<Light>(); carried.type = LightType.Point; carried.range = height * 1.6f; carried.intensity = carryLight; carried.color = new Color(1f, .82f, .6f);
        carriedLight = carried; carriedHeight = height;
        // One motion: up past his face, out over the stage and down to just above the spot (a curve through the shown
        // point and over the spot), leaning in as he goes; then he lets go.
        var high = spot + Vector3.up * (carryHeight + height * grip);
        var dropFrom = spot + Vector3.up * (height * grip + height * dropHeight);
        float total = dip + rise + carry + lower;
        for (float t = 0; t < total; t += Time.deltaTime)
        {
            float u = Mathf.SmoothStep(0, 1, t / total), v = 1f - u;
            leanGoal = Mathf.Lerp(reachLean * .3f, reachLean, u);
            // The wrist targets for the curve's control points are taken now: the pinch moves as his hand turns.
            var p = v * v * v * from + 3f * v * v * u * shown + 3f * v * u * u * Wrist(high) + u * u * u * Wrist(dropFrom);
            rightTarget = p;
            yield return null;
        }
        // He lets go: it falls straight down onto its spot (where his hand had it) and lands; his hand rises away.
        hanging = null;
        var landedAt = new Vector3(figure.position.x, spot.y, figure.position.z);
        LastPlacedAt = landedAt;
        var release = rightTarget;
        rightGoal = 0f; leanGoal = 0f;
        float speed = 0f;
        while (figure.position.y > landedAt.y)
        {
            speed += 9.81f * 2.5f * Time.deltaTime;
            var p = figure.position; p.y = Mathf.Max(landedAt.y, p.y - speed * Time.deltaTime);
            figure.SetPositionAndRotation(p, facing);
            rightTarget = release + Vector3.up * (release.y - p.y);
            yield return null;
        }
        figure.SetPositionAndRotation(landedAt, facing);
        if (lightGo != null) { lightGo.transform.position = landedAt + Vector3.up * height + (Camera.main != null ? Vector3.ProjectOnPlane(Camera.main.transform.position - landedAt, Vector3.up).normalized * height : Vector3.zero); Destroy(lightGo, .8f); }
        carriedLight = null;
        landed?.Invoke();
        Busy = false;
    }

    // Takes something off the table (the meddling's stolen chest, DungeonMasterMeddling): his hand comes over it from
    // above, pinches it by its top, lifts it up past his face and back down below the table's edge, where taken is called
    // (the caller removes it). tableTop: the world height of the table, below which his hand is hidden.
    public IEnumerator Take(Transform thing, float seconds, float tableTop, System.Action taken)
    {
        Busy = true;
        var hand = RightHand;
        float height = FigureHeight(thing);
        var spot = thing.position;
        Arc(spot, out var outward, out var shoulderAt, out var reach, out var shown);
        rightRotation = Quaternion.LookRotation(Vector3.down, outward);
        lookTarget = spot; lookGoal = 1f; rightGoal = 1f;
        Vector3 Wrist(Vector3 p) => hand != null ? p + (hand.position - Pinch()) : p;
        var from = hand != null ? hand.position : shoulderAt;
        var high = spot + Vector3.up * (carryHeight + height * grip);
        var grab = spot + Vector3.up * (height * grip);
        float down = seconds * .5f, up = seconds * .5f;
        for (float t = 0; t < down; t += Time.deltaTime)
        {
            float u = Mathf.SmoothStep(0, 1, t / down), v = 1f - u;
            leanGoal = Mathf.Lerp(reachLean * .3f, reachLean, u);
            rightTarget = v * v * v * from + 3f * v * v * u * shown + 3f * v * u * u * Wrist(high) + u * u * u * Wrist(grab);
            yield return null;
        }
        // He has it: it rises with his hand, up past his face and away below the table's edge.
        hanging = thing; hangHeight = height; hangFacing = thing.rotation;
        var start = rightTarget;
        var gone = transform.position + Vector3.Cross(Vector3.up, outward) * reach * .4f + Vector3.up * (tableTop - transform.position.y - height * 3f);
        for (float t = 0; t < up; t += Time.deltaTime)
        {
            float u = Mathf.SmoothStep(0, 1, t / up), v = 1f - u;
            leanGoal = Mathf.Lerp(reachLean, 0f, u);
            rightTarget = v * v * v * start + 3f * v * v * u * Wrist(high) + 3f * v * u * u * shown + u * u * u * Wrist(gone);
            yield return null;
        }
        hanging = null;
        rightGoal = 0f; leanGoal = 0f; lookGoal = 0f;
        taken?.Invoke();
        Busy = false;
    }

    // Brings something up from below the table's edge in his fingers (the dice room's die), out over a spot and up to
    // high above it, and lets go there: the caller moves it from then on (released is called as he lets go).
    public IEnumerator Drop(Transform thing, Vector3 over, float seconds, System.Action released)
    {
        Busy = true;
        var hand = RightHand;
        float height = FigureHeight(thing);
        Arc(over, out var outward, out var shoulderAt, out _, out var shown);
        rightRotation = Quaternion.LookRotation(Vector3.down, outward);
        lookTarget = over; lookGoal = 1f; rightGoal = 1f;
        Vector3 Wrist(Vector3 p) => hand != null ? p + (hand.position - Pinch()) : p;
        var from = hand != null ? hand.position : shoulderAt;
        hanging = thing; hangHeight = height; hangFacing = thing.rotation;
        var high = over + Vector3.up * (carryHeight + height * grip);
        for (float t = 0; t < seconds; t += Time.deltaTime)
        {
            float u = Mathf.SmoothStep(0, 1, t / seconds), v = 1f - u;
            leanGoal = Mathf.Lerp(reachLean * .3f, reachLean * .8f, u);
            rightTarget = v * v * from + 2f * v * u * shown + u * u * Wrist(high);
            yield return null;
        }
        hanging = null;
        released?.Invoke();
        yield return new WaitForSeconds(.5f);
        rightGoal = 0f; leanGoal = 0f;
        yield return new WaitForSeconds(blend);
        lookGoal = 0f;
        Busy = false;
    }

    // Where a reach toward a spot goes: the direction out to it, his shoulder, his arm's length and the point beside his
    // face where he shows what he holds.
    void Arc(Vector3 spot, out Vector3 outward, out Vector3 shoulderAt, out float reach, out Vector3 shown)
    {
        var toSpot = spot - transform.position; toSpot.y = 0;
        outward = toSpot.sqrMagnitude > .01f ? toSpot.normalized : transform.forward;
        var shoulder = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightUpperArm) : null;
        shoulderAt = shoulder != null ? shoulder.position : transform.position + Vector3.up * 26f;
        reach = ArmLength(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
        var right = Vector3.Cross(Vector3.up, outward);
        shown = shoulderAt + right * reach * .35f + outward * reach * .45f + Vector3.up * reach * raiseShown;
    }

    // How far (level, world units) from where he stands he can set something down: his arm's length plus his lean.
    public float Reach => ArmLength(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand) * 1.6f;

    static float FigureHeight(Transform figure)
    {
        var bounds = new Bounds(figure.position, Vector3.zero);
        foreach (var r in figure.GetComponentsInChildren<Renderer>()) if (r.enabled && r is not ParticleSystemRenderer) bounds.Encapsulate(r.bounds);
        return Mathf.Max(.5f, bounds.max.y - figure.position.y);
    }

    // While he aims: his left hand turned palm out at the target, fingers up, and the fingers opened (after the
    // animator and its IK have placed the hand, so they don't undo it).
    void OpenPalm()
    {
        if (leftWeight <= .01f || animator == null) return;
        var hand = LeftHand;
        if (hand == null) return;
        if (palmFacesTarget)
        {
            // The palm from the knuckles (this rig's hands have a thumb, index and middle finger only): across the
            // knuckles from middle to index, and out along the fingers.
            var index = animator.GetBoneTransform(HumanBodyBones.LeftIndexProximal);
            var middle = animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            var little = animator.GetBoneTransform(HumanBodyBones.LeftLittleProximal) ?? animator.GetBoneTransform(HumanBodyBones.LeftRingProximal) ?? middle;
            if (index != null && middle != null && little != null && little != index)
            {
                var normal = Vector3.Cross(little.position - hand.position, index.position - hand.position).normalized * (flipPalm ? -1f : 1f);
                var toward = palmTarget - hand.position;
                var level = new Vector3(toward.x, 0f, toward.z).normalized;
                var toTarget = Vector3.Slerp(level, toward.normalized, .35f).normalized;
                var fix = Quaternion.FromToRotation(normal, toTarget);
                // Then fingers up: turn about the palm's normal until the knuckles point as near to up as they can.
                var fingers = Vector3.ProjectOnPlane(fix * (middle.position - hand.position), toTarget);
                var up = Vector3.ProjectOnPlane(Vector3.up, toTarget);
                if (fingers.sqrMagnitude > 1e-6f && up.sqrMagnitude > 1e-6f) fix = Quaternion.FromToRotation(fingers, up) * fix;
                hand.rotation = Quaternion.Slerp(hand.rotation, fix * hand.rotation, leftWeight);
            }
        }
        float open = leftWeight * palmOpen;
        foreach (var (bone, rest) in openFingers) bone.localRotation = Quaternion.Slerp(bone.localRotation, rest, open);
    }

    // The beam's pose, a blessing raised: his upper arm down by his side, the elbow bent, the forearm upright and the
    // open hand at shoulder height in front of him, palm out at the target, fingers up.
    public void Aim(Vector3 target)
    {
        var shoulder = animator != null ? animator.GetBoneTransform(HumanBodyBones.LeftUpperArm) : null;
        var origin = shoulder != null ? shoulder.position : transform.position + Vector3.up * 25f;
        var flat = target - origin; flat.y = 0;
        flat = flat.sqrMagnitude > .01f ? flat.normalized : transform.forward;
        var outside = Vector3.Cross(flat, Vector3.up);   // his left
        float reach = ArmLength(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
        // Leaning over the table on his forearms (the boss fight) brings his shoulder down to the table's edge: the hand
        // is raised to just under his face instead, so the beam pours down from up high, not along the table top.
        var head = animator != null ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head != null) origin.y = Mathf.Max(origin.y, head.position.y - reach * .3f);
        leftTarget = origin + (flat * aimReach + outside * .14f - Vector3.up * .12f) * reach;
        // The elbow low and a little out, so the forearm stands up from it.
        leftHint = origin + (-Vector3.up * .55f + outside * .3f + flat * .1f) * reach;
        leftRotation = Quaternion.LookRotation(flat, Vector3.up);
        lookTarget = target; palmTarget = target;
        leftGoal = 1f; lookGoal = 1f; leanGoal = Mathf.Max(leanGoal, beamLean);
    }

    // Stopped half way (the fight ended): he lets go of whatever he holds and straightens up.
    public void Release() { if (handLight != null) Destroy(handLight.gameObject); handLight = null; curl = float.NaN; openHand = false; flickCarried = null; flickFromAbove = false; hanging = null; if (carriedLight != null) Destroy(carriedLight.gameObject); carriedLight = null; rightGoal = leftGoal = lookGoal = leanGoal = 0f; Busy = false; }

    public void StopAiming() { leftGoal = 0f; if (!Busy) { lookGoal = 0f; leanGoal = 0f; } }

    // Only his head: watching the stage between his moves.
    public void Watch(Vector3 target, bool on) { lookTarget = target; lookGoal = on ? .7f : 0f; }

    // ── The flick (the Flick spell) ─────────────────────────────────

    [Header("Flick")]
    [Range(0, 80), Tooltip("Degrees his flicking finger points down from level (steep: his hand hangs down into the room from above, fingers down, and the finger snaps out along the flick).")]
    public float flickTilt = 60f;
    [Min(0), Tooltip("How far above the enemy his hand comes over the room before it drops straight down onto it (world units, above Carry Height), so his arm comes in over the walls.")]
    public float flickAbove = 6f;
    [Range(0, 60), Tooltip("Most he leans over the table for a flick from above (more, and his head comes down over his own hand).")]
    public float flickAboveLean = 15f;
    bool flickFromAbove;
    [Range(0, 45), Tooltip("Degrees his finger points up when he flicks something into an arc (a die).")]
    public float flickTossTilt = 20f;
    [Range(0, 80), Tooltip("Degrees he leans over the table for a flick at the far end of his reach (the finger has to reach the enemy itself). Closer in he leans less, so his head stays up out of the room.")]
    public float flickLean = 55f;
    [Tooltip("Level distance from where he stands to the enemy over which his lean grows from half his reaching lean to Flick Lean (world units).")]
    public Vector2 flickLeanRange = new(9f, 17f);
    [Tooltip("Degrees each joint of his index finger bends when it is cocked behind his thumb (base, middle, tip).")]
    public Vector3 flickCurl = new(75f, 95f, 70f);
    [Min(0), Tooltip("Light on his hand while he flicks (the dungeon is dark: unlit, his hand is a shape in the gloom).")]
    public float flickLight = 3f;

    // How far his right index finger is cocked: 1 curled behind his thumb, 0 straight, below 0 snapped past straight.
    // NaN: not flicking (the hand goes by rightTarget and rightRotation as in his other moves).
    float curl = float.NaN;
    Vector3 flickAim = Vector3.forward;   // where the finger points: along the flick, tipped down by Flick Tilt
    Vector3 tipTarget;                    // where the straightened fingertip is to be
    // Learnt every frame from where the IK actually put the hand: the goal is turned and moved by these until the
    // knuckles point along the aim (palm down) and the straight fingertip sits on Tip Target.
    Quaternion handFix = Quaternion.identity; Vector3 tipFix;
    Transform flickCarried; float carriedSize;
    readonly Dictionary<Transform, Quaternion> rightRest = new();

    // He brings his hand in over point (from his own side of it: he flicks away from himself), cocks his index finger
    // behind his thumb, holds it a moment and snaps it out along direction; struck is called as the nail meets the point.
    // carried: something held in front of the cocked finger (a die), let go of as it is struck; the caller flies it on.
    // follow: where the point is now, asked every frame (the enemy may be pushed while his hand comes): the hand follows it.
    public IEnumerator Flick(Vector3 point, Vector3 direction, float seconds, float windup, Transform carried, System.Action struck, System.Func<Vector3> follow = null)
    {
        Busy = true;
        var level = direction; level.y = 0f; level = level.sqrMagnitude > .001f ? level.normalized : transform.forward;
        // Down at an enemy on the floor; a little up for something he sends flying in an arc (a die).
        float tilt = carried != null ? flickTossTilt : -flickTilt;
        flickAim = (level + Vector3.up * Mathf.Tan(tilt * Mathf.Deg2Rad)).normalized;
        float finger = FingerLength();
        flickCarried = carried; carriedSize = carried != null ? FigureHeight(carried) : 0f;
        // The straight tip a little short of the point (it is the nail that strikes), and behind it while cocked.
        var touch = point - flickAim * (finger * .15f + carriedSize * .5f);
        var cocked = touch - flickAim * finger * .35f;
        Arc(point, out _, out _, out _, out var shown);
        // A finger flick: high over the spot (over the walls), then straight down onto it. A die is held out over the
        // table's edge as before.
        flickFromAbove = carried == null;
        // From above, the hand comes over from his right side and from beyond the spot (away from his head), and drops
        // from there, so the arm never passes his head on the way down.
        var aside = AwayFromHead(cocked, finger);
        var high = flickFromAbove ? cocked + Vector3.up * (carryHeight + flickAbove) + aside
                                  : cocked + Vector3.up * carryHeight * .25f - level * finger * 1.5f;
        void Follow()
        {
            if (follow == null) return;
            var now = follow();
            var moved = now - point;
            if (moved.sqrMagnitude < 1e-6f) return;
            point = now; touch += moved; cocked += moved; high += moved;
        }
        var from = FingerTip(); tipTarget = from; tipFix = RightHand != null ? RightHand.position - from : Vector3.zero; handFix = Quaternion.identity;
        restTip = transform.InverseTransformPoint(from);
        lookTarget = point; lookGoal = 1f; rightGoal = 1f; curl = .2f;
        var flat = point - transform.position; flat.y = 0f;
        float lean = Mathf.Lerp(reachLean * .5f, flickLean, Mathf.InverseLerp(flickLeanRange.x, flickLeanRange.y, flat.magnitude));
        // From above he leans only a little: bent far over, his head came down over his own hand and the arm went through it.
        if (flickFromAbove) lean = Mathf.Min(lean, flickAboveLean);
        openHand = false;
        HandLight(flickLight, new Color(1f, .85f, .65f), finger);
        // In over the spot, the finger cocking as it comes down.
        for (float t = 0; t < seconds; t += Time.deltaTime)
        {
            Follow();
            float n = t / seconds, u = Mathf.SmoothStep(0, 1, n), v = 1f - u;
            leanGoal = Mathf.Lerp(reachLean * .3f, lean, u);
            // From above: one unbroken arc, out past his face, over and down onto the spot, its last stretch coming
            // straight down (it went up to a point high over the spot first, stopped there with the arm straight up, then
            // came down: the user found it unnatural).
            tipTarget = v * v * v * from + 3f * v * v * u * shown + 3f * v * u * u * high + u * u * u * cocked;
            curl = Mathf.Lerp(.2f, 1f, Mathf.InverseLerp(.55f, .95f, t / seconds));
            yield return null;
        }
        // The wind-up: drawn back a hair, the finger pressing against the thumb.
        for (float t = 0; t < windup; t += Time.deltaTime)
        {
            Follow();
            float u = t / windup;
            tipTarget = cocked - level * finger * .12f * Mathf.Sin(u * Mathf.PI * .5f) + Vector3.up * Mathf.Sin(u * 40f) * finger * .01f;
            curl = 1f + .06f * u;
            yield return null;
        }
        // The snap: out past straight in a few frames, the hand pushed after it.
        bool hit = false; var pulled = tipTarget; var pulledTouch = touch;
        const float Snap = .07f;
        for (float t = 0; t < Snap; t += Time.deltaTime)
        {
            Follow();
            float u = t / Snap;
            curl = Mathf.Lerp(1.06f, -.18f, u);
            tipTarget = Vector3.Lerp(pulled + (touch - pulledTouch), touch + level * finger * .25f, u);
            if (!hit && u >= .45f) { hit = true; flickCarried = null; struck?.Invoke(); }
            yield return null;
        }
        curl = -.18f; tipTarget = touch + level * finger * .25f;
        if (!hit) { flickCarried = null; struck?.Invoke(); }
        // Held out a moment, then up and away.
        yield return new WaitForSeconds(.3f);
        yield return Withdraw(level, finger, u => curl = Mathf.Lerp(-.18f, .3f, u));
        curl = float.NaN; lookGoal = 0f;
        if (handLight != null) Destroy(handLight.gameObject);
        handLight = null;
        Busy = false;
    }
    // Up and away from where his hand is and back down to where it rested before he reached (restTip, his own pose's
    // fingertip, kept relative to him), leaning back as it goes; only then is his arm handed back to his own pose, so
    // nothing is left to jump (faded out part way, the hand swept from the air to the table in a few frames).
    // step: told how far along it is (0 to 1).
    IEnumerator Withdraw(Vector3 level, float finger, System.Action<float> step)
    {
        const float Seconds = 1.2f;
        var after = tipTarget;
        float lean = leanGoal;
        bool above = flickFromAbove;
        var high = above ? after + Vector3.up * (carryHeight + flickAbove) + AwayFromHead(after, finger) : after + Vector3.up * carryHeight - level * finger;
        for (float t = 0; t < Seconds; t += Time.deltaTime)
        {
            float n = t / Seconds, u = Mathf.SmoothStep(0, 1, n), v = 1f - u;
            var rest = transform.TransformPoint(restTip);
            // From above: up out of the room and back to his side on one curve (straight up to a point high over the room
            // first stretched his arm up like the way in did).
            tipTarget = v * v * after + 2f * v * u * high + u * u * rest;
            step?.Invoke(u);
            leanGoal = lean * v;
            yield return null;
        }
        flickFromAbove = false;
        rightGoal = 0f; leanGoal = 0f;
        while (rightWeight > 0f) yield return null;
    }
    Vector3 restTip;   // where his fingertip rested before the reach, in his own space

    // Level offset from a point out away from his head (and a little to his right): where his hand is held high over it.
    Vector3 AwayFromHead(Vector3 point, float finger)
    {
        var head = animator != null ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        var away = point - (head != null ? head.position : transform.position); away.y = 0f;
        away = away.sqrMagnitude > .01f ? away.normalized : transform.forward;
        return away * finger * 1.5f + transform.right * finger * 1.2f;
    }

    Light handLight; float handLightStrength;
    bool openHand;   // the hand flat and open (Bless), not a cocked finger

    void HandLight(float strength, Color color, float finger)
    {
        if (handLight != null) Destroy(handLight.gameObject);
        handLight = null; handLightStrength = strength;
        if (strength <= 0f) return;
        var lightGo = new GameObject("DM hand light");
        handLight = lightGo.AddComponent<Light>(); handLight.type = LightType.Point; handLight.range = finger * 2.2f; handLight.intensity = 0f; handLight.color = color; handLight.shadows = LightShadows.None;
    }

    // ── The open hand (the Helping Hand spell) ──────────────────────

    [Header("Open hand")]
    [Range(0, 60), Tooltip("Degrees his fingers point down from level while he holds his hand over the player.")]
    public float blessTilt = 18f;
    [Min(0), Tooltip("Light on his hand while he holds it over the player.")]
    public float blessLight = 1.5f;

    // He brings his hand in flat over point (the palm's middle there, palm down, fingers pointing along direction), holds
    // it there for hold seconds (holding is called as it arrives) and takes it away. follow: where the point is now, asked
    // every frame (the player may walk meanwhile).
    // done: called as the hold ends and his hand starts away.
    public IEnumerator Bless(Vector3 point, Vector3 direction, float seconds, float hold, Color glow, System.Action holding, System.Func<Vector3> follow = null, System.Action done = null)
    {
        Busy = true;
        var level = direction; level.y = 0f; level = level.sqrMagnitude > .001f ? level.normalized : transform.forward;
        flickAim = (level - Vector3.up * Mathf.Tan(blessTilt * Mathf.Deg2Rad)).normalized;
        float finger = FingerLength();
        flickCarried = null; openHand = true;
        // The fingertip a finger's length on past the palm's middle.
        Vector3 Tip(Vector3 palm) => palm + flickAim * finger * 1.1f;
        Arc(point, out _, out _, out _, out var shown);
        var from = FingerTip(); tipTarget = from; tipFix = RightHand != null ? RightHand.position - from : Vector3.zero; handFix = Quaternion.identity;
        restTip = transform.InverseTransformPoint(from);
        lookTarget = point; lookGoal = 1f; rightGoal = 1f; curl = 0f;
        var flat = point - transform.position; flat.y = 0f;
        float lean = Mathf.Lerp(reachLean * .5f, flickLean, Mathf.InverseLerp(flickLeanRange.x, flickLeanRange.y, flat.magnitude));
        HandLight(blessLight, glow, finger);
        var high = Tip(point) + Vector3.up * carryHeight * .5f - level * finger;
        for (float t = 0; t < seconds; t += Time.deltaTime)
        {
            if (follow != null) { var now = follow(); high += now - point; point = now; }
            float u = Mathf.SmoothStep(0, 1, t / seconds), v = 1f - u;
            leanGoal = Mathf.Lerp(reachLean * .3f, lean, u);
            tipTarget = v * v * v * from + 3f * v * v * u * shown + 3f * v * u * u * high + u * u * u * Tip(point);
            lookTarget = point;
            yield return null;
        }
        holding?.Invoke();
        // Held over them, breathing a little.
        for (float t = 0; t < hold; t += Time.deltaTime)
        {
            if (follow != null) point = follow();
            tipTarget = Tip(point) + Vector3.up * Mathf.Sin(t * 2.4f) * finger * .06f;
            lookTarget = point;
            yield return null;
        }
        done?.Invoke();
        yield return Withdraw(level, finger, null);
        curl = float.NaN; openHand = false; lookGoal = 0f;
        if (handLight != null) Destroy(handLight.gameObject);
        handLight = null;
        Busy = false;
    }

    [Tooltip("Where he holds something out to flick it at a spot out of his reach: shares of his arm's length out toward it, to his right and above his shoulder.")]
    public Vector3 holdOut = new(.85f, .25f, .25f);

    // In front of his right shoulder toward the spot, the arm most of the way out: where he holds a die to flick it.
    public Vector3 HoldPoint(Vector3 toward)
    {
        var shoulder = Bone(HumanBodyBones.RightUpperArm);
        var at = shoulder != null ? shoulder.position : transform.position + Vector3.up * 26f;
        var flat = toward - at; flat.y = 0f; flat = flat.sqrMagnitude > .01f ? flat.normalized : transform.forward;
        var right = Vector3.Cross(Vector3.up, flat);
        float arm = ArmLength(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
        return at + (flat * holdOut.x + right * holdOut.y + Vector3.up * holdOut.z) * arm;
    }

    Transform Bone(HumanBodyBones b) => animator != null ? animator.GetBoneTransform(b) : null;

    // The tip of his right index finger as it is now (the last joint's own end).
    Vector3 FingerTip()
    {
        var distal = Bone(HumanBodyBones.RightIndexDistal);
        if (distal == null) return RightHand != null ? RightHand.position : transform.position;
        return distal.childCount > 0 ? distal.GetChild(0).position : distal.position + (distal.position - distal.parent.position) * .8f;
    }

    // Knuckle to fingertip of his right index finger, straight.
    float FingerLength()
    {
        var a = Bone(HumanBodyBones.RightIndexProximal); var b = Bone(HumanBodyBones.RightIndexIntermediate); var c = Bone(HumanBodyBones.RightIndexDistal);
        if (a == null || b == null || c == null) return 2f;
        var tip = c.childCount > 0 ? c.GetChild(0).position : c.position + (c.position - b.position) * .8f;
        return Vector3.Distance(a.position, b.position) + Vector3.Distance(b.position, c.position) + Vector3.Distance(c.position, tip);
    }

    // After the IK: learns how far the hand is from where the flick wants it (fed back into the next frame's goal), then
    // bends the index finger by Curl and folds the middle finger away, so only the index finger is out.
    void FlickPose()
    {
        if (float.IsNaN(curl) || animator == null || rightWeight <= .01f) return;
        var hand = RightHand;
        var p1 = Bone(HumanBodyBones.RightIndexProximal); var p2 = Bone(HumanBodyBones.RightIndexIntermediate); var p3 = Bone(HumanBodyBones.RightIndexDistal);
        var m1 = Bone(HumanBodyBones.RightMiddleProximal);
        if (hand == null || p1 == null || p2 == null || p3 == null) return;
        var across = Vector3.Cross(Vector3.up, flickAim).normalized;   // his right, with the palm down and the finger forward
        // The hand's own forward (wrist to index knuckle) and across (index knuckle to middle knuckle), as the IK left them.
        var forward = (p1.position - hand.position).normalized;
        var fix = Quaternion.FromToRotation(forward, flickAim);
        if (m1 != null)
        {
            var side = Vector3.ProjectOnPlane(fix * (m1.position - p1.position), flickAim);
            var want = Vector3.ProjectOnPlane(across, flickAim);
            if (side.sqrMagnitude > 1e-6f && want.sqrMagnitude > 1e-6f) fix = Quaternion.FromToRotation(side, want) * fix;
        }
        // Learnt only while the IK has the hand fully: blending in or out, the hand falls short of its goal by the blend
        // alone, and learning that would push the goal further and further off (the arm thrown about as it lets go).
        var straightTip = p1.position + forward * FingerLength();
        if (rightWeight >= .97f)
        {
            handFix = Quaternion.Slerp(handFix, fix * handFix, .5f);
            // The straight fingertip: from the knuckle along the hand's forward (the finger is bent below, after this).
            tipFix = Vector3.ClampMagnitude(tipFix + (tipTarget - straightTip) * .5f, 30f);
        }
        // The finger itself: each joint bent about his hand's across line (down toward the palm), the middle finger folded.
        // From his rest pose (straight, as the model was made; the animator has them curled), then bent.
        void Bend(Transform bone, float degrees)
        {
            if (bone == null) return;
            if (rightRest.TryGetValue(bone, out var rest)) bone.localRotation = rest;
            bone.rotation = Quaternion.AngleAxis(degrees, across) * bone.rotation;
        }
        float c = curl;
        if (openHand)
        {
            // Palm down, fingers straight and together (his rest pose): nothing bent, folded or held.
            foreach (var bone in rightRest.Keys) bone.localRotation = rightRest[bone];
            LightHand();
            return;
        }
        Bend(p1, flickCurl.x * c); Bend(p2, flickCurl.y * Mathf.Max(c, -.3f)); Bend(p3, flickCurl.z * Mathf.Max(c, -.3f));
        Bend(m1, 80f); Bend(Bone(HumanBodyBones.RightMiddleIntermediate), 95f); Bend(Bone(HumanBodyBones.RightMiddleDistal), 70f);
        // The thumb holds the cocked fingertip (lets go of it as the finger snaps out): its tip turned onto the bent finger.
        var thumb = Bone(HumanBodyBones.RightThumbProximal); var thumbEnd = Bone(HumanBodyBones.RightThumbDistal);
        if (thumb != null && thumbEnd != null)
        {
            if (rightRest.TryGetValue(thumb, out var thumbRest)) thumb.localRotation = thumbRest;
            var thumbTip = thumbEnd.childCount > 0 ? thumbEnd.GetChild(0).position : thumbEnd.position;
            var holdAt = p3.position;
            float hold = Mathf.Clamp01((c - .2f) / .6f);
            if (hold > 0f) thumb.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(thumbTip - thumb.position, holdAt - thumb.position), hold) * thumb.rotation;
        }
        if (flickCarried != null)
            flickCarried.position = FingerTip() + flickAim * (carriedSize * .6f) - Vector3.up * carriedSize * .5f;
        LightHand();
    }

    // His hand lit from the watcher's side, brighter as it comes down into the room.
    void LightHand()
    {
        if (handLight == null || Camera.main == null) return;
        var tip = FingerTip();
        // From above and to one side of the watcher's line, so the hand has a lit side and a shaded side (lit straight
        // from the watcher it was a flat white shape, like a ghost's). It lights his forearm and sleeve too, out to the
        // elbow: lit alone, the hand hung in the dark like a ghost's, its arm lost against the night.
        var elbow = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightLowerArm) : null;
        var middle = elbow != null ? Vector3.Lerp(tip, elbow.position, .45f) : tip;
        float span = elbow != null ? Vector3.Distance(tip, elbow.position) : FingerLength() * 2f;
        var toWatcher = (Camera.main.transform.position - middle).normalized;
        var side = Vector3.Cross(Vector3.up, toWatcher).normalized;
        handLight.transform.position = middle + (toWatcher * .5f + side * .6f + Vector3.up * .7f).normalized * span * .7f;
        handLight.range = span * 1.6f;
        handLight.intensity = Mathf.MoveTowards(handLight.intensity, handLightStrength * rightWeight, handLightStrength * 2f * Time.deltaTime);
    }

    float ArmLength(HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones hand)
    {
        if (animator == null) return 12f;
        var a = animator.GetBoneTransform(upper); var b = animator.GetBoneTransform(lower); var c = animator.GetBoneTransform(hand);
        return a != null && b != null && c != null ? Vector3.Distance(a.position, b.position) + Vector3.Distance(b.position, c.position) : 12f;
    }
}
