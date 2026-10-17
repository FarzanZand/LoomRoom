using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// R&D prototype (one body, true first person), branch onebody.
// The table player is one class body with one animator (FirstPersonTable): everyone else sees that body, and it casts
// the player's shadow. The owner's camera draws an arms-only cut of the same mesh on a camera-attached copy of the
// skeleton (TableArmsMesh under FirstPersonVisuals, no animator of its own), posed every frame from the body's animated
// bones before the look turns the body. So first person is framed, enlarged (the view-model squash) and lit exactly as
// the first-person arms always were, while the body others see is the same mesh moving with the same animation.
// - One place: the cut is made from whatever body and arm gear is on, by bone weights (RefreshArms after a change).
// - Held items live on the first-person hands as before (Hitbox, SlashLines, spell origin); the body holds visual copies.
// - For the owner's camera the body draws only its shadow. It bends with the look like a person: the chest up to
//   chestLimit degrees over Spine_01-03, the neck and head the rest.
[DefaultExecutionOrder(-500)]
public class OneBodyRig : MonoBehaviour
{
    [Tooltip("The player whose camera looks out of this body.")]
    public Player player;
    [Tooltip("The body's animator: the one controller.")]
    public Animator animator;
    [Tooltip("The camera-attached skeleton copy the owner's arms are drawn on (TableArmsMesh under FirstPersonVisuals).")]
    public Transform firstPersonArms;
    [Tooltip("Most the chest turns with the look, degrees; the neck and head take the rest.")]
    [Range(0f, 90f)] public float chestLimit = 15f;
    [Range(0f, 1f)] public float neckShare = .4f;
    [Tooltip("Off: the body keeps the animation's pose whatever the look (for comparing).")]
    public bool followLook = true;

    [Header("Arms seen from outside")]
    [Tooltip("The first-person clips hold the hands up in front of the eyes. From outside each hand is carried this many "
           + "degrees lower around its shoulder (the elbow bends to reach it). First person never sees this.")]
    [Range(0f, 60f)] public float armDrop = 25f;
    [Tooltip("How much of the hand's reach forward is taken in toward the body (0 = as the clip, 1 = at the shoulder).")]
    [Range(0f, .8f)] public float armTuck = .25f;
    [Tooltip("How much of a hand held out to the side, past its shoulder, is taken in toward the body.")]
    [Range(0f, .8f)] public float armTuckSide = .4f;
    [Tooltip("Off: from outside the arms play the first-person pose as it is (for comparing).")]
    public bool naturalArms = true;

    [Header("Seen as a teammate (co-op)")]
    [Tooltip("Co-op: a copy of this body shows a teammate (CoopAvatar). With no player it follows this transform's place and yaw, bends with remotePitch, and has no first-person arms.")]
    public Transform follow;
    [Tooltip("The teammate's look up or down, degrees (down positive), as their own look sent it.")]
    public float remotePitch;

    // A second camera that renders as the owner's eyes (the capture harness's first-person-only render).
    public static Camera AlsoOwner;

    // First-person framing a clip can ask for through animator curves of these names (all 0 for every other clip):
    // the first-person arms moved (ArmsViewX, Y, Z) in the view, metres, turned (ArmsViewPitch, Yaw, Roll) degrees,
    // and drawn through ArmsViewFov degrees more than the view model's own field of view (read by PlayerViewPresentation). The converted Unreal combo
    // uses them to see the arms as the Unreal camera did (UnrealComboConverter). The body others see doesn't move.
    public static readonly string[] ViewCurves = { "ArmsViewY", "ArmsViewZ", "ArmsViewPitch", "ArmsViewFov", "ArmsViewX", "ArmsViewYaw", "ArmsViewRoll" };
    static readonly int[] ViewHashes = System.Array.ConvertAll(ViewCurves, Animator.StringToHash);
    bool hasViewCurves;
    public float ViewFovOffset { get; private set; }

    static readonly string[] ArmBones = { "Clavicle_", "Shoulder_", "Elbow_", "Hand_", "Finger", "Thumb", "IndexFinger" };

