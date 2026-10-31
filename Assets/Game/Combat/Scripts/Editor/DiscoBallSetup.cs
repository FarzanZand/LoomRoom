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
            // He closes in to melee, so a ball that needed 3 m was never thrown once he was there: the single ball from the
            // two metres his footwork steps back to after a swing (its 1.35 s wind-up is the player's opening; any closer
            // and it burst in the player's face, its glitter shown back at his hand over the boss bar), the volley from a
            // step further.
            foreach (var s in data.spells)
                if (s != null && s.spell == spell) s.minRange = s.volley ? 2.5f : 2f;
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
        // Its beat, measured (librosa: 92.05 a minute, the first downbeat 0.41 s in). It goes round a 96-beat cycle whose
        // loudest bars start at beats 64, 160, 256: the beams land on those, the DM's volleys in the quieter half between.
        fight.bpm = 92.05f; fight.firstBeat = .41f;
        // His fall ends the party: the music cut dead on a record scratch.
        fight.deathSting = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Game/Audio/Clips/SFX/Music stop.mp3");
        fight.musicBackAfter = 3f;
        // Phase two is a light show: the stage's lights step through the disco ball's own colours on the beat.
        fight.phaseTwoDisco = new[] { new Color(1f, .3f, .8f), new Color(.25f, .85f, 1f), new Color(1f, .8f, .25f), new Color(.6f, .4f, 1f), new Color(.4f, 1f, .5f) };
        foreach (var e in fight.events)
        {
            if (e.what == BossEvent.What.Beam && e.when == BossEvent.When.EveryBeats) { e.startBeat = 64; e.beats = 96; }
            if (e.what == BossEvent.What.Volley && e.when == BossEvent.When.EveryBeats) { e.startBeat = 112; e.beats = 96; }
        }
        EditorUtility.SetDirty(fight);
        AssetDatabase.SaveAssets();
        return $"Crypt Lord: {swapped} fireballs are disco balls; beam dance {(dance != null ? dance.name + (dance.isLooping ? " (loops)" : " (does not loop)") : "MISSING")}; music {(fight.music != null ? fight.music.name : "MISSING")}";
    }

    // The disco boss's show (Levels/Dungeon1/Biomes/Disco; textures from Tools/disco_show_textures.py and
    // Tools/disco_ball_texture.py, sounds from Tools/boss_intro_sounds.py): the dance floor's material, the disco ball on
    // its chain (a 1 m ball at 128 texels a metre, the chain stacked from the realms' full height chains, a spot light
    // throwing dots), and on the Crypt Lord fight: his phase two line, the floor and the ball, the backup dancers (two
    // Crypt Soldiers doing the skeleton party's dance) and his defeat (a spin, the Point pose, a burst of glitter).
    // Safe to run again.
    public static string Show()
    {
        const string Disco = "Assets/Game/Levels/Dungeon1/Biomes/Disco/", Clips = "Assets/Game/Audio/Clips/Dialogue/";
        var tile = Import(Disco + "Dance floor tile.png", TextureWrapMode.Clamp);
        var dots = Import(Disco + "Disco spots.png", TextureWrapMode.Clamp);
        var floor = AssetDatabase.LoadAssetAtPath<Material>(Disco + "Dance floor.mat");
        if (floor == null) { floor = new Material(Shader.Find("LoomRoom/Dance Floor")); AssetDatabase.CreateAsset(floor, Disco + "Dance floor.mat"); }
        floor.shader = Shader.Find("LoomRoom/Dance Floor");
        floor.SetTexture("_BaseMap", tile);
        floor.SetFloat("_Intensity", 1f);
        EditorUtility.SetDirty(floor);

        // The big ball: the small ball's material with a texture at its own size.
        var ballMaterial = AssetDatabase.LoadAssetAtPath<Material>(Disco + "Disco Ball big.mat");
        if (ballMaterial == null) { ballMaterial = new Material(AssetDatabase.LoadAssetAtPath<Material>(Folder + "Disco Ball.mat")); AssetDatabase.CreateAsset(ballMaterial, Disco + "Disco Ball big.mat"); }
        ballMaterial.SetTexture("_BaseMap", Import(Disco + "Disco Ball big.png"));
        ballMaterial.SetTexture("_EmissionMap", Import(Disco + "Disco Ball big emission.png"));
        // Hung in the dark it has little to mirror: less metal so the stage's lights show on it, its lit tiles brighter.
        ballMaterial.SetFloat("_Metallic", .45f); ballMaterial.SetFloat("_Smoothness", .7f);
        ballMaterial.SetColor("_EmissionColor", Color.white * 3f);
        EditorUtility.SetDirty(ballMaterial);

        var rig = new GameObject("Disco ball rig");
        var show = rig.AddComponent<BossDiscoBall>();
        var ball = new GameObject("Ball");
        ball.transform.SetParent(rig.transform, false);
        var sphere = new GameObject("Mesh");
        sphere.transform.SetParent(ball.transform, false);
        sphere.transform.localScale = Vector3.one * .5f;   // the built-in sphere mesh is 2 units across: a 1 m ball
        sphere.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        var r = sphere.AddComponent<MeshRenderer>();
        r.sharedMaterial = ballMaterial; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var glow = new GameObject("Glow").AddComponent<Light>();
        glow.transform.SetParent(rig.transform, false);
        glow.transform.localPosition = new Vector3(0f, -.9f, 0f);
        glow.type = LightType.Point; glow.range = 3f; glow.intensity = 3f; glow.color = new Color(1f, .9f, 1f); glow.shadows = LightShadows.None;
        var spot = new GameObject("Spots").AddComponent<Light>();
        spot.transform.SetParent(rig.transform, false);
        spot.transform.localPosition = new Vector3(0f, -.55f, 0f);
        spot.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        spot.type = LightType.Spot; spot.spotAngle = 130f; spot.innerSpotAngle = 110f; spot.range = 14f; spot.intensity = 160f;
        spot.cookie = dots; spot.shadows = LightShadows.None;
        // The chain: copies of the realms' full height chains stacked up from the ball's top, out of sight above.
        var chainPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Levels/Dungeon1/Realms/Props/Full height chains.prefab");
        int links = 0;
        if (chainPrefab != null)
        {
            var chain = new GameObject("Chain");
            chain.transform.SetParent(rig.transform, false);
            float y = .48f;
            for (int i = 0; i < 7; i++)
            {
                var piece = (GameObject)PrefabUtility.InstantiatePrefab(chainPrefab, chain.transform);
                foreach (var c in piece.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                var bounds = new Bounds(piece.transform.position, Vector3.zero); bool any = false;
                foreach (var pr in piece.GetComponentsInChildren<Renderer>(true)) { if (!any) bounds = pr.bounds; else bounds.Encapsulate(pr.bounds); any = true; pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
                // Its foot on the top of the one below (or the ball), centred over the ball.
                piece.transform.position += new Vector3(-bounds.center.x, y - bounds.min.y, -bounds.center.z);
                y += bounds.size.y;
                links++;
            }
        }
        show.ball = ball.transform; show.spots = spot;
        show.lowerSound = AssetDatabase.LoadAssetAtPath<AudioClip>(Clips + "Disco ball lower.wav");
        var rigAsset = PrefabUtility.SaveAsPrefabAsset(rig, Disco + "Disco ball rig.prefab");
        Object.DestroyImmediate(rig);

        var fight = AssetDatabase.LoadAssetAtPath<BossFight>("Assets/Game/Levels/Dungeon1/Biomes/Crypt Lord fight.asset");
        Undo.RecordObject(fight, "Disco show");
        fight.phaseTwoBossLine = "Let's turn it up!";
        // The DM's beam comes at the boss's health, not on the beat: at 70% (the second phase begins with it) and at 30%.
        fight.phaseTwoHealth = .7f;
        BossEvent healthBeam = null;
        foreach (var e in fight.events)
        {
            if (e.what != BossEvent.What.Beam) continue;
            if (e.when == BossEvent.When.EveryBeats || e.when == BossEvent.When.AtBeat) e.enabled = false;
            if (e.when == BossEvent.When.AtBossHealth) { e.enabled = true; e.health = healthBeam == null ? .7f : .3f; healthBeam ??= e; }
        }
        if (healthBeam != null && !fight.events.Exists(e => e != healthBeam && e.what == BossEvent.What.Beam && e.when == BossEvent.When.AtBossHealth))
        {
            var second = new BossEvent();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(healthBeam), second);
            second.health = .3f;
            fight.events.Insert(fight.events.IndexOf(healthBeam) + 1, second);
        }
        fight.danceFloor = floor;
        fight.discoBall = rigAsset;
        fight.backupDancer = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Characters/Enemies/Crypt/Crypt Soldier.prefab");
        fight.backupCount = 2; fight.backupAtHealth = .45f; fight.backupHealth = 1f;
        fight.backupDance = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Game/Characters/Animations/ThirdPerson/Gestures/Skeleton dance.anim");
        fight.backupBossLine = "Boys! Get in here!";
        fight.backupBrokenLine = "Hey! That was my crew!";
        fight.backupLine = new DungeonMaster.Line { text = "Oh, he has a crew.", mumble = true };
        MakeDefeatPose();
        fight.defeatPose = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Game/Characters/Animations/ThirdPerson/Gestures/Disco pose.anim");
        fight.defeatSpinSeconds = .55f; fight.defeatHold = 1.1f;
        fight.defeatBurst = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "Disco Ball Impact.prefab");
        fight.defeatBurstScale = 2.5f;
        fight.defeatBurstSound = AssetDatabase.LoadAssetAtPath<AudioClip>(Clips + "Disco defeat burst.wav");
        EditorUtility.SetDirty(fight);
        AssetDatabase.SaveAssets();
        return $"dance floor {floor.shader.name}, disco ball rig ({links} chain pieces), backup {(fight.backupDancer != null ? fight.backupDancer.name : "MISSING")} dancing {(fight.backupDance != null ? fight.backupDance.name : "MISSING")}, defeat pose {(fight.defeatPose != null ? fight.defeatPose.name : "MISSING")}";
    }

    // The disco boss's last pose (Characters/Animations/ThirdPerson/Gestures/Disco pose.anim): the right arm thrown
    // straight up, the left hand on the hip, a lean away from the raised arm, the head tipped up after the hand, a leg out.
    // Made from the Point gesture's standing first frame (its body height and feet), eased into over a fifth of a second
    // and held. Edit the .anim freely; running this again overwrites it.
    public static readonly Dictionary<string, float> DiscoPose = new()
    {
        ["Right Shoulder Down-Up"] = .9f, ["Right Arm Down-Up"] = 1f, ["Right Arm Front-Back"] = .15f, ["Right Arm Twist In-Out"] = 0f,
        ["Right Forearm Stretch"] = 1f, ["Right Forearm Twist In-Out"] = 0f, ["Right Hand Down-Up"] = .2f,
        ["Left Arm Down-Up"] = -.15f, ["Left Arm Front-Back"] = -.35f, ["Left Arm Twist In-Out"] = .4f, ["Left Forearm Stretch"] = -.55f, ["Left Hand Down-Up"] = -.4f,
        ["Spine Left-Right"] = -.35f, ["Chest Left-Right"] = -.25f, ["UpperChest Left-Right"] = -.2f,
        ["Head Nod Down-Up"] = .35f, ["Head Tilt Left-Right"] = .2f, ["Neck Nod Down-Up"] = .2f,
        ["Right Upper Leg In-Out"] = .35f, ["Left Upper Leg In-Out"] = -.05f,
    };

    public static string MakeDefeatPose()
    {
        const string source = "Assets/Game/Characters/Animations/ThirdPerson/Gestures/Point.anim", target = "Assets/Game/Characters/Animations/ThirdPerson/Gestures/Disco pose.anim";
        var point = AssetDatabase.LoadAssetAtPath<AnimationClip>(source);
        if (point == null) return "no Point.anim";
        var clip = new AnimationClip { name = "Disco pose" };
        var settings = AnimationUtility.GetAnimationClipSettings(point);
        settings.loopTime = false; settings.stopTime = 1.5f;
        var done = new HashSet<string>();
        foreach (var binding in AnimationUtility.GetCurveBindings(point))
        {
            var curve = AnimationUtility.GetEditorCurve(point, binding);
            float start = curve.Evaluate(0f), end = DiscoPose.TryGetValue(binding.propertyName, out float pose) ? pose : start;
            if (DiscoPose.ContainsKey(binding.propertyName)) done.Add(binding.propertyName);
            AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(new Keyframe(0f, start), new Keyframe(.2f, end), new Keyframe(1.5f, end)));
        }
        // Muscles the gesture never keys get their own curves.
        foreach (var kv in DiscoPose)
        {
            if (done.Contains(kv.Key)) continue;
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), kv.Key), new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(.2f, kv.Value), new Keyframe(1.5f, kv.Value)));
        }
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        var old = AssetDatabase.LoadAssetAtPath<AnimationClip>(target);
        if (old != null) { EditorUtility.CopySerialized(clip, old); EditorUtility.SetDirty(old); } else AssetDatabase.CreateAsset(clip, target);
        AssetDatabase.SaveAssets();
        return "Disco pose.anim from Point's first frame, " + DiscoPose.Count + " muscles posed";
    }

    // A picture of the Crypt Lord in a clip at a time (for checking a pose), saved to path.
    public static string RenderPose(string clipPath, float time, string path)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Characters/Enemies/Crypt/Crypt Lord.prefab");
        var go = (GameObject)Object.Instantiate(prefab, new Vector3(0, -500, 0), Quaternion.identity);
        foreach (var b in go.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
        var anim = go.GetComponentInChildren<Animator>();
        clip.SampleAnimation(anim.gameObject, time);
        var bounds = new Bounds(go.transform.position + Vector3.up, Vector3.zero);
        foreach (var r in go.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
        var cam = new GameObject("Pose camera").AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.22f, .22f, .26f);
        cam.fieldOfView = 30f;
        cam.transform.position = bounds.center + new Vector3(0, .2f, 1f).normalized * (bounds.extents.magnitude * 3.2f);
        cam.transform.LookAt(bounds.center);
        var light = new GameObject("Pose light").AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.4f; light.transform.rotation = Quaternion.Euler(40, 160, 0);
        var rt = new RenderTexture(512, 640, 24);
        cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(512, 640, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 512, 640), 0, 0); tex.Apply();
        RenderTexture.active = null;
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(go); Object.DestroyImmediate(cam.gameObject); Object.DestroyImmediate(light.gameObject); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        return "rendered " + path;
    }

    static Texture2D Import(string path, TextureWrapMode wrap)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.wrapMode = wrap;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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
