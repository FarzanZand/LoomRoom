using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

// Barony's skeleton deaths: the body bursts into its own bones. At death the skinned mesh is baked and split by the
// bone each triangle follows (head, chest, hips, upper and lower arms and legs), each part becomes a small physics
// piece thrown out from the killing blow, and held or worn props (weapon, helmet) come loose too. The pieces stay on
// the floor, frozen once they settle. Replaces EnemyRagdoll on this character.
[RequireComponent(typeof(Character))]
public class DeathBurst : MonoBehaviour
{
    [Tooltip("How hard the pieces fly away from the killing blow.")]
    [SerializeField, Min(0)] float force = 2.2f;
    [SerializeField, Min(0)] float upward = 2f;
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

    void Awake() => character = GetComponent<Character>();
    void OnEnable() { character.Damaged += Remember; character.Died += OnDied; }
    void OnDisable() { character.Damaged -= Remember; character.Died -= OnDied; }
    void Remember(DamageInfo info) => lastHit = info;

    void OnDied()
    {
        if (HasBurst) return;
        HasBurst = true;
        var anim = character.Animator;
        var major = MajorBones(anim);
        Transform parent = transform.parent;

        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!smr.enabled || smr.sharedMesh == null) continue;
            SplitSkinned(smr, major, parent);
            smr.enabled = false;
        }
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

    void SplitSkinned(SkinnedMeshRenderer smr, HashSet<Transform> major, Transform parent)
    {
        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        var source = smr.sharedMesh;
        var weights = source.boneWeights;
        var bones = smr.bones;
        Vector3[] positions = baked.vertices, normals = baked.normals;
        // From the baked copy: the source mesh (Synty FBX) is not readable, so its UVs come back empty.
        Vector2[] uvs = baked.uv;
        var toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);

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
        for (int sub = 0; sub < source.subMeshCount; sub++)
        {
            var tris = baked.GetTriangles(sub);
            for (int i = 0; i < tris.Length; i += 3)
            {
                var g = GroupFor(tris[i]);
                if (!groups.TryGetValue(g, out var lists)) groups[g] = lists = new List<int>[source.subMeshCount];
                (lists[sub] ??= new List<int>()).AddRange(new[] { tris[i], tris[i + 1], tris[i + 2] });
            }
        }

        var materials = smr.sharedMaterials;
        foreach (var pair in groups)
        {
            var remap = new Dictionary<int, int>();
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uv = new List<Vector2>();
            var subTris = new List<List<int>>(); var mats = new List<Material>();
            Vector3 centre = Vector3.zero;
            for (int sub = 0; sub < pair.Value.Length; sub++)
            {
                if (pair.Value[sub] == null) continue;
                var list = new List<int>(pair.Value[sub].Count);
                foreach (int v in pair.Value[sub])
                {
                    if (!remap.TryGetValue(v, out int n))
                    {
                        n = remap[v] = verts.Count;
                        Vector3 w = toWorld.MultiplyPoint3x4(positions[v]);
                        verts.Add(w); centre += w;
                        norms.Add(v < normals.Length ? toWorld.MultiplyVector(normals[v]) : Vector3.up);
                        uv.Add(v < uvs.Length ? uvs[v] : Vector2.zero);
                    }
                    list.Add(n);
                }
                subTris.Add(list); mats.Add(sub < materials.Length ? materials[sub] : materials[materials.Length - 1]);
            }
            if (verts.Count == 0) continue;
            centre /= verts.Count;
            for (int i = 0; i < verts.Count; i++) verts[i] -= centre;

            var mesh = new Mesh { name = smr.sharedMesh.name + " piece", indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uv);
            mesh.subMeshCount = subTris.Count;
            for (int s = 0; s < subTris.Count; s++) mesh.SetTriangles(subTris[s], s);
            mesh.RecalculateBounds();

            var go = new GameObject(name + " - " + pair.Key.name);
            go.layer = smr.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.position = centre;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<OwnedMesh>().mesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats.ToArray();
            mr.renderingLayerMask = smr.renderingLayerMask;
            mr.shadowCastingMode = smr.shadowCastingMode;
            var box = go.AddComponent<BoxCollider>();
            box.center = mesh.bounds.center; box.size = Vector3.Max(mesh.bounds.size, Vector3.one * .04f);
            Throw(go.AddComponent<Rigidbody>(), centre);
        }
        Destroy(baked);
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
