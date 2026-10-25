// Mood pass captures (branch mood): renders the bedroom from fixed views with the game's own lighting and post effects
// (Play mode gives the real room light). Views in Temp/Mood/views.txt, one per line: name px py pz tx ty tz fov
// (camera position, the point it looks at, vertical field of view). Writes Temp/Mood/<tag>_<name>.png, 1024 x 576; the
// tag is Temp/Mood/tag.txt.
var ci = System.Globalization.CultureInfo.InvariantCulture;
string tag = System.IO.File.Exists("Temp/Mood/tag.txt") ? System.IO.File.ReadAllText("Temp/Mood/tag.txt").Trim() : "cap";
var main = UnityEngine.Camera.main; var log = new System.Text.StringBuilder();
var go = new UnityEngine.GameObject("mood capture cam"); var cam = go.AddComponent<UnityEngine.Camera>();
if (main) cam.CopyFrom(main);
var src = main ? main.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() : null;
var dst = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
dst.renderPostProcessing = true; if (src) { dst.antialiasing = src.antialiasing; dst.volumeLayerMask = src.volumeLayerMask; dst.SetRenderer(0); }
cam.cullingMask = main ? main.cullingMask : -1; cam.nearClipPlane = .3f; cam.farClipPlane = 3000f;
int W = 1024, H = 576; var rt = new UnityEngine.RenderTexture(W, H, 24, UnityEngine.RenderTextureFormat.ARGB32); cam.targetTexture = rt;
foreach (var line in System.IO.File.ReadAllLines("Temp/Mood/views.txt"))
{
  var p = line.Split(' ', System.StringSplitOptions.RemoveEmptyEntries); if (p.Length < 8 || p[0].StartsWith("#")) continue;
  float F(int i) => float.Parse(p[i], ci);
  go.transform.position = new UnityEngine.Vector3(F(1), F(2), F(3)); go.transform.LookAt(new UnityEngine.Vector3(F(4), F(5), F(6))); cam.fieldOfView = F(7);
  cam.Render(); UnityEngine.RenderTexture.active = rt;
  var t = new UnityEngine.Texture2D(W, H, UnityEngine.TextureFormat.RGB24, false); t.ReadPixels(new UnityEngine.Rect(0, 0, W, H), 0, 0); t.Apply(); UnityEngine.RenderTexture.active = null;
  System.IO.File.WriteAllBytes($"Temp/Mood/{tag}_{p[0]}.png", t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t); log.Append(p[0] + " ");
}
cam.targetTexture = null; rt.Release(); UnityEngine.Object.DestroyImmediate(go);
return $"{tag}: {log} (playing {UnityEngine.Application.isPlaying})";
