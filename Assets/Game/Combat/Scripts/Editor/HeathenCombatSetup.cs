using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Builds and wires the Heathen impact pieces: blood spray, bone dust, contact flash, stains and chunks
// (GoreManager under Systems), slash lines on the table player's blade, enemy swing trails, helmets that
// fly off, kill sounds and each enemy's blood. Safe to run again: it updates what is there.
public static class HeathenCombatSetup
{
    const string Gore = "Assets/Game/Combat/Effects/Gore";
    const string Kills = "Assets/Game/Audio/Clips/Kills";

    [MenuItem("Tools/LoomRoom/Combat/Set Up Heathen Impact")]
    public static string Run()
    {
        var log = new System.Text.StringBuilder();
        foreach (var tex in new[] { "Blood splats", "Blood drop", "Contact flash" }) PixelTexture($"{Gore}/{tex}.png");

        var stain = Material($"{Gore}/Blood stain.mat", "LoomRoom/Pixel Lit", m =>
        {
            m.SetTexture("_BaseMap", Tex("Blood splats")); m.SetFloat("_AlphaClip", 1f); m.EnableKeyword("_ALPHATEST_ON"); m.SetFloat("_Cutoff", .5f);
        });
        var chunk = Material($"{Gore}/Gore chunk.mat", "LoomRoom/Pixel Lit", m => m.SetColor("_BaseColor", Color.white));
        // Unlit, so blood stays vivid in the dark dungeon (Heathen's sprays read in any light).
        var drop = Material($"{Gore}/Blood drop.mat", "Universal Render Pipeline/Particles/Unlit", m => m.SetTexture("_BaseMap", Tex("Blood drop")));
        var flash = Material($"{Gore}/Contact flash.mat", "Sprites/Default", m => m.mainTexture = Tex("Contact flash"));
        var line = Material($"{Gore}/Slash line.mat", "Sprites/Default", null);

        var spray = Particles($"{Gore}/Blood spray.prefab", drop, ps =>
        {
            var main = ps.main; main.startLifetime = new ParticleSystem.MinMaxCurve(.5f, .9f); main.startSpeed = new ParticleSystem.MinMaxCurve(2.4f, 5.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(.045f, .1f); main.gravityModifier = 1.6f; main.maxParticles = 600;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 30f; shape.radius = .03f;
            var col = ps.collision; col.enabled = true; col.type = ParticleSystemCollisionType.World; col.mode = ParticleSystemCollisionMode.Collision3D;
            col.bounce = 0f; col.lifetimeLoss = 1f; col.dampen = 1f; col.sendCollisionMessages = true; col.quality = ParticleSystemCollisionQuality.Medium; col.radiusScale = .5f;
        }, typeof(GoreDroplets));
        var dust = Particles($"{Gore}/Bone dust.prefab", drop, ps =>
        {
            // Chips burst out of the bone and are gone in a moment, shrinking as they fly; they never rain down.
            var main = ps.main; main.startLifetime = new ParticleSystem.MinMaxCurve(.18f, .42f); main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 6.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(.03f, .065f); main.gravityModifier = .35f; main.maxParticles = 300;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 55f; shape.radius = .04f;
            var drag = ps.limitVelocityOverLifetime; drag.enabled = true; drag.drag = 4f; drag.multiplyDragByParticleSize = false;
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            var col = ps.collision; col.enabled = true; col.type = ParticleSystemCollisionType.World; col.bounce = .2f; col.dampen = .5f; col.lifetimeLoss = 1f; col.quality = ParticleSystemCollisionQuality.Medium;
        });
        var contact = Particles($"{Gore}/Contact flash.prefab", flash, ps =>
        {
            var main = ps.main; main.playOnAwake = true; main.startLifetime = .07f; main.startSpeed = 0f; main.startSize = new ParticleSystem.MinMaxCurve(.32f, .46f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f); main.maxParticles = 4;
            var emission = ps.emission; emission.enabled = true; emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            var shape = ps.shape; shape.enabled = false;
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, .8f, 1f, 1.25f));
        });
        log.AppendLine("effects built");

        // GoreManager under Systems.
        string managerPath = "Assets/Game/Combat/Prefabs/GoreManager.prefab";
        var managerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(managerPath);
        if (managerPrefab == null)
        {
            var go = new GameObject("GoreManager", typeof(GoreManager));
            managerPrefab = PrefabUtility.SaveAsPrefabAsset(go, managerPath);
            Object.DestroyImmediate(go);
        }
        Edit(managerPath, root =>
        {
            var so = new SerializedObject(root.GetComponent<GoreManager>());
            so.FindProperty("sprayPrefab").objectReferenceValue = spray.GetComponent<ParticleSystem>();
            so.FindProperty("dustPrefab").objectReferenceValue = dust.GetComponent<ParticleSystem>();
            so.FindProperty("stainMaterial").objectReferenceValue = stain;
            so.FindProperty("chunkMaterial").objectReferenceValue = chunk;
            so.FindProperty("dropsPerHit").intValue = 45; so.FindProperty("dropsPerKill").intValue = 110; so.FindProperty("splashBlobs").intValue = 7;
            so.ApplyModifiedPropertiesWithoutUndo();
        });
        if (Object.FindAnyObjectByType<GoreManager>(FindObjectsInactive.Include) == null)
        {
            var combat = Object.FindAnyObjectByType<CombatManager>(FindObjectsInactive.Include);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(managerPrefab, combat.transform.parent);
            instance.transform.SetSiblingIndex(combat.transform.GetSiblingIndex() + 1);
            log.AppendLine("GoreManager placed under " + combat.transform.parent.name);
        }

