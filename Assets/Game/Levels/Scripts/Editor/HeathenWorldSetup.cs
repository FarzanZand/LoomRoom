using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Wires the Heathen world pieces into the scene and prefabs. Safe to run again.
//   Blade blood (BladeBlood on the table player), the bat's flight (FlyingEnemy on Cave Bat), chest tiers (Supply
//   chest = wood, Iron chest and Gold chest variants with their own sting, roll and look; the lid opens with
//   "Chest lid open.anim"; Dungeon1's Chest Tiers), the pickup feed on the HUD, and the player's light reach.
public static class HeathenWorldSetup
{
    const string Props = "Assets/Game/Levels/Dungeon1/Prefabs/Props";
    const string Chests = Props + "/Chests";

    [MenuItem("Tools/LoomRoom/Heathen/Set Up World (blood, flyers, chests, pickup feed)")]
    public static string Run()
    {
        var log = new System.Text.StringBuilder();
        BladeBloodOnPlayer(log);
        BatFlight(log);
        ChestTiers(log);
        PickupFeed(log);
        PlayerLight(log);
        AssetDatabase.SaveAssets();
        var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        return log.ToString();
    }

    static Player TablePlayer()
    {
        foreach (var p in Object.FindObjectsByType<Player>(FindObjectsInactive.Include)) if (p.kind == PlayerKind.Table) return p;
        return null;
    }

    static void BladeBloodOnPlayer(System.Text.StringBuilder log)
    {
        var p = TablePlayer();
        if (p == null) { log.AppendLine("no table player"); return; }
        if (p.GetComponent<BladeBlood>() == null) p.gameObject.AddComponent<BladeBlood>();
        EditorUtility.SetDirty(p.gameObject);
        log.AppendLine("blade blood on " + p.name);
    }

    static void BatFlight(System.Text.StringBuilder log)
    {
        const string path = "Assets/Game/Characters/Enemies/Crypt/Cave Bat.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try { if (root.GetComponent<FlyingEnemy>() == null) root.AddComponent<FlyingEnemy>(); PrefabUtility.SaveAsPrefabAsset(root, path); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        log.AppendLine("Cave Bat flies");
    }

