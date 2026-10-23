using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Builds the class-unlocked card (ClassUnlockedUI) on the HUD canvas of the open scene: the class cards' paper, "New
// class", the class's sheet portrait, its name and where to pick it, in ink. Adds the UI sound key "classUnlocked" to
// AudioManager's UI Library when it is missing. Safe to run again: the card is rebuilt in place. Run from an eval:
// ClassUnlockedSetup.Build().
public static class ClassUnlockedSetup
{
    static readonly Color Ink = new(.24f, .17f, .1f), InkSoft = new(.36f, .28f, .19f);
    const string Sound = "Assets/ThirdParty/Generic Fantasy UI/Fantasy_UI_Gen_New_Skill_Unlocked_stereo.wav";

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
        var cardSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/UI/Sprites/Paper/Warm/Paper card.png");
        var divider = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/UI/Sprites/Paper/Warm/Paper divider.png");

        var root = new GameObject("Class unlocked", typeof(RectTransform), typeof(CanvasGroup), typeof(ClassUnlockedUI));
        Undo.RegisterCreatedObjectUndo(root, "Build class unlocked card");
        root.layer = hud.gameObject.layer;
        var rect = (RectTransform)root.transform;
        rect.SetParent(hud.transform, false);
        rect.SetAsLastSibling();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        var group = root.GetComponent<CanvasGroup>();
        group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;

        // The card: the class cards' paper, drawn in ink.
        var card = Picture("Card", rect, cardSprite, new Vector2(411f, 645f), Color.white).rectTransform;
        card.anchoredPosition = new Vector2(0f, 20f);
        var heading = Text("Heading", card, title, 30f, Ink, null, new Vector2(0f, 262f), "New class");
        heading.characterSpacing = 4f;
        var top = Picture("Divider", card, divider, new Vector2(315f, 16f), Color.white);
        top.rectTransform.anchoredPosition = new Vector2(0f, 228f);
        var portrait = Picture("Portrait", card, null, new Vector2(240f, 320f), Color.white);
        portrait.rectTransform.anchoredPosition = new Vector2(0f, 45f);
        portrait.preserveAspect = true;
        var bottom = Picture("Divider 2", card, divider, new Vector2(315f, 16f), Color.white);
        bottom.rectTransform.anchoredPosition = new Vector2(0f, -140f);
        var name = Text("Class name", card, title, 58f, Ink, null, new Vector2(0f, -190f), "Arcanist");
        var line = Text("Line", card, body, 28f, InkSoft, null, new Vector2(0f, -250f), "Pick it at the table.");

        var so = new SerializedObject(root.GetComponent<ClassUnlockedUI>());
        so.FindProperty("group").objectReferenceValue = group;
        so.FindProperty("card").objectReferenceValue = card;
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
