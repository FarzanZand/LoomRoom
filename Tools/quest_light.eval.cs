// The light a quest puts on a place (QuestGlow, QuestLight; F9 43e47c0c, the user, 2026-11-16: the blue outline "is real
// ugly and sticks out too much in the room, do something more fitting"). Run as an eval outside Play (safe to run again:
// it keeps the look set on the prefab's QuestLight and on the two materials; the pieces themselves are made again):
//   unity command eval_file --file Tools/quest_light.eval.cs
// Makes Progression/Resources/Quest light.prefab from the dungeon's own pieces: the moon shaft's volume and shader in a
// warm material of its own (Progression/Room/Quest shaft.mat), a spot lamp, and the dungeon air's dust motes in a cone
// (Progression/Room/Quest motes.mat, a copy of the dungeon air's). QuestGlow fits it to each place at run time.
const string Prefab = "Assets/Game/Progression/Resources/Quest light.prefab";
const string ShaftMat = "Assets/Game/Progression/Room/Quest shaft.mat";
const string MoteMat = "Assets/Game/Progression/Room/Quest motes.mat";
if (UnityEditor.EditorApplication.isPlaying) return "playing: stop first";
var log = new System.Text.StringBuilder();
var volume = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Game/Levels/Shared/Props/Effects/Moon shaft volume.asset");
var moonMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Levels/Shared/Props/Effects/Moon shaft warm.mat");
var air = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Levels/Resources/Dungeon air.prefab");
if (volume == null || moonMat == null || air == null) return "missing the moon shaft's volume or material, or the dungeon air";
var airMotes = air.GetComponent<ParticleSystem>(); var airDrawn = air.GetComponent<ParticleSystemRenderer>();

var shaftMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(ShaftMat);
if (shaftMat == null)
{
    shaftMat = new Material(moonMat);
    shaftMat.SetColor("_BaseColor", new Color(1f, .84f, .56f, 1f));
    shaftMat.SetFloat("_Intensity", .3f);
    shaftMat.SetFloat("_TopFade", .5f);      // no hard start under the ceiling
    shaftMat.SetFloat("_FootFade", .5f);
    shaftMat.SetFloat("_SoftFade", .3f);
    shaftMat.SetFloat("_Streaks", .1f);
    UnityEditor.AssetDatabase.CreateAsset(shaftMat, ShaftMat);
    log.AppendLine("made " + ShaftMat);
}
var moteMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(MoteMat);
if (moteMat == null)
{
    moteMat = new Material(airDrawn.sharedMaterial);
    UnityEditor.AssetDatabase.CreateAsset(moteMat, MoteMat);
    log.AppendLine("made " + MoteMat + " (shader " + moteMat.shader.name + ")");
}

// The look already set on an existing prefab is kept.
var old = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
QuestLight kept = old != null ? old.GetComponent<QuestLight>() : null;

var root = new GameObject("Quest light");
try
{
var q = root.AddComponent<QuestLight>();
if (kept != null) UnityEditor.EditorUtility.CopySerialized(kept, q);

var shaftGo = new GameObject("Shaft"); shaftGo.transform.SetParent(root.transform, false);
shaftGo.AddComponent<MeshFilter>().sharedMesh = volume;
var shaft = shaftGo.AddComponent<MeshRenderer>();
shaft.sharedMaterial = shaftMat;
shaft.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; shaft.receiveShadows = false;
shaft.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; shaft.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

var lampGo = new GameObject("Lamp"); lampGo.transform.SetParent(root.transform, false);
lampGo.transform.localPosition = new Vector3(0, 3, 0); lampGo.transform.localRotation = Quaternion.Euler(90, 0, 0);
var lamp = lampGo.AddComponent<Light>();
lamp.type = LightType.Spot; lamp.shadows = LightShadows.Soft; lamp.shadowStrength = .85f;   // unshadowed, the floor under a table was lit through its top
 lamp.color = new Color(1f, .84f, .56f); lamp.intensity = 40f; lamp.range = 5f; lamp.spotAngle = 40f;
var lampData = lampGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
// Its own shadow settings: at the pipeline's, a thin table top let a patch of light through onto the floor under it.
lampData.usePipelineSettings = false; lamp.shadowBias = .05f; lamp.shadowNormalBias = .1f; lamp.shadowNearPlane = .2f;
{ var so = new UnityEditor.SerializedObject(lampData); var tier = so.FindProperty("m_AdditionalLightsShadowResolutionTier"); if (tier != null) { tier.intValue = UnityEngine.Rendering.Universal.UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierHigh; so.ApplyModifiedPropertiesWithoutUndo(); } else log.AppendLine("no shadow resolution tier on the lamp's data"); }

var motesGo = new GameObject("Motes"); motesGo.transform.SetParent(root.transform, false);
motesGo.transform.localPosition = new Vector3(0, 3, 0); motesGo.transform.localRotation = Quaternion.Euler(90, 0, 0);
var motes = motesGo.AddComponent<ParticleSystem>();
UnityEditor.EditorUtility.CopySerialized(airMotes, motes);
UnityEditor.EditorUtility.CopySerialized(airDrawn, motesGo.GetComponent<ParticleSystemRenderer>());
{
    // The dungeon air's motes, hung in a cone that is this object's own: they stay with it, loop, and are there from the start.
    var main = motes.main; var shape = motes.shape; var emission = motes.emission;
    main.simulationSpace = ParticleSystemSimulationSpace.Local; main.scalingMode = ParticleSystemScalingMode.Local;
    main.loop = true; main.prewarm = true; main.playOnAwake = true; main.maxParticles = 30;
    shape.enabled = true; shape.shapeType = ParticleSystemShapeType.ConeVolume; shape.radius = .2f; shape.angle = 12f; shape.length = 3f;
    shape.position = Vector3.zero; shape.rotation = Vector3.zero; shape.scale = Vector3.one;
    emission.enabled = true; emission.rateOverTime = 4f;
    var drawn = motesGo.GetComponent<ParticleSystemRenderer>();
    drawn.sharedMaterial = moteMat; drawn.enabled = true;
    drawn.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; drawn.receiveShadows = false;
    log.AppendLine("motes: life " + main.startLifetime.constantMin + ".." + main.startLifetime.constantMax + " (" + main.startLifetime.mode + ") speed x" + main.startSpeedMultiplier + " noise " + motes.noise.enabled + " x" + motes.noise.strengthMultiplier + " drift " + motes.velocityOverLifetime.enabled + " size " + main.startSize.constantMin + ".." + main.startSize.constantMax + " collision " + motes.collision.enabled + " lights " + motes.lights.enabled);
}

q.shaft = shaft; q.lamp = lamp; q.motes = motes;
System.IO.Directory.CreateDirectory("Assets/Game/Progression/Resources");
UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, Prefab);
}
finally { UnityEngine.Object.DestroyImmediate(root); }   // never left in the open scene, whatever went wrong
UnityEditor.AssetDatabase.SaveAssets();
log.AppendLine((kept != null ? "rebuilt " : "made ") + Prefab);
return log.ToString();
