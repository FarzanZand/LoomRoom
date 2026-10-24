// His eyes, cleaned: Meshy painted his tired eyes with grey and brown specks all over the white, and in his texture each
// eye lies in many small pieces, so the clean-up is done by place on the head. Every texel of the restyled head is
// found on the mesh (its 3D point and facing); the eye texels (grey or dark, not skin, facing forward) are gathered per
// eye as seen from the front; the iris centre is the middle of the eye's dark texels; then each eye texel is redrawn by
// its distance from that centre: pupil, iris, white, in clean tones with a little variation. The eye's outline stays the
// model's own opening (lids are skin and stay as they are). Run after Tools/room_head_skin_tone.py.
if (UnityEngine.Application.isPlaying) return "playing";
var bt = UnityEngine.Object.FindAnyObjectByType<RoomBodyType>(UnityEngine.FindObjectsInactive.Include);
var go = bt.headB; bool was = go.activeSelf; go.SetActive(true);
var mesh = go.GetComponent<UnityEngine.MeshFilter>().sharedMesh; var ht = go.transform;
string tp = "Assets/Game/Art/Synty Pixel/Characters/Room Head Man/Room Head Man restyled.png";
var tex = new UnityEngine.Texture2D(2, 2); tex.LoadImage(System.IO.File.ReadAllBytes(tp)); int W = tex.width, H = tex.height; var px = tex.GetPixels();
var v = mesh.vertices; var nr = mesh.normals; var uv = mesh.uv; var tr = mesh.triangles;
var f = bt.transform.forward; var rt = UnityEngine.Vector3.Cross(UnityEngine.Vector3.up, f).normalized;
var c = go.GetComponent<UnityEngine.Renderer>().bounds.center; float hh = go.GetComponent<UnityEngine.Renderer>().bounds.size.y;
// texel -> (front x, front y, facing), for texels on the front of the face
var fx = new float[W * H]; var fy = new float[W * H]; var on = new bool[W * H];
for (int t = 0; t < tr.Length; t += 3)
{
  var a = uv[tr[t]] * new UnityEngine.Vector2(W, H); var b = uv[tr[t + 1]] * new UnityEngine.Vector2(W, H); var cc = uv[tr[t + 2]] * new UnityEngine.Vector2(W, H);
  float den = (b.y - cc.y) * (a.x - cc.x) + (cc.x - b.x) * (a.y - cc.y); if (UnityEngine.Mathf.Abs(den) < 1e-9f) continue;
  var fn = ht.TransformDirection(UnityEngine.Vector3.Cross(v[tr[t + 1]] - v[tr[t]], v[tr[t + 2]] - v[tr[t]])).normalized;
  if (UnityEngine.Vector3.Dot(fn, f) < .2f) continue;
  int x0 = UnityEngine.Mathf.Max(0, (int)UnityEngine.Mathf.Min(a.x, UnityEngine.Mathf.Min(b.x, cc.x))), x1 = UnityEngine.Mathf.Min(W - 1, (int)UnityEngine.Mathf.Max(a.x, UnityEngine.Mathf.Max(b.x, cc.x)) + 1);
  int y0 = UnityEngine.Mathf.Max(0, (int)UnityEngine.Mathf.Min(a.y, UnityEngine.Mathf.Min(b.y, cc.y))), y1 = UnityEngine.Mathf.Min(H - 1, (int)UnityEngine.Mathf.Max(a.y, UnityEngine.Mathf.Max(b.y, cc.y)) + 1);
  for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
  {
    float qx = x + .5f, qy = y + .5f; float w1 = ((b.y - cc.y) * (qx - cc.x) + (cc.x - b.x) * (qy - cc.y)) / den, w2 = ((cc.y - a.y) * (qx - cc.x) + (a.x - cc.x) * (qy - cc.y)) / den, w3 = 1 - w1 - w2;
    if (w1 < -.02f || w2 < -.02f || w3 < -.02f) continue;
    var p = ht.TransformPoint(v[tr[t]] * w1 + v[tr[t + 1]] * w2 + v[tr[t + 2]] * w3) - c; int i = y * W + x;
    fx[i] = UnityEngine.Vector3.Dot(p, rt) / hh; fy[i] = p.y / hh; on[i] = true;
  }
}
bool Skin(UnityEngine.Color q) => q.r - q.b > .14f && q.r > .55f;   // the toned skin is warm; the eyes are grey
bool Pale(UnityEngine.Color q) => q.maxColorComponent - UnityEngine.Mathf.Min(q.r, UnityEngine.Mathf.Min(q.g, q.b)) < .12f && q.grayscale > .45f;
var s = new System.Text.StringBuilder(); int changed = 0; var rng = new System.Random(5);
foreach (int side in new[] { -1, 1 })
{
  // the eye's white texels on this side, in the upper middle of the face
  var whites = new System.Collections.Generic.List<int>();
  for (int i = 0; i < px.Length; i++) if (on[i] && fx[i] * side > .04f && fx[i] * side < .3f && fy[i] > -.05f && fy[i] < .25f && Pale(px[i])) whites.Add(i);
  if (whites.Count < 5) { s.Append($"side {side}: no eye found\n"); continue; }
  // the eye is the band where most pale texels lie (grey patches on the forehead and cheeks are elsewhere): the row
  // with the most, and 0.025 head heights either side
  var bins = new int[80]; foreach (var i in whites) bins[UnityEngine.Mathf.Clamp((int)((fy[i] + .05f) / .005f), 0, 79)]++;
  int mode = 0; for (int k = 1; k < 80; k++) if (bins[k] > bins[mode]) mode = k; float my = -.05f + (mode + .5f) * .005f;
  whites.RemoveAll(i => UnityEngine.Mathf.Abs(fy[i] - my) > .025f);
  if (whites.Count < 5) { s.Append($"side {side}: no eye band\n"); continue; }
  float ex0 = 9, ex1 = -9, ey0 = 9, ey1 = -9; foreach (var i in whites) { ex0 = UnityEngine.Mathf.Min(ex0, fx[i]); ex1 = UnityEngine.Mathf.Max(ex1, fx[i]); ey0 = UnityEngine.Mathf.Min(ey0, fy[i]); ey1 = UnityEngine.Mathf.Max(ey1, fy[i]); }
  float padX = (ex1 - ex0) * .05f, padY = .004f, padTop = .012f;   // the iris rises under the upper lid, above the white
  bool InEye(int i) => on[i] && fx[i] > ex0 - padX && fx[i] < ex1 + padX && fy[i] > ey0 - padY && fy[i] < ey1 + padTop && !Skin(px[i]);
  // the iris centre: the middle of the dark texels in the eye
  float sx = 0, sy = 0; int nd = 0; for (int i = 0; i < px.Length; i++) if (InEye(i) && px[i].grayscale < .35f) { sx += fx[i]; sy += fy[i]; nd++; }
  if (nd < 3) { s.Append($"side {side}: no iris found\n"); continue; }
  float icx = sx / nd, icy = sy / nd; float ri = (ex1 - ex0) * .2f, rp = ri * .45f;
  var white = new UnityEngine.Color(.86f, .84f, .80f); var iris = new UnityEngine.Color(.40f, .27f, .17f); var pupil = new UnityEngine.Color(.07f, .05f, .04f);
  int n = 0;
  for (int i = 0; i < px.Length; i++)
  {
    if (!InEye(i)) continue; float d = UnityEngine.Mathf.Sqrt((fx[i] - icx) * (fx[i] - icx) + (fy[i] - icy) * (fy[i] - icy) * 1.2f);
    var col = d < rp ? pupil : d < ri ? iris : white; float k = .96f + .04f * (float)rng.NextDouble();
    px[i] = new UnityEngine.Color(col.r * k, col.g * k, col.b * k, 1); n++;
  }
  changed += n; s.Append($"side {side}: eye {ex0:F2}..{ex1:F2} x {ey0:F2}..{ey1:F2}, iris at {icx:F2},{icy:F2} r {ri:F3}, {n} texels\n");
}
tex.SetPixels(px); tex.Apply(); System.IO.File.WriteAllBytes(tp, tex.EncodeToPNG()); UnityEditor.AssetDatabase.ImportAsset(tp, UnityEditor.ImportAssetOptions.ForceUpdate);
go.SetActive(was);
return s.ToString() + $"{changed} texels redrawn";
