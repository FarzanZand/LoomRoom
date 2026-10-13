using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Tools > LoomRoom > Combat > Set Up Shield Bash: everything the shield bash (ShieldBash, CombatManager > Player >
// Shield bash) needs, safe to run again: the arms clip Shield_Bash (made once from BlockIdle; edit it by hand after),
// its LeftArm state and trigger in FirstPersonTable.controller, the gold streaks and the sounds in
// Combat/Effects/Shield bash, the component on the table player and the references on the Combat Manager prefab.
public static class ShieldBashSetup
{
    const string Folder = "Assets/Game/Combat/Effects/Shield bash";
    const string ClipPath = "Assets/Game/Characters/Animations/FirstPersonPlayer/Shield_Bash.anim";
    const string BaseClipPath = "Assets/Game/Characters/Animations/FirstPersonPlayer/BlockIdle.anim";
    const string ControllerPath = "Assets/Game/Characters/Animations/FirstPersonTable.controller";
    const string PlayerPath = "Assets/Game/Characters/Players/Table/TablePlayer.prefab";
    const string ManagerPath = "Assets/Game/Combat/Prefabs/CombatManager.prefab";
    const string Sounds = "Assets/Game/Audio/Library/_Sound Effects Library/";

    [MenuItem("Tools/LoomRoom/Combat/Set Up Shield Bash")]
    public static void SetUpMenu() => Debug.Log("[ShieldBash] " + SetUp());

    public static string SetUp()
    {
        Directory.CreateDirectory(Folder);
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath) == null) BakeClip();
        WireController();
        var streaks = BuildStreaks();
        var swing = MakeAudio("Shield bash", Sounds + "SoundFX/Wpn_Whoosh_0{0}.wav", 5, .8f, .85f);
        var hit = MakeAudio("Shield bash hit", Sounds + "Weapons/Wpn_Bash_Shield_0{0}.wav", 5, 1f, .9f);

        var root = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            // Next to PlayerCombat (the Player's own object, not the prefab root).
            var owner = root.GetComponentInChildren<PlayerCombat>(true).gameObject;
            foreach (var stray in root.GetComponentsInChildren<ShieldBash>(true)) if (stray.gameObject != owner) Object.DestroyImmediate(stray);
            if (owner.GetComponent<ShieldBash>() == null) owner.AddComponent<ShieldBash>();
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        var manager = PrefabUtility.LoadPrefabContents(ManagerPath);
        try
        {
            var combat = manager.GetComponent<CombatManager>();
            if (combat.shieldBashStreaks == null) combat.shieldBashStreaks = streaks;
            if (combat.shieldBashAudio == null) combat.shieldBashAudio = swing;
            if (combat.shieldBashHitAudio == null) combat.shieldBashHitAudio = hit;
            PrefabUtility.SaveAsPrefabAsset(manager, ManagerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(manager); }
        AssetDatabase.SaveAssets();
        return "clip, LeftArm state, streaks, sounds, component and references in place";
    }

    // ── The arms clip ─────────────────────────────────────────────────

    // The bash pose is BlockIdle's guard with these offsets (local Euler degrees per left-arm bone): the shield
    // thrust forward, tipped flat and raised a little. Keys: guard, thrust out (outAt), held through the dash
    // (holdUntil), back to the guard at the end.
    public static Vector3 Clavicle = new(0, 0, 0), Shoulder = new(-28, 13, 0), Elbow = new(45, 0, 25), Hand = new(10, 0, 0);
    public static float OutAt = .06f, HoldUntil = .4f, Length = .62f;

    public static string BakeClip()
    {
        var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(BaseClipPath);
        if (source == null) return "no " + BaseClipPath;
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        bool created = clip == null;
        if (created) clip = new AnimationClip { name = "Shield_Bash" };
        clip.ClearCurves();
        foreach (var binding in AnimationUtility.GetCurveBindings(source))
        {
            float guard = AnimationUtility.GetEditorCurve(source, binding).Evaluate(0f);
            float thrust = guard + Offset(binding);
            var curve = new AnimationCurve(
                new Keyframe(0f, guard), new Keyframe(OutAt, thrust), new Keyframe(HoldUntil, thrust + (thrust - guard) * .04f),
                new Keyframe(Length, guard));
            for (int i = 0; i < curve.length; i++) AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            for (int i = 0; i < curve.length; i++) AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        if (created) AssetDatabase.CreateAsset(clip, ClipPath);
        EditorUtility.SetDirty(clip);
        AssetDatabase.SaveAssets();
        return $"baked {ClipPath}";
    }

    static float Offset(EditorCurveBinding b)
    {
        if (!b.propertyName.StartsWith("localEulerAnglesRaw")) return 0f;
        int axis = b.propertyName.EndsWith(".x") ? 0 : b.propertyName.EndsWith(".y") ? 1 : 2;
        string bone = b.path.Substring(b.path.LastIndexOf('/') + 1);
        return bone switch { "Clavicle_L" => Clavicle[axis], "Shoulder_L" => Shoulder[axis], "Elbow_L" => Elbow[axis], "Hand_L" => Hand[axis], _ => 0f };
    }

    // ── The animator ──────────────────────────────────────────────────

    static void WireController()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        if (System.Array.Find(controller.parameters, p => p.name == "ShieldBash") == null)
            controller.AddParameter("ShieldBash", AnimatorControllerParameterType.Trigger);
        var layer = System.Array.Find(controller.layers, l => l.name == "LeftArm");
        var machine = layer.stateMachine;
        AnimatorState state = null, idle = null;
        foreach (var s in machine.states) { if (s.state.name == "ShieldBash") state = s.state; if (s.state.name == "Idle") idle = s.state; }
        if (state == null)
        {
            state = machine.AddState("ShieldBash", new Vector3(500, 260, 0));
            state.tag = "ShieldBash";
            var into = machine.AddAnyStateTransition(state);
            into.AddCondition(AnimatorConditionMode.If, 0, "ShieldBash");
            into.AddCondition(AnimatorConditionMode.If, 0, "LeftHandEquipped");
            into.hasExitTime = false; into.duration = .04f; into.canTransitionToSelf = false;
            if (idle != null)
            {
                var back = state.AddTransition(idle);
                back.hasExitTime = true; back.exitTime = .92f; back.duration = .12f;
            }
        }
        state.motion = clip;
        EditorUtility.SetDirty(controller);
    }

    // ── Streaks ───────────────────────────────────────────────────────

    public static bool RebuildStreaks;

    // Gold light streaks rushing past the edges of the view toward the camera while it dashes (under the camera).
    static GameObject BuildStreaks()
    {
        string prefabPath = Folder + "/Shield bash streaks.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existing != null && !RebuildStreaks) return existing;

        string texPath = Folder + "/Streak.png";
        var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                float dx = (x - 15.5f) / 15.5f, dy = (y - 15.5f) / 15.5f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                tex.SetPixel(x, y, new Color(1, 1, 1, a * a));
            }
        File.WriteAllBytes(texPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(texPath);

        var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "Shield bash streak" };
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
        material.SetFloat("_Surface", 1); material.SetFloat("_Blend", 2);   // transparent, additive
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        material.SetInt("_ZWrite", 0);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.EnableKeyword("_BLENDMODE_ADD");
        material.renderQueue = 3000;
        AssetDatabase.DeleteAsset(Folder + "/Shield bash streak.mat");
        AssetDatabase.CreateAsset(material, Folder + "/Shield bash streak.mat");

        var go = new GameObject("Shield bash streaks");
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false; main.loop = true; main.duration = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.14f, .24f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(24f, 34f);
        main.startSize = new ParticleSystem.MinMaxCurve(.14f, .24f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1.5f, 1f, .3f, 1f), new Color(1.7f, 1.3f, .55f, 1f));
        main.maxParticles = 400;
        var emission = ps.emission; emission.rateOverTime = 220f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 4.6f; shape.radiusThickness = .7f;   // a ring round the edge of the view, none in the middle
        shape.position = new Vector3(0, 0, 6f);
        shape.rotation = new Vector3(0, 180f, 0);           // flying toward the camera
        var fade = ps.colorOverLifetime; fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .25f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = .1f; renderer.lengthScale = 5f;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        Object.DestroyImmediate(go);
        return prefab;
    }

    static AudioData MakeAudio(string name, string pattern, int count, float volume, float pitch)
    {
        string path = $"{Folder}/{name}.asset";
        var data = AssetDatabase.LoadAssetAtPath<AudioData>(path);
        if (data != null) return data;
        data = ScriptableObject.CreateInstance<AudioData>();
        var clips = new List<AudioClip>();
        for (int i = 1; i <= count; i++) { var c = AssetDatabase.LoadAssetAtPath<AudioClip>(string.Format(pattern, i)); if (c != null) clips.Add(c); }
        data.clips = clips.ToArray(); data.volume = volume; data.pitch = pitch; data.pitchVariance = .06f;
        AssetDatabase.CreateAsset(data, path);
        return data;
    }
}
