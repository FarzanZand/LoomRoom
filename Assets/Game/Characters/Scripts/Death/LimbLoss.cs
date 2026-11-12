using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// A limb that comes off (the Black Knight in the Arena: his left arm, on the third blow the player lands). The skinned
// mesh is baked in the pose on screen and the triangles that follow the limb's bones become one physics piece thrown
// off to its side; on the body those bones are shrunk to nothing at the joint, so the limb is gone from the mesh and a
// stump is left. Whatever the limb held (a shield in the hand) falls with it. Blood spurts from the stump for a moment
// and he says his line, a bark, so the fight goes on. Works on any humanoid; after it the death (DeathBurst or the
// ragdoll) takes the body as it is.
[RequireComponent(typeof(Character)), DefaultExecutionOrder(9000)]
public class LimbLoss : MonoBehaviour
{
    [Tooltip("The limb that comes off, from this bone down (its children with it).")]
    public HumanBodyBones limb = HumanBodyBones.LeftUpperArm;
    [Min(1), Tooltip("Blows the player lands (doing damage, not blocked) before it comes off.")]
    public int hitsToLose = 3;
    [Tooltip("His words (Dialogue/Conversations): its Limb Lost Line is said as the limb comes off, as a bark (the fight goes on), where its placement says (in the dialogue box with his portrait, Standard or a corner; Simple: a subtitle), under its name. Empty: nothing is said.")]
    public BossDialogueData words;
    [Min(0), Tooltip("Seconds after the cut before the line, so the arm is seen to fall first.")]
    public float lineDelay = .5f;
    [Min(0), Tooltip("Seconds he stands stunned by it (his swing dropped). 0: he fights on at once.")]
    public float stunSeconds = 1.1f;

    [Header("The piece")]
    [Min(0), Tooltip("Metres a second the limb is thrown out to its side.")] public float throwSpeed = 2.4f;
    [Min(0)] public float throwUp = 2f;
    [Min(0), Tooltip("Random spin, radians a second.")] public float spin = 7f;
    [Min(.01f)] public float pieceMass = 2f;
    [Min(.5f), Tooltip("Seconds before the piece freezes where it lies.")] public float settleSeconds = 5f;

    [Header("Blood")]
    [Min(0), Tooltip("Drops in the first spurt (times the character's gore amount).")] public int firstSpurt = 140;
    [Min(0), Tooltip("Drops in each spurt after it.")] public int spurt = 40;
    [Min(0)] public int spurts = 6;
    [Min(.02f)] public float spurtGap = .16f;

    Character character;
    int hits;
    bool lost;
    readonly List<Transform> shrunk = new();
    public bool Lost => lost;

    void Awake() => character = GetComponent<Character>();
    void OnEnable() => character.Damaged += OnDamaged;
    void OnDisable() => character.Damaged -= OnDamaged;

    void OnDamaged(DamageInfo info)
    {
        if (lost || info.Source is not Player || info.Blocked || info.Amount <= 0f || !character.IsAlive) return;
        if (++hits >= hitsToLose) StartCoroutine(Cut(info));
    }

    // Kept shrunk after the animation and every bone tweak of the frame.
    void LateUpdate()
    {
        foreach (var b in shrunk) if (b != null) b.localScale = Vector3.one * .0001f;
    }

    IEnumerator Cut(DamageInfo info)
    {
        lost = true;
        var anim = character.Animator;
        var root = anim != null && anim.isHuman ? anim.GetBoneTransform(limb) : null;
        if (root == null) yield break;
        // Wait for the end of the frame: the pose the blow landed in, after every bone tweak.
        yield return new WaitForEndOfFrame();
        if (this == null || !character.IsAlive) yield break;

        var bones = new HashSet<Transform>(root.GetComponentsInChildren<Transform>(true));
        Vector3 stump = root.position;
        Vector3 side = (root.position - transform.position); side.y = 0f;
        side = side.sqrMagnitude > .001f ? side.normalized : -transform.right;
        var pieces = new List<Rigidbody>();

        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>())
            if (smr.enabled && smr.sharedMesh != null && Piece(smr, bones) is Rigidbody rb) pieces.Add(rb);
        // What the limb holds comes off whole (the shield), not its skinned parts.
        foreach (var mr in root.GetComponentsInChildren<MeshRenderer>())
        {
            if (!mr.enabled || !mr.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null) continue;
            var t = mr.transform;
            t.SetParent(transform.parent, true);
            foreach (var c in t.GetComponentsInChildren<Collider>()) c.enabled = false;
            var box = t.gameObject.AddComponent<BoxCollider>();
            var b = filter.sharedMesh.bounds;
            box.center = b.center; box.size = Vector3.Max(b.size, Vector3.one * .03f);
            pieces.Add(Throw(t.gameObject.AddComponent<Rigidbody>(), side));
        }
        shrunk.Add(root);
        root.localScale = Vector3.one * .0001f;
        DeathPhysics.IgnorePlayers(pieces);
        StartCoroutine(DeathPhysics.Settle(pieces, settleSeconds));
        if (stunSeconds > 0f && TryGetComponent<EnemyBrain>(out var brain)) brain.Stun(stunSeconds);
        StartCoroutine(Bleed(root, side));

