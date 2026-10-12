using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Worn gear cut out of a Synty outfit: the character's restyled skinned mesh is posed with its arms down, and only
// the triangles whose vertices are weighted to the slot's bones are kept (near a joint, only part of a bone:
// the top of the thighs for body armour, the wrist for gloves, the lower shin for boots).
public static partial class ItemIconRenderer
{
    // A posed outfit: world-space vertices, each vertex's main bone, joint positions.
    class Posed
    {
        public Vector3[] vertices; public Vector2[] uv; public int[][] triangles;
        public string[] bone; public Dictionary<string, Vector3> joint = new();
        public Material[] materials;
    }

    static readonly string[] Torso = { "Hips", "Spine_01", "Spine_02", "Spine_03", "Clavicle_L", "Clavicle_R", "Shoulder_L", "Shoulder_R" };
    static readonly string[] FingerBones = { "Thumb_01", "Thumb_02", "Thumb_03", "IndexFinger_01", "IndexFinger_02", "IndexFinger_03", "IndexFinger_04", "Finger_01", "Finger_02", "Finger_03", "Finger_04" };
    // Synty names the right hand's finger bones like the left's with " 1" after them.
    static IEnumerable<string> Fingers(string side) { foreach (var f in FingerBones) yield return side == "_L" ? f : f + " 1"; }

    static bool BuildCut(Session session, Job job, GameObject root)
    {
        var posed = Pose(session, job.outfit);
        if (posed == null) return false;
        var J = posed.joint;
        var pieces = new List<GameObject>();
        switch (job.cut)
        {
            case Cut.Body:
                pieces.Add(Piece(session, posed, Bones(posed, Torso) | Near(posed, "UpperLeg_L", "UpperLeg_L", "LowerLeg_L", .25f)
                    | Near(posed, "UpperLeg_R", "UpperLeg_R", "LowerLeg_R", .25f) | Near(posed, "Neck", "Neck", "Head", .3f), Vector3.zero, Quaternion.identity, Vector3.zero));
                break;
            case Cut.Gloves:
                // Each hand turned fingers up, leaning out a little, side by side.
                float x = 0;
                foreach (var side in new[] { "_R", "_L" })
                {
                    var wrist = J["Hand" + side];
                    var turn = Quaternion.Euler(0, 0, side == "_R" ? 12 : -12) * Quaternion.FromToRotation(wrist - J["Elbow" + side], Vector3.up);
                    pieces.Add(Piece(session, posed, Bones(posed, Fingers(side)) | Near(posed, "Hand" + side, "Hand" + side, "Elbow" + side, .12f)
                        | Near(posed, "Elbow" + side, "Hand" + side, "Elbow" + side, .12f), new Vector3(x, 0, 0), turn, wrist));
                    x += .2f;
                }
                break;
            case Cut.Boots:
                x = 0;
                foreach (var side in new[] { "_R", "_L" })
                {
                    pieces.Add(Piece(session, posed, Bones(posed, new[] { "Ankle" + side, "Ball" + side, "Toes" + side })
                        | Near(posed, "LowerLeg" + side, "Ankle" + side, "LowerLeg" + side, .55f), new Vector3(x, 0, 0), Quaternion.identity, J["Ankle" + side]));
                    x += .2f;
                }
                break;
            case Cut.Legs:
                // Both legs as they stand, joined at the crotch, down to just above the ankle.
                var hips = J["Hips"];
                var below = new Keep(posed.vertices.Length);
                for (int i = 0; i < posed.vertices.Length; i++) below.on[i] = posed.bone[i] == "Hips" && posed.vertices[i].y < hips.y - .03f;
                pieces.Add(Piece(session, posed, Bones(posed, new[] { "UpperLeg_L", "UpperLeg_R" }) | below
                    | Near(posed, "LowerLeg_L", "LowerLeg_L", "Ankle_L", .85f) | Near(posed, "LowerLeg_R", "LowerLeg_R", "Ankle_R", .85f), Vector3.zero, Quaternion.identity, Vector3.zero));
                break;
        }
        pieces.RemoveAll(p => p == null);
        if (pieces.Count == 0) return false;
        foreach (var p in pieces) p.transform.SetParent(root.transform, false);
        var points = Points(root);
        if (points.Count == 0) return false;
        var centre = BoundsOf(points).center;
        foreach (var p in pieces) p.transform.localPosition = -centre;
        return true;
    }