        // CombatManager: the contact flash replaces the dust puff.
        Edit("Assets/Game/Combat/Prefabs/CombatManager.prefab", root =>
        {
            var cm = root.GetComponent<CombatManager>();
            cm.hitParticlePrefabs = new List<GameObject> { contact };
            cm.hitReactionAngle = 35f;
        });
        var sceneCm = Object.FindAnyObjectByType<CombatManager>(FindObjectsInactive.Include);
        RevertOverride(sceneCm, "hitParticlePrefabs"); RevertOverride(sceneCm, "hitReactionAngle"); RevertOverride(sceneCm, "ragdollImpulse"); RevertOverride(sceneCm, "ragdollUpwardImpulse");

        // Slash lines on the table player's blade.
        Edit("Assets/Game/Characters/Players/Table/TablePlayer.prefab", root =>
        {
            foreach (var hitbox in root.GetComponentsInChildren<Hitbox>(true))
            {
                var lines = hitbox.GetComponent<SlashLines>(); if (lines == null) lines = hitbox.gameObject.AddComponent<SlashLines>();
                var so = new SerializedObject(lines); so.FindProperty("material").objectReferenceValue = line; so.FindProperty("trail").floatValue = .045f; so.ApplyModifiedPropertiesWithoutUndo();
                log.AppendLine("slash lines on " + hitbox.name);
            }
        });

