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
        ("Lesser spoils", "spoils_lesser", "A lump of iron ore. Worth 1 at the tally.",
            new[] { new Piece { mesh = "SM_Prop_Ore_Iron_01", at = Vector3.zero, scale = 1.3f } }),
        ("Greater spoils", "spoils_greater", "A lump of gold ore. Worth 5 at the tally.",
            new[] { new Piece { mesh = "SM_Prop_Ore_Gold_01", at = Vector3.zero, scale = 1.45f } }),
        ("Grand spoils", "spoils_grand", "Raw gems in their rock. Worth 10 at the tally.",
            new[] { new Piece { mesh = "SM_Prop_Ore_Gem_01", at = Vector3.zero, scale = 1.6f } }),
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
                    // The painted restyle, or the plain pixel restyle (the ores, built here from the Synty model).
                    var mesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{Painted}{piece.mesh}/{piece.mesh} painted.asset");
                    var mat = AssetDatabase.LoadAssetAtPath<Material>($"{Painted}{piece.mesh}/{piece.mesh} painted.mat");
                    if (mesh == null && piece.mesh.StartsWith("SM_Prop_Ore_")) (mesh, mat) = RestyleOre(piece.mesh, piece.scale, rebuildModels);
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
            if (item.iconSource != null && kind.pieces[0].mesh.StartsWith("SM_Prop_Ore_")) item.iconSource.turn = new Vector3(-25, 30, 0);   // low heaps: seen from above
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

    // An ore (spoils) in the pixel style without painting: PixelRestyle's plain restyle of the Synty model (its own
    // colours, stone grain, baked creases), baked so that at the spoils' scale it lands on the item pixel size.
    static (Mesh, Material) RestyleOre(string meshName, float scale, bool rebuild)
    {
        string dir = $"{Painted}{meshName}";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{dir}/{meshName}.asset");
        var mat = AssetDatabase.LoadAssetAtPath<Material>($"{dir}/{meshName}.mat");
        if (mesh != null && mat != null && !rebuild) return (mesh, mat);
        var source = AssetDatabase.FindAssets(meshName + " t:Prefab", new[] { "Assets/Synty" });
        GameObject synty = null;
        foreach (var g in source) { var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)); if (go != null && go.name == meshName) { synty = go; break; } }
        if (synty == null) { Debug.LogError("Spoils: no Synty prefab " + meshName); return (null, null); }
        var smf = synty.GetComponentInChildren<MeshFilter>();
        var probe = new GameObject("Ore probe") { hideFlags = HideFlags.HideAndDontSave };
        probe.transform.localScale = Vector3.one * scale;
        var pmf = probe.AddComponent<MeshFilter>(); var pmr = probe.AddComponent<MeshRenderer>();
        try
        {
            float aim = PixelRestyle.ItemTexelsPerUnit * scale;
            for (int pass = 0; pass < 4; pass++)
            {
                (mesh, mat) = PixelRestyle.Build(smf.sharedMesh, smf.GetComponent<MeshRenderer>().sharedMaterials, "Props", meshName, aim, dir);
                if (mesh == null) break;
                pmf.sharedMesh = mesh; pmr.sharedMaterial = mat;
                AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(mat.GetTexture("_BaseMap")), ImportAssetOptions.ForceUpdate);
                float measured = PixelRestyle.TexelsPerUnit(pmr);
                if (measured <= 0f || Mathf.Abs(measured / PixelRestyle.ItemTexelsPerUnit - 1f) < .05f) break;
                aim *= PixelRestyle.ItemTexelsPerUnit / measured;
            }
        }
        finally { Object.DestroyImmediate(probe); }
        if (mesh != null && OreRock.TryGetValue(meshName, out var rock)) RecolourRock(meshName, mesh, mat, rock);
        return (mesh, mat);
    }

    // The rock around the nuggets in a colour of our own (Synty's brown rock hid the iron's rust nuggets and dulled the
    // gold): the model's most common colour is the rock, its triangles are repainted keeping each texel's shading.
    static readonly System.Collections.Generic.Dictionary<string, Color> OreRock = new()
    {
        { "SM_Prop_Ore_Iron_01", new Color(.3f, .32f, .36f) },   // cool slate
        { "SM_Prop_Ore_Gold_01", new Color(.25f, .25f, .29f) },  // dark charcoal, so the gold shows
    };

    static void RecolourRock(string meshName, Mesh mesh, Material mat, Color target)
    {
        var tris = mesh.triangles; int n = tris.Length / 3;
        var colours = PixelRestyle.SyntyFaceColours(meshName, n);
        if (colours == null) { Debug.LogWarning("Spoils: no Synty colours for " + meshName); return; }
        var v = mesh.vertices; var area = new System.Collections.Generic.Dictionary<Color, float>();
        for (int f = 0; f < n; f++) { float a = Vector3.Cross(v[tris[f * 3 + 1]] - v[tris[f * 3]], v[tris[f * 3 + 2]] - v[tris[f * 3]]).magnitude; area[colours[f]] = (area.TryGetValue(colours[f], out var x) ? x : 0) + a; }
        Color rock = default; float best = -1; foreach (var kv in area) if (kv.Value > best) { best = kv.Value; rock = kv.Key; }
        string texPath = AssetDatabase.GetAssetPath(mat.GetTexture("_BaseMap"));
        var tex = new Texture2D(2, 2); tex.LoadImage(System.IO.File.ReadAllBytes(texPath));
        int W = tex.width, H = tex.height; var px = tex.GetPixels(); var mask = new bool[px.Length]; var other = new bool[px.Length];
        var uv = mesh.uv;
        for (int f = 0; f < n; f++)
        {
            var into = colours[f] == rock ? mask : other;
            Vector2 a = uv[tris[f * 3]] * new Vector2(W, H), b = uv[tris[f * 3 + 1]] * new Vector2(W, H), c = uv[tris[f * 3 + 2]] * new Vector2(W, H);
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))) - 1), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))) + 1);
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))) - 1), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))) + 1);
            float den = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y); if (Mathf.Abs(den) < 1e-9f) continue;
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            {
                float qx = x + .5f, qy = y + .5f;
                float w1 = ((b.y - c.y) * (qx - c.x) + (c.x - b.x) * (qy - c.y)) / den, w2 = ((c.y - a.y) * (qx - c.x) + (a.x - c.x) * (qy - c.y)) / den;
                if (w1 >= -.05f && w2 >= -.05f && 1 - w1 - w2 >= -.05f) into[y * W + x] = true;
            }
        }
        // Two texels past the rock's own triangles (the padding round each island kept the old brown as seams), never
        // into a nugget's.
        for (int grow = 0; grow < 2; grow++)
        {
            var next = (bool[])mask.Clone();
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
            {
                int i = y * W + x; if (mask[i] || other[i]) continue;
                if ((x > 0 && mask[i - 1]) || (x < W - 1 && mask[i + 1]) || (y > 0 && mask[i - W]) || (y < H - 1 && mask[i + W])) next[i] = true;
            }
            mask = next;
        }
        float sum = 0; int count = 0; for (int i = 0; i < px.Length; i++) if (mask[i] && px[i].a > .5f) { sum += px[i].grayscale; count++; }
        if (count == 0) return;
        float mean = sum / count;
        for (int i = 0; i < px.Length; i++) if (mask[i] && px[i].a > .5f) { float k = px[i].grayscale / mean; px[i] = new Color(target.r * k, target.g * k, target.b * k, px[i].a); }
        tex.SetPixels(px); tex.Apply();
        System.IO.File.WriteAllBytes(texPath, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);
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
