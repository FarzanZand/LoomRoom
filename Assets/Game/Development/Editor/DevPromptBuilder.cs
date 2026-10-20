using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Builds Development/Resources/Dev Prompt.prefab, the developer prompt's window (DevPrompt): the game's plain grey
// frames and PixelOperator, gold titles. Left: the requests and their status; middle: the request; right: the
// screenshot (click to draw on it) and the game state; along the bottom: Send, Outbox, Close and Close on send. The
// markup view covers the screen with the screenshot and its palette. Run DevPromptBuilder.Build() from an eval; edit
// the prefab freely afterwards (running it again replaces it).
public static class DevPromptBuilder
{
    const string Folder = "Assets/Game/Development/Resources";
    const string PrefabPath = Folder + "/Dev Prompt.prefab";
    const string Body = "Assets/Game/UI/Fonts/PixelOperator.asset";
    const string Bold = "Assets/Game/UI/Fonts/PixelOperator-Bold.asset";
    const string Frame = "Assets/Game/UI/Sprites/Inventory/Plain frame grey.png";
    const string Slot = "Assets/Game/UI/Sprites/Inventory/Plain slot grey.png";

    static readonly Color Gold = new(.91f, .74f, .38f);
    static readonly Color Cream = new(.93f, .91f, .87f);
    static readonly Color Dim = new(.66f, .64f, .6f);

    static TMP_FontAsset body, bold;
    static Sprite frame, slot;

