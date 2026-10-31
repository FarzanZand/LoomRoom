// Furnishing groups (Levels/Shared/Props/Groups) put right after their pieces were resized (cellar_texels.eval.cs scales a
// piece about its foot, so what stood on it was left in the air, and draws heaps in, so pieces pushed into each other):
//  - every piece not on the floor is set down on what is under it (the highest surface of another piece below its
//    footprint), sunk 1 cm into it so no face lies in that surface's plane (the candles over the shrunk barrel);
//  - a piece pushed into another (its vertices inside the other's box, past 1 cm) is moved straight away from it,
//    sideways only, until it stands 1 cm clear (the apple sack inside the crates); only pieces that both stand on the
//    floor, since what stands on a shelf lies inside the shelf's box;
// measured on the pieces' own meshes in the prefab. Safe to run again. Writes Temp/Props/settle.txt. Edit mode.
if (UnityEngine.Application.isPlaying) return "playing";
var sb = new System.Text.StringBuilder();
const float Sink = .01f, Clear = .01f;

// A piece: a direct child of the group with renderers under it.
(UnityEngine.Transform t, UnityEngine.MeshFilter[] parts)[] Pieces(UnityEngine.GameObject root)
{
  var list = new System.Collections.Generic.List<(UnityEngine.Transform, UnityEngine.MeshFilter[])>();
  foreach (UnityEngine.Transform c in root.transform)
  {
    var mfs = System.Array.FindAll(c.GetComponentsInChildren<UnityEngine.MeshFilter>(), m => m.sharedMesh != null && m.GetComponent<UnityEngine.MeshRenderer>() != null && m.GetComponent<UnityEngine.MeshRenderer>().enabled);
    if (mfs.Length > 0) list.Add((c, mfs));
  }
  return list.ToArray();
}
// World vertices of a piece (readable meshes only; a part that isn't readable counts by its bounds' corners).
System.Collections.Generic.List<UnityEngine.Vector3> Verts(UnityEngine.MeshFilter[] parts)
{
  var v = new System.Collections.Generic.List<UnityEngine.Vector3>();
  foreach (var m in parts)
  {
    if (m.sharedMesh.isReadable) foreach (var p in m.sharedMesh.vertices) v.Add(m.transform.TransformPoint(p));
    else { var b = m.sharedMesh.bounds; for (int i = 0; i < 8; i++) v.Add(m.transform.TransformPoint(new UnityEngine.Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z))); }
  }
  return v;
}
// The height of a piece's surface at (x, z): the highest triangle of its meshes crossing that vertical line, or -inf.
float TopAt(UnityEngine.MeshFilter[] parts, float x, float z, float below)
{
  float best = float.NegativeInfinity;
  foreach (var m in parts)
  {
    if (!m.sharedMesh.isReadable) continue;
    var v = m.sharedMesh.vertices; var t = m.sharedMesh.triangles; var tr = m.transform;
    for (int i = 0; i < t.Length; i += 3)
    {
      var a = tr.TransformPoint(v[t[i]]); var b = tr.TransformPoint(v[t[i + 1]]); var c = tr.TransformPoint(v[t[i + 2]]);
      float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
      if (UnityEngine.Mathf.Abs(d) < 1e-9f) continue;
      float l1 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d, l2 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d, l3 = 1 - l1 - l2;
      if (l1 < 0 || l2 < 0 || l3 < 0) continue;
      float y = l1 * a.y + l2 * b.y + l3 * c.y;
      if (y <= below && y > best) best = y;
    }
  }
  return best;
}
UnityEngine.Bounds WorldBounds(System.Collections.Generic.List<UnityEngine.Vector3> v)
{
  var b = new UnityEngine.Bounds(v[0], UnityEngine.Vector3.zero); foreach (var p in v) b.Encapsulate(p); return b;
}

foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Levels/Shared/Props/Groups" }))
{
  var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
  var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
  bool changed = false; var name = System.IO.Path.GetFileNameWithoutExtension(path);
  try
  {
    var pieces = Pieces(root);
    // Lowest first, so a stack settles from the bottom up.
    System.Array.Sort(pieces, (p, q) => WorldBounds(Verts(p.parts)).min.y.CompareTo(WorldBounds(Verts(q.parts)).min.y));
    float floor = root.transform.position.y;
    foreach (var p in pieces)
    {
      var b = WorldBounds(Verts(p.parts));
      if (b.min.y < floor + .02f) continue;   // stands on the floor
      // The support: the highest surface of the other pieces under five points of its footprint.
      float support = float.NegativeInfinity;
      var c = b.center; float ex = b.extents.x * .5f, ez = b.extents.z * .5f;
      foreach (var (x, z) in new[] { (c.x, c.z), (c.x - ex, c.z - ez), (c.x + ex, c.z - ez), (c.x - ex, c.z + ez), (c.x + ex, c.z + ez) })
        foreach (var o in pieces) if (o.t != p.t) support = UnityEngine.Mathf.Max(support, TopAt(o.parts, x, z, b.min.y + .05f));
      if (float.IsNegativeInfinity(support)) support = floor;
      float dy = support - Sink - b.min.y;
      if (UnityEngine.Mathf.Abs(dy) < .002f) continue;
      p.t.position += UnityEngine.Vector3.up * dy; changed = true;
      sb.Append($"{name}/{p.t.name}: set down {dy * 100:F1} cm onto {(support > floor + .001f ? "the piece below" : "the floor")}\n");
    }
    // Pieces pushed into each other: the smaller moved straight away from the larger, sideways, until 1 cm clear of
    // the larger's box (in the larger's own frame, so a turned crate counts as the crate).
    for (int pass = 0; pass < 3; pass++)
    for (int i = 0; i < pieces.Length; i++)
    for (int j = 0; j < pieces.Length; j++)
    {
      if (i == j) continue;
      var big = pieces[i]; var small = pieces[j];
      var bv = Verts(big.parts); var sv = Verts(small.parts);
      if (WorldBounds(sv).size.sqrMagnitude > WorldBounds(bv).size.sqrMagnitude) continue;
      // Only pieces that both stand on the floor: a pot on a shelf lies inside the shelf's box and belongs there.
      if (WorldBounds(sv).min.y > floor + .02f || WorldBounds(bv).min.y > floor + .02f) continue;
      // The big piece's box in its own frame (its first part's transform).
      var frame = big.parts[0].transform;
      var lb = new UnityEngine.Bounds(frame.InverseTransformPoint(bv[0]), UnityEngine.Vector3.zero); foreach (var q in bv) lb.Encapsulate(frame.InverseTransformPoint(q));
      var inner = new UnityEngine.Bounds(lb.center, lb.size - UnityEngine.Vector3.one * (2 * Clear));
      var away = small.t.position - big.t.position; away.y = 0;
      if (away.sqrMagnitude < 1e-6f) continue;
      away.Normalize();
      float moved = 0;
      for (int k = 0; k < 60; k++)
      {
        sv = Verts(small.parts);
        bool inside = false;
        foreach (var q in sv) { var l = frame.InverseTransformPoint(q); if (q.y > big.t.position.y + .03f && inner.Contains(l)) { inside = true; break; } }
        if (!inside) break;
        small.t.position += away * .005f; moved += .005f;
      }
      if (moved > 0) { changed = true; sb.Append($"{name}/{small.t.name}: moved {moved * 100:F1} cm out of {big.t.name}\n"); }
    }
    if (changed) UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
  }
  finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
}
UnityEditor.AssetDatabase.SaveAssets();
System.IO.Directory.CreateDirectory("Temp/Props");
System.IO.File.WriteAllText("Temp/Props/settle.txt", sb.Length > 0 ? sb.ToString() : "nothing to settle\n");
return sb.ToString();
