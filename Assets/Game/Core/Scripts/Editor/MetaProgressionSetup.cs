using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Tools > LoomRoom > Meta: builds the meta progression's pieces (spoils items, the Progression Manager's tracks, the
// character sheet, the tally rows, the trophy wall and the hobby table). Each step keeps what already exists, so it is
// safe to run again; edit the results by hand afterwards.
public static partial class MetaProgressionSetup
{
    const string SpoilsData = "Assets/Game/Items/Data/Spoils", SpoilsPrefabs = "Assets/Game/Items/Prefabs/Spoils";
    const string Painted = "Assets/Game/Art/Synty Pixel/Props/";
    const string ManagerPrefab = "Assets/Game/Core/Prefabs/ProgressionManager.prefab";
    const string SkillIcons = "Assets/Game/Art/PixelItems/Skills/Singles/";

    [MenuItem("Tools/LoomRoom/Meta/Build Everything", priority = 300)]
    public static void BuildEverything()
    {
        CreateSpoils();
        FillManager();
        BuildCharacterSheet();
        BuildTally();
        BuildRoom();
    }

    // ── Spoils ────────────────────────────────────────────────────────

    // Each piece: a painted mesh (Art/Synty Pixel/Props/<mesh>/<mesh> painted), a position and a uniform scale.
    struct Piece { public string mesh; public Vector3 at; public float scale; public Vector3 euler; }
    static readonly (string name, string id, string description, Piece[] pieces)[] SpoilsKinds =
    {
        ("Lesser spoils", "spoils_lesser", "A small sack of teeth, claws and the odd bauble. Worth 1 at the tally.",
            new[] { new Piece { mesh = "SM_Prop_Sack_03", at = Vector3.zero, scale = .42f } }),
        ("Greater spoils", "spoils_greater", "A sack of monster parts with a skull on top and baubles at the bottom. Worth 5 at the tally.",
            new[] { new Piece { mesh = "SM_Prop_Sack_01", at = Vector3.zero, scale = .55f }, new Piece { mesh = "SM_Prop_Skull_01", at = new Vector3(.17f, 0, -.05f), scale = .55f, euler = new Vector3(0, 155, 0) } }),
        ("Grand spoils", "spoils_grand", "A heap of bones, a skull and a full sack. Worth 10 at the tally.",
            new[] { new Piece { mesh = "SM_Prop_Bone_Pile_01", at = Vector3.zero, scale = .42f }, new Piece { mesh = "SM_Prop_Sack_01", at = new Vector3(-.08f, .03f, .05f), scale = .5f },
                    new Piece { mesh = "SM_Prop_Skull_01", at = new Vector3(.17f, .04f, -.08f), scale = .62f, euler = new Vector3(0, 200, 0) } }),
        ("Crypt Lord's spoils", "spoils_crypt_lord", "What the Crypt Lord left behind: his skull, his bones, his hoard. Worth 25 at the tally.",
            new[] { new Piece { mesh = "SM_Prop_Bone_Pile_01", at = Vector3.zero, scale = .5f }, new Piece { mesh = "SM_Prop_Skull_01", at = new Vector3(0, .1f, 0), scale = 1.15f, euler = new Vector3(-10, 185, 0) },
                    new Piece { mesh = "SM_Prop_Sack_01", at = new Vector3(-.24f, .02f, .12f), scale = .45f } }),
    };

