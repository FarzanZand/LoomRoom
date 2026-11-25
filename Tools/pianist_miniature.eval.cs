// The Pianist's table figure (Progression/Room/Miniatures/Pianist Miniature.prefab), made the way the other classes'
// are: a Fantasy Kingdom character on its own skeleton, standing in a held pose, its mesh a pewter pixel copy at the
// figures' 200 texels a unit (Art/Synty Pixel/Miniatures/<mesh>/). The Pianist's is the Prince (a slim figure in a
// doublet and short cape: the nearest the pack has to a player in concert dress), standing as the Idle clip stands
// with both forearms raised level in front and the hands palm down, fingers hanging, as over a keyboard. Safe to run again (it rebuilds it).
// Sets nothing on the class: Pianist.asset's `miniature` is set by whoever owns that asset.
// Also writes a picture of it from the front and the side to Temp/Posters/pianist miniature.png.
if (Application.isPlaying) return "playing";
const string Source = "Assets/Synty/PolygonFantasyKingdom/Prefabs/Characters/SM_Chr_Prince_01.prefab", Body = "SM_Chr_Prince_01";
const string Folder = "Assets/Game/Art/Synty Pixel/Miniatures/" + Body, Out = "Assets/Game/Progression/Room/Miniatures/Pianist Miniature.prefab";
const float Texels = 200f;
var sb = new System.Text.StringBuilder();
var prince = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Source);
if (prince == null) return "no " + Source;
if (!UnityEditor.AssetDatabase.IsValidFolder(Folder)) UnityEditor.AssetDatabase.CreateFolder("Assets/Game/Art/Synty Pixel/Miniatures", Body);
foreach (var old in UnityEditor.AssetDatabase.FindAssets("", new[] { Folder })) UnityEditor.AssetDatabase.DeleteAsset(UnityEditor.AssetDatabase.GUIDToAssetPath(old));

// The other figures' pewter: the middle colour of one's texture, as a plain material named pewter (the restyle casts
// anything so named as metal).
var like = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Progression/Room/Miniatures/Cleric Miniature.prefab").GetComponentInChildren<SkinnedMeshRenderer>();
var likeTex = like.sharedMaterial.mainTexture;
var rt = RenderTexture.GetTemporary(64, 64, 0, RenderTextureFormat.ARGB32);
Graphics.Blit(likeTex, rt);
var was = RenderTexture.active; RenderTexture.active = rt;
var read = new Texture2D(64, 64, TextureFormat.RGBA32, false); read.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
RenderTexture.active = was; RenderTexture.ReleaseTemporary(rt);
Vector3 sum = Vector3.zero; int n = 0;
foreach (var c in read.GetPixels()) if (c.a > .5f && c.maxColorComponent > .08f) { sum += new Vector3(c.r, c.g, c.b); n++; }
UnityEngine.Object.DestroyImmediate(read);
var grey = n > 0 ? new Color(sum.x / n, sum.y / n, sum.z / n, 1) : new Color(.5f, .5f, .52f, 1);
var pewter = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "pewter", color = grey };
sb.AppendLine("pewter " + grey.ToString("0.00") + " from " + likeTex.name);

