using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Builds the boss dialogue prefab (Assets/Game/Dialogue/UI/Boss dialogue.prefab): its own canvas holding the portrait
// dialogue box (PortraitDialogueUI) and the versus screen (VersusScreenUI), placed as a child of the Dialogue Manager
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
        string box = BuildBox((RectTransform)holder.transform), versus = BuildVersus((RectTransform)holder.transform);

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PrefabPath));
        PrefabUtility.SaveAsPrefabAssetAndConnect(holder, PrefabPath, InteractionMode.AutomatedAction);
        Undo.RegisterCreatedObjectUndo(holder, "Build boss dialogue");
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(holder.scene);
        return $"{box}; {versus}; saved {PrefabPath} under the Dialogue Manager";
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

    // The cinematic versus screen: black bars top and bottom over a darkened screen; at the left the player's class
    // painting melting into a blue glow, at the right the boss's live portrait in a red glow; a thin gold line across
    // the middle with the VS medallion on it; names letter-spaced in the bottom bar under each side.
    static string BuildVersus(RectTransform parent)
    {
        var glowSprite = ImportSprite(Sprites + "Soft glow.png", 0, smooth: true);
        var medalSprite = ImportSprite(Sprites + "Medallion.png", 0, smooth: true);
        var bebas = BebasAsset();
        var small = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator.asset");
        var soft = SoftEdgeMaterial();

        var root = new GameObject("Versus screen", typeof(RectTransform), typeof(CanvasGroup), typeof(VersusScreenUI));
        root.layer = parent.gameObject.layer;
        var rect = (RectTransform)root.transform;
        rect.SetParent(parent, false);
        Stretch(rect, 0f, 0f, 0f, 0f);
        var group = root.GetComponent<CanvasGroup>(); group.alpha = 0f; group.blocksRaycasts = false;

        var shake = new GameObject("Shake", typeof(RectTransform)).GetComponent<RectTransform>();
        shake.gameObject.layer = root.layer; shake.SetParent(rect, false); Stretch(shake, 0f, 0f, 0f, 0f);
        var dim = Picture("Dim", shake, null, new Color(0f, 0f, 0f, 0f), Vector2.zero, Vector2.zero); Stretch(dim, -60f, -60f, -60f, -60f);

        RectTransform Bar(string name, bool top)
        {
            var bar = Picture(name, shake, null, Color.black, Vector2.zero, Vector2.zero);
            bar.anchorMin = new Vector2(0f, top ? 1f : 0f); bar.anchorMax = new Vector2(1f, top ? 1f : 0f);
            bar.pivot = new Vector2(.5f, top ? 1f : 0f); bar.sizeDelta = new Vector2(120f, 165f); bar.anchoredPosition = Vector2.zero;
            return bar;
        }
        RectTransform Side(string name, bool left, Color glowColour, out Graphic art, out RectTransform label, out TextMeshProUGUI who, out TextMeshProUGUI what)
        {
            var side = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
            side.gameObject.layer = root.layer; side.SetParent(shake, false);
            side.anchorMin = side.anchorMax = side.pivot = new Vector2(.5f, .5f); side.sizeDelta = new Vector2(700f, 1080f);
            side.anchoredPosition = new Vector2(left ? -500f : 520f, 0f);
            var glow = Picture("Glow", side, glowSprite, glowColour, new Vector2(left ? 1100f : 1180f, 1000f), Vector2.zero);
            glow.anchorMin = glow.anchorMax = glow.pivot = new Vector2(.5f, .5f); glow.anchoredPosition = new Vector2(0f, 20f);
            if (left)
            {
                var image = new GameObject("Portrait", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                image.preserveAspect = true; image.material = soft; art = image;
            }
            else art = new GameObject("Portrait", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            art.gameObject.layer = root.layer; art.raycastTarget = false;
            var artRect = art.rectTransform; artRect.SetParent(side, false);
            artRect.anchorMin = artRect.anchorMax = artRect.pivot = new Vector2(.5f, .5f);
            artRect.sizeDelta = left ? new Vector2(470f, 560f) : new Vector2(700f, 700f);
            // The boss's render ends in a hard edge at its foot: that edge sits behind the bottom bar.
            artRect.anchoredPosition = new Vector2(0f, left ? 20f : -40f);
            // The name and title are their own layer, drawn over the bars (VersusScreenUI moves it with the side).
            label = new GameObject(name + " label", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
            label.gameObject.layer = root.layer; label.SetParent(shake, false);
            label.anchorMin = label.anchorMax = label.pivot = new Vector2(.5f, .5f); label.sizeDelta = side.sizeDelta;
            label.anchoredPosition = side.anchoredPosition;
            who = Text("Name", label, bebas, 76f, Color.white, null, left ? "YOU" : "EDGAR THE DISCO KING");
            who.rectTransform.anchorMin = who.rectTransform.anchorMax = who.rectTransform.pivot = new Vector2(.5f, .5f);
            // Fitted to the side's width (a long name gets smaller, never runs off the screen).
            who.rectTransform.sizeDelta = new Vector2(780f, 90f); who.rectTransform.anchoredPosition = new Vector2(0f, -432f);
            who.characterSpacing = 24f; who.enableAutoSizing = true; who.fontSizeMin = 36f; who.fontSizeMax = 76f;
            what = Text("Title", label, small, 40f, new Color(.88f, .86f, .82f), null, left ? "Level 1 Warrior" : "Lord of the Crypt");
            what.rectTransform.anchorMin = what.rectTransform.anchorMax = what.rectTransform.pivot = new Vector2(.5f, .5f);
            what.rectTransform.sizeDelta = new Vector2(1000f, 44f); what.rectTransform.anchoredPosition = new Vector2(0f, -490f);
            return side;
        }
        var leftSide = Side("Player side", true, new Color(.25f, .45f, 1f, .5f), out var playerArt, out var leftLabel, out var playerName, out var playerTitle);
        var rightSide = Side("Boss side", false, new Color(.95f, .15f, .15f, .52f), out var bossArt, out var rightLabel, out var bossName, out var bossTitle);
        // In drawing order: the sides (glow, portrait), the bars over them, the names over the bars.
        var topBar = Bar("Top bar", true);
        var bottomBar = Bar("Bottom bar", false);
        leftLabel.SetAsLastSibling(); rightLabel.SetAsLastSibling();

        var line = Picture("Gold line", shake, null, Gold, new Vector2(1560f, 3f), Vector2.zero);
        line.anchorMin = line.anchorMax = line.pivot = new Vector2(.5f, .5f); line.anchoredPosition = Vector2.zero;
        var medal = Picture("Medallion", shake, medalSprite, Color.white, new Vector2(184f, 184f), Vector2.zero);
        medal.anchorMin = medal.anchorMax = medal.pivot = new Vector2(.5f, .5f); medal.anchoredPosition = Vector2.zero;
        var vs = Text("VS", medal, bebas, 112f, Gold, null, "VS");
        Stretch(vs.rectTransform, 0f, 0f, 0f, -6f);
        var flash = Picture("Flash", rect, null, new Color(1f, 1f, 1f, 0f), Vector2.zero, Vector2.zero); Stretch(flash, 0f, 0f, 0f, 0f);

        var so = new SerializedObject(root.GetComponent<VersusScreenUI>());
        so.FindProperty("group").objectReferenceValue = group;
        so.FindProperty("dim").objectReferenceValue = dim.GetComponent<Image>();
        so.FindProperty("flash").objectReferenceValue = flash.GetComponent<Image>();
        so.FindProperty("shake").objectReferenceValue = shake;
        so.FindProperty("topBar").objectReferenceValue = topBar;
        so.FindProperty("bottomBar").objectReferenceValue = bottomBar;
        so.FindProperty("leftSide").objectReferenceValue = leftSide;
        so.FindProperty("rightSide").objectReferenceValue = rightSide;
        so.FindProperty("leftLabel").objectReferenceValue = leftLabel;
        so.FindProperty("rightLabel").objectReferenceValue = rightLabel;
        so.FindProperty("line").objectReferenceValue = line;
        so.FindProperty("medallion").objectReferenceValue = medal;
        so.FindProperty("playerPortrait").objectReferenceValue = playerArt;
        so.FindProperty("bossPortrait").objectReferenceValue = bossArt;
        so.FindProperty("playerName").objectReferenceValue = playerName;
        so.FindProperty("playerTitle").objectReferenceValue = playerTitle;
        so.FindProperty("bossName").objectReferenceValue = bossName;
        so.FindProperty("bossTitle").objectReferenceValue = bossTitle;
        so.ApplyModifiedPropertiesWithoutUndo();
        return "the versus screen";
    }

    // Bebas Neue as a TextMesh Pro font (dynamic: its glyphs are filled in as text uses them).
    static TMP_FontAsset BebasAsset()
    {
        const string path = "Assets/Game/UI/Fonts/BebasNeue SDF.asset";
        var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (asset != null) return asset;
        var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Game/UI/Fonts/BebasNeue-Regular.ttf");
        asset = TMP_FontAsset.CreateFontAsset(font, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic);
        asset.name = "BebasNeue SDF";
        AssetDatabase.CreateAsset(asset, path);
        asset.material.name = "BebasNeue SDF Material";
        AssetDatabase.AddObjectToAsset(asset.material, asset);
        asset.atlasTextures[0].name = "BebasNeue SDF Atlas";
        AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
        AssetDatabase.SaveAssets();
        return asset;
    }

    static Material SoftEdgeMaterial()
    {
        const string path = "Assets/Game/UI/Shaders/UI Soft Edge.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("LoomRoom/UI Soft Edge"));
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    // The test dialogue for the Crypt Lord fight: the boss as Edgar the disco king, who asks on his third box whether
    // the player wants to party; each answer has its own reply, and the Dungeon Master's word closes it.
    public static string Example()
    {
        var fight = AssetDatabase.LoadAssetAtPath<BossFight>("Assets/Game/Levels/Dungeon1/Biomes/Crypt Lord fight.asset");
        if (fight == null) return "no Crypt Lord fight asset";
        Undo.RecordObject(fight, "Intro dialogue");
        fight.introDialogue = true;
        fight.versusScreen = true;
        // The intro around it: the theatre spotlight (the forest's warm moonbeam: a spot, a shaft and dust from high
        // above) with its switch-on clunk, and the versus screen's whooshes, thuds and crash.
        fight.enterMusicFade = 2f; fight.spotlightDelay = 1f; fight.dialogueDelay = 2f; fight.spotlightFadeOut = .8f; fight.fightMusicFade = .25f;
        fight.spotlight = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Levels/Dungeon1/Forest/Props/Moonbeam warm.prefab");
        fight.spotlightSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Game/Audio/Clips/Dialogue/Spotlight on.wav");
        const string Ui = "Assets/ThirdParty/Generic Fantasy UI/", Magic = "Assets/ThirdParty/Sound Effects Library/Magic/";
        fight.versus ??= new VersusSettings();
        fight.versus.bandWhoosh = Load(Ui + "Fantasy_UI_Gen_Inventory_Whoosh_01_stereo.wav");
        fight.versus.bandLand = Load(Ui + "Fantasy_UI_Gen_Discard_Thud_01_mono.wav", Ui + "Fantasy_UI_Gen_Notification_Impact_02_mono.wav");
        fight.versus.slam = Load("Assets/Game/Audio/Clips/Reveal/Entrance slam.wav", Magic + "Mgc_Fire_Impact_01.wav", Ui + "Fantasy_UI_Gen_Notification_Impact_01_mono.wav");
        fight.versus.throwOff = Load(Ui + "Fantasy_UI_Gen_Inventory_Whoosh_02_stereo.wav");
        fight.bossName = "Edgar the disco king";
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
        return $"Crypt Lord fight: {fight.introLines.Count} lines, boss name {fight.bossName}, versus screen on";
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
