using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Builds the boss dialogue prefab (Assets/Game/Dialogue/UI/Boss dialogue.prefab): its own canvas holding the portrait
// dialogue box (PortraitDialogueUI), and the boss title (BossTitleUI; the fight's
// Intro Card says whether it plays), placed as a child of the Dialogue Manager
// in the open scene. Build it once, then edit the prefab by hand (looks, sizes, typing, sounds, timing); running
// Build() again makes it anew and loses those edits. The box: a wide warm dark box with a thin gold line at the foot of the screen, the speaker's head and
// shoulders standing large over its left end, their name in gold capitals above it, the line typed out in cream, a
// gold marker and a nudging arrow, answers stacked over its right end. Sprites from Tools/dialogue_box_sprites.py.
// Run from an eval: PortraitDialogueSetup.Build(); Example() writes the test dialogue into the Crypt Lord fight.
public static class PortraitDialogueSetup
{
    static readonly Color Gold = new(.93f, .76f, .38f), Cream = new(1f, .94f, .82f), Panel = new(.12f, .08f, .05f, .94f);
    const string Sprites = "Assets/Game/UI/Sprites/Dialogue/";

    public const string PrefabPath = "Assets/Game/Dialogue/UI/Boss dialogue.prefab";

    public static string Build()
    {
        var manager = Object.FindAnyObjectByType<DungeonMaster>(FindObjectsInactive.Include);
        if (manager == null) return "no Dialogue Manager (DungeonMaster) in the open scene (open Room)";
        // Earlier builds lay on the HUD canvas; a build before this one under the manager is replaced.
        var hud = Object.FindAnyObjectByType<HudCanvas>(FindObjectsInactive.Include);
        foreach (var name in new[] { "Portrait dialogue", "Versus screen" })
            if (hud != null && hud.transform.Find(name) is Transform stale) Undo.DestroyObjectImmediate(stale.gameObject);
        if (manager.transform.Find("Boss dialogue") is Transform before) Undo.DestroyObjectImmediate(before.gameObject);

        var holder = new GameObject("Boss dialogue", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        holder.layer = LayerMask.NameToLayer("UI");
        holder.transform.SetParent(manager.transform, false);
        var canvas = holder.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 20;   // over the HUD and the feed
        var scaler = holder.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = .5f;
        string box = BuildBox((RectTransform)holder.transform), title = BuildTitle((RectTransform)holder.transform);

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PrefabPath));
        PrefabUtility.SaveAsPrefabAssetAndConnect(holder, PrefabPath, InteractionMode.AutomatedAction);
        Undo.RegisterCreatedObjectUndo(holder, "Build boss dialogue");
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(holder.scene);
        return $"{box}; {title}; saved {PrefabPath} under the Dialogue Manager";
    }

    static string BuildBox(RectTransform parent)
    {
        var boxSprite = ImportSprite(Sprites + "Dialogue box.png", 8);

        var title = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator-Bold.asset");
        var titleMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/UI/Fonts/PixelOperator-Bold Outline.mat");
        var body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator.asset");
        var bodyMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/UI/Fonts/PixelOperator Underlay.mat");

        var root = new GameObject("Portrait dialogue", typeof(RectTransform), typeof(CanvasGroup), typeof(PortraitDialogueUI));
        root.layer = parent.gameObject.layer;
        var rect = (RectTransform)root.transform;
        rect.SetParent(parent, false);
        rect.SetAsLastSibling();
        Stretch(rect, 0f, 0f, 0f, 0f);
        var group = root.GetComponent<CanvasGroup>();
        group.alpha = 0f; group.blocksRaycasts = false; group.interactable = true;

        // The box first, so the portrait stands over its left end.
        var box = Picture("Box", rect, boxSprite, Color.white, new Vector2(1400f, 190f), new Vector2(60f, 30f));
        box.GetComponent<Image>().type = Image.Type.Sliced;
        box.GetComponent<Image>().pixelsPerUnitMultiplier = .25f;   // the 8-texel border drawn four times larger
        var portraitGo = new GameObject("Portrait", typeof(RectTransform), typeof(RawImage));
        portraitGo.layer = root.layer;
        var portraitRect = (RectTransform)portraitGo.transform;
        portraitRect.SetParent(rect, false);
        Bottom(portraitRect, new Vector2(540f, 540f), new Vector2(-700f, 0f));
        var portrait = portraitGo.GetComponent<RawImage>(); portrait.raycastTarget = false;

        var nameText = Text("Name", box, title, 36f, Gold, titleMaterial, "EDGAR THE DISCO KING");
        nameText.rectTransform.anchorMin = nameText.rectTransform.anchorMax = new Vector2(0f, 1f);
        nameText.rectTransform.pivot = new Vector2(0f, 0f);
        nameText.rectTransform.sizeDelta = new Vector2(800f, 48f);
        nameText.rectTransform.anchoredPosition = new Vector2(262f, 6f);
        nameText.alignment = TextAlignmentOptions.BottomLeft;
        nameText.characterSpacing = 4f;

        var marker = Picture("Marker", box, null, Gold, new Vector2(11f, 11f), Vector2.zero);
        marker.anchorMin = marker.anchorMax = new Vector2(0f, 1f); marker.pivot = new Vector2(.5f, .5f);
        marker.anchoredPosition = new Vector2(238f, -50f);
        marker.localRotation = Quaternion.Euler(0f, 0f, 45f);

        var line = Text("Line", box, body, 36f, Cream, bodyMaterial, "Well, well. Someone found my dance floor.");
        Stretch(line.rectTransform, 262f, 90f, 30f, 24f);
        line.alignment = TextAlignmentOptions.TopLeft;
        line.textWrappingMode = TextWrappingModes.Normal;
        line.lineSpacing = 8f;

        var arrow = Text("Arrow", box, title, 36f, Gold, titleMaterial, ">");
        arrow.rectTransform.anchorMin = arrow.rectTransform.anchorMax = new Vector2(1f, 0f);
        arrow.rectTransform.pivot = new Vector2(1f, 0f);
        arrow.rectTransform.sizeDelta = new Vector2(40f, 44f);
        arrow.rectTransform.anchoredPosition = new Vector2(-34f, 18f);
        arrow.gameObject.AddComponent<PortraitDialogueArrow>();

        // The answers, stacked over the box's right end.
        var columnGo = new GameObject("Choices", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        columnGo.layer = root.layer;
        var column = (RectTransform)columnGo.transform;
        column.SetParent(rect, false);
        column.anchorMin = column.anchorMax = new Vector2(.5f, 0f); column.pivot = new Vector2(1f, 0f);
        column.anchoredPosition = new Vector2(740f, 236f);
        var layout = columnGo.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 10f; layout.childAlignment = TextAnchor.LowerRight; layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var fitter = columnGo.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var buttonRect = Picture("Choice template", column, boxSprite, Color.white, new Vector2(300f, 64f), Vector2.zero);
        var buttonImage = buttonRect.GetComponent<Image>();
        buttonImage.type = Image.Type.Sliced; buttonImage.pixelsPerUnitMultiplier = .25f; buttonImage.raycastTarget = true;
        var element = buttonRect.gameObject.AddComponent<LayoutElement>();
        element.preferredWidth = 300f; element.preferredHeight = 64f;
        var button = buttonRect.gameObject.AddComponent<Button>();
        var colours = button.colors;
        colours.normalColor = new Color(.85f, .85f, .85f); colours.highlightedColor = Color.white; colours.selectedColor = Color.white;
        colours.pressedColor = new Color(.7f, .7f, .7f); colours.fadeDuration = .05f;
        button.colors = colours;
        var label = Text("Label", buttonRect, body, 34f, Cream, bodyMaterial, "Yes");
        Stretch(label.rectTransform, 16f, 16f, 4f, 6f);
        buttonRect.gameObject.SetActive(false);
        columnGo.SetActive(false);

        var so = new SerializedObject(root.GetComponent<PortraitDialogueUI>());
        so.FindProperty("group").objectReferenceValue = group;
        so.FindProperty("portrait").objectReferenceValue = portrait;
        so.FindProperty("nameText").objectReferenceValue = nameText;
        so.FindProperty("body").objectReferenceValue = line;
        so.FindProperty("arrow").objectReferenceValue = arrow;
        so.FindProperty("choiceColumn").objectReferenceValue = column;
        so.FindProperty("choiceTemplate").objectReferenceValue = button;
        int blips = Clips(so.FindProperty("blips"), "Assets/Game/Audio/Clips/Dialogue/Type blip 1.wav", "Assets/Game/Audio/Clips/Dialogue/Type blip 2.wav",
            "Assets/Game/Audio/Clips/Dialogue/Type blip 3.wav", "Assets/Game/Audio/Clips/Dialogue/Type blip 4.wav");
        so.ApplyModifiedPropertiesWithoutUndo();

        return $"the portrait dialogue box ({blips} blips)";
    }

    // The boss title: the name in large PixelOperator-Bold with a dark outline (the font the game uses everywhere),
    // centred in the upper half of the screen, the boss's title in gold beneath it. Sizes are whole multiples of the
    // font's 16 px so its pixels stay square.
    static string BuildTitle(RectTransform parent)
    {
        var bold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator-Bold.asset");
        var outline = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/UI/Fonts/PixelOperator-Bold Outline.mat");

        var root = new GameObject("Boss title", typeof(RectTransform), typeof(CanvasGroup), typeof(BossTitleUI));
        root.layer = parent.gameObject.layer;
        var rect = (RectTransform)root.transform;
        rect.SetParent(parent, false);
        Stretch(rect, 0f, 0f, 0f, 0f);
        var group = root.GetComponent<CanvasGroup>(); group.alpha = 0f; group.blocksRaycasts = false;

        var name = Text("Name", rect, bold, 112f, Color.white, outline, "EDGAR THE DISCO KING");
        name.rectTransform.anchorMin = name.rectTransform.anchorMax = name.rectTransform.pivot = new Vector2(.5f, .5f);
        name.rectTransform.sizeDelta = new Vector2(1800f, 180f); name.rectTransform.anchoredPosition = new Vector2(0f, 250f);
        name.characterSpacing = 4f; name.enableAutoSizing = true; name.fontSizeMin = 48f; name.fontSizeMax = 112f;
        var title = Text("Title", rect, bold, 48f, Gold, outline, "Lord of the Crypt");
        title.rectTransform.anchorMin = title.rectTransform.anchorMax = title.rectTransform.pivot = new Vector2(.5f, .5f);
        title.rectTransform.sizeDelta = new Vector2(1200f, 60f); title.rectTransform.anchoredPosition = new Vector2(0f, 160f);
        title.characterSpacing = 2f;

        var so = new SerializedObject(root.GetComponent<BossTitleUI>());
        so.FindProperty("group").objectReferenceValue = group;
        so.FindProperty("nameText").objectReferenceValue = name;
        so.FindProperty("subtitle").objectReferenceValue = title;
        so.ApplyModifiedPropertiesWithoutUndo();
        return "the boss title";
    }

    // The test dialogue for the Crypt Lord fight: the boss as Edgar the disco king, who asks on his third box whether
    // the player wants to party; each answer has its own reply, and the Dungeon Master's word closes it.
    public static string Example()
    {
        var fight = AssetDatabase.LoadAssetAtPath<BossFight>("Assets/Game/Levels/Dungeon1/Biomes/Crypt Lord fight.asset");
        if (fight == null) return "no Crypt Lord fight asset";
        Undo.RecordObject(fight, "Intro dialogue");
        fight.introDialogue = true;
        fight.introCard = BossIntroCard.BossTitle;
        // The intro around it: the theatre spotlight (the forest's warm moonbeam: a spot, a shaft and dust from high
        // above) with its switch-on clunk (Tools/boss_intro_sounds.py, as is the boss's landing), and a heavy thud for each letter of the boss's name.
        fight.enterMusicFade = 2f; fight.spotlightDelay = 1f; fight.dialogueDelay = 2f; fight.spotlightFadeOut = .8f; fight.fightMusicFade = .25f;
        fight.spotlight = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Levels/Dungeon1/Forest/Props/Moonbeam warm.prefab");
        fight.spotlightSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Game/Audio/Clips/Dialogue/Spotlight clunk.wav");
        fight.landSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Game/Audio/Clips/Dialogue/Boss landing.wav");
        fight.spotlightVolume = .9f; fight.landVolume = 1f;
        const string Ui = "Assets/ThirdParty/Generic Fantasy UI/";
        fight.title ??= new BossTitleSettings();
        fight.title.letterSound = Load(Ui + "Fantasy_UI_Gen_Discard_Thud_01_mono.wav", Ui + "Fantasy_UI_Gen_Discard_Thud_02_mono.wav");
        fight.bossName = "Edgar the Disco King";
        fight.introLines = new List<PortraitLine>
        {
            new() { speaker = PortraitSpeaker.Boss, text = "Well, well. Someone found my dance floor." },
            new() { speaker = PortraitSpeaker.Boss, text = "Nobody has come down those stairs in a hundred years. The music got lonely." },
            new()
            {
                speaker = PortraitSpeaker.Boss, text = "So tell me. Do you want to party?",
                choices = new List<PortraitChoice>
                {
                    new() { label = "Yes", reply = new List<PortraitReply> { new() { speaker = PortraitSpeaker.Boss, text = "Finally, someone with taste! Try to keep up." } } },
                    new() { label = "No", reply = new List<PortraitReply> { new() { speaker = PortraitSpeaker.Boss, text = "Nobody says no to the disco king. You're dancing anyway!" } } },
                },
            },
            new() { speaker = PortraitSpeaker.DungeonMaster, text = "You heard the king. Dance." },
        };
        EditorUtility.SetDirty(fight);
        AssetDatabase.SaveAssetIfDirty(fight);
        return $"Crypt Lord fight: {fight.introLines.Count} lines, boss name {fight.bossName}, intro card {fight.introCard}";
    }

    // A dark outline on the big Lilita One words, as a material asset (a prefab cannot keep a text's own material copy).
    static Material OutlineMaterial(TMP_FontAsset font, string path)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null || font == null) return material;
        material = new Material(font.material) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
        material.EnableKeyword("OUTLINE_ON");
        material.SetFloat("_OutlineWidth", .26f);
        material.SetColor("_OutlineColor", new Color32(30, 10, 5, 255));
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static Sprite ImportSprite(string path, int border, bool smooth = false)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null) return null;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.filterMode = smooth ? FilterMode.Bilinear : FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.spritePixelsPerUnit = 100f;
        importer.spriteBorder = new Vector4(border, border, border, border);
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static AudioClip[] Load(params string[] paths)
    {
        var clips = new List<AudioClip>();
        foreach (var p in paths) { var c = AssetDatabase.LoadAssetAtPath<AudioClip>(p); if (c != null) clips.Add(c); else Debug.LogWarning("Missing sound " + p); }
        return clips.ToArray();
    }

    static int Clips(SerializedProperty list, params string[] paths)
    {
        var clips = new List<AudioClip>();
        foreach (var p in paths) { var c = AssetDatabase.LoadAssetAtPath<AudioClip>(p); if (c != null) clips.Add(c); else Debug.LogWarning("Missing sound " + p); }
        list.arraySize = clips.Count;
        for (int i = 0; i < clips.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        return clips.Count;
    }

    static void Bottom(RectTransform r, Vector2 size, Vector2 at)
    {
        r.anchorMin = r.anchorMax = new Vector2(.5f, 0f); r.pivot = new Vector2(.5f, 0f);
        r.sizeDelta = size; r.anchoredPosition = at;
    }

    static RectTransform Picture(string name, RectTransform parent, Sprite sprite, Color colour, Vector2 size, Vector2 at)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.layer = parent.gameObject.layer;
        var r = (RectTransform)go.transform;
        r.SetParent(parent, false);
        Bottom(r, size, at);
        var image = go.GetComponent<Image>();
        image.sprite = sprite; image.color = colour; image.raycastTarget = false;
        return r;
    }

    static void Stretch(RectTransform r, float left, float right, float top, float bottom)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(left, bottom); r.offsetMax = new Vector2(-right, -top);
    }

    static TextMeshProUGUI Text(string name, RectTransform parent, TMP_FontAsset font, float size, Color colour, Material material, string text)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.layer = parent.gameObject.layer;
        var r = (RectTransform)go.transform;
        r.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.font = font; if (material != null) t.fontSharedMaterial = material;
        t.fontSize = size; t.color = colour; t.text = text; t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.raycastTarget = false;
        return t;
    }
}
