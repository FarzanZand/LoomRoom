using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Builds the main menu's save pieces in the open Main Menu scene: the button column (Continue, New game, Load game,
// Quit), the New game choice (the player's name, Begin, Back), the save list (three slot rows, each with a Delete
// button, and Back) and the question asked before a save is deleted or replaced, all in the menu's painted gold
// frames and Lilita One, and binds them on MainMenuUI. Safe to run again: the panels are rebuilt, the buttons kept.
public static class MainMenuSaves
{
    const string Frame = "Assets/Game/UI/Main Menu/Textures/Menu button.png", FrameHover = "Assets/Game/UI/Main Menu/Textures/Menu button hover.png";
    static readonly Color Dim = new(.68f, .65f, .58f), Gold = new(.93f, .76f, .38f);

    [MenuItem("Tools/LoomRoom/UI/Build Main Menu Saves")]
    public static void Build()
    {
        var menu = Object.FindAnyObjectByType<MainMenuUI>(FindObjectsInactive.Include);
        if (menu == null) { Debug.LogError("Open Scenes/Main Menu first: no MainMenuUI in the open scene."); return; }
        var so = new SerializedObject(menu);
        var play = (Button)so.FindProperty("playButton").objectReferenceValue;
        var quit = (Button)so.FindProperty("quitButton").objectReferenceValue;
        if (play == null || quit == null) { Debug.LogError("MainMenuUI is missing its Play or Quit button."); return; }
        var canvas = (RectTransform)play.transform.parent;
        if (canvas.name == "Buttons") canvas = (RectTransform)canvas.parent;
        Undo.RegisterFullObjectHierarchyUndo(canvas.gameObject, "Build main menu saves");

        // The painted frame stretches for the save list: 9-sliced at its rough gold edge (plain buttons stay Simple).
        foreach (var path in new[] { Frame, FrameHover })
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer != null && importer.spriteBorder != new Vector4(26, 26, 26, 26)) { importer.spriteBorder = new Vector4(26, 26, 26, 26); importer.SaveAndReimport(); }
        }

        // ── The button column ───────────────────────────────────────
        var column = canvas.Find("Buttons") as RectTransform;
        if (column == null)
        {
            column = New("Buttons", canvas);
            column.anchorMin = column.anchorMax = Vector2.zero; column.pivot = new Vector2(.5f, 0f);
            column.anchoredPosition = new Vector2(265f, 64f - 30.5f); column.sizeDelta = new Vector2(326f, 460f);
            var layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.LowerCenter; layout.spacing = 15f;
            layout.childControlWidth = layout.childControlHeight = false; layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            column.SetSiblingIndex(play.transform.GetSiblingIndex());
        }
        var newGame = FindOrCopy(column, play, "New game");
        var load = FindOrCopy(column, play, "Load game");
        play.name = "Continue"; Label(play).text = "Continue";
        Button[] order = { play, newGame, load, quit };
        for (int i = 0; i < order.Length; i++) { order[i].transform.SetParent(column, false); order[i].transform.SetSiblingIndex(i); }
        // A Skip intro button from an older build of the menu goes (a new game always plays the intro).
        foreach (var stray in new[] { column.Find("Skip intro"), canvas.Find("Skip intro") }) if (stray != null) Object.DestroyImmediate(stray.gameObject);

        // ── The New game choice ─────────────────────────────────────
        var oldChoice = canvas.Find("New game choice"); if (oldChoice != null) Object.DestroyImmediate(oldChoice.gameObject);
        var choice = New("New game choice", canvas);
        choice.anchorMin = choice.anchorMax = Vector2.zero; choice.pivot = Vector2.zero;
        choice.anchoredPosition = new Vector2(102f, 33.5f); choice.sizeDelta = new Vector2(900f, 380f);
        var choiceTitle = Text("Title", choice, Label(play), 34f, Gold, TextAlignmentOptions.BottomLeft);
        Place(choiceTitle.rectTransform, 0f, 300f, 700f, 60f);
        choiceTitle.text = "New game";
        // The player's name, in the menu's frame: the new save is known by it.
        var nameRect = New("Name", choice);
        Place(nameRect, 0f, 228f, 440f, 61f);   // wide enough for the longest name (AdventureSave.NameLength)
        var nameFrame = nameRect.gameObject.AddComponent<Image>();
        nameFrame.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Frame); nameFrame.type = Image.Type.Sliced; nameFrame.pixelsPerUnitMultiplier = 2f;
        var nameArea = New("Text Area", nameRect);
        Inset(nameArea, 26f, 8f, 26f, 8f);
        nameArea.gameObject.AddComponent<RectMask2D>();
        var nameText = Text("Text", nameArea, Label(play), 28f, Label(play).color, TextAlignmentOptions.Left);
        Inset(nameText.rectTransform, 0f, 0f, 0f, 0f);
        nameText.overflowMode = TextOverflowModes.Overflow;   // the field scrolls its text itself
        nameText.text = "";
        var namePlaceholder = Text("Placeholder", nameArea, Label(play), 28f, Dim, TextAlignmentOptions.Left);
        Inset(namePlaceholder.rectTransform, 0f, 0f, 0f, 0f);
        namePlaceholder.text = AdventureSave.DefaultName;   // the name a new game gets when none is typed
        var nameInput = nameRect.gameObject.AddComponent<TMP_InputField>();
        nameInput.textViewport = nameArea; nameInput.textComponent = nameText; nameInput.placeholder = namePlaceholder;
        nameInput.characterLimit = AdventureSave.NameLength; nameInput.targetGraphic = nameFrame;
        nameInput.customCaretColor = true; nameInput.caretColor = Gold; nameInput.caretWidth = 2; nameInput.selectionColor = new Color(Gold.r, Gold.g, Gold.b, .3f);
        var hover = nameInput.spriteState; hover.highlightedSprite = hover.selectedSprite = AssetDatabase.LoadAssetAtPath<Sprite>(FrameHover);
        nameInput.transition = Selectable.Transition.SpriteSwap; nameInput.spriteState = hover;
        var nameNote = Text("Name note", choice, Label(play), 22f, Label(play).color, TextAlignmentOptions.Left);
        Place(nameNote.rectTransform, 464f, 228f, 480f, 61f);
        nameNote.text = "Your save is called by this name.";
        var playIntro = Copy(play, choice, "Begin"); Label(playIntro).text = "Begin";
        Place((RectTransform)playIntro.transform, 0f, 152f, 326f, 61f);
        var choiceBack = Copy(quit, choice, "Back"); Label(choiceBack).text = "Back";
        Place((RectTransform)choiceBack.transform, 0f, 0f, 326f, 61f);
        choice.gameObject.SetActive(false);

        // ── The save list ───────────────────────────────────────────
        var old = canvas.Find("Saves"); if (old != null) Object.DestroyImmediate(old.gameObject);
        var saves = New("Saves", canvas);
        saves.anchorMin = saves.anchorMax = Vector2.zero; saves.pivot = Vector2.zero;
        saves.anchoredPosition = new Vector2(102f, 33.5f); saves.sizeDelta = new Vector2(700f, 560f);
        var title = Text("Title", saves, Label(play), 34f, Gold, TextAlignmentOptions.BottomLeft);
        Place(title.rectTransform, 0f, 470f, 700f, 70f);
        title.text = "Load game";

        var rows = so.FindProperty("slotRows");
        rows.arraySize = AdventureSave.SlotCount;
        for (int i = 0; i < AdventureSave.SlotCount; i++)
        {
            var row = Copy(play, saves, $"Slot {i + 1}");
            Object.DestroyImmediate(Label(row).gameObject);
            Slice(row);
            // An empty slot keeps its frame, dimmed (MainMenuUI fades it), instead of the disabled sprite.
            var states = row.spriteState; states.disabledSprite = row.GetComponent<Image>().sprite; row.spriteState = states;
            row.gameObject.AddComponent<CanvasGroup>();
            Place((RectTransform)row.transform, 0f, 91f + (AdventureSave.SlotCount - 1 - i) * 124f, 700f, 112f);
            var name = Text("Name", row.transform, Label(play), 30f, Label(play).color, TextAlignmentOptions.TopLeft);
            Inset(name.rectTransform, 30f, 18f, 200f, 22f);
            name.text = "Empty";
            var time = Text("Last played", row.transform, Label(play), 20f, Dim, TextAlignmentOptions.TopRight);
            Inset(time.rectTransform, 300f, 24f, 30f, 22f);
            var details = Text("Details", row.transform, Label(play), 20f, Dim, TextAlignmentOptions.BottomLeft);
            Inset(details.rectTransform, 30f, 60f, 170f, 20f);
            var delete = Copy(play, row.transform, "Delete");
            Slice(delete);
            var d = (RectTransform)delete.transform;
            d.anchorMin = d.anchorMax = d.pivot = new Vector2(1f, 0f); d.anchoredPosition = new Vector2(-24f, 16f); d.sizeDelta = new Vector2(124f, 44f);
            Label(delete).text = "Delete"; Label(delete).fontSize = 20f;
            var entry = rows.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("button").objectReferenceValue = row;
            entry.FindPropertyRelative("title").objectReferenceValue = name;
            entry.FindPropertyRelative("details").objectReferenceValue = details;
            entry.FindPropertyRelative("time").objectReferenceValue = time;
            entry.FindPropertyRelative("delete").objectReferenceValue = delete;
        }
        var back = Copy(quit, saves, "Back");
        Label(back).text = "Back";
        Place((RectTransform)back.transform, 0f, 0f, 326f, 61f);
        saves.gameObject.SetActive(false);

        // ── The question ────────────────────────────────────────────
        var oldAsk = canvas.Find("Confirm"); if (oldAsk != null) Object.DestroyImmediate(oldAsk.gameObject);
        var ask = New("Confirm", canvas);
        ask.anchorMin = Vector2.zero; ask.anchorMax = Vector2.one; ask.offsetMin = ask.offsetMax = Vector2.zero;
        var shade = ask.gameObject.AddComponent<Image>(); shade.color = new Color(0f, 0f, 0f, .55f);   // keeps clicks off the menu behind
        var box = New("Box", ask);
        box.anchorMin = box.anchorMax = new Vector2(.5f, .5f); box.sizeDelta = new Vector2(760f, 230f);
        var boxImage = box.gameObject.AddComponent<Image>();
        boxImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Frame); boxImage.type = Image.Type.Sliced; boxImage.pixelsPerUnitMultiplier = 2f;
        var question = Text("Question", box, Label(play), 28f, Label(play).color, TextAlignmentOptions.Center);
        Inset(question.rectTransform, 40f, 30f, 40f, 100f);
        question.textWrappingMode = TextWrappingModes.Normal;
        question.text = "Delete this save? It will be gone for good.";
        var yes = Copy(play, box, "Yes"); Label(yes).text = "Yes";
        var no = Copy(play, box, "No"); Label(no).text = "No";
        foreach (var (b, x) in new[] { (yes, -130f), (no, 130f) })
        {
            var r = (RectTransform)b.transform;
            r.anchorMin = r.anchorMax = new Vector2(.5f, 0f); r.pivot = new Vector2(.5f, 0f); r.anchoredPosition = new Vector2(x, 32f); r.sizeDelta = new Vector2(220f, 61f);
        }
        ask.gameObject.SetActive(false);

        // The fade stays over everything.
        var fade = (CanvasGroup)so.FindProperty("fade").objectReferenceValue;
        if (fade != null) fade.transform.SetAsLastSibling();

        so.FindProperty("newGameButton").objectReferenceValue = newGame;
        so.FindProperty("newGamePanel").objectReferenceValue = choice.gameObject;
        so.FindProperty("nameField").objectReferenceValue = nameInput;
        so.FindProperty("playIntroButton").objectReferenceValue = playIntro;
        so.FindProperty("newGameBack").objectReferenceValue = choiceBack;
        so.FindProperty("loadButton").objectReferenceValue = load;
        so.FindProperty("playLabel").objectReferenceValue = Label(play);
        so.FindProperty("buttons").objectReferenceValue = column.gameObject;
        so.FindProperty("savesPanel").objectReferenceValue = saves.gameObject;
        so.FindProperty("savesTitle").objectReferenceValue = title;
        so.FindProperty("savesBack").objectReferenceValue = back;
        so.FindProperty("confirmPanel").objectReferenceValue = ask.gameObject;
        so.FindProperty("confirmText").objectReferenceValue = question;
        so.FindProperty("confirmYes").objectReferenceValue = yes;
        so.FindProperty("confirmNo").objectReferenceValue = no;
        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(menu.gameObject.scene);
        Debug.Log("Main menu saves built: button column, save list with " + AdventureSave.SlotCount + " slots, and the question.");
    }

    static RectTransform New(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static TMP_Text Label(Button b) => b.GetComponentInChildren<TMP_Text>(true);

    static Button Copy(Button source, Transform parent, string name)
    {
        var b = Object.Instantiate(source, parent, false);
        b.name = name;
        b.onClick = new Button.ButtonClickedEvent();
        return b;
    }

    static Button FindOrCopy(Transform column, Button source, string name)
    {
        var found = column.Find(name);
        if (found != null && found.GetComponent<Button>() is Button b) return b;
        var copy = Copy(source, column, name);
        Label(copy).text = name;
        return copy;
    }

    // The frame and its hover frame drawn 9-sliced, edges at the plain buttons' size.
    static void Slice(Button b)
    {
        var image = b.GetComponent<Image>();
        image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = 2f;
    }

    static TMP_Text Text(string name, Transform parent, TMP_Text style, float size, Color color, TextAlignmentOptions align)
    {
        var t = Object.Instantiate(style, parent, false);
        t.name = name;
        t.fontSize = size; t.color = color; t.alignment = align;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis;
        t.raycastTarget = false;
        return t;
    }

    // Bottom-left corner and size inside a bottom-left anchored parent.
    static void Place(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = Vector2.zero; r.pivot = Vector2.zero;
        r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h);
    }

    // Stretched over the parent, in from each side (left, top, right, bottom).
    static void Inset(RectTransform r, float left, float top, float right, float bottom)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(.5f, .5f);
        r.offsetMin = new Vector2(left, bottom); r.offsetMax = new Vector2(-right, -top);
    }
}
