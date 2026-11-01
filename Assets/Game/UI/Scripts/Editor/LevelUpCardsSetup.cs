using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Makes the level-up cards (LevelUpCardsUI) as a prefab, UI/Prefabs/Progression/Level up cards, which ProgressionManager
// puts on the HUD canvas (ProgressionManager.levelUpCards, set here on the manager's prefab and the scene's): a dark backdrop that takes the clicks
// and four cards in a row (each a pointer target, a body that moves, a gold glow behind it, face and back,
// and on the face the attribute's icon, name, score, a divider, its gains and its key). Fills in the tarot art from
// UI/Sprites/Level up cards (Tools/level_up_cards.py), makes the sounds as AudioData in Audio/Data/Level up cards from
// the sound libraries, and gives the Dungeon Master his deal lines. Safe to run again: the prefab is made again. Run from an eval:
// LevelUpCardsSetup.Build().
public static class LevelUpCardsSetup
{
    const string Art = "Assets/Game/UI/Sprites/Level up cards/", Sounds = "Assets/Game/Audio/Data/Level up cards/";
    const string UI = "Assets/ThirdParty/Generic Fantasy UI/Fantasy_UI_Gen_", Lib = "Assets/ThirdParty/Sound Effects Library/";
    const string PrefabPath = "Assets/Game/UI/Prefabs/Progression/Level up cards.prefab";
    const float CardW = 300f, CardH = 460f, Spacing = 330f;
    static readonly List<string> missing = new();

