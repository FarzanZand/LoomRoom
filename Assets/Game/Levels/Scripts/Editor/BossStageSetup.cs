using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Tools > LoomRoom > Dungeon > Set Up Boss Stage: everything the boss stage fight needs, safe to run again (what
// exists is kept, so hand edits stay): IK Pass on the humanoid controller's Base Layer (the Dungeon Master's hands),
// DungeonMasterHand and DungeonMasterBeam on the Dungeon Master with the beam's material and effects, the Boss stage
// room template, the Crypt Lord's fight script, the Crypt biome's guardian on the stage, and the Crypt Lord casting.
public static class BossStageSetup
{
    const string Controller = "Assets/Game/Characters/Animations/HumanoidController.controller";
    const string DungeonMaster = "Assets/Game/Characters/NPCs/DungeonMaster.prefab";
    const string BeamFolder = "Assets/Game/Combat/Effects/DM beam";
    const string StagePath = "Assets/Game/Levels/Dungeon1/Rooms/Templates/Boss stage.prefab";
    const string FightPath = "Assets/Game/Levels/Dungeon1/Biomes/Crypt Lord fight.asset";
    const string CryptBiome = "Assets/Game/Levels/Dungeon1/Biomes/Crypt.asset";
    const string CryptLord = "Assets/Game/Characters/Enemies/Crypt/Crypt Lord.prefab";
    const string Acolyte = "Assets/Game/Characters/Enemies/Crypt/Crypt Acolyte.prefab";
    const string Brazier = "Assets/Game/Levels/Dungeon1/Prefabs/Furnishing/Brazier.prefab";
    const string Sounds = "Assets/Game/Audio/Library/_Sound Effects Library/";

    public static void SetUpMenu() => Debug.Log("[Boss stage] " + SetUp());

    public static string SetUp()
    {
        IkPass();
        var beam = BeamAssets();
        DungeonMasterParts(beam);
        var stage = StageTemplate();
        var fight = Fight();
        Guardian(stage, fight);
        CryptLordCasts();
        AssetDatabase.SaveAssets();
        return "IK pass, the DM's hand and beam, Boss stage, Crypt Lord fight, Crypt guardian and the Crypt Lord's spells in place";
    }

    static void IkPass()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
        var layers = controller.layers;
        if (layers[0].iKPass) return;
        layers[0].iKPass = true;
        controller.layers = layers;
        EditorUtility.SetDirty(controller);
    }

    // The beam's material (the pack's trail texture, pixel copy) and two effect prefabs SpellPackEffects fills.
    static (Material material, SpellVisual charge, SpellVisual hit) BeamAssets()
    {
        Directory.CreateDirectory(BeamFolder);
        string matPath = BeamFolder + "/DM beam.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (material == null)
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Combat/Spells/_Pack/Materials/Trail_LWRP.mat")
                ?? AssetDatabase.LoadAssetAtPath<Material>("Assets/Spells Pack/LWRP(URP)/Particles_LWRP/Materials/Trail_LWRP.mat");
            material = new Material(source) { name = "DM beam" };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            AssetDatabase.CreateAsset(material, matPath);
        }
        return (material, EffectPrefab("DM beam charge", new Color(1f, .75f, .4f)), EffectPrefab("DM beam hit", null));
    }

    static SpellVisual EffectPrefab(string name, Color? light)
    {
        string path = BeamFolder + "/" + name + ".prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing.GetComponent<SpellVisual>();
        var go = new GameObject(name);
        var visual = go.AddComponent<SpellVisual>();
        visual.stopLinger = .35f;
        if (light.HasValue)
        {
            var lightGo = new GameObject("Light"); lightGo.transform.SetParent(go.transform, false);
            var l = lightGo.AddComponent<Light>(); l.type = LightType.Point; l.color = light.Value; l.range = 2.5f; l.intensity = .45f;
            visual.glow = l; visual.intensity = .45f;
        }
        var saved = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return saved.GetComponent<SpellVisual>();
    }

    // The beam's layers, from the Spells Pack (pixel copies): FireTrail core, Trail halo, a Thunder bolt turned along
    // the beam (DM beam bolt.png, cut from the pack's Thunder), Glow sparks. Made again each run from the pack.
    public static void BeamArt(DungeonMasterBeam beam)
    {
        const string pack = "Assets/Spells Pack/LWRP(URP)/Particles_LWRP/Materials/";
        Material Pack(string name, int divisor) => SpellPackEffects.PixelMaterial(AssetDatabase.LoadAssetAtPath<Material>(pack + name), divisor);
        // Core and halo textures are cut from the pack's FireTrail and Glow with real alpha (DM beam core/glow.png):
        // the pack's own copies, alpha from grayscale, draw nothing on a line here.
        // Body, core and flare are painted from the pack's Glow, Spell 5 and Spell 3 by Tools/dm_beam_texture.py.
        beam.bodyMaterial = LineMaterial("DM beam body", null, "DM beam body.png");
        beam.coreMaterial = LineMaterial("DM beam core", null, "DM beam core.png");
        beam.flareMaterial = LineMaterial("DM beam flare", null, "DM beam flare.png");
        beam.glowMaterial = LineMaterial("DM beam glow", Pack("Glow_LWRP.mat", 8), "DM beam glow.png");
        beam.sparkMaterial = Pack("Glow_LWRP.mat", 8);
        string boltPath = BeamFolder + "/DM beam bolt.mat";
        var bolt = AssetDatabase.LoadAssetAtPath<Material>(boltPath);
        if (bolt == null) { bolt = new Material(AssetDatabase.LoadAssetAtPath<Material>(pack + "Thunder_LWRP.mat")); AssetDatabase.CreateAsset(bolt, boltPath); }
        string texPath = BeamFolder + "/DM beam bolt.png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(texPath);
        if (importer != null && (importer.filterMode != FilterMode.Point || importer.wrapMode != TextureWrapMode.Repeat))
        { importer.filterMode = FilterMode.Point; importer.wrapMode = TextureWrapMode.Repeat; importer.mipmapEnabled = false; importer.alphaIsTransparency = true; importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport(); }
        bolt.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
        if (bolt.HasProperty("_BaseColor")) bolt.SetColor("_BaseColor", Color.white);
        EditorUtility.SetDirty(bolt);
        beam.boltMaterial = LineMaterial("DM beam arc", bolt, "DM beam bolt.png");
        beam.bodyMaterial.SetFloat("_Intensity", .85f); beam.coreMaterial.SetFloat("_Intensity", 2.4f); beam.glowMaterial.SetFloat("_Intensity", .6f);
        beam.boltMaterial.SetFloat("_Intensity", 2.2f); beam.flareMaterial.SetFloat("_Intensity", 2f);
        beam.handEffect = AssetDatabase.LoadAssetAtPath<GameObject>(BeamFolder + "/DM beam charge.prefab")?.GetComponent<SpellVisual>();
        beam.impactEffect = EffectPrefab("DM beam impact", null);
        beam.handEffectScale = 1.6f; beam.impactInterval = .35f;
        beam.blockSparks = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Combat/Effects/BlockSparks.prefab");
    }

    // A copy of a pack particle material that draws on a line: both sides, no soft-particle or near fade (they cut a
    // line that runs up to the camera), added on (glows).
    static Material LineMaterial(string name, Material source, string texture)
    {
        string texPath = BeamFolder + "/" + texture;
        if (AssetImporter.GetAtPath(texPath) is TextureImporter ti && (ti.filterMode != FilterMode.Point || ti.wrapMode != TextureWrapMode.Repeat || ti.mipmapEnabled))
        { ti.filterMode = FilterMode.Point; ti.wrapMode = TextureWrapMode.Repeat; ti.mipmapEnabled = false; ti.alphaIsTransparency = true; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.SaveAndReimport(); }
        string path = BeamFolder + "/" + name + ".mat";
        var shader = Shader.Find("LoomRoom/Beam Additive");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        m.shader = shader;
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Intensity", 1f);
        EditorUtility.SetDirty(m);
        return m;
    }

    static void DungeonMasterParts((Material material, SpellVisual charge, SpellVisual hit) beam)
    {
        var root = PrefabUtility.LoadPrefabContents(DungeonMaster);
        try
        {
            var animator = root.GetComponentInChildren<Animator>(true);
            var owner = animator.gameObject;
            if (owner.GetComponent<DungeonMasterHand>() == null) owner.AddComponent<DungeonMasterHand>();
            var b = owner.GetComponent<DungeonMasterBeam>();
            if (b == null) b = owner.AddComponent<DungeonMasterBeam>();
            BeamArt(b);
            PrefabUtility.SaveAsPrefabAsset(root, DungeonMaster);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // A square stage, nine cells a side, braziers at its corners; it never turns (the DM's side is the far side).
    static GameObject StageTemplate()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
        if (existing != null) return existing;
        var go = new GameObject("Boss stage");
        var template = go.AddComponent<DungeonRoomTemplate>();
        template.minimumCells = new Vector2Int(9, 9);
        template.allowRotation = false;
        template.heightTiles = 1;
        template.replaceGeneratedProps = true;
        template.replaceGeneratedEnemies = true;
        template.roomEvent = DungeonRoomEvent.None;
        go.AddComponent<DungeonStage>();
        var brazier = AssetDatabase.LoadAssetAtPath<GameObject>(Brazier);
        if (brazier != null)
            foreach (var p in new[] { new Vector3(-7, 0, -7), new Vector3(7, 0, -7), new Vector3(-7, 0, 7), new Vector3(7, 0, 7), new Vector3(7, 0, 0) })
            {
                var b = (GameObject)PrefabUtility.InstantiatePrefab(brazier, go.transform);
                b.transform.localPosition = p;
                b.transform.localScale = Vector3.one * 1.6f;
            }
        var saved = PrefabUtility.SaveAsPrefabAsset(go, StagePath);
        Object.DestroyImmediate(go);
        return saved;
    }

    static BossFight Fight()
    {
        var fight = AssetDatabase.LoadAssetAtPath<BossFight>(FightPath);
        if (fight != null) return fight;
        fight = ScriptableObject.CreateInstance<BossFight>();
        fight.music = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Game/Audio/CHIP_07_Forge.mp3");
        fight.bpm = 129.2f; fight.firstBeat = .743f; fight.musicVolume = .8f;
        fight.landSound = AssetDatabase.LoadAssetAtPath<AudioClip>(Sounds + "Magic/Mgc_Electric_Impact_01.wav");
        fight.beamChargeSound = AssetDatabase.LoadAssetAtPath<AudioClip>(Sounds + "Magic/Mgc_Electric_Cast_Start_01.wav");
        fight.beamLoopSound = AssetDatabase.LoadAssetAtPath<AudioClip>(Sounds + "Magic/Mgc_Electric_Hold_Loop_01.wav");
        fight.beamBlockedSound = AssetDatabase.LoadAssetAtPath<AudioClip>(Sounds + "Magic/Mgc_Electric_Cast_Stop_01.wav");
        // His fireball volley, between the beams (switch it off on the event to try the boss's own volley alone).
        fight.events.Add(new BossEvent { when = BossEvent.When.EveryBeats, startBeat = 24, beats = 24, what = BossEvent.What.Volley,
            spell = AssetDatabase.LoadAssetAtPath<SpellDefinition>("Assets/Game/Combat/Spells/Fireball/Fireball.asset"), damage = 8f });
        AssetDatabase.CreateAsset(fight, FightPath);
        return fight;
    }

    static void Guardian(GameObject stage, BossFight fight)
    {
        var biome = AssetDatabase.LoadAssetAtPath<DungeonBiome>(CryptBiome);
        if (biome == null || biome.guardian == null) return;
        biome.guardian.arena = stage;
        if (biome.guardian.fight == null) biome.guardian.fight = fight;
        EditorUtility.SetDirty(biome);
    }

    // The Crypt Lord keeps his blows and casts what the Acolyte casts, closing in all the same.
    static void CryptLordCasts()
    {
        EnemyData Data(string path) { foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path)) if (o is EnemyData d) return d; return null; }
        var lord = Data(CryptLord); var acolyte = Data(Acolyte);
        if (lord == null || acolyte == null || (lord.caster && lord.spells.Count > 0)) return;
        lord.caster = true; lord.closesIn = true;
        lord.spells.Clear();
        foreach (var s in acolyte.spells) lord.spells.Add(JsonUtility.FromJson<EnemySpell>(JsonUtility.ToJson(s)));
        foreach (var s in lord.spells) { s.cooldown *= 1.4f; s.minRange = Mathf.Max(s.minRange, 3f); s.damageMultiplier *= .6f; }
        // His own fireball volley too (weight 0 on it leaves only the Dungeon Master's).
        var single = lord.spells.Find(s => s.spell != null && s.spell.name == "Fireball");
        if (single != null)
        {
            var volley = JsonUtility.FromJson<EnemySpell>(JsonUtility.ToJson(single));
            volley.volley = true; volley.volleySettings = new FireballVolley.Settings { scale = 1.1f, rise = .9f, spread = .9f };
            volley.cooldown = 14f; volley.minRange = 4f; volley.maxRange = 14f; volley.weight = 1.5f;
            lord.spells.Add(volley);
        }
        EditorUtility.SetDirty(lord);
    }
}
