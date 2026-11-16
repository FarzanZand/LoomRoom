// Eyes that glow in the old skeleton bodies' sockets, step 1 of 2 (the night review, 2026-11-16). The Crypt Archer,
// Warden and Soldier wear Synty's older skeleton bodies, whose skulls have modelled sockets and no painted eyes. This
// writes each head's triangles (place relative to the head bone, and uv) to Temp/SocketEyes; Tools/socket_eyes.py then
// finds the two sockets by depth and paints an ember into their texels (<texture> eyes.png beside the body's texture),
// and Tools/enemy_eyes.eval.cs switches the materials' emission on.
//   unity command eval_file --file Tools/socket_eyes.eval.cs
//   python Tools/socket_eyes.py
//   unity command eval_file --file Tools/enemy_eyes.eval.cs
string dir = System.IO.Path.GetFullPath("Temp/SocketEyes/").Replace('\\', '/');
System.IO.Directory.CreateDirectory(dir);
var ci = System.Globalization.CultureInfo.InvariantCulture;
var log = new System.Text.StringBuilder();
foreach (var kind in new[] { "Crypt Archer", "Crypt Warden", "Crypt Soldier" })
{
    var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Game/Characters/Enemies/Crypt/" + kind + ".prefab");
    var go = UnityEngine.Object.Instantiate(prefab, UnityEngine.Vector3.zero, UnityEngine.Quaternion.identity);
    try
    {
        var anim = go.GetComponentInChildren<UnityEngine.Animator>();
        var head = anim.GetBoneTransform(UnityEngine.HumanBodyBones.Head);
        UnityEngine.SkinnedMeshRenderer body = null;
        foreach (var r in go.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>()) if (r.enabled && r.gameObject.activeInHierarchy && (body == null || r.sharedMesh.triangles.Length > body.sharedMesh.triangles.Length)) body = r;
        var baked = new UnityEngine.Mesh(); body.BakeMesh(baked, true);
        var v = baked.vertices; var uv = baked.uv; var t = baked.triangles;
        var toWorld = body.transform.localToWorldMatrix; var toHead = head.worldToLocalMatrix;
        var fwd = go.transform.forward; var right = go.transform.right; var up = UnityEngine.Vector3.up; var hp = head.position;
        var lines = new System.Collections.Generic.List<string>();
        for (int i = 0; i < t.Length; i += 3)
        {
            var a = toWorld.MultiplyPoint3x4(v[t[i]]); var b = toWorld.MultiplyPoint3x4(v[t[i + 1]]); var c = toWorld.MultiplyPoint3x4(v[t[i + 2]]);
            var cen = (a + b + c) / 3f - hp;
            if (cen.magnitude > .28f) continue;
            string P(UnityEngine.Vector3 w) { var d = w - hp; return UnityEngine.Vector3.Dot(d, right).ToString("R", ci) + " " + UnityEngine.Vector3.Dot(d, up).ToString("R", ci) + " " + UnityEngine.Vector3.Dot(d, fwd).ToString("R", ci); }
            string U(UnityEngine.Vector2 q) => q.x.ToString("R", ci) + " " + q.y.ToString("R", ci);
            lines.Add(P(a) + " " + P(b) + " " + P(c) + " " + U(uv[t[i]]) + " " + U(uv[t[i + 1]]) + " " + U(uv[t[i + 2]]));
        }
        System.IO.File.WriteAllLines(dir + kind + ".txt", lines);
        var tex = body.sharedMaterial.mainTexture;
        log.AppendLine(kind + ";" + body.sharedMesh.name + ";" + UnityEditor.AssetDatabase.GetAssetPath(body.sharedMaterial) + ";" + UnityEditor.AssetDatabase.GetAssetPath(tex) + ";" + tex.width + ";" + lines.Count + ";" + go.transform.localScale.x.ToString(ci) + ";" + body.sharedMaterial.shader.name);
        UnityEngine.Object.DestroyImmediate(baked);
    }
    finally { UnityEngine.Object.DestroyImmediate(go); }
}
System.IO.File.WriteAllText(dir + "index.txt", log.ToString());
return log.ToString();