    // The lid swings up fast, overshoots and settles, rattling once.
    static AnimationClip LidClip(string lidPath)
    {
        string clipPath = Chests + "/Chest lid open.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (clip == null) { clip = new AnimationClip { legacy = true }; AssetDatabase.CreateAsset(clip, clipPath); }
        clip.legacy = true;
        clip.ClearCurves();
        var x = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(.16f, -70f), new Keyframe(.3f, -114f), new Keyframe(.42f, -99f), new Keyframe(.54f, -107f), new Keyframe(.66f, -104f));
        for (int i = 0; i < x.length; i++) AnimationUtility.SetKeyLeftTangentMode(x, i, AnimationUtility.TangentMode.Auto);
        clip.SetCurve(lidPath, typeof(Transform), "localEulerAnglesRaw.x", x);
        clip.SetCurve(lidPath, typeof(Transform), "localEulerAnglesRaw.y", AnimationCurve.Constant(0f, .66f, 0f));
        clip.SetCurve(lidPath, typeof(Transform), "localEulerAnglesRaw.z", AnimationCurve.Constant(0f, .66f, 0f));
        EditorUtility.SetDirty(clip);
        return clip;
    }

    static string PathFrom(Transform root, Transform t)
    {
        string s = t.name;
        while (t.parent != null && t.parent != root) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }

    static Material TintedCopy(Material source, string name, Color tint)
    {
        string path = $"{Chests}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(source); AssetDatabase.CreateAsset(m, path); }
        else m.CopyPropertiesFromMaterial(source);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
        if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
        EditorUtility.SetDirty(m);
        return m;
    }

    static void ChestTiers(System.Text.StringBuilder log)
    {
        if (!AssetDatabase.IsValidFolder(Chests)) AssetDatabase.CreateFolder(Props, "Chests");
        string wood = Props + "/Supply chest.prefab";
        var woodPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(wood);
        var lidPath = PathFrom(woodPrefab.transform, woodPrefab.GetComponent<DungeonContainer>().lid);
        var clip = LidClip(lidPath);
        AudioClip Sting(string tier) => AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/Game/Audio/Clips/Chests/Chest sting {tier}.wav");

        // Wood: the supply chest itself.
        var root = PrefabUtility.LoadPrefabContents(wood);
        try
        {
            var c = root.GetComponent<DungeonContainer>();
            c.lidClip = clip; c.stingAudio = Sting("wood"); c.stingVolume = .55f; c.chestName = "chest"; c.extraRolls = 0; c.rareRollFloors = 0;
            // The painting reached the lid from the front and back only, so its underside is a smear: plain planks
            // line it, like the chest's own inside.
            var lid = c.lid;
            var lining = lid.Find("Lid lining");
            if (lining == null)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad); quad.name = "Lid lining";
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                quad.transform.SetParent(lid, false); lining = quad.transform;
            }
            var bounds = lid.GetComponent<MeshFilter>().sharedMesh.bounds;
            lining.localPosition = new Vector3(bounds.center.x, bounds.min.y + .006f, bounds.center.z);
            lining.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // facing down into the chest when closed
            lining.localScale = new Vector3(bounds.size.x * .92f, bounds.size.z * .9f, 1f);
            var woodLevel = AssetDatabase.LoadAssetAtPath<TableLevelData>("Assets/Game/Levels/Dungeon1/Dungeon1.asset");
            var liningRenderer = lining.GetComponent<MeshRenderer>();
            // The level's oak, tiled to the chest's pixel size (its texture repeats once per 3 m tile otherwise) and shaded.
            if (!AssetDatabase.IsValidFolder(Chests)) AssetDatabase.CreateFolder(Props, "Chests");
            var liningMaterial = TintedCopy(woodLevel.woodMaterial, "Chest lining", new Color(.46f, .43f, .41f));
            var tex = woodLevel.woodMaterial.GetTexture("_BaseMap");
            float texels = tex != null ? tex.width : 32f;
            liningMaterial.SetTextureScale("_BaseMap", new Vector2(bounds.size.x * .92f * lid.lossyScale.x * 128f / texels, bounds.size.z * .9f * lid.lossyScale.z * 128f / texels));
            liningRenderer.sharedMaterial = liningMaterial;
            liningRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            PrefabUtility.SaveAsPrefabAsset(root, wood);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        GameObject Variant(string name, float scale, Color tint, Color glow, float glowIntensity, int extra, int rare, string sting, float volume)
        {
            string path = $"{Chests}/{name}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(woodPrefab);
                PrefabUtility.SaveAsPrefabAsset(inst, path);
                Object.DestroyImmediate(inst);
            }
            var r = PrefabUtility.LoadPrefabContents(path);
            try
            {
                r.name = name;
                r.transform.localScale = Vector3.one * scale;
                foreach (var mr in r.GetComponentsInChildren<MeshRenderer>())
                {
                    var mats = mr.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] != null) mats[i] = TintedCopy(mats[i].name.StartsWith(name) ? AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GetAssetPath(mats[i])) : mats[i], $"{name} {mr.name}", tint);
                    mr.sharedMaterials = mats;
                }
                var c = r.GetComponent<DungeonContainer>();
                c.lidClip = clip; c.stingAudio = Sting(sting); c.stingVolume = volume; c.chestName = name.ToLowerInvariant();
                c.extraRolls = extra; c.rareRollFloors = rare; c.glowColor = glow; c.glowIntensity = glowIntensity;
                PrefabUtility.SaveAsPrefabAsset(r, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(r); }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        // Copies are tinted from the wood chest's painted materials (re-running starts from those again).
        var iron = Variant("Iron chest", 1.08f, new Color(.66f, .7f, .76f), new Color(.85f, .9f, 1f), 7f, 1, 0, "iron", .6f);
        var gold = Variant("Gold chest", 1.16f, new Color(1.18f, .98f, .62f), new Color(1f, .8f, .35f), 10f, 1, 3, "gold", .7f);

        var level = AssetDatabase.LoadAssetAtPath<TableLevelData>("Assets/Game/Levels/Dungeon1/Dungeon1.asset");
        level.chestTiers = new[]
        {
            new DungeonWeightedPrefab { prefab = woodPrefab, weight = 6 },
            new DungeonWeightedPrefab { prefab = iron, weight = 3 },
            new DungeonWeightedPrefab { prefab = gold, weight = 1 },
        };
        EditorUtility.SetDirty(level);
        log.AppendLine("chest tiers: wood 6, iron 3, gold 1; lid clip " + lidPath);
    }

    static void PickupFeed(System.Text.StringBuilder log)
    {
        var labels = Object.FindAnyObjectByType<LootLabelsUI>(FindObjectsInactive.Include);
        if (labels == null) { log.AppendLine("no HUD canvas"); return; }
        var canvas = labels.GetComponentInParent<Canvas>().rootCanvas.transform;
        var feed = canvas.Find("Pickup feed") as RectTransform;
        if (feed == null) { feed = new GameObject("Pickup feed", typeof(RectTransform)).GetComponent<RectTransform>(); feed.SetParent(canvas, false); }
        feed.anchorMin = feed.anchorMax = new Vector2(1f, .5f); feed.pivot = new Vector2(1f, .5f);
        feed.anchoredPosition = new Vector2(-28f, -40f); feed.sizeDelta = new Vector2(380f, 300f);
        var layout = feed.GetComponent<VerticalLayoutGroup>() ?? feed.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.LowerRight; layout.spacing = 4f; layout.childControlWidth = false; layout.childControlHeight = false; layout.childForceExpandWidth = false; layout.childForceExpandHeight = false;
        var row = feed.Find("Row") as RectTransform;
        if (row == null) { row = new GameObject("Row", typeof(RectTransform)).GetComponent<RectTransform>(); row.SetParent(feed, false); }
        row.sizeDelta = new Vector2(360f, 40f);
        if (row.GetComponent<CanvasGroup>() == null) row.gameObject.AddComponent<CanvasGroup>();
        var icon = row.Find("Icon")?.GetComponent<Image>();
        if (icon == null) { icon = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>(); icon.transform.SetParent(row, false); }
        var ir = icon.rectTransform; ir.anchorMin = ir.anchorMax = ir.pivot = new Vector2(1f, .5f); ir.anchoredPosition = Vector2.zero; ir.sizeDelta = new Vector2(38f, 38f);
        icon.preserveAspect = true; icon.raycastTarget = false;
        var label = row.Find("Label")?.GetComponent<TextMeshProUGUI>();
        if (label == null) { label = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>(); label.transform.SetParent(row, false); }
        var lr = label.rectTransform; lr.anchorMin = lr.anchorMax = lr.pivot = new Vector2(1f, .5f); lr.anchoredPosition = new Vector2(-48f, 0f); lr.sizeDelta = new Vector2(320f, 34f);
        label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator.asset");
        label.fontSize = 24; label.alignment = TextAlignmentOptions.Right; label.textWrappingMode = TextWrappingModes.NoWrap; label.raycastTarget = false;
        // The font's own material: an outline (or UI Shadow) leaves a pixel font hollow and dim.
        label.fontSharedMaterial = label.font.material;
        var shadow = label.GetComponent<Shadow>(); if (shadow != null) Object.DestroyImmediate(shadow);
        row.gameObject.SetActive(false);
        var ui = feed.GetComponent<PickupFeedUI>() ?? feed.gameObject.AddComponent<PickupFeedUI>();
        var so = new SerializedObject(ui);
        so.FindProperty("rows").objectReferenceValue = feed;
        so.FindProperty("template").objectReferenceValue = row;
        var gold = Object.FindAnyObjectByType<GoldCounterUI>(FindObjectsInactive.Include);
        var coin = gold != null ? gold.transform.Find("Coin icon")?.GetComponent<Image>() : null;
        if (coin != null) so.FindProperty("goldIcon").objectReferenceValue = coin.sprite;
        so.ApplyModifiedPropertiesWithoutUndo();
        log.AppendLine("pickup feed under " + canvas.name);
    }

    // Heathen's dark: the player's lights fall off within about ten metres; the light radius does the rest.
    static void PlayerLight(System.Text.StringBuilder log)
    {
        var lighting = Object.FindAnyObjectByType<FirstPersonLighting>(FindObjectsInactive.Include);
        if (lighting == null) return;
        if (lighting.shadowLight != null) { lighting.shadowLight.range = 10f; EditorUtility.SetDirty(lighting.shadowLight); }
        if (lighting.worldLight != null) { lighting.worldLight.range = 11f; EditorUtility.SetDirty(lighting.worldLight); }
        foreach (var g in AssetDatabase.FindAssets("t:DungeonBiome"))
        {
            var b = AssetDatabase.LoadAssetAtPath<DungeonBiome>(AssetDatabase.GUIDToAssetPath(g));
            b.shadowTint = new Color(.55f, .85f, 1f); b.lampTint = new Color(1.06f, .97f, .86f); EditorUtility.SetDirty(b);
        }
        log.AppendLine("player light reach 10/11 m, biome tints set");
    }
}