    [MenuItem("Tools/LoomRoom/Meta/Create Spoils Items", priority = 310)]
    public static void CreateSpoils() => CreateSpoils(false);
    public static void CreateSpoils(bool rebuildModels)
    {
        Folder(SpoilsData); Folder(SpoilsPrefabs);
        foreach (var kind in SpoilsKinds)
        {
            string prefabPath = $"{SpoilsPrefabs}/{kind.name}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab != null && rebuildModels) { AssetDatabase.DeleteAsset(prefabPath); prefab = null; }
            if (prefab == null)
            {
                var root = new GameObject(kind.name);
                foreach (var piece in kind.pieces)
                {
                    var mesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{Painted}{piece.mesh}/{piece.mesh} painted.asset");
                    var mat = AssetDatabase.LoadAssetAtPath<Material>($"{Painted}{piece.mesh}/{piece.mesh} painted.mat");
                    if (mesh == null || mat == null) { Debug.LogError($"Spoils: missing painted {piece.mesh}"); continue; }
                    var go = new GameObject("Model " + piece.mesh.Replace("SM_Prop_", ""));
                    go.transform.SetParent(root.transform, false);
                    go.transform.localPosition = piece.at; go.transform.localEulerAngles = piece.euler; go.transform.localScale = Vector3.one * piece.scale;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                }
                prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Object.DestroyImmediate(root);
            }
            string dataPath = $"{SpoilsData}/{kind.name}.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemData>(dataPath);
            if (item == null) { item = ScriptableObject.CreateInstance<ItemData>(); AssetDatabase.CreateAsset(item, dataPath); }
            item.itemName = kind.name; item.saveId = kind.id; item.description = kind.description;
            item.itemType = ItemType.Spoils; item.rarity = ItemRarity.Spoils; item.maxStackSize = 5;
            item.value = 0; item.tier = 0; item.lootWeight = 0; item.worldPrefab = prefab;
            EditorUtility.SetDirty(item);
        }
        AssetDatabase.SaveAssets();
        foreach (var kind in SpoilsKinds)
        {
            var item = AssetDatabase.LoadAssetAtPath<ItemData>($"{SpoilsData}/{kind.name}.asset");
            if (item != null && (rebuildModels || item.icon == null || ItemIconRenderer.NeedsUpdate(item))) ItemIconRenderer.Generate(item);
        }
        EditorApplication.ExecuteMenuItem("Tools/LoomRoom/Sync Item Catalog");
        AssetDatabase.SaveAssets();
        Debug.Log("Meta: spoils items ready in " + SpoilsData);
    }

    // ── The Progression Manager's tracks and spoils ───────────────────