    static Posed Pose(Session session, ItemIconSettings.Outfit outfit)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(outfit.prefab, session.scene);
        try
        {
            instance.transform.position = new Vector3(100, 0, 0);
            SkinnedMeshRenderer skin = null;
            foreach (var s in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (s.name == outfit.renderer) skin = s;
            if (skin == null) { Debug.LogWarning($"[Item icons] No renderer {outfit.renderer} in {outfit.prefab.name}."); return null; }
            if (outfit.mesh.bindposes.Length != skin.bones.Length) { Debug.LogWarning($"[Item icons] {outfit.mesh.name} doesn't fit the bones of {outfit.renderer}."); return null; }
            skin.sharedMesh = outfit.mesh;
            var bones = new Dictionary<string, Transform>();
            foreach (var b in skin.bones) if (b != null) bones[b.name] = b;
            // Arms down, the way armour hangs.
            foreach (var side in new[] { "_L", "_R" })
            {
                if (!bones.TryGetValue("Shoulder" + side, out var shoulder) || !bones.TryGetValue("Elbow" + side, out var elbow)) continue;
                float y = elbow.position.y;
                shoulder.RotateAround(shoulder.position, Vector3.forward, 75f);
                if (elbow.position.y > y) shoulder.RotateAround(shoulder.position, Vector3.forward, -150f);
            }
            var baked = new Mesh(); skin.BakeMesh(baked, true);
            var posed = new Posed { vertices = baked.vertices, uv = outfit.mesh.uv, triangles = new int[outfit.mesh.subMeshCount][], materials = new Material[outfit.mesh.subMeshCount] };
            Object.DestroyImmediate(baked);
            for (int i = 0; i < posed.vertices.Length; i++) posed.vertices[i] = skin.transform.position + skin.transform.rotation * posed.vertices[i] - instance.transform.position;
            var weights = outfit.mesh.boneWeights;
            posed.bone = new string[weights.Length];
            for (int i = 0; i < weights.Length; i++) posed.bone[i] = skin.bones[weights[i].boneIndex0] != null ? skin.bones[weights[i].boneIndex0].name : "";
            for (int s = 0; s < posed.triangles.Length; s++) { posed.triangles[s] = outfit.mesh.GetTriangles(s); posed.materials[s] = outfit.material; }
            foreach (var kv in bones) posed.joint[kv.Key] = kv.Value.position - instance.transform.position;
            return posed;
        }
        finally { Object.DestroyImmediate(instance); }
    }

    // Which vertices to keep; combine with |.
    class Keep
    {
        public readonly bool[] on;
        public Keep(int count) { on = new bool[count]; }
        public static Keep operator |(Keep a, Keep b) { var k = new Keep(a.on.Length); for (int i = 0; i < k.on.Length; i++) k.on[i] = a.on[i] || b.on[i]; return k; }
    }
    static Keep Bones(Posed posed, IEnumerable<string> names)
    {
        var set = new HashSet<string>(names); var k = new Keep(posed.vertices.Length);
        for (int i = 0; i < k.on.Length; i++) k.on[i] = set.Contains(posed.bone[i]);
        return k;
    }
    // Vertices of `bone` within `fraction` of the way from joint `from` to joint `to` (and beyond `from`).
    static Keep Near(Posed posed, string bone, string from, string to, float fraction)
    {
        var k = new Keep(posed.vertices.Length);
        if (!posed.joint.TryGetValue(from, out var a) || !posed.joint.TryGetValue(to, out var b)) return k;
        var d = b - a;
        for (int i = 0; i < k.on.Length; i++)
            k.on[i] = posed.bone[i] == bone && Vector3.Dot(posed.vertices[i] - a, d) / d.sqrMagnitude <= fraction;
        return k;
    }

    // The triangles whose three vertices are kept, moved by (rotation about pivot) + offset, as a new mesh object.
    static GameObject Piece(Session session, Posed posed, Keep keep, Vector3 offset, Quaternion rotation, Vector3 pivot)
    {
        var map = new Dictionary<int, int>(); var vertices = new List<Vector3>(); var uv = new List<Vector2>();
        var subs = new List<int[]>(); bool any = false;
        foreach (var triangles in posed.triangles)
        {
            var kept = new List<int>();
            for (int t = 0; t < triangles.Length; t += 3)
            {
                if (!keep.on[triangles[t]] || !keep.on[triangles[t + 1]] || !keep.on[triangles[t + 2]]) continue;
                for (int m = 0; m < 3; m++)
                {
                    int i = triangles[t + m];
                    if (!map.TryGetValue(i, out int j)) { j = vertices.Count; map[i] = j; vertices.Add(rotation * (posed.vertices[i] - pivot) + offset); uv.Add(posed.uv.Length > i ? posed.uv[i] : Vector2.zero); }
                    kept.Add(j);
                }
            }
            any |= kept.Count > 0;
            subs.Add(kept.ToArray());
        }
        if (!any) return null;
        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
        mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.subMeshCount = subs.Count;
        for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        var piece = session.Make("Piece");
        piece.AddComponent<MeshFilter>().sharedMesh = mesh;
        piece.AddComponent<MeshRenderer>().sharedMaterials = posed.materials;
        return piece;
    }
}
