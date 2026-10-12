using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Puts the painted, rigged Meshy creatures (Crypt_Rat_E, Cave_Bat_B, Urn_Crawler_B in Assets/Game/Meshy) into
// the game. Each FBX take is copied into an editable .anim next to the enemy (paths under the prefab's "Model"
// child, loops set, the bite's OnAttackHit on its contact frame), the Crypt Rat's controller is copied with those
// clips and the creature's walk and run speeds, and the enemy prefab gets the model, its material, collider and
// agent sizes. The Crypt Rat is rebuilt in place (its old model goes to Enemies/_Archive/Crypt Rat (box)); the
// bat and the urn crawler are copies of it with their own data. Safe to run again.
public static class MeshyCreatureSetup
{
    const string Crypt = "Assets/Game/Characters/Enemies/Crypt";
    const string RatPrefab = Crypt + "/Crypt Rat.prefab";

    class Spec
    {
        public string name, fbx, texture;
        public float hitTime, walk, run;
        public Vector3 capsuleCenter; public float capsuleRadius, capsuleHeight; public int capsuleDirection;
        public float agentRadius, agentHeight, eyeHeight;
        public bool twoSided;   // thin single surfaces (the bat's wing membranes)
    }

    static readonly Spec Rat = new()
    {
        name = "Crypt Rat", fbx = "Assets/Game/Meshy/Crypt_Rat_E/Crypt_Rat_E_rigged.fbx", texture = "Assets/Game/Meshy/Crypt_Rat_E/Crypt_Rat_E_painted.png",
        hitTime = .433f, walk = .5f, run = 2.4f,
        capsuleCenter = new Vector3(0f, .32f, 0f), capsuleRadius = .3f, capsuleHeight = 1f, capsuleDirection = 2,
        agentRadius = .35f, agentHeight = .65f, eyeHeight = .4f,
    };
    static readonly Spec Bat = new()
    {
        name = "Cave Bat", fbx = "Assets/Game/Meshy/Cave_Bat_B/Cave_Bat_B_rigged.fbx", texture = "Assets/Game/Meshy/Cave_Bat_B/Cave_Bat_B_painted.png",
        hitTime = .467f, walk = .7f, run = 3f,
        capsuleCenter = new Vector3(0f, 1.22f, 0f), capsuleRadius = .28f, capsuleHeight = .95f, capsuleDirection = 0,
        agentRadius = .35f, agentHeight = 1.5f, eyeHeight = 1.2f, twoSided = true,
    };
    static readonly Spec Urn = new()
    {
        name = "Urn Crawler", fbx = "Assets/Game/Meshy/Urn_Crawler_B/Urn_Crawler_B_rigged.fbx", texture = "Assets/Game/Meshy/Urn_Crawler_B/Urn_Crawler_B_painted.png",
        hitTime = .433f, walk = .6f, run = 1.6f,
        capsuleCenter = new Vector3(0f, .5f, 0f), capsuleRadius = .42f, capsuleHeight = 1f, capsuleDirection = 1,
        agentRadius = .45f, agentHeight = 1f, eyeHeight = .8f,
    };

