// Life in the air for the Realms floors (Levels/Dungeon1/Realms/Props/Life), as the forest's fireflies are made: a
// particle system over a room, set down by a furnishing rule, no light of its own. Taken from the demo scenes' FX
// (Dust_Embers, Lava_Rocks, Hell_Rocks_Floating), drawn with the Spells Pack's pixel glow so they stay in the pixel look:
//  - Dust motes (the Deep Halls): pale specks hanging and turning slowly in the teal dark.
//  - Rising embers (the Forge and the Pit): orange sparks drifting up from the floor, cooling to red as they fade.
//  - Falling ash (the Pit): grey flakes, square texels, sinking slowly.
//  - Witch-fire (the Pit): green wisps that blink as they drift, the hell demo's green glow.
// Safe to run again. Writes Temp/Realms/life.txt.
if (UnityEngine.Application.isPlaying) return "playing";
const string D = "Assets/Game/Levels/Dungeon1/Realms/Props/Life";
var sb = new System.Text.StringBuilder();
void Folder(string path) { var parts = path.Split('/'); var cur = parts[0]; for (int i = 1; i < parts.Length; i++) { var next = cur + "/" + parts[i]; if (!UnityEditor.AssetDatabase.IsValidFolder(next)) UnityEditor.AssetDatabase.CreateFolder(cur, parts[i]); cur = next; } }
Folder(D);
var glow = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>("Assets/Game/Combat/Spells/_Pack/Materials/Glow_LWRP.mat");
if (glow == null) sb.Append("MISSING glow\n");
// Ash: plain square flakes (no texture: a pixel square), lit by the floor's lights.
var ashMat = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>($"{D}/Ash.mat");
if (ashMat == null) { ashMat = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Particles/Simple Lit")); UnityEditor.AssetDatabase.CreateAsset(ashMat, $"{D}/Ash.mat"); }
ashMat.SetColor("_BaseColor", new UnityEngine.Color(.38f, .34f, .33f));   // dark flakes: lit near a torch, a white base showed as white squares UnityEditor.EditorUtility.SetDirty(ashMat);

UnityEngine.Gradient Fade(UnityEngine.Color a, UnityEngine.Color b, bool blink)
{
  var g = new UnityEngine.Gradient();
  var alphas = blink
    ? new[] { new UnityEngine.GradientAlphaKey(0, 0), new UnityEngine.GradientAlphaKey(1, .12f), new UnityEngine.GradientAlphaKey(.15f, .3f), new UnityEngine.GradientAlphaKey(1, .45f), new UnityEngine.GradientAlphaKey(.1f, .62f), new UnityEngine.GradientAlphaKey(1, .78f), new UnityEngine.GradientAlphaKey(0, 1) }
    : new[] { new UnityEngine.GradientAlphaKey(0, 0), new UnityEngine.GradientAlphaKey(1, .15f), new UnityEngine.GradientAlphaKey(.8f, .7f), new UnityEngine.GradientAlphaKey(0, 1) };
  g.SetKeys(new[] { new UnityEngine.GradientColorKey(a, 0), new UnityEngine.GradientColorKey(b, 1) }, alphas);
  return g;
}

void Make(string name, UnityEngine.Material mat, UnityEngine.Color c0, UnityEngine.Color c1, bool blink, UnityEngine.Vector2 life, UnityEngine.Vector2 size, float rate, int max,
          UnityEngine.Vector3 boxPos, UnityEngine.Vector3 box, UnityEngine.Vector2 rise, float noise)
{
  var root = new UnityEngine.GameObject(name);
  var ps = root.AddComponent<UnityEngine.ParticleSystem>();
  var main = ps.main; main.loop = true; main.playOnAwake = true; main.prewarm = true; main.simulationSpace = UnityEngine.ParticleSystemSimulationSpace.World;
  main.startLifetime = new UnityEngine.ParticleSystem.MinMaxCurve(life.x, life.y); main.startSpeed = 0f;
  main.startSize = new UnityEngine.ParticleSystem.MinMaxCurve(size.x, size.y); main.maxParticles = max; main.startColor = UnityEngine.Color.white;
  var em = ps.emission; em.rateOverTime = rate;
  var shape = ps.shape; shape.shapeType = UnityEngine.ParticleSystemShapeType.Box; shape.position = boxPos; shape.scale = box;
  var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = UnityEngine.ParticleSystemSimulationSpace.World;
  vel.x = new UnityEngine.ParticleSystem.MinMaxCurve(-.05f, .05f); vel.y = new UnityEngine.ParticleSystem.MinMaxCurve(rise.x, rise.y); vel.z = new UnityEngine.ParticleSystem.MinMaxCurve(-.05f, .05f);
  var n = ps.noise; n.enabled = noise > 0f; n.strength = noise; n.frequency = .4f; n.scrollSpeed = .25f;
  var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(c0, c1, blink);
  var r = root.GetComponent<UnityEngine.ParticleSystemRenderer>();
  r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; r.sharedMaterial = mat;
  UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, $"{D}/{name}.prefab");
  UnityEngine.Object.DestroyImmediate(root);
  sb.Append(name + "\n");
}
Make("Dust motes", glow, new UnityEngine.Color(.75f, .95f, .9f, 1), new UnityEngine.Color(.6f, .85f, .85f, 1), false, new UnityEngine.Vector2(8, 14), new UnityEngine.Vector2(.025f, .045f), 3f, 40,
     new UnityEngine.Vector3(0, 1.5f, 0), new UnityEngine.Vector3(7f, 2.4f, 7f), new UnityEngine.Vector2(-.03f, .03f), .25f);
Make("Rising embers", glow, new UnityEngine.Color(1f, .75f, .3f, 1), new UnityEngine.Color(1f, .25f, .05f, 1), false, new UnityEngine.Vector2(2.5f, 4.5f), new UnityEngine.Vector2(.035f, .06f), 7f, 40,
     new UnityEngine.Vector3(0, .25f, 0), new UnityEngine.Vector3(6f, .3f, 6f), new UnityEngine.Vector2(.45f, .9f), .5f);
Make("Falling ash", ashMat, new UnityEngine.Color(.42f, .38f, .36f, 1), new UnityEngine.Color(.25f, .22f, .22f, 1), false, new UnityEngine.Vector2(9, 13), new UnityEngine.Vector2(.02f, .035f), 4f, 50,
     new UnityEngine.Vector3(0, 2.8f, 0), new UnityEngine.Vector3(7f, .2f, 7f), new UnityEngine.Vector2(-.3f, -.18f), .35f);
Make("Witch-fire", glow, new UnityEngine.Color(.35f, 1f, .5f, 1), new UnityEngine.Color(.2f, .9f, .6f, 1), true, new UnityEngine.Vector2(6, 9), new UnityEngine.Vector2(.07f, .11f), 1.2f, 10,
     new UnityEngine.Vector3(0, 1.2f, 0), new UnityEngine.Vector3(6f, 1.4f, 6f), new UnityEngine.Vector2(-.05f, .08f), .6f);
UnityEditor.AssetDatabase.SaveAssets();
System.IO.Directory.CreateDirectory("Temp/Realms");
System.IO.File.WriteAllText("Temp/Realms/life.txt", sb.ToString());
return "ok";