    Transform[] spine;
    Transform neck, head;
    Transform[] turned;
    Quaternion[] rest, written;
    readonly List<(Transform from, Transform to)> pose = new();
    readonly List<GameObject> cuts = new();
    readonly List<Renderer> bodyRenderers = new();
    readonly List<(Renderer renderer, Material[] shown, Material[] owner)> hidden = new();
    readonly Dictionary<Material, Material> shadowOnly = new();
    Transform bodyRight, bodyLeft, armsRight, armsLeft;
    // Per arm: upper arm, forearm, hand; and what the animator and this rig last wrote there.
    Transform[][] arms;
    Quaternion[][] armWritten, armAnimated;
    string heldSignature, bodySignature;
    bool ownerView;

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        var bones = Names(animator.transform);
        spine = new[] { bones["Spine_01"], bones["Spine_02"], bones["Spine_03"] };
        bones.TryGetValue("Neck", out neck);
        bones.TryGetValue("Head", out head);
        var list = new List<Transform>(spine);
        if (neck != null) list.Add(neck);
        if (head != null) list.Add(head);
        turned = list.ToArray();
        rest = new Quaternion[turned.Length];
        written = new Quaternion[turned.Length];
        for (int i = 0; i < turned.Length; i++) rest[i] = written[i] = turned[i].localRotation;
        var armList = new List<Transform[]>();
        foreach (var side in new[] { "_R", "_L" })
            if (bones.TryGetValue("Shoulder" + side, out var upper) && bones.TryGetValue("Elbow" + side, out var fore) && bones.TryGetValue("Hand" + side, out var hand))
                armList.Add(new[] { upper, fore, hand });
        arms = armList.ToArray();
        armWritten = new Quaternion[arms.Length][];
        armAnimated = new Quaternion[arms.Length][];
        for (int i = 0; i < arms.Length; i++) { armWritten[i] = new Quaternion[3]; armAnimated[i] = new Quaternion[3]; }
        foreach (var p in animator.parameters) if (p.nameHash == ViewHashes[0]) hasViewCurves = true;
        bones.TryGetValue("RightHand_Socket", out bodyRight);
        bones.TryGetValue("LeftHand_Socket", out bodyLeft);