    public static string Build()
    {
        missing.Clear();
        var hud = Object.FindAnyObjectByType<HudCanvas>(FindObjectsInactive.Include);
        if (hud == null) return "no HudCanvas in the open scene (open Room)";
        PixelSprites();
        // Built on the HUD canvas so the layout sees its size, saved as the prefab, then taken off again.
        var old = hud.transform.Find("Level up cards");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var bold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator-Bold.asset");
        var body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator.asset");
        var outline = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/UI/Fonts/PixelOperator-Bold Outline.mat");
        var underlay = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/UI/Fonts/PixelOperator Underlay.mat");

        var root = new GameObject("Level up cards", typeof(RectTransform), typeof(LevelUpCardsUI));
        Undo.RegisterCreatedObjectUndo(root, "Build level up cards");
        root.layer = hud.gameObject.layer;
        var top = (RectTransform)root.transform;
        top.SetParent(hud.transform, false);
        top.SetAsLastSibling();
        Stretch(top);
        // The picker (backdrop, title, cards) fades as one; the prompt beside it shows on its own.
        var rect = new GameObject("Picker", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
        rect.gameObject.layer = root.layer;
        rect.SetParent(top, false);
        Stretch(rect);
        var group = rect.GetComponent<CanvasGroup>();
        group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;

        // "Press R to level up": a dark band under the crosshair with the line in gold.
        var promptRoot = new GameObject("Prompt", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
        promptRoot.gameObject.layer = root.layer;
        promptRoot.SetParent(top, false);
        promptRoot.sizeDelta = new Vector2(460f, 54f);
        promptRoot.anchoredPosition = new Vector2(0f, -140f);
        var promptGroup = promptRoot.GetComponent<CanvasGroup>();
        promptGroup.alpha = 0f; promptGroup.blocksRaycasts = false; promptGroup.interactable = false;
        var band = Box("Band", promptRoot, AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/UI/Sprites/Inventory/Plain slot grey.png"), new Vector2(460f, 54f), new Color(.1f, .09f, .11f, .82f), false);
        band.type = Image.Type.Sliced;
        var promptText = Text("Line", promptRoot, bold, 32f, new Color(.95f, .82f, .5f), outline, Vector2.zero, "Press R to level up", 440f);

        var backdrop = Box("Backdrop", rect, null, Vector2.zero, new Color(0f, 0f, 0f, 0f), true);
        Stretch(backdrop.rectTransform);

        var cards = new LevelUpCardUI[4];
        for (int i = 0; i < 4; i++)
        {
            var slot = new GameObject($"Card {i + 1}", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(LevelUpCardUI));
            slot.layer = root.layer;
            var s = (RectTransform)slot.transform;
            s.SetParent(rect, false);
            s.sizeDelta = new Vector2(CardW, CardH);
            s.anchoredPosition = new Vector2((i - 1.5f) * Spacing, -22f);
            var hit = slot.GetComponent<Image>(); hit.color = new Color(1f, 1f, 1f, 0f); hit.raycastTarget = true;
            var card = slot.GetComponent<LevelUpCardUI>();
            card.group = slot.GetComponent<CanvasGroup>();

            var b = new GameObject("Body", typeof(RectTransform)).GetComponent<RectTransform>();
            b.gameObject.layer = root.layer;
            b.SetParent(s, false); b.sizeDelta = new Vector2(CardW, CardH);
            card.body = b;
            card.glow = Box("Glow", b, null, new Vector2(CardW + 22f, CardH + 22f), new Color(1f, .8f, .35f, 0f), false);
            card.face = Box("Face", b, null, new Vector2(CardW, CardH), Color.white, false);

            var parts = new GameObject("Face parts", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
            parts.gameObject.layer = root.layer;
            parts.SetParent(b, false); parts.sizeDelta = new Vector2(CardW, CardH);
            var partsGroup = parts.GetComponent<CanvasGroup>(); partsGroup.blocksRaycasts = false;
            card.faceParts = partsGroup;
            card.icon = Box("Icon", parts, null, new Vector2(96f, 96f), Color.white, false);   // 32 px icons at exactly 3x
            card.icon.rectTransform.anchoredPosition = new Vector2(0f, 138f);
            card.icon.preserveAspect = true;
            card.attributeName = Text("Name", parts, bold, 34f, Color.white, null, new Vector2(0f, 70f), "STRENGTH", 260f);
            card.attributeName.characterSpacing = 3f;
            card.score = Text("Score", parts, bold, 28f, Color.white, null, new Vector2(0f, 34f), "10  >  11", 260f);
            card.divider = Box("Divider", parts, null, new Vector2(220f, 10f), Color.white, false);
            card.divider.rectTransform.anchoredPosition = new Vector2(0f, 6f);
            card.effects = Text("Effects", parts, body, 24f, Color.white, null, new Vector2(0f, -68f), "+1 damage\n+2 shield bash damage\n+10% heavy attack damage", 260f);
            card.effects.rectTransform.sizeDelta = new Vector2(260f, 130f);
            card.effects.textWrappingMode = TextWrappingModes.Normal;
            card.effects.lineSpacing = 18f;
            card.effects.alignment = TextAlignmentOptions.Center;
            card.keyFrame = Box("Key frame", parts, null, new Vector2(28f, 28f), Color.white, false);
            card.keyFrame.rectTransform.anchoredPosition = new Vector2(0f, -166f);
            card.key = Text("Key", parts, bold, 20f, Color.white, null, new Vector2(0f, -166f), (i + 1).ToString(), 40f);
            cards[i] = card;
        }

        var ui = root.GetComponent<LevelUpCardsUI>();
        var so = new SerializedObject(ui);
        so.FindProperty("group").objectReferenceValue = group;
        so.FindProperty("prompt").objectReferenceValue = promptGroup;
        so.FindProperty("promptText").objectReferenceValue = promptText;
        so.FindProperty("backdrop").objectReferenceValue = backdrop;
        var list = so.FindProperty("cards");
        list.arraySize = 4;
        for (int i = 0; i < 4; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = cards[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        ui.art = TarotArt(outline, underlay);
        ui.dealLines = new List<DungeonMaster.Line>
        {
            new() { text = "Choose." },
            new() { text = "Choose well." },
            new() { text = "One card. Make it count." },
            new() { text = "Take one. The rest go back in the deck." },
        };

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath).GetComponent<LevelUpCardsUI>();
        Object.DestroyImmediate(root);
        // The manager's prefab and the scene's own manager (not a prefab instance) both point at it.
        var pmPrefab = AssetDatabase.LoadAssetAtPath<ProgressionManager>("Assets/Game/Core/Prefabs/ProgressionManager.prefab");
        foreach (var pm in new[] { pmPrefab, Object.FindAnyObjectByType<ProgressionManager>(FindObjectsInactive.Include) })
        {
            if (pm == null) continue;
            var pso = new SerializedObject(pm);
            pso.FindProperty("levelUpCards").objectReferenceValue = prefab;
            pso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pm);
        }
        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
        return "made the level up cards prefab" + (missing.Count > 0 ? "; missing: " + string.Join(", ", missing) : "");
    }

    static LevelUpCardsUI.CardArt TarotArt(Material outline, Material underlay)
    {
        Color Rgb(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);
        return new LevelUpCardsUI.CardArt
        {
            face = Sprite("Tarot face"), divider = Sprite("Tarot divider"), keyFrame = Sprite("Tarot key"),
            name = Rgb(226, 182, 92), ink = Rgb(232, 222, 204), gain = Rgb(242, 210, 130), faded = Rgb(160, 150, 160, .8f), glow = Rgb(255, 170, 70, .9f), dim = .62f,
            titleMaterial = outline, bodyMaterial = underlay,
            deal = Sound("Tarot pop", .45f, .03f, UI + "Swap_High_01_mono.wav", UI + "Swap_High_02_mono.wav"),
            hover = Sound("Tarot hover", .8f, .04f, UI + "Toggle_01A_mono.wav", UI + "Toggle_02A_mono.wav"),
            pick = Sound("Tarot pick", 1f, 0f, UI + "Item_Upgrade_Success_stereo.wav"),
            leave = Sound("Tarot leave", .5f, .04f, UI + "Transition_Swish_03_mono.wav"),
        };
    }

    // The card art is pixel art drawn at half size: point filtered, uncompressed, no mipmaps.
    static void PixelSprites()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Art.TrimEnd('/') }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is not TextureImporter ti) continue;
            if (ti.textureType == TextureImporterType.Sprite && ti.filterMode == FilterMode.Point && !ti.mipmapEnabled && ti.textureCompression == TextureImporterCompression.Uncompressed) continue;
            ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single;
            ti.filterMode = FilterMode.Point; ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.alphaIsTransparency = true; ti.npotScale = TextureImporterNPOTScale.None;
            ti.SaveAndReimport();
        }
    }