    public static string Build()
    {
        body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Body);
        bold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Bold);
        frame = AssetDatabase.LoadAssetAtPath<Sprite>(Frame);
        slot = AssetDatabase.LoadAssetAtPath<Sprite>(Slot);

        var root = new GameObject("Dev Prompt", typeof(RectTransform));
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 950;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        root.AddComponent<GraphicRaycaster>();
        var prompt = root.AddComponent<DevPrompt>();

        // The window: a dim backing over the paused game, the framed panel in the middle.
        var panel = Rect("Panel", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Img(panel, null, new Color(0, 0, 0, .55f));
        var window = Rect("Window", panel, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1440, 760));
        Img(window, frame, Color.white, Image.Type.Sliced);

        // Left: the requests made from here.
        var side = Edges("Requests", window, new Vector2(0, 0), new Vector2(0, 1), new Vector2(28, 120), new Vector2(358, -28));
        Img(side, slot, new Color(1, 1, 1, .9f), Image.Type.Sliced);
        var sideTitle = Text("Title", side, "REQUESTS", 30, bold, Gold, TextAlignmentOptions.TopLeft);
        Edges(sideTitle.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -60), new Vector2(-16, -14));
        var counts = Text("Counts", side, "0 active / 0 waiting", 24, body, Dim, TextAlignmentOptions.TopLeft);
        Edges(counts.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -92), new Vector2(-16, -60));
        var list = Text("List", side, "No requests yet.", 24, body, Cream, TextAlignmentOptions.TopLeft);
        Edges(list.rectTransform, Vector2.zero, Vector2.one, new Vector2(16, 16), new Vector2(-16, -100));
        list.textWrappingMode = TextWrappingModes.Normal; list.overflowMode = TextOverflowModes.Truncate;

        // Middle: what Main should look into.
        const float L = 386, R = -516;
        var title = Text("Title", window, "ASK MAIN", 46, bold, Gold, TextAlignmentOptions.TopLeft);
        Edges(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(L, -84), new Vector2(R, -22));
        var sub = Text("Subtitle", window, "Developer request · F9", 24, body, Dim, TextAlignmentOptions.TopLeft);
        Edges(sub.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(L, -118), new Vector2(R, -86));
        var ask = Text("Prompt", window, "What should Main look into?", 26, body, Cream, TextAlignmentOptions.TopLeft);
        Edges(ask.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(L, -160), new Vector2(R, -126));

        var inputGo = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
        inputGo.name = "Message";
        inputGo.transform.SetParent(window, false);
        Edges((RectTransform)inputGo.transform, Vector2.zero, Vector2.one, new Vector2(L, 200), new Vector2(R, -170));
        var inputImage = inputGo.GetComponent<Image>();
        inputImage.sprite = slot; inputImage.type = Image.Type.Sliced; inputImage.color = new Color(.55f, .55f, .55f, 1f);
        var input = inputGo.GetComponent<TMP_InputField>();
        foreach (var t in inputGo.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            t.font = body; t.fontSize = 28;
            bool placeholder = t.gameObject.name == "Placeholder";
            t.color = placeholder ? new Color(.62f, .6f, .56f) : Cream;
            t.alignment = TextAlignmentOptions.TopLeft;
            t.textWrappingMode = TextWrappingModes.Normal;
            if (placeholder) t.text = "e.g. The torch in this corner flickers. Make it steady and a bit warmer.";
        }
        var area = inputGo.transform.Find("Text Area") as RectTransform;
        if (area != null) { area.offsetMin = new Vector2(16, 12); area.offsetMax = new Vector2(-16, -12); }
        input.pointSize = 28;
        input.lineType = TMP_InputField.LineType.MultiLineSubmit;
        input.restoreOriginalTextOnEscape = false;
        input.characterLimit = DevReportStore.MaxMessage;
        input.caretColor = Gold;
        input.selectionColor = new Color(.91f, .74f, .38f, .35f);

        var hint = Text("Hint", window, "Enter saves it for Main · Shift+Enter new line", 22, body, Dim, TextAlignmentOptions.TopLeft);
        Edges(hint.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(L, 160), new Vector2(R, 194));
        var status = Text("Status", window, "", 22, body, Gold, TextAlignmentOptions.TopLeft);
        Edges(status.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(L, 116), new Vector2(R, 158));
        status.textWrappingMode = TextWrappingModes.Normal;

        // Right: the screenshot (a button that opens the markup view) and the game state.
        var shotLabel = Text("Screenshot label", window, "SCREENSHOT · CLICK TO MARK", 24, bold, Gold, TextAlignmentOptions.TopLeft);
        Edges(shotLabel.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-488, -84), new Vector2(-28, -50));
        var shotFrame = Rect("Screenshot", window, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-28, -96), new Vector2(460, 259), pivot: new Vector2(1, 1));
        Img(shotFrame, slot, new Color(.4f, .4f, .4f, 1f), Image.Type.Sliced);
        var thumbGo = new GameObject("Picture", typeof(RectTransform), typeof(RawImage), typeof(Button));
        thumbGo.transform.SetParent(shotFrame, false);
        Edges((RectTransform)thumbGo.transform, Vector2.zero, Vector2.one, new Vector2(6, 6), new Vector2(-6, -6));
        var thumb = thumbGo.GetComponent<RawImage>(); thumb.color = Color.white;
        var thumbButton = thumbGo.GetComponent<Button>(); thumbButton.targetGraphic = thumb;
        var state = Text("Game state", window, "Game state attached", 22, body, Dim, TextAlignmentOptions.TopLeft);
        Edges(state.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-488, -520), new Vector2(-28, -370));
        state.textWrappingMode = TextWrappingModes.Normal;

        // Bottom: the buttons.
        var send = ButtonAt("Send", window, "SEND TO MAIN", new Vector2(L, 28), new Vector2(300, 74), Gold);
        var outbox = ButtonAt("Outbox", window, "OUTBOX", new Vector2(L + 316, 28), new Vector2(180, 74), Cream);
        var closeOnSend = ButtonAt("Close on send", window, "Close on send: off", new Vector2(-488, 28), new Vector2(240, 74), Dim, fromRight: true);
        var close = ButtonAt("Close", window, "CLOSE", new Vector2(-28 - 200, 28), new Vector2(200, 74), Cream, fromRight: true);

        // The markup view: the screenshot as large as fits, the palette and history along the bottom.
        var markup = Rect("Markup", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Img(markup, null, new Color(.03f, .03f, .04f, .96f));
        var fitArea = Rect("Fit area", markup, Vector2.zero, Vector2.one, new Vector2(40, 120), new Vector2(-40, -40));
        var picGo = new GameObject("Picture", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter), typeof(DevPromptDrawSurface));
        picGo.transform.SetParent(fitArea, false);
        var pr = (RectTransform)picGo.transform; pr.anchorMin = Vector2.zero; pr.anchorMax = Vector2.one; pr.offsetMin = pr.offsetMax = Vector2.zero;
        var fitter = picGo.GetComponent<AspectRatioFitter>(); fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent; fitter.aspectRatio = 16f / 9f;
        var pic = picGo.GetComponent<RawImage>();
        var palette = new Button[DevPromptMarkup.Palette.Length];
        string[] names = { "R", "G", "B", "Y" };
        for (int i = 0; i < palette.Length; i++)
        {
            var b = ButtonAt("Colour " + names[i], markup, names[i], new Vector2(40 + i * 92, 28), new Vector2(76, 70), Color.white, colourFill: DevPromptMarkup.Palette[i]);
            var swatch = b.GetComponent<Image>(); swatch.sprite = null; swatch.type = Image.Type.Simple;
            var outline = b.gameObject.AddComponent<Outline>(); outline.effectColor = Cream; outline.effectDistance = new Vector2(4, -4); outline.enabled = false;
            palette[i] = b;
        }
        var undo = ButtonAt("Undo", markup, "UNDO", new Vector2(440, 28), new Vector2(170, 70), Cream);
        var redo = ButtonAt("Redo", markup, "REDO", new Vector2(624, 28), new Vector2(170, 70), Cream);
        var clear = ButtonAt("Clear", markup, "CLEAR", new Vector2(808, 28), new Vector2(170, 70), Cream);
        var markHint = Text("Hint", markup, "Drag to draw · R G B Y colours · Ctrl+Z undo · Ctrl+Shift+Z redo · Esc done", 22, body, Dim, TextAlignmentOptions.MidlineLeft);
        Edges(markHint.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1000, 28), new Vector2(-260, 98));
        var done = ButtonAt("Done", markup, "DONE", new Vector2(-40 - 200, 28), new Vector2(200, 70), Gold, fromRight: true);
        markup.gameObject.SetActive(false);

        var so = new SerializedObject(prompt);
        so.FindProperty("panel").objectReferenceValue = panel.gameObject;
        so.FindProperty("message").objectReferenceValue = input;
        so.FindProperty("thumbnail").objectReferenceValue = thumb;
        so.FindProperty("thumbnailButton").objectReferenceValue = thumbButton;
        so.FindProperty("stateLine").objectReferenceValue = state;
        so.FindProperty("statusLine").objectReferenceValue = status;
        so.FindProperty("requests").objectReferenceValue = list;
        so.FindProperty("counts").objectReferenceValue = counts;
        so.FindProperty("sendButton").objectReferenceValue = send;
        so.FindProperty("closeButton").objectReferenceValue = close;
        so.FindProperty("outboxButton").objectReferenceValue = outbox;
        so.FindProperty("closeOnSendButton").objectReferenceValue = closeOnSend;
        so.FindProperty("closeOnSendLabel").objectReferenceValue = closeOnSend.GetComponentInChildren<TMP_Text>();
        so.FindProperty("markupPanel").objectReferenceValue = markup.gameObject;
        so.FindProperty("markupImage").objectReferenceValue = pic;
        so.FindProperty("markupAspect").objectReferenceValue = fitter;
        var pal = so.FindProperty("paletteButtons"); pal.arraySize = palette.Length;
        for (int i = 0; i < palette.Length; i++) pal.GetArrayElementAtIndex(i).objectReferenceValue = palette[i];
        so.FindProperty("undoButton").objectReferenceValue = undo;
        so.FindProperty("redoButton").objectReferenceValue = redo;
        so.FindProperty("clearButton").objectReferenceValue = clear;
        so.FindProperty("doneButton").objectReferenceValue = done;
        so.ApplyModifiedPropertiesWithoutUndo();

        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Game/Development", "Resources");
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return PrefabPath;
    }

    // ── Helpers ──

    static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 a, Vector2 b, Vector2? pivot = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var r = (RectTransform)go.transform;
        r.anchorMin = anchorMin; r.anchorMax = anchorMax; r.pivot = pivot ?? new Vector2(.5f, .5f);
        if (anchorMin == anchorMax) { r.anchoredPosition = a; r.sizeDelta = b; }
        else { r.offsetMin = a; r.offsetMax = b; }
        return r;
    }

    static RectTransform Edges(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return Edges((RectTransform)go.transform, anchorMin, anchorMax, offsetMin, offsetMax);
    }

    static RectTransform Edges(RectTransform r, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        r.anchorMin = anchorMin; r.anchorMax = anchorMax; r.pivot = new Vector2(.5f, .5f);
        r.offsetMin = offsetMin; r.offsetMax = offsetMax;
        return r;
    }

    static Image Img(RectTransform r, Sprite sprite, Color colour, Image.Type type = Image.Type.Simple)
    {
        var img = r.gameObject.AddComponent<Image>();
        img.sprite = sprite; img.color = colour; img.type = type;
        if (type == Image.Type.Sliced) img.pixelsPerUnitMultiplier = 1f;
        return img;
    }

    static TextMeshProUGUI Text(string name, Transform parent, string text, float size, TMP_FontAsset font, Color colour, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text; t.font = font; t.fontSize = size; t.color = colour; t.alignment = align;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.raycastTarget = false;
        return t;
    }

    static Button ButtonAt(string name, Transform parent, string label, Vector2 pos, Vector2 size, Color text, bool fromRight = false, Color32? colourFill = null)
    {
        var r = Rect(name, parent, new Vector2(fromRight ? 1 : 0, 0), new Vector2(fromRight ? 1 : 0, 0), pos, size, pivot: new Vector2(0, 0));
        var img = Img(r, slot, colourFill.HasValue ? (Color)colourFill.Value : new Color(.62f, .62f, .62f, 1f), Image.Type.Sliced);
        var b = r.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        var colours = b.colors; colours.highlightedColor = new Color(1.15f, 1.12f, 1.05f); colours.pressedColor = new Color(.8f, .78f, .74f); colours.disabledColor = new Color(.5f, .5f, .5f, .6f); b.colors = colours;
        var t = Text("Label", r, label, 28, bold, colourFill.HasValue ? new Color(0, 0, 0, .75f) : text, TextAlignmentOptions.Center);
        var tr = t.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
        return b;
    }
}
