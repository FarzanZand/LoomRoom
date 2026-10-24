// Character review sheet (run with: unity command eval_file --file Tools/character_review.eval.cs). For the room player's
// two bodies: twelve zoomed crops of the head's features (eyes front and three-quarter, nose and mouth, both ears, behind
// the ears, back of the head, nape, neck join front and side, hairline), each in neutral light (the character alone, two
// directional lights) and in the room's own light; also the neck from behind and below, and the upper back. Writes Temp/CharacterReview/<body>.png (7 x 4 tiles). Judge every crop.
if (UnityEngine.Application.isPlaying) return "stop Play first";
var bt = UnityEngine.Object.FindAnyObjectByType<RoomBodyType>(UnityEngine.FindObjectsInactive.Include); var keepBody = bt.body;
System.IO.Directory.CreateDirectory("Temp/CharacterReview"); var log = new System.Text.StringBuilder();
foreach (var body in new[] { BodyType.BodyA, BodyType.BodyB })
{
  bt.body = body; bt.Apply();
  var bodyGo = body == BodyType.BodyA ? bt.bodyA : bt.bodyB; var headGo = body == BodyType.BodyA ? bt.headA : bt.headB;
  var smr = bodyGo.GetComponent<UnityEngine.SkinnedMeshRenderer>(); var hb = System.Array.Find(smr.bones, x => x.name == "head");
  var f = bt.transform.forward; var up = UnityEngine.Vector3.up; var right = UnityEngine.Vector3.Cross(up, f).normalized;
  var hbounds = headGo.GetComponent<UnityEngine.Renderer>().bounds;
  float stand = smr.bounds.size.y, H = stand / 6.4f, chinY = hb.position.y - .24f * H;
  var axis = new UnityEngine.Vector3(hb.position.x, 0, hb.position.z);
  UnityEngine.Vector3 At(float yFrac, float fwd, float side) => axis + up * (chinY + yFrac * H) + f * (fwd * H) + right * (side * H);
  // (aim, camera direction from the aim, frame width in head heights)
  var crops = new (string name, UnityEngine.Vector3 aim, UnityEngine.Vector3 dir, float width)[] {
    ("eyes front", At(.58f, .32f, 0), f, .6f),
    ("eyes 3/4", At(.58f, .3f, .05f), (f + right * .9f).normalized, .6f),
    ("nose and mouth", At(.3f, .34f, 0), f, .55f),
    ("ear right", At(.48f, 0f, .3f), right, .5f),
    ("ear left", At(.48f, 0f, -.3f), -right, .5f),
    ("behind ears", At(.45f, -.15f, .25f), (-f + right * 1.2f).normalized, .65f),
    ("back of head", At(.6f, -.1f, 0), -f, 1.05f),
    ("nape", At(.12f, -.2f, 0), (-f + up * -.1f).normalized, .6f),
    ("neck join front", At(.02f, .15f, 0), (f + up * -.35f).normalized, .6f),
    ("neck join side", At(.02f, .05f, .1f), (right + f * .3f + up * -.2f).normalized, .6f),
    ("hairline front", At(.9f, .25f, 0), (f + up * .25f).normalized, .75f),
    ("whole head", At(.5f, .1f, 0), (f + right * .5f).normalized, 1.5f),
    ("neck from behind and below", At(.05f, -.1f, 0), (-f + up * -.8f).normalized, .9f),
    ("upper back", At(-.7f, -.45f, 0), (-f + up * .15f).normalized, 1.8f) };
  int T = 400; var sheet = new UnityEngine.Texture2D(T * 7, T * 4, UnityEngine.TextureFormat.RGB24, false);
  var go = new UnityEngine.GameObject("review cam"); var cam = go.AddComponent<UnityEngine.Camera>(); cam.CopyFrom(UnityEngine.Camera.main); cam.nearClipPlane = .05f;
  var rt = new UnityEngine.RenderTexture(T, T, 24); cam.targetTexture = rt;
  for (int mode = 0; mode < 2; mode++)
  {
    var objs = new System.Collections.Generic.List<(UnityEngine.GameObject, int)>(); var lights = new System.Collections.Generic.List<UnityEngine.GameObject>();
    if (mode == 0)
    {
      foreach (var t in bt.GetComponentsInChildren<UnityEngine.Transform>(true)) { objs.Add((t.gameObject, t.gameObject.layer)); t.gameObject.layer = 30; }
      cam.cullingMask = 1 << 30; cam.clearFlags = UnityEngine.CameraClearFlags.SolidColor; cam.backgroundColor = new UnityEngine.Color(.42f, .42f, .44f);
      foreach (var (d, i) in new[] { (new UnityEngine.Vector3(-.5f, -.6f, -.6f), 1.3f), (new UnityEngine.Vector3(.7f, -.2f, .7f), .6f) })
      { var lg = new UnityEngine.GameObject("l"); var l = lg.AddComponent<UnityEngine.Light>(); l.type = UnityEngine.LightType.Directional; l.intensity = i; l.cullingMask = 1 << 30; lg.transform.rotation = UnityEngine.Quaternion.LookRotation(bt.transform.rotation * d); lights.Add(lg); }
    }
    else { cam.CopyFrom(UnityEngine.Camera.main); cam.nearClipPlane = .05f; cam.targetTexture = rt; cam.cullingMask = -1; }
    for (int i = 0; i < crops.Length; i++)
    {
      var (n, aim, dir, width) = crops[i]; float dist = 4f * H;
      go.transform.position = aim + dir * dist; go.transform.LookAt(aim); cam.fieldOfView = 2f * UnityEngine.Mathf.Atan(width * H * .5f / dist) * UnityEngine.Mathf.Rad2Deg;
      cam.Render(); UnityEngine.RenderTexture.active = rt;
      int col = i % 7, row = mode * 2 + i / 7; sheet.ReadPixels(new UnityEngine.Rect(0, 0, T, T), col * T, (3 - row) * T);
    }
    foreach (var (o, l) in objs) o.layer = l; foreach (var l in lights) UnityEngine.Object.DestroyImmediate(l);
  }
  sheet.Apply(); UnityEngine.RenderTexture.active = null; UnityEngine.Object.DestroyImmediate(go);
  var path = $"Temp/CharacterReview/{body}.png"; System.IO.File.WriteAllBytes(path, sheet.EncodeToPNG());
  log.Append($"{path}: rows 1-2 neutral, rows 3-4 room light; columns: " + string.Join(", ", System.Array.ConvertAll(crops, c => c.name)) + "\n");
}
bt.body = keepBody; bt.Apply();
return log.ToString();
