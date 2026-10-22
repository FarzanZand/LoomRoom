using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Builds the class-unlocked card (ClassUnlockedUI) on the HUD canvas of the open scene: a gold glow and turning rays
// behind a card in the inventory's grey frame, the class's sheet portrait on paper, "NEW CLASS" and the class name in
// gold, and where to pick it. Adds the UI sound key "classUnlocked" to AudioManager's UI Library when it is missing.
// Safe to run again: the card is rebuilt in place. Run from an eval: ClassUnlockedSetup.Build().
public static class ClassUnlockedSetup
{
    static readonly Color Gold = new(.93f, .76f, .38f);
    const string Sound = "Assets/Game/Audio/Library/Generic Fantasy UI/Fantasy_UI_Gen_New_Skill_Unlocked_stereo.wav";

    public static string Build()
    {
        var hud = Object.FindAnyObjectByType<HudCanvas>(FindObjectsInactive.Include);
        if (hud == null) return "no HudCanvas in the open scene (open Room)";
        var old = hud.transform.Find("Class unlocked");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var title = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator-Bold.asset");
        var titleMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/UI/Fonts/PixelOperator-Bold Outline.mat");
        var body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator.asset");
        var bodyMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/UI/Fonts/PixelOperator Underlay.mat");
        var frame = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/UI/Sprites/Inventory/Plain frame grey.png");
        var paper = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/UI/Sprites/Meta/Paper.png");
        var glowSprite = PointSprite("Assets/Game/UI/Sprites/Unlock/Unlock glow.png");
        var raySprite = PointSprite("Assets/Game/UI/Sprites/Unlock/Unlock rays.png");

        var root = new GameObject("Class unlocked", typeof(RectTransform), typeof(CanvasGroup), typeof(ClassUnlockedUI));
        Undo.RegisterCreatedObjectUndo(root, "Build class unlocked card");
        root.layer = hud.gameObject.layer;
        var rect = (RectTransform)root.transform;
        rect.SetParent(hud.transform, false);
        rect.SetAsLastSibling();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        var group = root.GetComponent<CanvasGroup>();
        group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;

        var rays = Picture("Rays", rect, raySprite, new Vector2(860f, 860f), new Color(1f, 1f, 1f, .45f));
        var glow = Picture("Glow", rect, glowSprite, new Vector2(720f, 720f), Color.white);

        var card = new GameObject("Card", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        card.gameObject.layer = root.layer;
        card.SetParent(rect, false);
        // The card itself is the gold edge; the grey panel and everything on it sit inside.
        card.sizeDelta = new Vector2(452f, 612f);
        var cardImage = card.GetComponent<Image>();
        cardImage.color = Gold; cardImage.raycastTarget = false;
        var panel = Picture("Panel", card, frame, new Vector2(440f, 600f), new Color(.16f, .16f, .18f, 1f));
        if (frame != null) panel.type = Image.Type.Sliced;
        var heading = Text("Heading", card, title, 34f, Gold, titleMaterial, new Vector2(0f, 250f), "NEW CLASS");
        heading.characterSpacing = 8f;
        var paperImage = Picture("Paper", card, paper, new Vector2(264f, 344f), Color.white);
        paperImage.rectTransform.anchoredPosition = new Vector2(0f, 40f);
        if (paper != null) paperImage.type = Image.Type.Sliced;
        var portrait = Picture("Portrait", card, null, new Vector2(240f, 320f), Color.white);
        portrait.rectTransform.anchoredPosition = new Vector2(0f, 40f);
        portrait.preserveAspect = true;
        var name = Text("Class name", card, title, 64f, Gold, titleMaterial, new Vector2(0f, -185f), "ARCANIST");
        var line = Text("Line", card, body, 30f, Color.white, bodyMaterial, new Vector2(0f, -250f), "Pick it at the table.");

        var so = new SerializedObject(root.GetComponent<ClassUnlockedUI>());
        so.FindProperty("group").objectReferenceValue = group;
        so.FindProperty("card").objectReferenceValue = card;
        so.FindProperty("glow").objectReferenceValue = glow;
        so.FindProperty("rays").objectReferenceValue = rays;
        so.FindProperty("portrait").objectReferenceValue = portrait;
        so.FindProperty("heading").objectReferenceValue = heading;
        so.FindProperty("className").objectReferenceValue = name;
        so.FindProperty("line").objectReferenceValue = line;
        so.ApplyModifiedPropertiesWithoutUndo();

        string sound = AddSound();
        // The puzzle names its class: the card shows that class's portrait.
        var arcanist = AssetDatabase.LoadAssetAtPath<AdventurerClass>("Assets/Game/Progression/Data/Classes/Arcanist.asset");
        foreach (var puzzle in Object.FindObjectsByType<AdventureRoomPuzzle>(FindObjectsInactive.Include))
            if (puzzle.unlockedClass == null && arcanist != null && puzzle.unlockFlag == arcanist.unlockFlag)
            { Undo.RecordObject(puzzle, "Puzzle class"); puzzle.unlockedClass = arcanist; EditorUtility.SetDirty(puzzle); }
        var burst = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Items/Effects/Chest burst.prefab");
        foreach (var puzzle in Object.FindObjectsByType<AdventureRoomPuzzle>(FindObjectsInactive.Include))
            if (puzzle.unlockEffect == null && burst != null) { Undo.RecordObject(puzzle, "Puzzle burst"); puzzle.unlockEffect = burst; EditorUtility.SetDirty(puzzle); }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
        return "built the class unlocked card; " + sound;
    }

    // The UI Library's "classUnlocked" key (the fantasy UI pack's skill unlocked chime), added once.
    static string AddSound()
    {
        var audio = Object.FindAnyObjectByType<AudioManager>(FindObjectsInactive.Include);
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Sound);
        if (audio == null || clip == null) return "no AudioManager or sound clip: key not added";
        var so = new SerializedObject(audio);
        var list = so.FindProperty("uiLibrary");
        for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).FindPropertyRelative("key").stringValue == "classUnlocked") return "sound key already there";
        list.arraySize++;
        var e = list.GetArrayElementAtIndex(list.arraySize - 1);
        e.FindPropertyRelative("key").stringValue = "classUnlocked";
        e.FindPropertyRelative("source").enumValueIndex = 0;
        e.FindPropertyRelative("clip").objectReferenceValue = clip;
        e.FindPropertyRelative("data").objectReferenceValue = null;
        e.FindPropertyRelative("volume").floatValue = .9f;
        e.FindPropertyRelative("pitch").floatValue = 1f;
        so.ApplyModifiedProperties();
        return "added sound key classUnlocked";
    }

    static Sprite PointSprite(string path)
    {
        if (AssetImporter.GetAtPath(path) is TextureImporter importer && (importer.textureType != TextureImporterType.Sprite || importer.filterMode != FilterMode.Point))
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static Image Picture(string name, RectTransform parent, Sprite sprite, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.layer = parent.gameObject.layer;
        var r = (RectTransform)go.transform;
        r.SetParent(parent, false);
        r.sizeDelta = size;
        var image = go.GetComponent<Image>();
        image.sprite = sprite; image.color = color; image.raycastTarget = false;
        return image;
    }

    static TextMeshProUGUI Text(string name, RectTransform parent, TMP_FontAsset font, float size, Color color, Material material, Vector2 at, string text)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.layer = parent.gameObject.layer;
        var r = (RectTransform)go.transform;
        r.SetParent(parent, false);
        r.sizeDelta = new Vector2(420f, size * 1.4f);
        r.anchoredPosition = at;
        var t = go.GetComponent<TextMeshProUGUI>();
        t.font = font; if (material != null) t.fontSharedMaterial = material;
        t.fontSize = size; t.color = color; t.text = text; t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.raycastTarget = false;
        return t;
    }
}