var inst = (GameObject)UnityEngine.Object.Instantiate(prince);
inst.name = "Pianist Miniature";
inst.transform.SetPositionAndRotation(new Vector3(0, -500, 0), Quaternion.identity); inst.transform.localScale = Vector3.one;
try
{
    // Only the Prince's body stays (the prefab carries every character of the pack on one skeleton).
    SkinnedMeshRenderer body = null;
    foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    {
        if (smr.name == Body) { body = smr; smr.gameObject.SetActive(true); }
        else UnityEngine.Object.DestroyImmediate(smr.gameObject);
    }
    if (body == null) return "no " + Body + " in the prefab";

    // The pose: the Idle clip's first frame, then both arms brought forward with the forearms out and the hands low.
    var animator = inst.GetComponent<Animator>();
    var idle = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Game/Characters/Animations/ThirdPerson/Locomotion/Idle.anim");
    if (idle != null) idle.SampleAnimation(inst, 0f);
    var handler = new HumanPoseHandler(animator.avatar, inst.transform);
    var pose = new HumanPose(); handler.GetHumanPose(ref pose);
    var names = HumanTrait.MuscleName;
    System.Action<string, float> set = (muscle, value) => { int i = System.Array.IndexOf(names, muscle); if (i >= 0) pose.muscles[i] = value; else sb.AppendLine("no muscle " + muscle); };
    foreach (var side in new[] { "Left", "Right" })
    {
        set(side + " Shoulder Down-Up", -.1f);
        set(side + " Arm Down-Up", -.6f);             // the upper arms down by the body
        set(side + " Arm Front-Back", .55f);          // and forward
        set(side + " Arm Twist In-Out", .1f);
        set(side + " Forearm Stretch", -.5f);         // the forearms raised level in front, at keyboard height
        set(side + " Forearm Twist In-Out", -.5f);    // palms down (.45 the other way turned them up: an open-handed shrug)
        set(side + " Hand Down-Up", -.05f);
        set(side + " Hand In-Out", 0f);
        // The fingers open, a little curled and spread, as over keys (the idle's hands are fists).
        foreach (var finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
        {
            foreach (var joint in new[] { "1", "2", "3" }) set(side + " " + finger + " " + joint + " Stretched", .3f);
            set(side + " " + finger + " Spread", .2f);
        }
    }
    pose.bodyPosition = new Vector3(0, pose.bodyPosition.y, 0); pose.bodyRotation = Quaternion.identity;
    handler.SetHumanPose(ref pose);
    handler.Dispose();
    inst.transform.SetPositionAndRotation(new Vector3(0, -500, 0), Quaternion.identity);
    UnityEngine.Object.DestroyImmediate(animator);

    // The pewter copy, measured as it stands and baked again until within 5% of the figures' density.
    Mesh mesh = null; Material mat = null; float aim = Texels, measured = 0;
    var srcMesh = body.sharedMesh;
    for (int pass = 0; pass < 4; pass++)
    {
        (mesh, mat) = PixelRestyle.Build(srcMesh, new[] { pewter }, "Miniatures", Body + " pewter", aim, Folder);
        if (mesh == null) break;
        body.sharedMesh = mesh; body.sharedMaterials = new[] { mat };
        UnityEditor.AssetDatabase.ImportAsset(UnityEditor.AssetDatabase.GetAssetPath(mat.GetTexture("_BaseMap")), UnityEditor.ImportAssetOptions.ForceUpdate);
        measured = PixelRestyle.TexelsPerUnit(body);
        if (measured <= 0f || Mathf.Abs(measured / Texels - 1f) < .05f) break;
        aim *= Texels / measured;
    }
    if (mesh == null) return sb + "restyle failed";
    body.shadowCastingMode = like.shadowCastingMode; body.receiveShadows = like.receiveShadows; body.updateWhenOffscreen = like.updateWhenOffscreen;
    body.gameObject.layer = like.gameObject.layer;
    sb.AppendLine("body " + mesh.triangles.Length / 3 + " triangles at " + measured.ToString("0.0") + " texels a unit (the Cleric's figure: " + PixelRestyle.TexelsPerUnit(like).ToString("0.0") + ")");

    // A look at it: front and side, lit plainly.
    var camGo = new GameObject("Miniature look"); var cam = camGo.AddComponent<Camera>();
    var lightGo = new GameObject("Miniature look light"); var l = lightGo.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.2f;
    try
    {
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.12f, .12f, .14f); cam.orthographic = true; cam.orthographicSize = 1.15f;
        cam.nearClipPlane = .1f; cam.farClipPlane = 20f;
        var shots = new RenderTexture(512, 512, 24); cam.targetTexture = shots;
        var sheet = new Texture2D(1536, 512, TextureFormat.RGB24, false);
        var centre = inst.transform.position + Vector3.up * .95f;
        var views = new[] { Vector3.forward, new Vector3(1, 0, .2f).normalized, new Vector3(.7f, .5f, .7f).normalized };
        for (int i = 0; i < views.Length; i++)
        {
            camGo.transform.position = centre + views[i] * 5f; camGo.transform.LookAt(centre);
            lightGo.transform.rotation = Quaternion.LookRotation(-views[i] + Vector3.down * .6f + camGo.transform.right * .5f);
            cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = shots;
            sheet.ReadPixels(new Rect(0, 0, 512, 512), i * 512, 0); RenderTexture.active = prev;
        }
        sheet.Apply();
        System.IO.Directory.CreateDirectory("Temp/Posters");
        System.IO.File.WriteAllBytes("Temp/Posters/pianist miniature.png", ImageConversion.EncodeToPNG(sheet));
        cam.targetTexture = null; shots.Release(); UnityEngine.Object.DestroyImmediate(sheet);
    }
    finally { UnityEngine.Object.DestroyImmediate(camGo); UnityEngine.Object.DestroyImmediate(lightGo); }

    inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(inst, Out);
    sb.AppendLine("saved " + Out);
}
finally { UnityEngine.Object.DestroyImmediate(inst); UnityEngine.Object.DestroyImmediate(pewter); }
UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.AssetDatabase.ImportAsset(Out, UnityEditor.ImportAssetOptions.ForceUpdate);
return sb.ToString();
