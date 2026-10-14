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
    [Tooltip("Where the figure hangs below his fingertips: a share of its own height (he pinches it by the head).")]
    [Range(.5f, 1.2f)] public float grip = .97f;
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
        {
            var at = Fingertips() - Vector3.up * (hangHeight * grip);
            // Set down, it comes straight down onto its spot and stops on the floor: where his arm falls short of the
            // spot it would otherwise land early and be dragged along the stage.
            // Set down, it stops on the floor (his hand may go a little lower); it is never moved sideways off his
            // fingers.
            if (settle > 0f) at.y = Mathf.Max(at.y, settleAt.y);
            hanging.SetPositionAndRotation(at, hangFacing);
        }
        if (carriedLight != null && hanging != null && Camera.main != null)
        {
            var middle = hanging.position + Vector3.up * carriedHeight * .45f;
            carriedLight.transform.position = middle + (Camera.main.transform.position - middle).normalized * carriedHeight * .8f;
        }
    }

    Light carriedLight; float carriedHeight;
    // While he lowers it: how far (0-1) the figure has been drawn over its spot, and the spot.
    float settle; Vector3 settleAt;

    // Where his fingers pinch the figure: the lowest of his right fingertips (the figure is smaller than his hand; hung
    // from the wrist it would be inside his fist).
    Vector3 Fingertips()
    {
        var hand = RightHand;
        Vector3 low = hand.position; bool any = false;
        foreach (var b in new[] { HumanBodyBones.RightThumbDistal, HumanBodyBones.RightIndexDistal, HumanBodyBones.RightMiddleDistal })
        {
            var t = animator.GetBoneTransform(b);
            if (t == null) continue;
            var tip = t.childCount > 0 ? t.GetChild(0).position : t.position + (t.position - t.parent.position) * .8f;
            if (!any || tip.y < low.y) { low = tip; any = true; }
        }
        return low;
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
    // his hand goes down below the table's edge (out of sight) and comes up holding the figure by the head, he holds
    // it up toward the player for a moment, carries it out over the stage and lowers it slowly onto it, then lets go.
    // tableTop: the world height of the table, below which his hand is hidden.
    public IEnumerator Place(Transform figure, Vector3 spot, float seconds, float tableTop, System.Action landed)
    {
        Busy = true;
        var hand = RightHand;
        float height = FigureHeight(figure);
        var toSpot = spot - transform.position; toSpot.y = 0;
        var outward = toSpot.sqrMagnitude > .01f ? toSpot.normalized : transform.forward;
        // It faces away from him, toward the rest of the stage (the player's side).
        var facing = Quaternion.LookRotation(outward);
        var shoulder = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightUpperArm) : null;
        var shoulderAt = shoulder != null ? shoulder.position : transform.position + Vector3.up * 26f;
        float reach = ArmLength(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
        var from = hand != null ? hand.position : shoulderAt;
        // Under the table's edge on his side, then up in front of him, then out over the stage.
        // Shown out to his right at shoulder height, clear of his body (in front of his chest it is lost against his robe).
        var right = Vector3.Cross(Vector3.up, outward);
        var under = new Vector3(shoulderAt.x, tableTop - height * 1.2f, shoulderAt.z) + outward * reach * .25f + right * reach * .2f;
        var shown = shoulderAt + right * reach * .45f + outward * reach * .35f + Vector3.up * reach * .05f;
        var high = spot + Vector3.up * (carryHeight + height * grip);
        var low = spot + Vector3.up * (height * grip);
        rightRotation = Quaternion.LookRotation(Vector3.down, outward);
        lookTarget = spot; lookGoal = 1f; rightGoal = 1f;

        var renderers = figure.GetComponentsInChildren<Renderer>();
        var visible = new bool[renderers.Length];
        for (int i = 0; i < renderers.Length; i++) { visible[i] = renderers[i].enabled; renderers[i].enabled = false; }
        float dip = seconds * .14f, rise = seconds * .18f, hold = seconds * .2f, carry = seconds * .2f, lower = seconds * .28f;

        // Down below the edge, empty-handed.
        leanGoal = reachLean * .4f;
        yield return Move(from, under, dip, 0f);
        // He has it: up into view, held by the head.
        var turned = Quaternion.LookRotation(Quaternion.Euler(0, 60f, 0) * outward);
        hanging = figure; hangHeight = height; hangFacing = turned;
        // Lit from the player's side (LateUpdate keeps the light between it and the camera): the room is dark by now.
        var lightGo = new GameObject("Carried light");
        var carried = lightGo.AddComponent<Light>(); carried.type = LightType.Point; carried.range = height * 1.6f; carried.intensity = carryLight; carried.color = new Color(1f, .82f, .6f);
        carriedLight = carried; carriedHeight = height;
        yield return null;
        for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) renderers[i].enabled = visible[i];
        leanGoal = reachLean * .3f;
        yield return Move(under, shown, rise, height * .3f);
        // Shown to the player, turning slowly to face them.
        for (float t = 0; t < hold; t += Time.deltaTime)
        {
            hangFacing = Quaternion.Slerp(turned, facing, Mathf.SmoothStep(0, 1, t / hold));
            rightTarget = shown + Vector3.up * Mathf.Sin(t * 2.4f) * height * .04f;
            yield return null;
        }
        hangFacing = facing;
        // Out over the stage, leaning in.
        leanGoal = reachLean;
        yield return Move(shown, high, carry, height * .5f);
        // And down, slowing as it nears the floor.
        for (float t = 0; t < lower; t += Time.deltaTime)
        {
            float u = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / lower), 2.2f);
            // The figure hangs from his fingertips, a hand's length from the wrist the IK places: the wrist goes where
            // it puts the fingertips straight over the spot.
            var fingers = hand != null ? hand.position - Fingertips() : Vector3.zero;
            float ease = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / lower * 4f));   // the correction comes in gently
            rightTarget = Vector3.Lerp(high, low + Vector3.up * fingers.y, u) + new Vector3(fingers.x, 0, fingers.z) * ease;
            settle = 1f; settleAt = spot;
            yield return null;
        }
        hanging = null; settle = 0f;
        // It stands where his hand set it down (on the spot when his reach allows), on the floor there.
        var landedAt = new Vector3(figure.position.x, spot.y, figure.position.z);
        if (UnityEngine.AI.NavMesh.SamplePosition(landedAt, out var onFloor, 1.5f, UnityEngine.AI.NavMesh.AllAreas)) landedAt = onFloor.position;
        for (float t = 0; t < .12f; t += Time.deltaTime) { figure.position = Vector3.Lerp(figure.position, landedAt, t / .12f); yield return null; }
        figure.SetPositionAndRotation(landedAt, facing);
        LastPlacedAt = landedAt;
        if (lightGo != null) { lightGo.transform.position = landedAt + Vector3.up * height + (Camera.main != null ? Vector3.ProjectOnPlane(Camera.main.transform.position - landedAt, Vector3.up).normalized * height : Vector3.zero); Destroy(lightGo, .8f); }
        carriedLight = null;
        landed?.Invoke();
        // He lets go and straightens up.
        var release = rightTarget;
        rightGoal = 0f; leanGoal = 0f;
        for (float t = 0; t < blend; t += Time.deltaTime) { rightTarget = release + Vector3.up * (t / blend) * height; yield return null; }
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
    public void Release() { hanging = null; settle = 0f; if (carriedLight != null) Destroy(carriedLight.gameObject); carriedLight = null; rightGoal = leftGoal = lookGoal = leanGoal = 0f; Busy = false; }

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
