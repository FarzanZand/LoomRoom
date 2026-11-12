using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

// Skeleton deaths: the body bursts into its own bones. At death the skinned mesh is baked and split by the
// bone each triangle follows (head, chest, hips, upper and lower arms and legs), each part becomes a small physics
// piece thrown out from the killing blow, and held or worn props (weapon, helmet) come loose too. The pieces stay on
// the floor, frozen once they settle. Replaces EnemyRagdoll on this character.
// The burst happens at the end of the death frame, from the pose that was on screen: the Animator is frozen at once
// (so the death trigger never plays) and the mesh is baked in a late LateUpdate, after every procedural bone tweak.
[RequireComponent(typeof(Character)), DefaultExecutionOrder(10000)]
public class DeathBurst : MonoBehaviour
{
    [Tooltip("How hard the pieces fly away from the killing blow.")]
    [SerializeField, Min(0)] float force = 2.2f;
    [Tooltip("Upward speed. Kept small: the bones should fall apart where they stood, not hop.")]
    [SerializeField, Min(0)] float upward = .5f;
    [Tooltip("Random spread, so the pieces scatter instead of flying off together.")]
    [SerializeField, Min(0)] float scatter = 1.2f;
    [Tooltip("Random spin, radians per second.")]
    [SerializeField, Min(0)] float spin = 9f;
    [SerializeField, Min(.01f)] float pieceMass = .4f;
    [Tooltip("Seconds before the pieces freeze where they lie (saves physics time).")]
    [SerializeField, Min(.5f)] float settleSeconds = 4f;

    Character character;
    DamageInfo lastHit;
    readonly List<Rigidbody> pieces = new();
    public bool HasBurst { get; private set; }
    // Seconds between the death and the burst, for a last moment someone else plays out (a boss's defeat pose,
    // BossStageEncounter): the Animator keeps running meanwhile. 0: the burst comes at the end of the death frame.
    public float HoldSeconds { get; set; }
    float burstAt;

    void Awake() => character = GetComponent<Character>();

