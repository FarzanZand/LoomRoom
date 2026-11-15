// Dust in the dungeon's air (DungeonAir): makes Levels/Resources/Dungeon air.prefab and its material. Run as an eval
// (safe to run again): unity command eval_file --file Tools/dungeon_air.eval.cs
const string Dir = "Assets/Game/Levels/Resources";
var shader = Shader.Find("LoomRoom/Air Mote");
if (shader == null) return "no LoomRoom/Air Mote shader";
string matPath = Dir + "/Dungeon air mote.mat";
var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(matPath);
if (mat == null) { mat = new Material(shader); UnityEditor.AssetDatabase.CreateAsset(mat, matPath); }
mat.shader = shader;
mat.SetColor("_BaseColor", new Color(1f, .93f, .82f, 1f)); mat.SetFloat("_Strength", 1.1f);
mat.SetFloat("_Near", 1f); mat.SetFloat("_NearFull", 2.6f); mat.SetFloat("_Far", 11f);
UnityEditor.EditorUtility.SetDirty(mat);

var go = new GameObject("Dungeon air");
var ps = go.AddComponent<ParticleSystem>();
ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
var main = ps.main;
main.simulationSpace = ParticleSystemSimulationSpace.World; main.loop = true; main.playOnAwake = false; main.duration = 5; main.prewarm = false;
main.maxParticles = 420;
main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f); main.startSpeed = 0;
// About a texel of the dungeon's 128 a metre: larger, the near ones were bright squares, not dust.
main.startSize = new ParticleSystem.MinMaxCurve(.006f, .011f);
main.startColor = new ParticleSystem.MinMaxGradient(new Color(1, 1, 1, .55f), new Color(1, 1, 1, 1f));
var em = ps.emission; em.rateOverTime = 44;
// A box of air round the player: 18 m square, floor to ceiling.
var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(18f, 2.9f, 18f);
var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
vel.x = new ParticleSystem.MinMaxCurve(-.025f, .025f); vel.y = new ParticleSystem.MinMaxCurve(-.03f, .012f); vel.z = new ParticleSystem.MinMaxCurve(-.025f, .025f);
var noise = ps.noise; noise.enabled = true; noise.strength = .07f; noise.frequency = .35f; noise.scrollSpeed = .08f; noise.quality = ParticleSystemNoiseQuality.Low;
var g = new Gradient();
g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
          new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .15f), new GradientAlphaKey(1, .8f), new GradientAlphaKey(0, 1) });
var colour = ps.colorOverLifetime; colour.enabled = true; colour.color = g;
var r = go.GetComponent<ParticleSystemRenderer>();
r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.Billboard;
r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
r.maxParticleSize = .006f;   // close to the eye a mote stays a speck (six pixels of a 1080 picture at most)
r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
UnityEditor.PrefabUtility.SaveAsPrefabAsset(go, Dir + "/Dungeon air.prefab");
UnityEngine.Object.DestroyImmediate(go);
UnityEditor.AssetDatabase.SaveAssets();
return "made " + Dir + "/Dungeon air.prefab";
