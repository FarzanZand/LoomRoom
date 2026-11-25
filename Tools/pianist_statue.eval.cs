// The Pianist's desk statue: a small cast grand piano with its lid up and a bench, on an oval cast base
// (World/Art/Class props/Pianist statue.prefab), for the writing desk under the class posters. The other classes'
// statues are their figures; the Pianist's is the instrument. Safe to run again (it rebuilds it).
// The model is ours: Tools/pianist_piano.py (Blender) writes World/Art/Class props/Pianist statue/Pianist piano.fbx, with
// one material per tone (Pewter, Pewter light for the white keys, Pewter dark for the black keys and the inside). Here
// its parts are merged, made 4.2 units wide, turned so the keyboard is toward -z, and restyled at the bedroom's 6.4
// texels a unit as pewter: the figures' grey, the white keys near white, the black keys and the inside near black,
// measured and baked again until within 5%, as the statues and their round base are. The cast's mottling is then
// evened out a good deal on the piano (Calm): at about 27 texels across, full mottling hid the keys and the lid's edge. Its base is Unity's cylinder cut to an oval disc under it, restyled the same.
// (Round 1 was the library's upright piano in plain pewter: from where the player stands it was a grey box.)
// Writes Temp/Posters/pianist statue.txt.
if (Application.isPlaying) return "playing";
const string D = "Assets/Game/World/Art/Class props", Dir = D + "/Pianist statue", Source = Dir + "/Pianist piano.fbx";
const float TPU = 6.4f, Wide = 4.2f, Thick = .7f, Rim = .4f;
var sb = new System.Text.StringBuilder();
UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
var piano = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Source);
if (piano == null) return "no " + Source + ": run blender -b -P Tools/pianist_piano.py -- \"" + Source + "\" first";
// Old restyles go first (a restyle written over an existing mesh asset keeps stale vertices); the model stays.
foreach (var old in UnityEditor.AssetDatabase.FindAssets("", new[] { Dir }))
{
    string oldPath = UnityEditor.AssetDatabase.GUIDToAssetPath(old);
    if (oldPath != Source && !oldPath.Contains("~")) UnityEditor.AssetDatabase.DeleteAsset(oldPath);
}
// Pewter in three tones, as plain colours (in memory; a material named pewter is cast as metal). The figures' own
// pewter material can't be used for the piano: it carries their texture, which this model's faces would all read at
// one spot (round 2's first try: the key tones were lost).
var cast = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Progression/Room/Miniatures/Warrior Miniature.prefab").GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterials[0];
var lit = Shader.Find("Universal Render Pipeline/Lit");
var grey = new Material(lit) { name = "pewter", color = new Color(.57f, .59f, .6f) };
var pale = new Material(lit) { name = "pewter light", color = new Color(.95f, .95f, .96f) };
var dark = new Material(lit) { name = "pewter dark", color = new Color(.07f, .07f, .08f) };
System.Func<string, Material> tone = name => name.Contains("light") ? pale : name.Contains("dark") ? dark : grey;

// The model's parts as one mesh in its root's frame, a submesh per part.
var inst = (GameObject)UnityEngine.Object.Instantiate(piano);
inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); inst.transform.localScale = Vector3.one;
var combine = new System.Collections.Generic.List<CombineInstance>(); var mats = new System.Collections.Generic.List<Material>();
foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
{
    var r = mf.GetComponent<MeshRenderer>();
    if (mf.sharedMesh == null || r == null) continue;
    for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
    {
        combine.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = s, transform = mf.transform.localToWorldMatrix });
        var from = r.sharedMaterials[Mathf.Min(s, r.sharedMaterials.Length - 1)];
        mats.Add(tone(from != null ? from.name : mf.name));
    }
    sb.AppendLine("part " + mf.name + " " + mf.sharedMesh.triangles.Length / 3 + " triangles, material " + (r.sharedMaterial != null ? r.sharedMaterial.name : "none"));
}
var src = new Mesh { name = "Pianist statue source" };
src.CombineMeshes(combine.ToArray(), false, true);
UnityEngine.Object.DestroyImmediate(inst);
// At its own size with its foot on 0 and its middle over the origin, so it is baked and measured at scale 1.
var b0 = src.bounds; float S = Wide / b0.size.x;
var v = src.vertices;
for (int i = 0; i < v.Length; i++) v[i] = new Vector3((v[i].x - b0.center.x) * S, (v[i].y - b0.min.y) * S, (v[i].z - b0.center.z) * S);
// The keyboard toward -z, whichever way the model came in (the white keys are the pale tone's submesh).
float keysZ = 0; int keysN = 0;
for (int s = 0; s < src.subMeshCount; s++)
    if (mats[s] == pale) foreach (int i in src.GetTriangles(s)) { keysZ += v[i].z; keysN++; }
if (keysN > 0 && keysZ / keysN > 0f)
{
    for (int i = 0; i < v.Length; i++) v[i] = new Vector3(-v[i].x, v[i].y, -v[i].z);
    var nn = src.normals; for (int i = 0; i < nn.Length; i++) nn[i] = new Vector3(-nn[i].x, nn[i].y, -nn[i].z);
    src.vertices = v; src.normals = nn;
    sb.AppendLine("turned half round: the keys were toward +z");
}
src.vertices = v; src.RecalculateBounds();
sb.AppendLine("model " + b0.size.ToString("0.00") + " as imported, x" + S.ToString("0.00") + " = " + src.bounds.size.ToString("0.00") + " units");