    // The first body of each kind on a floor starts working out its split as it appears, a little each frame, so even
    // the first kill of that kind in a run needs no working out (claimed per mesh, so only one body of a kind does it).
    void Start()
    {
        var smr = GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr == null || smr.sharedMesh == null) return;
        var key = (smr.sharedMesh, character.Animator != null ? character.Animator.avatar : null);
        if (!worked.ContainsKey(key) && claimed.Add(key) && plans == null && planning == null) planning = MakePlans();
    }
    void OnEnable() { character.Damaged += Remember; character.Died += OnDied; }
    void OnDisable() { character.Damaged -= Remember; character.Died -= OnDied; }
    void Remember(DamageInfo info)
    {
        lastHit = info;
        // The first blow starts working out the split (if no body of its kind has yet) a little each frame (PlanAheadMilliseconds), so the kill a few blows
        // later only bakes the pose and fills the pieces: worked out in the death frame, a skeleton's burst took 23 ms in
        // one frame on the 4090 (2026-11-12, the user's stutter in fights).
        if (!HasBurst && !pending && plans == null && planning == null) planning = MakePlans();
    }

    bool pending;

    void OnDied()
    {
        if (HasBurst || pending) return;
        pending = true;
        burstAt = Time.time + HoldSeconds;
        // Hold the current pose: with the Animator running, the Dead flag and death trigger would move it first.
        if (HoldSeconds <= 0f && character.Animator != null) character.Animator.enabled = false;
        else planning ??= plans == null ? MakePlans() : null;
    }

    void LateUpdate()
    {
        if (!pending) { StepPlans(PlanAheadMilliseconds); return; }
        if (Time.time < burstAt) { StepPlans(PlanMillisecondsPerFrame); return; }
        pending = false;
        if (character.Animator != null) character.Animator.enabled = false;
        Burst();
    }

    void Burst()
    {
        HasBurst = true;
        var anim = character.Animator;
        Transform parent = transform.parent;

        // Whatever is left of the plan (all of it when the burst comes in the death frame).
        planning ??= plans == null ? MakePlans() : null;
        while (planning != null && planning.MoveNext()) { }
        planning = null;
        foreach (var plan in plans)
        {
            if (plan.smr == null || !plan.smr.enabled) continue;
            SplitSkinned(plan, parent);
            plan.smr.enabled = false;
        }
        plans = null;
        // Weapons, helmets and other rigid props come off whole. World-space UI and text
        // (health bars, labels) are not props and stay with the body.
        foreach (var mr in GetComponentsInChildren<MeshRenderer>())
        {
            if (!mr.enabled || !mr.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null) continue;
            if (mr.GetComponentInParent<Canvas>() != null || mr.GetComponent<TMP_Text>() != null) continue;
            var t = mr.transform;
            t.SetParent(parent, true);
            foreach (var c in t.GetComponentsInChildren<Collider>()) c.enabled = false;
            var box = t.gameObject.AddComponent<BoxCollider>();
            var bounds = filter.sharedMesh.bounds;
            box.center = bounds.center; box.size = Vector3.Max(bounds.size, Vector3.one * .03f);
            Throw(t.gameObject.AddComponent<Rigidbody>(), t.position);
        }

        DeathPhysics.StopPosing(this, anim);
        // The body is gone: nothing left to bump into.
        foreach (var c in GetComponents<Collider>()) if (!c.isTrigger) c.enabled = false;
        DeathPhysics.IgnorePlayers(pieces);
        StartCoroutine(DeathPhysics.Settle(pieces, settleSeconds));
        // Kept for Raise the Fallen: each piece with the bone it came from (props have none).
        // A boss (steadfast, and no elite) is never raised as itself, nor an event's own actor (it would stand still).
        bool boss = TryGetComponent<EnemyMotor>(out var motor) && motor.Steadfast && !TryGetComponent<EliteEnemy>(out _) || TryGetComponent<EventActor>(out _);
        var heap = new BoneHeap { body = name, parent = parent, kind = boss ? null : character.data as EnemyData };
        string prefix = name + " - ";
        foreach (var rb in pieces)
            if (rb != null) heap.pieces.Add(new BoneHeap.Piece { transform = rb.transform, bone = rb.name.StartsWith(prefix) ? rb.name.Substring(prefix.Length) : null });
        BoneHeap.Add(heap);
    }

    // Each skinned bone is grouped under the nearest of these humanoid bones; unmapped bones go with the hips.
    static HashSet<Transform> MajorBones(Animator anim)
    {
        var set = new HashSet<Transform>();
        if (anim == null || !anim.isHuman) return set;
        foreach (var b in new[] { HumanBodyBones.Hips, HumanBodyBones.Chest, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg })
        {
            var t = anim.GetBoneTransform(b);
            if (b == HumanBodyBones.Chest && t == null) t = anim.GetBoneTransform(HumanBodyBones.Spine);
            if (t != null) set.Add(t);
        }
        return set;
    }

    // ── The split, worked out ahead ──
    // What doesn't depend on the pose (which bone group each triangle goes to, each group's vertices in its own
    // numbering, its triangles and UVs) is worked out a few milliseconds a frame while a boss's defeat pose plays
    // (HoldSeconds), so the burst itself only bakes the pose and fills the pieces: the Crypt Lord's burst took 156 ms in
    // one frame (2026-11-12). With no hold it is all done in the death frame, as before. The pieces come out the same.
    const float PlanMillisecondsPerFrame = 3f;
    const float PlanAheadMilliseconds = .5f;   // while it still stands: a frame may hold several hurt enemies
    const int PlanStep = 4096;   // triangles or vertices between checks of the frame's time
    // A group only names its bone and its submeshes, so one worked out for a mesh serves every body wearing it (each kind
    // of skeleton is worked out once a session, not once a kill: it made about 2 MB of garbage a kill, 2026-11-12).
    sealed class Group
    {
        public string bone;
        public int[] vertices;               // the baked mesh's vertex for each of the piece's
        public Vector2[] uvs;
        public readonly List<int[]> triangles = new();   // per submesh the piece has, in the piece's numbering
        public readonly List<int> submeshes = new();     // the source submesh of each, for its material
    }
    sealed class Plan { public SkinnedMeshRenderer smr; public List<Group> groups = new(); }
    static readonly Dictionary<(Mesh, Avatar), List<Group>> worked = new();
    static readonly HashSet<(Mesh, Avatar)> claimed = new();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { worked.Clear(); claimed.Clear(); }
    List<Plan> plans;
    IEnumerator<bool> planning;
    readonly System.Diagnostics.Stopwatch planWatch = new();

    void StepPlans(float milliseconds)
    {
        if (planning == null) return;
        planWatch.Restart();
        while (planWatch.Elapsed.TotalMilliseconds < milliseconds)
            if (!planning.MoveNext()) { planning = null; return; }
    }

    IEnumerator<bool> MakePlans()
    {
        plans = new List<Plan>();
        var major = MajorBones(character.Animator);
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!smr.enabled || smr.sharedMesh == null) continue;
            var plan = new Plan { smr = smr };
            plans.Add(plan);
            var key = (smr.sharedMesh, character.Animator != null ? character.Animator.avatar : null);
            if (worked.TryGetValue(key, out var known)) { plan.groups = known; continue; }
            var baked = new Mesh();
            // The pose doesn't matter here: only the UVs and the triangles are taken from this bake. From a bake because
            // the source mesh (Synty FBX) is not readable, so its UVs come back empty.
            smr.BakeMesh(baked, false);
            var source = smr.sharedMesh;
            var weights = source.boneWeights;
            var bones = smr.bones;
            Vector2[] uvs = baked.uv;
            int subCount = source.subMeshCount;
            var tris = new int[subCount][];
            for (int sub = 0; sub < subCount; sub++) tris[sub] = baked.GetTriangles(sub);
            Destroy(baked);
            yield return true;

            // Group of each skinned bone: its nearest major ancestor (or itself).
            var groupOf = new Transform[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                var t = bones[i];
                while (t != null && major.Count > 0 && !major.Contains(t) && t != transform) t = t.parent;
                groupOf[i] = t != null && major.Contains(t) ? t : smr.rootBone != null ? smr.rootBone : smr.transform;
            }
            Transform GroupFor(int vertex)
            {
                if (weights.Length == 0 || bones.Length == 0) return smr.transform;
                int b = weights[vertex].boneIndex0;
                return b >= 0 && b < groupOf.Length && groupOf[b] != null ? groupOf[b] : smr.transform;
            }

            // triangles per group, per submesh
            var groups = new Dictionary<Transform, List<int>[]>();
            var order = new List<Transform>();   // groups in the order first met, as the pieces were made before
            for (int sub = 0; sub < subCount; sub++)
            {
                var subTris = tris[sub];
                for (int i = 0; i < subTris.Length; i += 3)
                {
                    var g = GroupFor(subTris[i]);
                    if (!groups.TryGetValue(g, out var lists)) { groups[g] = lists = new List<int>[subCount]; order.Add(g); }
                    var list = lists[sub] ??= new List<int>();
                    list.Add(subTris[i]); list.Add(subTris[i + 1]); list.Add(subTris[i + 2]);
                    if (i % PlanStep == 0) yield return true;
                }
            }

            var remap = new Dictionary<int, int>();
            foreach (var bone in order)
            {
                var lists = groups[bone];
                remap.Clear();
                var vertices = new List<int>(); var uv = new List<Vector2>();
                var group = new Group { bone = bone.name };
                for (int sub = 0; sub < lists.Length; sub++)
                {
                    if (lists[sub] == null) continue;
                    var subList = lists[sub];
                    var local = new int[subList.Count];
                    for (int k = 0; k < subList.Count; k++)
                    {
                        int v = subList[k];
                        if (!remap.TryGetValue(v, out int n))
                        {
                            n = remap[v] = vertices.Count;
                            vertices.Add(v);
                            uv.Add(v < uvs.Length ? uvs[v] : Vector2.zero);
                        }
                        local[k] = n;
                        if (k % PlanStep == 0) yield return true;
                    }
                    group.triangles.Add(local);
                    group.submeshes.Add(sub);
                }
                if (vertices.Count == 0) continue;
                group.vertices = vertices.ToArray(); group.uvs = uv.ToArray();
                plan.groups.Add(group);
                yield return true;
            }
            worked[key] = plan.groups;
        }
    }

    void SplitSkinned(Plan plan, Transform parent)
    {
        var smr = plan.smr;
        var baked = new Mesh();
        // Without scale: the baked vertices are then in the renderer's unscaled space, which toWorld below expects.
        // Baking with scale stretched the pieces (taller body, lifted arms) on characters with a scaled rig.
        smr.BakeMesh(baked, false);
        Vector3[] positions = baked.vertices, normals = baked.normals;
        Destroy(baked);
        var toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);

        foreach (var group in plan.groups)
        {
            int count = group.vertices.Length;
            var verts = new Vector3[count]; var norms = new Vector3[count];
            Vector3 centre = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                int v = group.vertices[i];
                Vector3 w = toWorld.MultiplyPoint3x4(positions[v]);
                verts[i] = w; centre += w;
                norms[i] = v < normals.Length ? toWorld.MultiplyVector(normals[v]) : Vector3.up;
            }
            centre /= count;
            for (int i = 0; i < count; i++) verts[i] -= centre;

            var mesh = new Mesh { name = smr.sharedMesh.name + " piece", indexFormat = count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, group.uvs);
            mesh.subMeshCount = group.triangles.Count;
            for (int s = 0; s < group.triangles.Count; s++) mesh.SetTriangles(group.triangles[s], s, false);
            mesh.RecalculateBounds();
            var materials = smr.sharedMaterials;
            var mats = new Material[group.submeshes.Count];
            for (int s = 0; s < mats.Length; s++) { int sub = group.submeshes[s]; mats[s] = materials[sub < materials.Length ? sub : materials.Length - 1]; }

            var go = new GameObject(name + " - " + group.bone);
            go.layer = smr.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.position = centre;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<OwnedMesh>().mesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.renderingLayerMask = smr.renderingLayerMask;
            mr.shadowCastingMode = smr.shadowCastingMode;
            var box = go.AddComponent<BoxCollider>();
            box.center = mesh.bounds.center; box.size = Vector3.Max(mesh.bounds.size, Vector3.one * .04f);
            Throw(go.AddComponent<Rigidbody>(), centre);
        }
    }

    void Throw(Rigidbody rb, Vector3 at)
    {
        rb.mass = pieceMass;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        Vector3 dir = DeathPhysics.HitDirection(lastHit, transform).normalized;
        Vector3 outward = at - transform.position; outward.y = 0f;
        float heavy = lastHit.Heavy ? 1.4f : 1f;
        rb.linearVelocity = (dir * force + outward.normalized * force * .4f) * heavy + Vector3.up * upward + Random.insideUnitSphere * scatter;
        rb.angularVelocity = Random.insideUnitSphere * spin;
        pieces.Add(rb);
    }

}