        // Enemies: swing trails and helmets on the humanoid base, recoil on the creatures without it.
        Edit("Assets/Game/Characters/Enemies/Base/Humanoid enemy base.prefab", root =>
        {
            var trail = root.GetComponent<EnemySwingTrail>(); if (trail == null) trail = root.AddComponent<EnemySwingTrail>();
            var so = new SerializedObject(trail); so.FindProperty("material").objectReferenceValue = line; so.ApplyModifiedPropertiesWithoutUndo();
            if (root.GetComponent<HelmetPop>() == null) root.AddComponent<HelmetPop>();
        });
        foreach (var path in new[] { "Assets/Game/Characters/Enemies/Crypt/Crypt Rat.prefab", "Assets/Game/Characters/Enemies/Crypt/Green Slime.prefab" })
            Edit(path, root =>
            {
                if (root.GetComponent<HitReactionController>() == null) root.AddComponent<HitReactionController>();
                // Normal enemies are thrown as ragdolls, not played dead.
                var ragdoll = root.GetComponent<EnemyRagdoll>();
                if (ragdoll != null) { var so = new SerializedObject(ragdoll); so.FindProperty("deathAnimationInstead").boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); }
            });

        // Kill sounds.
        var bones = Audio("Kill bones", .7f); var flesh = Audio("Kill flesh", .65f); var slime = Audio("Kill slime", .7f);

        // Each enemy's blood, and harder bone bursts for skeletons.
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Characters/Enemies" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("_Archive")) continue;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var character = go.GetComponent<Character>();
            if (character == null || character.data == null || path.Contains("/Base/")) continue;
            var gore = character.data.gore;
            bool skeleton = go.GetComponent<DeathBurst>() != null;
            bool isSlime = path.Contains("Slime");
            if (skeleton) { gore.kind = BloodKind.BoneDust; gore.color = new Color(.86f, .82f, .7f); gore.burstOnDeath = false; gore.killSound = bones; gore.amount = 1f; }
            else if (isSlime) { gore.kind = BloodKind.Slime; gore.color = new Color(.42f, .95f, .25f); gore.burstOnDeath = true; gore.killSound = slime; gore.amount = 1.3f; }
            else { gore.kind = BloodKind.Blood; gore.color = new Color(.72f, .05f, .04f); gore.burstOnDeath = false; gore.killSound = flesh; gore.amount = 1f; }
            EditorUtility.SetDirty(character.data);
            if (skeleton) Edit(path, root =>
            {
                var so = new SerializedObject(root.GetComponent<DeathBurst>());
                so.FindProperty("force").floatValue = 4.5f; so.FindProperty("upward").floatValue = 1.8f; so.FindProperty("scatter").floatValue = 1.4f;
                so.ApplyModifiedPropertiesWithoutUndo();
            });
            log.AppendLine($"{go.name}: {gore.kind}");
        }
        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(sceneCm.gameObject.scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(sceneCm.gameObject.scene);
        return log.ToString();
    }

    // Loot: beams over floor items, their names nearby, and a chest that glows and throws its loot.
    [MenuItem("Tools/LoomRoom/Combat/Set Up Heathen Loot")]
    public static string Loot()
    {
        var log = new System.Text.StringBuilder();
        const string Fx = "Assets/Game/Items/Effects";
        if (!AssetDatabase.IsValidFolder(Fx)) AssetDatabase.CreateFolder("Assets/Game/Items", "Effects");
        var beam = Material($"{Fx}/Loot beam.mat", "Sprites/Default", null);
        var spark = Material($"{Fx}/Chest spark.mat", "Universal Render Pipeline/Particles/Unlit", m => m.SetTexture("_BaseMap", Tex("Blood drop")));
        var burst = Particles($"{Fx}/Chest burst.prefab", spark, ps =>
        {
            var main = ps.main; main.playOnAwake = true; main.startLifetime = new ParticleSystem.MinMaxCurve(.5f, 1.1f); main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(.03f, .06f); main.gravityModifier = .35f; main.maxParticles = 80;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .85f, .35f), new Color(1f, .62f, .2f));
            var emission = ps.emission; emission.enabled = true; emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 40) });
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 35f; shape.radius = .2f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        });
        Edit("Assets/Game/Core/Prefabs/InventoryManager.prefab", root => root.GetComponent<InventoryManager>().beamMaterial = beam);
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Levels" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var container = go.GetComponent<DungeonContainer>();
            if (container == null || !container.chest) continue;
            Edit(path, root => root.GetComponent<DungeonContainer>().openBurst = burst);
            log.AppendLine("chest burst on " + go.name);
        }
        // The label template, authored under the HUD canvas.
        var hotbar = Object.FindAnyObjectByType<HotbarUI>(FindObjectsInactive.Include);
        var canvas = hotbar.transform.parent;
        var holder = canvas.Find("Loot labels");
        if (holder == null)
        {
            holder = new GameObject("Loot labels", typeof(RectTransform)).transform;
            holder.SetParent(canvas, false);
            holder.SetSiblingIndex(hotbar.transform.GetSiblingIndex());
            var rt = (RectTransform)holder; rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f); rt.sizeDelta = Vector2.zero;
            var label = new GameObject("Loot label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TMPro.TextMeshProUGUI)).GetComponent<TMPro.TextMeshProUGUI>();
            label.transform.SetParent(holder, false);
            label.font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator.asset") ?? label.font;
            label.fontSize = 26; label.alignment = TMPro.TextAlignmentOptions.Center; label.raycastTarget = false;
            label.textWrappingMode = TMPro.TextWrappingModes.NoWrap; label.outlineWidth = .25f; label.outlineColor = new Color32(0, 0, 0, 255);
            ((RectTransform)label.transform).sizeDelta = new Vector2(400, 40);
            var ui = holder.gameObject.AddComponent<LootLabelsUI>();
            var so = new SerializedObject(ui); so.FindProperty("template").objectReferenceValue = label; so.ApplyModifiedPropertiesWithoutUndo();
            label.gameObject.SetActive(false);
            log.AppendLine("loot labels added to the HUD");
        }
        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(canvas.gameObject.scene);
        return log.ToString();
    }

    static Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Gore}/{name}.png");

    static void PixelTexture(string path)
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        if (ti == null) { AssetDatabase.ImportAsset(path); ti = (TextureImporter)AssetImporter.GetAtPath(path); }
        ti.textureType = TextureImporterType.Default; ti.filterMode = FilterMode.Point; ti.mipmapEnabled = false;
        ti.textureCompression = TextureImporterCompression.Uncompressed; ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp;
        ti.SaveAndReimport();
    }

    static Material Material(string path, string shader, System.Action<Material> setup)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
        else m.shader = Shader.Find(shader);
        setup?.Invoke(m);
        EditorUtility.SetDirty(m);
        return m;
    }

    static GameObject Particles(string path, Material material, System.Action<ParticleSystem> setup, params System.Type[] extra)
    {
        var go = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path), typeof(ParticleSystem));
        foreach (var t in extra) go.AddComponent(t);
        var ps = go.GetComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.playOnAwake = false; main.loop = false; main.duration = 1f; main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission; emission.rateOverTime = 0f;
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = material; r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        setup(ps);
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    static AudioData Audio(string name, float volume)
    {
        string path = $"Assets/Game/Audio/Data/{name}.asset";
        var data = AssetDatabase.LoadAssetAtPath<AudioData>(path);
        if (data == null) { data = ScriptableObject.CreateInstance<AudioData>(); AssetDatabase.CreateAsset(data, path); }
        var clips = new List<AudioClip>();
        foreach (var guid in AssetDatabase.FindAssets($"t:AudioClip {name}", new[] { Kills })) clips.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid)));
        data.clips = clips.ToArray(); data.volume = volume; data.pitchVariance = .08f; data.minDistance = 2f; data.maxDistance = 30f;
        EditorUtility.SetDirty(data);
        return data;
    }

    static void Edit(string path, System.Action<GameObject> edit)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try { edit(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static void RevertOverride(Object target, string field)
    {
        if (target == null) return;
        var p = new SerializedObject(target).FindProperty(field);
        if (p != null && p.prefabOverride) PrefabUtility.RevertPropertyOverride(p, InteractionMode.AutomatedAction);
    }
}
