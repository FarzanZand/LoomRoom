// Painted-head pipeline, step 1 (run from an eval): each room player head is rendered unlit with its full-detail Meshy
// bake, orthographic, from the front, right, back, left and top, into Temp/RoomHead/views/<who>_<view>.png. The view
// is a square round the head's bounds; the painting grid is FaceDetail x the body's texel size (Temp/RoomHead/grid.txt
// holds each head's grid size in texels), and the render is Over times finer for the painter to read.
// Step 2: python Tools/room_head_paint.py. Step 3: Tools/room_head_project.eval.cs.
if (UnityEngine.Application.isPlaying) return "playing";
const float FaceDetail = 1.5f; const int Over = 8;
var bt = UnityEngine.Object.FindAnyObjectByType<RoomBodyType>(UnityEngine.FindObjectsInactive.Include); var log = new System.Text.StringBuilder();
System.IO.Directory.CreateDirectory("Temp/RoomHead/views"); var grid = new System.Text.StringBuilder();
foreach (var (headGo, bodyGo, name, who) in new[] { (bt.headA, bt.bodyA, "Room Head Woman", "woman"), (bt.headB, bt.bodyB, "Room Head Man", "man") })
{
  bool hw = headGo.activeSelf, bw = bodyGo.activeSelf; headGo.SetActive(true); bodyGo.SetActive(true);
  var feat = headGo.transform.Find("Face features"); if (feat) feat.gameObject.SetActive(false);
  float tpu = PixelRestyle.TexelsPerUnit(bodyGo.GetComponent<UnityEngine.SkinnedMeshRenderer>()) * FaceDetail;
  var mr = headGo.GetComponent<UnityEngine.Renderer>(); var keepMat = mr.sharedMaterial; int keepLayer = headGo.layer;
  var bake = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>($"Assets/Game/Meshy/RoomHead{(who == "woman" ? "Woman" : "Man")}/{name} baked.png");
  var unlit = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Unlit")); unlit.SetTexture("_BaseMap", bake); unlit.SetFloat("_Cull", 0);
  // a skinned head is rendered from its bind-pose mesh, the positions the projection maps from (the live skeleton's pose can differ)
  UnityEngine.GameObject bindGo = null; var renderR = mr;
  if (mr is UnityEngine.SkinnedMeshRenderer skr)
  { bindGo = new UnityEngine.GameObject("bind pose view"); bindGo.transform.SetPositionAndRotation(headGo.transform.position, headGo.transform.rotation); bindGo.transform.localScale = headGo.transform.lossyScale;
    bindGo.AddComponent<UnityEngine.MeshFilter>().sharedMesh = skr.sharedMesh; renderR = bindGo.AddComponent<UnityEngine.MeshRenderer>(); renderR.sharedMaterial = unlit; bindGo.layer = 30; }
  else { mr.sharedMaterial = unlit; headGo.layer = 30; }
  var b = renderR.bounds; var c = b.center; var f = bt.transform.forward; var r = UnityEngine.Vector3.Cross(UnityEngine.Vector3.up, f).normalized;
  float region = UnityEngine.Mathf.Max(b.size.x, UnityEngine.Mathf.Max(b.size.y, b.size.z)) * 1.1f; int G = UnityEngine.Mathf.CeilToInt(region * tpu);
  var ci = System.Globalization.CultureInfo.InvariantCulture; grid.Append($"{who} {G} {region.ToString(ci)} {c.x.ToString(ci)} {c.y.ToString(ci)} {c.z.ToString(ci)}\n");   // the exact centre the views are framed on
  var go = new UnityEngine.GameObject("view cam"); var cam = go.AddComponent<UnityEngine.Camera>(); cam.orthographic = true; cam.orthographicSize = region * .5f; cam.cullingMask = 1 << 30;
  cam.clearFlags = UnityEngine.CameraClearFlags.SolidColor; cam.backgroundColor = new UnityEngine.Color(1, 0, 1, 1); cam.nearClipPlane = .01f; cam.farClipPlane = region * 4; cam.allowHDR = false; cam.allowMSAA = false;
  var rt = new UnityEngine.RenderTexture(G * Over, G * Over, 24, UnityEngine.RenderTextureFormat.ARGB32, UnityEngine.RenderTextureReadWrite.sRGB); cam.targetTexture = rt;
  foreach (var (vn, dir, up) in new[] { ("front", f, UnityEngine.Vector3.up), ("right", r, UnityEngine.Vector3.up), ("back", -f, UnityEngine.Vector3.up), ("left", -r, UnityEngine.Vector3.up), ("top", UnityEngine.Vector3.up, -f) })
  {
    go.transform.position = c + dir * region * 2; go.transform.rotation = UnityEngine.Quaternion.LookRotation(-dir, up); cam.Render();
    UnityEngine.RenderTexture.active = rt; var t = new UnityEngine.Texture2D(G * Over, G * Over, UnityEngine.TextureFormat.RGB24, false); t.ReadPixels(new UnityEngine.Rect(0, 0, G * Over, G * Over), 0, 0); t.Apply(); UnityEngine.RenderTexture.active = null;
    System.IO.File.WriteAllBytes($"Temp/RoomHead/views/{who}_{vn}.png", t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
  }
  UnityEngine.Object.DestroyImmediate(go); rt.Release();
  if (bindGo) UnityEngine.Object.DestroyImmediate(bindGo); mr.sharedMaterial = keepMat; headGo.layer = keepLayer; if (feat) feat.gameObject.SetActive(true);
  log.Append($"{who}: grid {G} texels over {region:F2} units ({tpu:F2} a unit)\n");
  headGo.SetActive(hw); bodyGo.SetActive(bw);
}
System.IO.File.WriteAllText("Temp/RoomHead/grid.txt", grid.ToString());
return log.ToString();