        if (lineDelay > 0f) yield return new WaitForSeconds(lineDelay);
        if (words != null && !string.IsNullOrWhiteSpace(words.limbLostLine) && character.IsAlive && PortraitDialogueUI.Current != null)
            PortraitDialogueUI.Current.Bark(words.limbLostLine, character, !string.IsNullOrWhiteSpace(words.bossName) ? words.bossName : character.TheNameCap, 1.6f, words.placement, true);
    }

    // Spurts from the stump (the shrunk bone stays at the shoulder joint), out to the side and up.
    IEnumerator Bleed(Transform at, Vector3 side)
    {
        var gore = character.data != null ? character.data.gore : null;
        if (gore == null || !GoreManager.HasInstance) yield break;
        for (int i = 0; i <= spurts && at != null; i++)
        {
            int drops = i == 0 ? firstSpurt : spurt;
            GoreManager.Instance.SprayAt(gore, at.position, side + Vector3.up * .6f + Random.insideUnitSphere * .25f, drops, i == 0 ? 1.3f : .8f);
            yield return new WaitForSeconds(spurtGap * (1f + i * .25f));
        }
    }

    // The triangles of this renderer that follow the limb's bones, as one piece where they are on screen.
    Rigidbody Piece(SkinnedMeshRenderer smr, HashSet<Transform> bones)
    {
        var source = smr.sharedMesh;
        var weights = source.boneWeights;
        var smrBones = smr.bones;
        if (weights.Length == 0 || smrBones.Length == 0) return null;
        var inLimb = new bool[smrBones.Length];
        bool any = false;
        for (int i = 0; i < smrBones.Length; i++) if (smrBones[i] != null && bones.Contains(smrBones[i])) inLimb[i] = any = true;
        if (!any) return null;
        bool Takes(int v) { int b = weights[v].boneIndex0; return b >= 0 && b < inLimb.Length && inLimb[b]; }

        var baked = new Mesh();
        smr.BakeMesh(baked, false);
        Vector3[] positions = baked.vertices, normals = baked.normals;
        Vector2[] uvs = baked.uv;
        var toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
        var remap = new Dictionary<int, int>();
        var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uv = new List<Vector2>();
        var subs = new List<int[]>(); var mats = new List<Material>();
        var materials = smr.sharedMaterials;
        for (int sub = 0; sub < source.subMeshCount; sub++)
        {
            var tris = baked.GetTriangles(sub);
            var kept = new List<int>();
            for (int i = 0; i < tris.Length; i += 3)
            {
                // A triangle goes with the limb when most of its corners follow the limb.
                int n = (Takes(tris[i]) ? 1 : 0) + (Takes(tris[i + 1]) ? 1 : 0) + (Takes(tris[i + 2]) ? 1 : 0);
                if (n < 2) continue;
                for (int k = 0; k < 3; k++)
                {
                    int v = tris[i + k];
                    if (!remap.TryGetValue(v, out int m))
                    {
                        m = remap[v] = verts.Count;
                        verts.Add(toWorld.MultiplyPoint3x4(positions[v]));
                        norms.Add(v < normals.Length ? toWorld.MultiplyVector(normals[v]) : Vector3.up);
                        uv.Add(v < uvs.Length ? uvs[v] : Vector2.zero);
                    }
                    kept.Add(m);
                }
            }
            if (kept.Count == 0) continue;
            subs.Add(kept.ToArray());
            mats.Add(sub < materials.Length ? materials[sub] : materials[materials.Length - 1]);
        }
        Destroy(baked);
        if (verts.Count == 0) return null;
        Vector3 centre = Vector3.zero;
        foreach (var v in verts) centre += v;
        centre /= verts.Count;
        for (int i = 0; i < verts.Count; i++) verts[i] -= centre;

        var mesh = new Mesh { name = source.name + " limb", indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uv);
        mesh.subMeshCount = subs.Count;
        for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s, false);
        mesh.RecalculateBounds();

        var go = new GameObject(name + " - " + limb);
        go.layer = smr.gameObject.layer;
        go.transform.SetParent(transform.parent, false);
        go.transform.position = centre;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<OwnedMesh>().mesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = mats.ToArray();
        mr.renderingLayerMask = smr.renderingLayerMask;
        mr.shadowCastingMode = smr.shadowCastingMode;
        var box = go.AddComponent<BoxCollider>();
        box.center = mesh.bounds.center; box.size = Vector3.Max(mesh.bounds.size, Vector3.one * .04f);
        var side = centre - transform.position; side.y = 0f;
        return Throw(go.AddComponent<Rigidbody>(), side.sqrMagnitude > .001f ? side.normalized : -transform.right);
    }

    Rigidbody Throw(Rigidbody rb, Vector3 side)
    {
        rb.mass = pieceMass;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.linearVelocity = side * throwSpeed + Vector3.up * throwUp + Random.insideUnitSphere * .4f;
        rb.angularVelocity = Random.insideUnitSphere * spin;
        return rb;
    }
}
