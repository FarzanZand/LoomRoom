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
    public float aimReach = .5f;
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
    Vector3 rightTarget, leftTarget, lookTarget;
    Quaternion rightRotation, leftRotation;
    float rightWeight, leftWeight, lookWeight, lean;
    float rightGoal, leftGoal, lookGoal, leanGoal;
    // The figure in his hand, placed every frame after the animator and its IK have moved the hand.
    Transform hanging; float hangHeight; Quaternion hangFacing;

    public bool Busy { get; private set; }
    // Where the last figure he set down ended up (Place).
    public Vector3 LastPlacedAt { get; private set; }
    public Transform RightHand => animator != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
    public Transform LeftHand => animator != null ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;

    void Awake() => animator = GetComponent<Animator>();

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
        if (rightWeight > 0) { animator.SetIKPosition(AvatarIKGoal.RightHand, rightTarget); animator.SetIKRotation(AvatarIKGoal.RightHand, rightRotation); }
        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, leftWeight);
        animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, leftWeight * .7f);
        if (leftWeight > 0) { animator.SetIKPosition(AvatarIKGoal.LeftHand, leftTarget); animator.SetIKRotation(AvatarIKGoal.LeftHand, leftRotation); }
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

    // The right hand from a to b over seconds, eased, rising by arc in the middle.
    IEnumerator Move(Vector3 a, Vector3 b, float seconds, float arc)
    {
        for (float t = 0; t < seconds; t += Time.deltaTime)
        {
            float u = Mathf.SmoothStep(0, 1, t / seconds);
            rightTarget = Vector3.Lerp(a, b, u) + Vector3.up * Mathf.Sin(u * Mathf.PI) * arc;
            yield return null;
        }
        rightTarget = b;
    }

    static float FigureHeight(Transform figure)
    {
        var bounds = new Bounds(figure.position, Vector3.zero);
        foreach (var r in figure.GetComponentsInChildren<Renderer>()) if (r.enabled && r is not ParticleSystemRenderer) bounds.Encapsulate(r.bounds);
        return Mathf.Max(.5f, bounds.max.y - figure.position.y);
    }

    // The beam's pose: his left hand thrust out at shoulder height, a little to his side, palm toward the target, and he
    // leans into it. Pointed straight down at the player, the hand would come right up to their face.
    public void Aim(Vector3 target)
    {
        var shoulder = animator != null ? animator.GetBoneTransform(HumanBodyBones.LeftUpperArm) : null;
        var origin = shoulder != null ? shoulder.position : transform.position + Vector3.up * 25f;
        var flat = target - origin; flat.y = 0;
        flat = flat.sqrMagnitude > .01f ? flat.normalized : transform.forward;
        var outside = Vector3.Cross(flat, Vector3.up);   // his left
        float reach = ArmLength(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
        leftTarget = origin + (flat * aimReach + outside * .22f + Vector3.up * .08f) * reach;
        // Never right over the player: his hand is the size of their whole view.
        var fromTarget = leftTarget - target; fromTarget.y = 0;
        if (fromTarget.magnitude < aimKeepAway) leftTarget += -flat * (aimKeepAway - fromTarget.magnitude);
        var palmOut = (target - leftTarget).normalized;
        leftRotation = Quaternion.LookRotation(palmOut, Vector3.up) * Quaternion.Euler(0, 0, 90);
        lookTarget = target;
        leftGoal = 1f; lookGoal = 1f; leanGoal = Mathf.Max(leanGoal, beamLean);
    }

    // Stopped half way (the fight ended): he lets go of whatever he holds and straightens up.
    public void Release() { hanging = null; if (carriedLight != null) Destroy(carriedLight.gameObject); carriedLight = null; rightGoal = leftGoal = lookGoal = leanGoal = 0f; Busy = false; }

    public void StopAiming() { leftGoal = 0f; if (!Busy) { lookGoal = 0f; leanGoal = 0f; } }

    // Only his head: watching the stage between his moves.
    public void Watch(Vector3 target, bool on) { lookTarget = target; lookGoal = on ? .7f : 0f; }

    float ArmLength(HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones hand)
    {
        if (animator == null) return 12f;
        var a = animator.GetBoneTransform(upper); var b = animator.GetBoneTransform(lower); var c = animator.GetBoneTransform(hand);
        return a != null && b != null && c != null ? Vector3.Distance(a.position, b.position) + Vector3.Distance(b.position, c.position) : 12f;
    }
}
