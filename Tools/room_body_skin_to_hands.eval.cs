// The room player's faces are matched to their hands (Tools/room_head_project.eval.cs); the man's neck and the V of his
// chest are the body's own skin, darker and dirtier than his hands, so the face sat on a dark neck. This maps the
// body's skin texels on the triangles of the neck, chest and shoulders (weighted to neck_01, head, spine_03 or the
// clavicles) the same way: their median to the hands' median, their spread to the hands' spread (one slope for all three
// channels, so shadows keep their hue). Run once per body texture (it reads the hands from the same texture, so running
// it again changes nothing more).
if (UnityEngine.Application.isPlaying) return "playing";
var bt = UnityEngine.Object.FindAnyObjectByType<RoomBodyType>(UnityEngine.FindObjectsInactive.Include);
var smr = bt.bodyB.GetComponent<UnityEngine.SkinnedMeshRenderer>(); var m = smr.sharedMesh; var uv = m.uv; var tr = m.triangles; var w = m.boneWeights;
string tp = UnityEditor.AssetDatabase.GetAssetPath(smr.sharedMaterial.GetTexture("_BaseMap"));
var png = new UnityEngine.Texture2D(2, 2); png.LoadImage(System.IO.File.ReadAllBytes(tp)); int W = png.width, H = png.height; var px = png.GetPixels();
bool Skin(UnityEngine.Color c) => c.r > c.g * 1.06f && c.g > c.b && c.r > c.b * 1.12f;
bool HandBone(string n) { n = n.ToLower(); return n.Contains("hand") || n.Contains("finger") || n.Contains("thumb") || n.Contains("index") || n.Contains("middle") || n.Contains("ring") || n.Contains("pinky"); }
bool NeckBone(string n) { n = n.ToLower(); return n.Contains("neck") || n == "head" || n.Contains("spine_03") || n.Contains("clavicle"); }
var hand = new System.Collections.Generic.HashSet<int>(); var neck = new System.Collections.Generic.HashSet<int>();
for (int t = 0; t < tr.Length; t += 3)
{
  bool isHand = true, isNeck = false;
  for (int e = 0; e < 3; e++) { var b = w[tr[t + e]]; var n0 = smr.bones[b.boneIndex0].name; if (!HandBone(n0)) isHand = false; if (NeckBone(n0) && b.weight0 > .3f) isNeck = true; }
  if (!isHand && !isNeck) continue;
  var a = uv[tr[t]]; var bb = uv[tr[t + 1]]; var c = uv[tr[t + 2]];
  int x0 = UnityEngine.Mathf.Max(0, (int)(UnityEngine.Mathf.Min(a.x, UnityEngine.Mathf.Min(bb.x, c.x)) * W)), x1 = UnityEngine.Mathf.Min(W - 1, (int)(UnityEngine.Mathf.Max(a.x, UnityEngine.Mathf.Max(bb.x, c.x)) * W));
  int y0 = UnityEngine.Mathf.Max(0, (int)(UnityEngine.Mathf.Min(a.y, UnityEngine.Mathf.Min(bb.y, c.y)) * H)), y1 = UnityEngine.Mathf.Min(H - 1, (int)(UnityEngine.Mathf.Max(a.y, UnityEngine.Mathf.Max(bb.y, c.y)) * H));
  float den = (bb.y - c.y) * (a.x - c.x) + (c.x - bb.x) * (a.y - c.y); if (UnityEngine.Mathf.Abs(den) < 1e-12f) continue;
  for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
  {
    float px0 = (x + .5f) / W, py0 = (y + .5f) / H;
    float w1 = ((bb.y - c.y) * (px0 - c.x) + (c.x - bb.x) * (py0 - c.y)) / den, w2 = ((c.y - a.y) * (px0 - c.x) + (a.x - c.x) * (py0 - c.y)) / den; if (w1 < -.02f || w2 < -.02f || 1 - w1 - w2 < -.02f) continue;
    int i = y * W + x; if (!Skin(px[i])) continue; if (isHand) hand.Add(i); else neck.Add(i);
  }
}
foreach (var i in hand) neck.Remove(i);
if (hand.Count < 20 || neck.Count < 20) return $"hand texels {hand.Count}, neck texels {neck.Count}: too few";
float[] Pct(System.Collections.Generic.IEnumerable<int> set, float q) { var r = new System.Collections.Generic.List<float>(); var g = new System.Collections.Generic.List<float>(); var b = new System.Collections.Generic.List<float>(); foreach (var i in set) { r.Add(px[i].r); g.Add(px[i].g); b.Add(px[i].b); } r.Sort(); g.Sort(); b.Sort(); int k = UnityEngine.Mathf.Clamp((int)(r.Count * q), 0, r.Count - 1); return new[] { r[k], g[k], b[k] }; }
var mH = Pct(hand, .5f); var tH = Pct(hand, .9f); var mN = Pct(neck, .5f); var tN = Pct(neck, .9f);
float sH = (tH[0] + tH[1] + tH[2] - mH[0] - mH[1] - mH[2]) / 3f, sN = (tN[0] + tN[1] + tN[2] - mN[0] - mN[1] - mN[2]) / 3f; float k1 = sN > 1e-4f ? sH / sN : 1f;
if (UnityEngine.Mathf.Abs(mH[0] - mN[0]) < .02f && UnityEngine.Mathf.Abs(mH[1] - mN[1]) < .02f) return "the neck already matches the hands";
foreach (var i in neck) { var q = px[i]; px[i] = new UnityEngine.Color(UnityEngine.Mathf.Clamp01(mH[0] + (q.r - mN[0]) * k1), UnityEngine.Mathf.Clamp01(mH[1] + (q.g - mN[1]) * k1), UnityEngine.Mathf.Clamp01(mH[2] + (q.b - mN[2]) * k1), q.a); }
png.SetPixels(px); png.Apply(); System.IO.File.WriteAllBytes(tp, png.EncodeToPNG()); UnityEditor.AssetDatabase.ImportAsset(tp, UnityEditor.ImportAssetOptions.ForceUpdate);
return $"{neck.Count} neck and chest skin texels: median {mN[0]:F2},{mN[1]:F2},{mN[2]:F2} -> hands {mH[0]:F2},{mH[1]:F2},{mH[2]:F2}, spread x{k1:F2} (hand texels {hand.Count})";