    static Sprite Sprite(string name)
    {
        var s = AssetDatabase.LoadAssetAtPath<Sprite>(Art + name + ".png");
        if (s == null) missing.Add(name);
        return s;
    }

    static AudioData Sound(string name, float volume, float variance, params string[] clips)
    {
        if (!AssetDatabase.IsValidFolder(Sounds.TrimEnd('/'))) AssetDatabase.CreateFolder("Assets/Game/Audio/Data", "Level up cards");
        string path = Sounds + name + ".asset";
        var data = AssetDatabase.LoadAssetAtPath<AudioData>(path);
        if (data == null) { data = ScriptableObject.CreateInstance<AudioData>(); AssetDatabase.CreateAsset(data, path); }
        var list = new List<AudioClip>();
        foreach (var c in clips)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(c);
            if (clip != null) list.Add(clip); else missing.Add(System.IO.Path.GetFileName(c));
        }
        data.clips = list.ToArray(); data.volume = volume; data.pitch = 1f; data.pitchVariance = variance;
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssetIfDirty(data);
        return data;
    }

    static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

    static Image Box(string name, RectTransform parent, Sprite sprite, Vector2 size, Color colour, bool raycast)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.layer = parent.gameObject.layer;
        var r = (RectTransform)go.transform;
        r.SetParent(parent, false);
        r.sizeDelta = size;
        var image = go.GetComponent<Image>();
        image.sprite = sprite; image.color = colour; image.raycastTarget = raycast;
        return image;
    }

    static TextMeshProUGUI Text(string name, RectTransform parent, TMP_FontAsset font, float size, Color colour, Material material, Vector2 at, string text, float width)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.layer = parent.gameObject.layer;
        var r = (RectTransform)go.transform;
        r.SetParent(parent, false);
        r.sizeDelta = new Vector2(width, size * 1.4f);
        r.anchoredPosition = at;
        var t = go.GetComponent<TextMeshProUGUI>();
        t.font = font; if (material != null) t.fontSharedMaterial = material;
        t.fontSize = size; t.color = colour; t.text = text; t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.raycastTarget = false;
        return t;
    }
}