        // Every bone of the camera-attached copy takes its local pose from the body's bone of the same name.
        if (firstPersonArms != null)
        {
            var copy = Names(firstPersonArms);
            copy.TryGetValue("RightHand_Socket", out armsRight);
            copy.TryGetValue("LeftHand_Socket", out armsLeft);
            foreach (var pair in copy)
                if (pair.Value != firstPersonArms && bones.TryGetValue(pair.Key, out var from) && !Held(pair.Value)) pose.Add((from, pair.Value));
        }
    }

    // Under a hand socket: a held item, not a bone.
    bool Held(Transform t) =>
        (armsRight != null && t != armsRight && t.IsChildOf(armsRight)) || (armsLeft != null && t != armsLeft && t.IsChildOf(armsLeft));

    static Dictionary<string, Transform> Names(Transform root)
    {
        var d = new Dictionary<string, Transform>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) d.TryAdd(t.name, t);
        return d;
    }

    void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += BeginCamera;
        RenderPipelineManager.endCameraRendering += EndCamera;
        RefreshArms();
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeginCamera;
        RenderPipelineManager.endCameraRendering -= EndCamera;
        SetOwnerView(false);
    }

    void OnDestroy()
    {
        foreach (var m in shadowOnly.Values) if (m != null) Destroy(m);
    }

    // ── Every frame ──────────────────────────────────────────────────

    void LateUpdate()
    {
        if (player == null && follow == null) return;
        // Under the player: position from the controller, yaw from the look. Never the camera's own motion.
        // A teammate's copy: under its avatar, which moves and turns as they do.
        var look = player != null ? player.Look : null;
        float yaw = player == null ? follow.eulerAngles.y : look != null ? look.YawTransform.eulerAngles.y : player.transform.eulerAngles.y;
        transform.SetPositionAndRotation(player != null ? player.transform.position : follow.position, Quaternion.Euler(0f, yaw, 0f));
        // A bone the animator didn't write this frame still holds last frame's turn: start it from its rest pose.
        for (int i = 0; i < turned.Length; i++)
            if (turned[i].localRotation == written[i]) turned[i].localRotation = rest[i];
        // The same for the arms: a bone still holding last frame's reach gets back the animation's pose it had then.
        for (int a = 0; a < arms.Length; a++)
            for (int b = 0; b < 3; b++)
                if (arms[a][b].localRotation == armWritten[a][b]) arms[a][b].localRotation = armAnimated[a][b];
        // The first-person arms take the animation's pose this frame, before the look turns the body.
        foreach (var (from, to) in pose) to.SetLocalPositionAndRotation(from.localPosition, from.localRotation);
        if (hasViewCurves && firstPersonArms != null)
        {
            firstPersonArms.SetLocalPositionAndRotation(new Vector3(animator.GetFloat(ViewHashes[4]), animator.GetFloat(ViewHashes[0]), animator.GetFloat(ViewHashes[1])),
                Quaternion.Euler(animator.GetFloat(ViewHashes[2]), animator.GetFloat(ViewHashes[5]), animator.GetFloat(ViewHashes[6])));
            ViewFovOffset = animator.GetFloat(ViewHashes[3]);
        }
        if (HeldSignature() != heldSignature) MirrorHeld();
        // A class or gear change (a body mesh switched on or off or swapped): the arms are cut again, at once.
        if (BodySignature() != bodySignature) RefreshArms();
        if (followLook && look != null && player.IsActive) Bend(look.PitchTransform.rotation);
        else if (followLook && player == null) Bend(Quaternion.Euler(remotePitch, yaw, 0f));
        for (int i = 0; i < turned.Length; i++) written[i] = turned[i].localRotation;
        for (int a = 0; a < arms.Length; a++)
        {
            for (int b = 0; b < 3; b++) armAnimated[a][b] = arms[a][b].localRotation;
            if (naturalArms) Reach(arms[a]);
            for (int b = 0; b < 3; b++) armWritten[a][b] = arms[a][b].localRotation;
        }
    }

    void Bend(Quaternion look)
    {
        // The look in the body's frame: x pitch (down positive), y turn, z roll (lean).
        var e = (Quaternion.Inverse(transform.rotation) * look).eulerAngles;
        float pitch = Mathf.DeltaAngle(0f, e.x), turn = Mathf.DeltaAngle(0f, e.y), roll = Mathf.DeltaAngle(0f, e.z);
        float chestPitch = Mathf.Clamp(pitch, -chestLimit, chestLimit), chestTurn = Mathf.Clamp(turn, -chestLimit, chestLimit);
        float restPitch = pitch - chestPitch, restTurn = turn - chestTurn;
        Turn(spine, chestPitch, chestTurn, roll);
        if (neck != null) Turn(new[] { neck }, restPitch * neckShare, restTurn * neckShare, 0f);
        if (head != null) Turn(new[] { head }, restPitch * (1f - neckShare), restTurn * (1f - neckShare), 0f);
    }

    // From outside, a hand held up in front of the eyes is carried lower and closer, as someone would really hold it:
    // its place around the shoulder turned down by armDrop and pulled in by armTuck (forward) and armTuckSide (out to the side), the elbow bent to reach it (a
    // two-bone solve that keeps the elbow on the side it was on), and the hand turned down with it so the blade swings
    // the same way. Timing and direction come from the clip; only where the arm is changes.
    void Reach(Transform[] arm) => Reach(arm, transform, armDrop, armTuck, armTuckSide);

    // The same for any body (the Animation Workbench's outside view): arm = upper arm, forearm, hand; body = the body's root.
    public static void Reach(Transform[] arm, Transform body, float armDrop, float armTuck, float armTuckSide)
    {
        var transform = body;
        Transform upper = arm[0], fore = arm[1], hand = arm[2];
        Vector3 s = upper.position, e = fore.position, h = hand.position;
        float a = Vector3.Distance(s, e), b = Vector3.Distance(e, h);
        if (a < 1e-4f || b < 1e-4f) return;
        var drop = Quaternion.AngleAxis(armDrop, transform.right);
        var reach = drop * (h - s);
        float forward = Vector3.Dot(reach, transform.forward);
        if (forward > 0f) reach -= transform.forward * forward * armTuck;
        var outward = transform.right * Mathf.Sign(Vector3.Dot(s - transform.position, transform.right));
        float wide = Vector3.Dot(reach, outward);
        if (wide > 0f) reach -= outward * wide * armTuckSide;
        reach = Vector3.ClampMagnitude(reach, (a + b) * .999f);
        reach = reach.normalized * Mathf.Max(reach.magnitude, Mathf.Abs(a - b) * 1.001f);
        var target = s + reach;
        var handRotation = drop * hand.rotation;

        // The elbow stays on its side of the shoulder-hand line, carried down with the hand.
        var pole = drop * (e - s);
        float d = reach.magnitude;
        float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
        var axis = reach / d;
        var side = Vector3.ProjectOnPlane(pole, axis);
        if (side.sqrMagnitude < 1e-8f) side = Vector3.ProjectOnPlane(-transform.up, axis);
        side.Normalize();
        var elbow = s + axis * (a * cosA) + side * (a * Mathf.Sqrt(1f - cosA * cosA));

        upper.rotation = Quaternion.FromToRotation(e - s, elbow - s) * upper.rotation;
        e = fore.position; h = hand.position;
        fore.rotation = Quaternion.FromToRotation(h - e, target - e) * fore.rotation;
        hand.rotation = handRotation;
    }

    // Spread a turn (in the body's frame) evenly over the bones, root to tip.
    void Turn(Transform[] bones, float pitch, float turn, float roll)
    {
        if (bones.Length == 0 || (pitch == 0f && turn == 0f && roll == 0f)) return;
        float n = bones.Length;
        var step = Quaternion.AngleAxis(turn / n, transform.up) * Quaternion.AngleAxis(pitch / n, transform.right) *
                   Quaternion.AngleAxis(roll / n, transform.forward);
        foreach (var b in bones) b.rotation = step * b.rotation;
    }

    // ── The first-person arms: a cut of the body ─────────────────────

    // Rebuild the arms cut from the body and its gear as they are now (after a class or gear change).
    public void RefreshArms()
    {
        bodySignature = BodySignature();
        foreach (var c in cuts) if (c != null) Destroy(c);
        cuts.Clear();
        if (firstPersonArms == null) return;
        var copy = Names(firstPersonArms);
        foreach (var r in animator.GetComponentsInChildren<SkinnedMeshRenderer>(false))
        {
            if (!r.enabled || r.sharedMesh == null || !r.sharedMesh.isReadable) continue;
            var mesh = ArmsOf(r);
            if (mesh == null) continue;
            var go = new GameObject(r.name + " (arms)");
            go.transform.SetParent(firstPersonArms, false);
            var cut = go.AddComponent<SkinnedMeshRenderer>();
            var bones = new Transform[r.bones.Length];
            for (int i = 0; i < bones.Length; i++) if (r.bones[i] != null) copy.TryGetValue(r.bones[i].name, out bones[i]);
            cut.bones = bones;
            cut.rootBone = r.rootBone != null && copy.TryGetValue(r.rootBone.name, out var rb) ? rb : firstPersonArms;
            cut.sharedMesh = mesh;
            cut.sharedMaterials = r.sharedMaterials;
            cut.shadowCastingMode = ShadowCastingMode.Off;
            cut.updateWhenOffscreen = true;
            cuts.Add(go);
        }
        RefreshBody();
    }

    readonly List<SkinnedMeshRenderer> skins = new();

    string BodySignature()
    {
        animator.GetComponentsInChildren(false, skins);
        var sb = new System.Text.StringBuilder();
        foreach (var r in skins) if (r.enabled && r.sharedMesh != null) sb.Append(r.sharedMesh.GetHashCode()).Append('|');
        return sb.ToString();
    }

    // The triangles of a skinned mesh that belong to the arms and hands (more than half their weight on arm bones).
    public static Mesh ArmsOf(SkinnedMeshRenderer r)
    {
        var mesh = r.sharedMesh;
        var arm = new HashSet<int>();
        for (int i = 0; i < r.bones.Length; i++)
        {
            if (r.bones[i] == null) continue;
            foreach (var a in ArmBones) if (r.bones[i].name.StartsWith(a)) { arm.Add(i); break; }
        }
        if (arm.Count == 0) return null;
        var weights = mesh.boneWeights;
        var cut = Instantiate(mesh);
        cut.name = mesh.name + " (arms)";
        bool any = false;
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var tris = mesh.GetTriangles(s);
            var keep = new List<int>(tris.Length / 4);
            for (int t = 0; t < tris.Length; t += 3)
            {
                float w = 0f;
                for (int k = 0; k < 3; k++) w += Weight(weights[tris[t + k]], arm);
                if (w / 3f > .5f) { keep.Add(tris[t]); keep.Add(tris[t + 1]); keep.Add(tris[t + 2]); }
            }
            cut.SetTriangles(keep, s);
            any |= keep.Count > 0;
        }
        if (any) return cut;
        if (Application.isPlaying) Destroy(cut); else DestroyImmediate(cut);
        return null;
    }

    static float Weight(BoneWeight w, HashSet<int> set) =>
        (set.Contains(w.boneIndex0) ? w.weight0 : 0f) + (set.Contains(w.boneIndex1) ? w.weight1 : 0f) +
        (set.Contains(w.boneIndex2) ? w.weight2 : 0f) + (set.Contains(w.boneIndex3) ? w.weight3 : 0f);

    // ── Held items: the body holds copies ────────────────────────────

    string HeldSignature()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var socket in new[] { armsRight, armsLeft })
        {
            if (socket == null) continue;
            foreach (Transform child in socket) sb.Append(child.GetHashCode()).Append(child.gameObject.activeSelf ? '+' : '-').Append('|');
            sb.Append('/');
        }
        return sb.ToString();
    }

    void MirrorHeld()
    {
        heldSignature = HeldSignature();
        // The body's copies cast the held items' shadows; the first-person ones (at the camera, enlarged) must not.
        if (firstPersonArms != null)
            foreach (var r in firstPersonArms.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
        foreach (var (from, to) in new[] { (armsRight, bodyRight), (armsLeft, bodyLeft) })
        {
            if (from == null || to == null) continue;
            for (int i = to.childCount - 1; i >= 0; i--) Destroy(to.GetChild(i).gameObject);
            foreach (Transform item in from)
            {
                if (!item.gameObject.activeSelf) continue;
                var holder = new GameObject("held copy holder");
                holder.SetActive(false);
                var copy = Instantiate(item.gameObject, holder.transform);
                copy.name = item.name + " (body)";
                Strip(copy);
                foreach (var t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
                copy.transform.SetParent(to, false);
                copy.transform.SetLocalPositionAndRotation(item.localPosition, item.localRotation);
                copy.transform.localScale = item.localScale;
                Destroy(holder);
            }
        }
        RefreshBody();
    }

    static readonly System.Type[] Effects = { typeof(ParticleSystem), typeof(Light), typeof(TrailRenderer), typeof(LineRenderer) };

    // Keep only transforms and meshes: no hitbox, scripts, lights or effects on the copy.
    static void Strip(GameObject root)
    {
        foreach (var type in Effects)
            foreach (var c in root.GetComponentsInChildren(type, true))
                if (c != null && c.gameObject != root && c.GetComponentsInChildren<MeshFilter>(true).Length == 0)
                    DestroyImmediate(c.gameObject);
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            var components = t.GetComponents<Component>();
            for (int i = components.Length - 1; i >= 0; i--)
            {
                var c = components[i];
                if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer || c is SkinnedMeshRenderer) continue;
                DestroyImmediate(c);
            }
        }
    }

    // ── Per camera: the owner sees the arms, everyone else the body ──

    void RefreshBody()
    {
        SetOwnerView(false);
        hidden.Clear();
        animator.GetComponentsInChildren(true, bodyRenderers);
        foreach (var r in bodyRenderers)
        {
            r.renderingLayerMask = FirstPersonLighting.WorldLayer;
            hidden.Add((r, r.sharedMaterials, Shadow(r.sharedMaterials)));
        }
    }

    Material[] Shadow(Material[] materials)
    {
        var copy = new Material[materials.Length];
        for (int i = 0; i < materials.Length; i++) copy[i] = ShadowOf(materials[i]);
        return copy;
    }

    // The same material drawing only its shadow (every other pass off).
    static readonly string[] DrawPasses = { "UniversalForward", "UniversalForwardOnly", "SRPDefaultUnlit", "DepthOnly", "DepthNormals",
        "DepthNormalsOnly", "UniversalGBuffer", "MotionVectors", "Meta" };

    Material ShadowOf(Material m)
    {
        if (m == null) return null;
        if (shadowOnly.TryGetValue(m, out var s)) return s;
        s = new Material(m) { name = m.name + " (shadow only)" };
        foreach (var pass in DrawPasses) s.SetShaderPassEnabled(pass, false);
        shadowOnly[m] = s;
        return s;
    }

    void BeginCamera(ScriptableRenderContext context, Camera cam)
    {
        bool owner = player != null && player.IsActive && PlayerManager.HasInstance &&
                     (cam == PlayerManager.Instance.OutputCamera || (AlsoOwner != null && cam == AlsoOwner));
        SetOwnerView(owner);
    }

    void EndCamera(ScriptableRenderContext context, Camera cam) => SetOwnerView(false);

    void SetOwnerView(bool owner)
    {
        if (owner == ownerView) return;
        ownerView = owner;
        foreach (var (r, shown, own) in hidden) if (r != null) r.sharedMaterials = owner ? own : shown;
    }
}
