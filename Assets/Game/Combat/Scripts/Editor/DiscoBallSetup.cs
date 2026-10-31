using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// The Disco Ball spell (Combat/Spells/Disco Ball) and the disco boss test: a spinning mirror-tile ball (texture from
// Tools/disco_ball_texture.py, 128 texels a metre) with a light walking round the party colours, thrown like a fireball
// and bursting into glitter. Its projectile, enemy hand and impact are copies of Fireball's with the fire switched off
// and the ball put in (made again, they keep their ids). Boss() gives the Crypt Lord the disco balls in place of his fireballs, dances the TutDance during
// the DM's beam in place of kneeling and plays 05 - Battle 1 as the fight's music. Run from an eval: DiscoBallSetup.Build(),
// then DiscoBallSetup.Boss(). Safe to run again (the copies are made anew).
public static class DiscoBallSetup
{
    const string Folder = "Assets/Game/Combat/Spells/Disco Ball/", Fire = "Assets/Game/Combat/Spells/Fireball/";
    static readonly Color Pink = new(1f, .35f, .8f);

    public static string Build()
    {
        var baseMap = Import(Folder + "Disco Ball.png");
        var glowMap = Import(Folder + "Disco Ball emission.png");
        var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "Disco Ball.mat");
        if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, Folder + "Disco Ball.mat"); }
        material.SetTexture("_BaseMap", baseMap);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Metallic", .85f);
        material.SetFloat("_Smoothness", .8f);
        material.SetTexture("_EmissionMap", glowMap);
        material.SetColor("_EmissionColor", Color.white * 1.6f);
        material.EnableKeyword("_EMISSION");
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        EditorUtility.SetDirty(material);

        // The ball: Unity's sphere (round, its UVs wrap once round and pole to pole as the texture is laid), 0.4 m across.
        // The root stays at scale 1 (texel checks measure a prefab at its root's scale) and turns; the mesh is its child.
        var ball = new GameObject("Disco Ball");
        var mesh = new GameObject("Ball");
        mesh.transform.SetParent(ball.transform, false);
        mesh.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        var renderer = mesh.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mesh.transform.localScale = Vector3.one * .2f;   // the built-in sphere mesh is 2 units across
        var spin = ball.AddComponent<DiscoBallSpin>();
        var lightGo = new GameObject("Sparkle light");
        lightGo.transform.SetParent(ball.transform, false);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point; light.range = 5f; light.intensity = 6f; light.color = Pink; light.shadows = LightShadows.None;
        spin.sparkle = light;
        var art = PrefabUtility.SaveAsPrefabAsset(ball, Folder + "Disco Ball.prefab");
        Object.DestroyImmediate(ball);

        string projectile = Copy(Fire + "Fireball Projectile.prefab", Folder + "Disco Ball Projectile.prefab", root =>
        {
            Quench(root);
            Put(art, root.transform, 1f);
            foreach (var v in root.GetComponentsInChildren<SpellVisual>(true)) if (v.glow != null) v.glow.color = Pink;
        });
        string hand = Copy(Fire + "Fireball Enemy Hand.prefab", Folder + "Disco Ball Enemy Hand.prefab", root =>
        {
            Quench(root);
            Put(art, root.transform, .6f);
            foreach (var v in root.GetComponentsInChildren<SpellVisual>(true)) if (v.glow != null) v.glow.color = Pink;
        });
        string impact = Copy(Fire + "Fireball Impact.prefab", Folder + "Disco Ball Impact.prefab", root =>
        {
            // The burst stays the burst, in the party colours: each particle picks one.
            var gradient = new Gradient();
            gradient.mode = GradientMode.Fixed;
            gradient.SetKeys(new[] { new GradientColorKey(Pink, .25f), new GradientColorKey(new Color(.3f, .9f, 1f), .5f), new GradientColorKey(new Color(1f, .82f, .3f), .75f), new GradientColorKey(new Color(.65f, .45f, 1f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.startColor = new ParticleSystem.MinMaxGradient(gradient) { mode = ParticleSystemGradientMode.RandomColor };
                var colour = ps.colorOverLifetime;
                if (colour.enabled) colour.enabled = false;
            }
            foreach (var v in root.GetComponentsInChildren<SpellVisual>(true)) if (v.glow != null) v.glow.color = Pink;
        });

        var fireball = AssetDatabase.LoadAssetAtPath<SpellDefinition>(Fire + "Fireball.asset");
        var spellPath = Folder + "Disco Ball.asset";
        CopyKeepingId(Fire + "Fireball.asset", spellPath);
        var spell = AssetDatabase.LoadAssetAtPath<SpellDefinition>(spellPath);
        spell.displayName = "Disco Ball";
        spell.description = "Throws a spinning disco ball that bursts into glitter.";
        spell.projectile = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "Disco Ball Projectile.prefab").GetComponent<SpellProjectile>();
        spell.impact = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "Disco Ball Impact.prefab").GetComponent<SpellVisual>();
        EditorUtility.SetDirty(spell);
        AssetDatabase.SaveAssets();
        return $"disco ball art, {projectile}, {hand}, {impact}, spell (projectile {spell.projectile != null}, impact {spell.impact != null}; fireball {fireball != null})";
    }

    // The Crypt Lord as the disco boss: his fireballs become disco balls, he dances the TutDance during the beam, and
    // the fight plays 05 - Battle 1.
    public static string Boss()
    {
        var spell = AssetDatabase.LoadAssetAtPath<SpellDefinition>(Folder + "Disco Ball.asset");
        var fireball = AssetDatabase.LoadAssetAtPath<SpellDefinition>(Fire + "Fireball.asset");
        var hand = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "Disco Ball Enemy Hand.prefab");
        if (spell == null || hand == null) return "run DiscoBallSetup.Build() first";
        const string lordPath = "Assets/Game/Characters/Enemies/Crypt/Crypt Lord.prefab";
        var root = PrefabUtility.LoadPrefabContents(lordPath);
        int swapped = 0;
        foreach (var character in root.GetComponentsInChildren<Character>(true))
        {
            if (!(character.data is EnemyData data) || data.spells == null) continue;
            foreach (var s in data.spells)
                if (s != null && (s.spell == fireball || s.spell == null)) { s.spell = spell; s.handEffect = hand.GetComponent<SpellVisual>(); swapped++; }
            EditorUtility.SetDirty(data);
        }
        PrefabUtility.SaveAsPrefabAsset(root, lordPath);
        PrefabUtility.UnloadPrefabContents(root);

        var fight = AssetDatabase.LoadAssetAtPath<BossFight>("Assets/Game/Levels/Dungeon1/Biomes/Crypt Lord fight.asset");
        const string dancePath = "Assets/Game/Characters/Animations/ThirdPerson/Gestures/TutDance.fbx";
        AnimationClip dance = null;
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(dancePath)) if (a is AnimationClip c && !c.name.StartsWith("__preview")) dance = c;
        Undo.RecordObject(fight, "Disco boss");
        fight.beamKneel = true;
        fight.beamDance = dance;
        fight.music = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Game/Audio/Music/Xdeviruchi/05 - Battle 1.mp3");
        EditorUtility.SetDirty(fight);
        AssetDatabase.SaveAssets();
        return $"Crypt Lord: {swapped} fireballs are disco balls; beam dance {(dance != null ? dance.name + (dance.isLooping ? " (loops)" : " (does not loop)") : "MISSING")}; music {(fight.music != null ? fight.music.name : "MISSING")}";
    }

    static Texture2D Import(string path)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.wrapModeU = TextureWrapMode.Repeat; importer.wrapModeV = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // Fireball's flames, sparks and smoke switched off (the light and the scripts stay).
    static void Quench(GameObject root)
    {
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (ps.gameObject != root) { ps.gameObject.SetActive(false); continue; }
            var emission = ps.emission; emission.enabled = false;   // a system on the root itself: kept, but silent
            var r = ps.GetComponent<ParticleSystemRenderer>(); if (r != null) r.enabled = false;
        }
    }

    static void Put(GameObject art, Transform parent, float scale)
    {
        var ball = (GameObject)PrefabUtility.InstantiatePrefab(art, parent);
        ball.transform.localPosition = Vector3.zero;
        ball.transform.localScale *= scale;
    }

    // A copy that keeps the copy's own id when it is made again, so what points at it (the spell, the Crypt Lord) holds.
    static bool CopyKeepingId(string from, string to)
    {
        if (!System.IO.File.Exists(to)) return AssetDatabase.CopyAsset(from, to);
        System.IO.File.Copy(from, to, true);
        AssetDatabase.ImportAsset(to, ImportAssetOptions.ForceUpdate);
        return true;
    }

    static string Copy(string from, string to, System.Action<GameObject> change)
    {
        if (!CopyKeepingId(from, to)) return "could not copy " + from;
        var root = PrefabUtility.LoadPrefabContents(to);
        change(root);
        PrefabUtility.SaveAsPrefabAsset(root, to);
        PrefabUtility.UnloadPrefabContents(root);
        return System.IO.Path.GetFileNameWithoutExtension(to);
    }
}