    [MenuItem("Tools/LoomRoom/Enemies/Set Up Meshy Creatures")]
    public static string Run()
    {
        var log = new System.Text.StringBuilder();
        ArchiveOldRat(log);
        Build(Rat, RatPrefab, log);
        foreach (var spec in new[] { Bat, Urn })
        {
            string path = $"{Crypt}/{spec.name}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) AssetDatabase.CopyAsset(RatPrefab, path);
            Build(spec, path, log);
        }
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    // The box-built rat's mesh, fur and eyes are kept for reference, out of the enemy's folder.
    static void ArchiveOldRat(System.Text.StringBuilder log)
    {
        const string archive = "Assets/Game/Characters/Enemies/_Archive/Crypt Rat (box)";
        if (!AssetDatabase.IsValidFolder(archive)) AssetDatabase.CreateFolder("Assets/Game/Characters/Enemies/_Archive", "Crypt Rat (box)");
        foreach (var file in new[] { "Crypt Rat mesh.asset", "Crypt Rat fur.mat", "Crypt Rat fur.png", "Crypt Rat eyes.mat" })
        {
            string from = $"{Crypt}/Crypt Rat/{file}";
            if (AssetDatabase.LoadMainAssetAtPath(from) == null) continue;
            var error = AssetDatabase.MoveAsset(from, $"{archive}/{file}");
            log.AppendLine(string.IsNullOrEmpty(error) ? "archived " + file : error);
        }
    }

    static void Build(Spec spec, string prefabPath, System.Text.StringBuilder log)
    {
        string folder = $"{Crypt}/{spec.name}";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Crypt, spec.name);

        // Texture: point filtered at full size, so the painted texels stay as painted.
        var ti = (TextureImporter)AssetImporter.GetAtPath(spec.texture);
        ti.filterMode = FilterMode.Point; ti.npotScale = TextureImporterNPOTScale.None; ti.maxTextureSize = 4096;
        ti.textureCompression = TextureImporterCompression.Uncompressed; ti.SaveAndReimport();
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(spec.texture);

        string matPath = $"{folder}/{spec.name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, matPath);
        }
        material.SetTexture("_BaseMap", texture); material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Smoothness", 0f); material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Cull", spec.twoSided ? 0f : 2f); material.doubleSidedGI = spec.twoSided;
        EditorUtility.SetDirty(material);

        // Clips: each take copied under "Model/".
        var clips = new Dictionary<string, AnimationClip>();
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(spec.fbx))
        {
            if (asset is not AnimationClip source || source.name.StartsWith("__")) continue;
            string clipPath = $"{folder}/{spec.name} {source.name}.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, clipPath); }
            clip.ClearCurves();
            clip.frameRate = source.frameRate;
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                var b = binding; b.path = string.IsNullOrEmpty(b.path) ? "Model" : "Model/" + b.path;
                AnimationUtility.SetEditorCurve(clip, b, AnimationUtility.GetEditorCurve(source, binding));
            }
            bool loop = source.name is "idle" or "walk" or "run";
            var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = loop; AnimationUtility.SetAnimationClipSettings(clip, settings);
            AnimationUtility.SetAnimationEvents(clip, source.name == "bite" ? new[] { new AnimationEvent { functionName = "OnAttackHit", time = spec.hitTime } } : new AnimationEvent[0]);
            EditorUtility.SetDirty(clip);
            clips[source.name] = clip;
        }
        log.AppendLine($"{spec.name}: {clips.Count} clips");

        // Controller: the rat's state machine with this creature's clips and speeds.
        string controllerPath = $"{folder}/{spec.name}.controller";
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) == null)
            AssetDatabase.CopyAsset($"{Crypt}/Crypt Rat/Crypt Rat.controller", controllerPath);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        foreach (var child in controller.layers[0].stateMachine.states)
        {
            var state = child.state;
            switch (state.name)
            {
                case "Locomotion" when state.motion is BlendTree tree:
                    var children = tree.children;
                    if (children.Length >= 3)
                    {
                        children[0].motion = clips.GetValueOrDefault("idle"); children[0].threshold = 0f;
                        children[1].motion = clips.GetValueOrDefault("walk"); children[1].threshold = spec.walk;
                        children[2].motion = clips.GetValueOrDefault("run");  children[2].threshold = spec.run;
                        tree.children = children; EditorUtility.SetDirty(tree);
                    }
                    break;
                case "Bite":  state.motion = clips.GetValueOrDefault("bite"); break;
                case "Hurt":  state.motion = clips.GetValueOrDefault("hurt"); break;
                case "Death": state.motion = clips.GetValueOrDefault("death"); break;
            }
        }
        EditorUtility.SetDirty(controller);

        // Prefab: the model under "Model" at world scale 1 (the paint is at 150 texels per metre at that size).
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            root.name = spec.name;
            root.transform.localScale = Vector3.one;
            var old = root.transform.Find("Model");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(spec.fbx), root.transform);
            model.name = "Model";
            model.transform.SetSiblingIndex(0);
            model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity; model.transform.localScale = Vector3.one;
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>()) { smr.sharedMaterial = material; smr.updateWhenOffscreen = false; }
            var animator = root.GetComponent<Animator>(); animator.runtimeAnimatorController = controller; animator.avatar = null;
            var capsule = root.GetComponent<CapsuleCollider>();
            capsule.center = spec.capsuleCenter; capsule.radius = spec.capsuleRadius; capsule.height = spec.capsuleHeight; capsule.direction = spec.capsuleDirection;
            var agent = root.GetComponent<UnityEngine.AI.NavMeshAgent>(); agent.radius = spec.agentRadius; agent.height = spec.agentHeight;
            var character = root.GetComponent<Character>();
            if (character.data is EnemyData data)
            {
                data.name = spec.name; data.characterName = spec.name;
                data.behaviourSettings.eyeHeight = spec.eyeHeight;
                EditorUtility.SetDirty(data);
            }
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        log.AppendLine($"{spec.name}: prefab {prefabPath}");
    }
}