    [MenuItem("Tools/LoomRoom/Meta/Fill Progression Manager", priority = 311)]
    public static void FillManager()
    {
        var root = PrefabUtility.LoadPrefabContents(ManagerPrefab);
        try
        {
            var pm = root.GetComponent<ProgressionManager>();
            ItemData Item(string folder, string name) => AssetDatabase.LoadAssetAtPath<ItemData>($"Assets/Game/Items/Data/{folder}/{name}.asset");
            if (pm.spoils.Count == 0)
            {
                pm.spoils.Add(new ProgressionManager.SpoilsKind { item = Item("Spoils", "Lesser spoils"), worth = 1, floor1 = 75, perFloor = -5 });
                pm.spoils.Add(new ProgressionManager.SpoilsKind { item = Item("Spoils", "Greater spoils"), worth = 5, floor1 = 15, perFloor = 3 });
                pm.spoils.Add(new ProgressionManager.SpoilsKind { item = Item("Spoils", "Grand spoils"), worth = 10, floor1 = 10, perFloor = 2 });
            }
            if (pm.bossSpoils.Count == 0)
            {
                var lord = FindCharacterData("Crypt Lord");
                pm.bossSpoils.Add(new ProgressionManager.BossSpoils { boss = lord, item = Item("Spoils", "Crypt Lord's spoils"), worth = 25 });
                if (lord == null) Debug.LogWarning("Meta: no Crypt Lord data found; set the boss on the Progression Manager by hand.");
            }
            if (pm.tracks.Count == 0)
            {
                Sprite Icon(string file) => AssetDatabase.LoadAssetAtPath<Sprite>(SkillIcons + file);
                Sprite SkillIcon(AdventureSkill s) => AssetDatabase.LoadAssetAtPath<AdventureSkillData>($"Assets/Game/Progression/Data/Skills/{s}.asset")?.icon;
                void Skill(AdventureSkill s, string first, string second, string trophyFolder1, string trophy1, string trophyFolder2, string trophy2)
                {
                    var t = new ProgressionManager.MetaTrack { id = s.ToString().ToLowerInvariant(), displayName = s.ToString(), kind = MetaTrackKind.Skill, skill = s, icon = SkillIcon(s), givenLevels = 2, levels = 10, perLevel = 5 };
                    int firstAt = s == AdventureSkill.Blocking ? 15 : 25;
                    t.milestones.Add(new ProgressionManager.Milestone { value = firstAt, unlock = first, trophy = Item(trophyFolder1, trophy1),
                        icon = s == AdventureSkill.Blocking ? Icon("164_Mastery_Shield.png") : null,
                        description = s == AdventureSkill.Blocking ? "With a shield up, press attack: dash into the first enemy ahead and stun it." : "" });
                    t.milestones.Add(new ProgressionManager.Milestone { value = 50, unlock = second, trophy = Item(trophyFolder2, trophy2) });
                    pm.tracks.Add(t);
                }
                Skill(AdventureSkill.Blocking, "Shield bash", "?", "Shields", "Bronze Shield", "Shields", "Iron Shield");
                Skill(AdventureSkill.Swords, "?", "?", "Weapons", "Bronze Sword", "Weapons", "Iron Sword");
                Skill(AdventureSkill.Maces, "?", "?", "Weapons", "Bronze Mace", "Weapons", "Iron Mace");
                Skill(AdventureSkill.Sorcery, "?", "?", "Trinkets", "Bone charm", "Trinkets", "Garnet ring");
                Skill(AdventureSkill.Thaumaturgy, "?", "?", "Trinkets", "Warding amulet", "Trinkets", "Crystal pendant");
                Skill(AdventureSkill.Athletics, "?", "?", "Armor", "Leather Boots", "Armor", "Iron Boots");
                Skill(AdventureSkill.Stealth, "?", "?", "_Archive", "Knife", "_Archive", "Notched knife");
                Skill(AdventureSkill.Trading, "?", "?", "Trinkets", "Copper ring", "Trinkets", "Iron band");
                void Attribute(MetaAttribute a, string file, float perPoint, string unit) =>
                    pm.tracks.Add(new ProgressionManager.MetaTrack { id = a.ToString().ToLowerInvariant(), displayName = a.ToString(), kind = MetaTrackKind.Attribute, attribute = a, icon = Icon(file),
                        givenLevels = 0, levels = 8, baseScore = 8, bonusPerPoint = perPoint, unit = unit });
                Attribute(MetaAttribute.Constitution, "102_Passive_Male_Vitality.png", 5, "health");
                Attribute(MetaAttribute.Intelligence, "157_Mastery_Staff.png", 5, "mana");
                Attribute(MetaAttribute.Strength, "134_Strength.png", 1, "damage");
            }
            PrefabUtility.SaveAsPrefabAsset(root, ManagerPrefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        // The scene's manager is a plain scene object (not an instance of the prefab): its meta fields are copied over.
        var scene = Object.FindAnyObjectByType<ProgressionManager>(FindObjectsInactive.Include);
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefab).GetComponent<ProgressionManager>();
        if (scene != null && !PrefabUtility.IsPartOfPrefabInstance(scene) && scene.tracks.Count == 0)
        {
            var from = new SerializedObject(asset); var to = new SerializedObject(scene);
            foreach (var name in MetaFields) { var prop = from.FindProperty(name); if (prop != null) to.CopyFromSerializedProperty(prop); }
            to.ApplyModifiedProperties();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene.gameObject.scene);
        }
        Debug.Log("Meta: Progression Manager filled (" + ManagerPrefab + " and the scene's)");
    }

    static readonly string[] MetaFields = { "spoils", "enemySpoilsChance", "chestSpoilsChance", "healthPerSpoil", "bossSpoils", "firstPrice", "priceMultiplier", "tracks",
        "lessonIntro", "lessonBlockingFirst", "lessonTopUp", "handOverOne", "handOverMany", "shieldBashMounted", "lessonDelay" };

    static CharacterData FindCharacterData(string displayName)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Characters/Enemies" }))
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            var brain = go != null ? go.GetComponent<EnemyBrain>() : null;
            var data = brain != null ? brain.Data : null;
            if (data != null && (data.characterName == displayName || go.name == displayName)) return data;
        }
        return null;
    }

    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        Folder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