var probe = new GameObject("Texel probe") { hideFlags = HideFlags.HideAndDontSave };
var pf = probe.AddComponent<MeshFilter>(); var pr = probe.AddComponent<MeshRenderer>();
System.Func<Mesh, Material[], string, (Mesh, Material, float)> restyle = (from, with, name) =>
{
    Mesh mesh = null; Material mat = null; float aim = TPU, measured = 0;
    for (int pass = 0; pass < 4; pass++)
    {
        (mesh, mat) = PixelRestyle.Build(from, with, "Props", name, aim, Dir);
        if (mesh == null) break;
        pf.sharedMesh = mesh; pr.sharedMaterial = mat;
        UnityEditor.AssetDatabase.ImportAsset(UnityEditor.AssetDatabase.GetAssetPath(mat.GetTexture("_BaseMap")), UnityEditor.ImportAssetOptions.ForceUpdate);
        measured = PixelRestyle.TexelsPerUnit(pr);
        if (measured <= 0f || Mathf.Abs(measured / TPU - 1f) < .05f) break;
        aim *= TPU / measured;
    }
    return (mesh, mat, measured);
};
var (pianoMesh, pianoMat, pianoTexels) = restyle(src, mats.ToArray(), "Pianist statue");
// Calm: each texel drawn most of the way toward the middle tone of the texels round it that are near its own tone (so
// the keys' white and black and the edges between parts stay, and the blotches within a part even out).
if (pianoMat != null)
{
    const float Calm = .6f;
    string texPath = UnityEditor.AssetDatabase.GetAssetPath(pianoMat.GetTexture("_BaseMap"));
    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false); tex.LoadImage(System.IO.File.ReadAllBytes(texPath));
    int tw = tex.width, th = tex.height; var px = tex.GetPixels(); var res = (Color[])px.Clone();
    for (int y = 0; y < th; y++) for (int x = 0; x < tw; x++)
    {
        var c = px[y * tw + x]; if (c.a < .5f) continue;
        float lum = c.grayscale; Vector3 sum = Vector3.zero; int n = 0;
        for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++)
        {
            int xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= tw || yy >= th) continue;
            var o = px[yy * tw + xx]; if (o.a < .5f || Mathf.Abs(o.grayscale - lum) > .28f) continue;
            sum += new Vector3(o.r, o.g, o.b); n++;
        }
        var mean = new Color(sum.x / n, sum.y / n, sum.z / n, c.a);
        res[y * tw + x] = Color.Lerp(c, mean, Calm);
    }
    tex.SetPixels(res); tex.Apply();
    System.IO.File.WriteAllBytes(texPath, ImageConversion.EncodeToPNG(tex)); UnityEngine.Object.DestroyImmediate(tex);
    UnityEditor.AssetDatabase.ImportAsset(texPath, UnityEditor.ImportAssetOptions.ForceUpdate);
}

// The base: an oval disc a rim wider than the piano and its bench all round.
var cyl = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
var disc = UnityEngine.Object.Instantiate(cyl);
var cv = disc.vertices; var cb = cyl.bounds; float bx = src.bounds.size.x + Rim * 2, bz = src.bounds.size.z + Rim * 2;
for (int i = 0; i < cv.Length; i++) cv[i] = new Vector3(cv[i].x / cb.size.x * bx, (cv[i].y - cb.min.y) / cb.size.y * Thick, cv[i].z / cb.size.z * bz);
disc.vertices = cv; disc.RecalculateBounds(); disc.RecalculateNormals();
var (baseMesh, baseMat, baseTexels) = restyle(disc, new[] { cast }, "Pianist statue base");
UnityEngine.Object.DestroyImmediate(grey); UnityEngine.Object.DestroyImmediate(pale); UnityEngine.Object.DestroyImmediate(dark);
UnityEngine.Object.DestroyImmediate(probe); UnityEngine.Object.DestroyImmediate(src); UnityEngine.Object.DestroyImmediate(disc);
if (pianoMesh == null || baseMesh == null) return sb + "restyle failed";

string path = D + "/Pianist statue.prefab";
var root = new GameObject("Pianist statue");
try
{
    var bg = new GameObject("Base"); bg.transform.SetParent(root.transform, false);
    bg.AddComponent<MeshFilter>().sharedMesh = baseMesh; bg.AddComponent<MeshRenderer>().sharedMaterial = baseMat;
    var mg = new GameObject("Model"); mg.transform.SetParent(root.transform, false);
    mg.transform.localPosition = new Vector3(0, Thick, 0);
    mg.AddComponent<MeshFilter>().sharedMesh = pianoMesh; mg.AddComponent<MeshRenderer>().sharedMaterial = pianoMat;
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
}
finally { UnityEngine.Object.DestroyImmediate(root); }
UnityEditor.AssetDatabase.SaveAssets();
// The prefab, and the poster wall that holds a copy of it, are imported again: after a rebuild (the old meshes deleted and
// new ones made in one go) they kept pointing at nothing until then, and the statue stood on the desk unseen.
UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate);
UnityEditor.AssetDatabase.ImportAsset("Assets/Game/World/Art/Class posters/Class poster wall.prefab", UnityEditor.ImportAssetOptions.ForceUpdate);
sb.AppendLine("piano " + pianoMesh.triangles.Length / 3 + " triangles at " + pianoTexels.ToString("0.00") + " texels a unit; base " + baseMesh.triangles.Length / 3 + " triangles at " + baseTexels.ToString("0.00") + ", " + bx.ToString("0.0") + " x " + bz.ToString("0.0") + " units");
sb.AppendLine("saved " + path);
System.IO.Directory.CreateDirectory("Temp/Posters");
System.IO.File.WriteAllText("Temp/Posters/pianist statue.txt", sb.ToString());
return sb.ToString();
