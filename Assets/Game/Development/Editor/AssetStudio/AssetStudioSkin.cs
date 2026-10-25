using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Asset Studio in the game's own UI look: the inventory's plain grey stone frames and slots (UI/Sprites/Inventory, drawn
// at two screen pixels per art pixel as in the game), gold titles, off-white text and the PixelOperator fonts (editor
// copies in Fonts/, imported as hinted raster at 16 px so every letter stays on whole pixels). While the window draws
// (Use), Unity's own controls inside it take the fonts and colours too; they are put back as soon as it is done.
public static class StudioSkin
{
    public static readonly Color Back = new(.075f, .075f, .085f), Text = new(.902f, .902f, .902f), Dim = new(.847f, .8f, .706f),
        Gold = new(1f, .851f, .4f), Green = new(.45f, .85f, .4f), Warn = new(.88f, .66f, .35f);
    public const int Size = 16;
    const string Fonts = "Assets/Game/Development/Editor/AssetStudio/Fonts/", Sprites = "Assets/Game/UI/Sprites/Inventory/";

    static bool made;
    static Font body, bold;
    static Texture2D frame, slot, slotHover, slotDown, slotOn, field, fieldFocus, row, rowOn, line;
    static GUISkin skin;

    public static Font Body { get { Make(); return body; } }
    public static Font Bold { get { Make(); return bold; } }
    public static Texture2D Slot { get { Make(); return slot; } }

    // The window's own styles (titles, sections, rows, buttons), made once.
    public static GUIStyle Title => Style(ref title, () => new GUIStyle { font = Bold, fontSize = Size * 2, normal = { textColor = Gold }, padding = new RectOffset(0, 0, 0, 0), richText = true });
    public static GUIStyle Section => Style(ref section, () => new GUIStyle { font = Bold, fontSize = Size, normal = { textColor = Gold }, padding = new RectOffset(0, 0, 0, 0) });
    public static GUIStyle Label => Style(ref label, () => new GUIStyle { font = Body, fontSize = Size, normal = { textColor = Text }, richText = true, wordWrap = true, padding = new RectOffset(0, 0, 0, 0) });
    public static GUIStyle Strong => Style(ref strong, () => new GUIStyle(Label) { font = Bold, wordWrap = false, clipping = TextClipping.Clip });
    public static GUIStyle Faint => Style(ref faint, () => new GUIStyle(Label) { normal = { textColor = Dim * new Color(1, 1, 1, .8f) } });
    public static GUIStyle FaintLine => Style(ref faintLine, () => new GUIStyle(Faint) { wordWrap = false, clipping = TextClipping.Clip });
    public static GUIStyle Button => Style(ref button, () => SlotStyle(TextAnchor.MiddleCenter));
    public static GUIStyle Tab => Style(ref tab, () => SlotStyle(TextAnchor.MiddleCenter));
    public static GUIStyle Row => Style(ref rowStyle, () => new GUIStyle(Label) { wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(8, 6, 2, 2), hover = { background = row, textColor = Color.white }, normal = { textColor = Text } });
    public static GUIStyle RowOn => Style(ref rowOnStyle, () => new GUIStyle(Row) { normal = { background = rowOn, textColor = Color.white }, hover = { background = rowOn, textColor = Color.white }, border = new RectOffset(2, 2, 2, 2) });
    public static GUIStyle Panel => Style(ref panel, () => new GUIStyle { normal = { background = frame }, border = new RectOffset(16, 16, 16, 16), padding = new RectOffset(14, 14, 12, 12) });
    public static GUIStyle Inset => Style(ref inset, () => new GUIStyle { normal = { background = slot }, border = new RectOffset(8, 8, 8, 8), padding = new RectOffset(10, 10, 8, 8) });
    static GUIStyle title, section, label, strong, faint, faintLine, button, tab, rowStyle, rowOnStyle, panel, inset;
    static GUIStyle Style(ref GUIStyle s, Func<GUIStyle> make) { Make(); return s ??= make(); }

    static GUIStyle SlotStyle(TextAnchor align) => new()
    {
        font = Body, fontSize = Size, alignment = align, border = new RectOffset(8, 8, 8, 8), padding = new RectOffset(10, 10, 4, 5), margin = new RectOffset(2, 2, 2, 2),
        normal = { background = slot, textColor = Text }, hover = { background = slotHover, textColor = Color.white }, active = { background = slotDown, textColor = Gold },
        onNormal = { background = slotOn, textColor = Gold }, onHover = { background = slotOn, textColor = Gold }, onActive = { background = slotOn, textColor = Gold }, richText = true,
    };

    static void Make()
    {
        if (made && frame != null && body != null) return;
        made = true;
        body = AssetDatabase.LoadAssetAtPath<Font>(Fonts + "Studio PixelOperator.ttf");
        bold = AssetDatabase.LoadAssetAtPath<Font>(Fonts + "Studio PixelOperator-Bold.ttf") ?? body;
        // Read straight from the files (the sprites' own import settings are the game's), doubled point for point.
        var frameArt = Load(Sprites + "Plain frame grey.png");
        var slotArt = Load(Sprites + "Plain slot grey.png");
        frame = Double(frameArt, null);
        slot = Double(slotArt, null);
        slotHover = Double(slotArt, c => new Color(Mathf.Min(1, c.r * 1.35f + .02f), Mathf.Min(1, c.g * 1.35f + .02f), Mathf.Min(1, c.b * 1.38f + .02f), c.a));
        slotDown = Double(slotArt, c => new Color(c.r * .75f, c.g * .75f, c.b * .75f, c.a));
        slotOn = GoldEdge(Double(slotArt, null), 2);
        row = Flat(new Color(1, 1, 1, .06f));
        rowOn = GoldEdge(Flat(new Color(.17f, .16f, .12f), 8), 1);
        field = Flat(new Color(.09f, .09f, .1f));
        fieldFocus = GoldEdge(Flat(new Color(.09f, .09f, .1f), 8), 1);
        line = Flat(new Color(.27f, .27f, .29f));
        foreach (var t in new[] { frame, slot, slotHover, slotDown, slotOn, row, rowOn, field, fieldFocus, line }) { t.hideFlags = HideFlags.HideAndDontSave; t.filterMode = FilterMode.Point; }
        title = section = label = strong = faint = faintLine = button = tab = rowStyle = rowOnStyle = panel = inset = null;
        skin = null;
    }

    static Texture2D Load(string path)
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (File.Exists(path)) t.LoadImage(File.ReadAllBytes(path));
        return t;
    }

    static Texture2D Double(Texture2D src, Func<Color, Color> tint)
    {
        var t = new Texture2D(src.width * 2, src.height * 2, TextureFormat.RGBA32, false);
        var px = src.GetPixels();
        var output = new Color[t.width * t.height];
        for (int y = 0; y < t.height; y++)
            for (int x = 0; x < t.width; x++)
            {
                var c = px[(y / 2) * src.width + x / 2];
                output[y * t.width + x] = tint != null ? tint(c) : c;
            }
        t.SetPixels(output); t.Apply();
        return t;
    }

    static Texture2D Flat(Color c, int size = 1)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color[size * size];
        for (int i = 0; i < px.Length; i++) px[i] = c;
        t.SetPixels(px); t.Apply();
        return t;
    }

    // The game marks what is in hand with a gold frame: the outer ring of pixels turned gold.
    static Texture2D GoldEdge(Texture2D t, int width)
    {
        for (int y = 0; y < t.height; y++)
            for (int x = 0; x < t.width; x++)
                if (x < width || y < width || x >= t.width - width || y >= t.height - width) t.SetPixel(x, y, Gold);
        t.Apply();
        return t;
    }

    // ── Unity's controls while the window draws ──

    // A copy of the editor's skin with the game's buttons, frames and fonts, for GUI and GUILayout calls.
    static GUISkin Skin
    {
        get
        {
            Make();
            if (skin != null) return skin;
            skin = UnityEngine.Object.Instantiate(EditorGUIUtility.GetBuiltinSkin(EditorSkin.Inspector));
            skin.hideFlags = HideFlags.HideAndDontSave;
            skin.font = body;
            foreach (var s in new[] { skin.label, skin.toggle, skin.textField, skin.textArea, skin.box, skin.window })
            { s.font = body; s.fontSize = Size; s.normal.textColor = Text; s.padding = new RectOffset(s.padding.left, s.padding.right, Mathf.Min(1, s.padding.top), Mathf.Min(1, s.padding.bottom)); }
            skin.button = new GUIStyle(Button);
            skin.toggle.onNormal.textColor = skin.toggle.onHover.textColor = skin.toggle.hover.textColor = skin.toggle.onActive.textColor = Text;
            skin.box = new GUIStyle(Panel);
            skin.textField.normal.background = field; skin.textField.focused.background = fieldFocus; skin.textField.hover.background = field;
            skin.textField.border = new RectOffset(2, 2, 2, 2);
            skin.textField.normal.textColor = skin.textField.focused.textColor = Text;
            return skin;
        }
    }

    // EditorGUI's styles take the game's fonts and colours while the window draws; Unity's own look stays everywhere
    // else (they are put back when the window is done, even if its drawing stops early).
    static readonly List<(GUIStyle style, Font font, int size, Color normal, Color hover, Color focused, Color onNormal, RectOffset padding)> saved = new();

    public static IDisposable Use() => new Scope();

    sealed class Scope : IDisposable
    {
        readonly GUISkin before;
        readonly Color background;
        public Scope()
        {
            before = GUI.skin;
            background = GUI.backgroundColor;
            var s = Skin;
            GUI.skin = s;
            saved.Clear();
            void Patch(GUIStyle style, Font font)
            {
                if (style == null) return;
                saved.Add((style, style.font, style.fontSize, style.normal.textColor, style.hover.textColor, style.focused.textColor, style.onNormal.textColor, new RectOffset(style.padding.left, style.padding.right, style.padding.top, style.padding.bottom)));
                style.font = font; style.fontSize = Size;
                // 16 px of text in Unity's 18 px lines: at most one pixel above and below, or descenders are cut.
                style.padding = new RectOffset(style.padding.left, style.padding.right, Mathf.Min(1, style.padding.top), Mathf.Min(1, style.padding.bottom));
                style.normal.textColor = style.hover.textColor = style.focused.textColor = style.onNormal.textColor = Text;
            }
            foreach (var st in new[] { EditorStyles.label, EditorStyles.miniLabel, EditorStyles.largeLabel, EditorStyles.wordWrappedLabel, EditorStyles.wordWrappedMiniLabel,
                EditorStyles.textField, EditorStyles.textArea, EditorStyles.numberField, EditorStyles.popup, EditorStyles.objectField, EditorStyles.toggle,
                EditorStyles.helpBox, EditorStyles.miniButton, EditorStyles.toolbarSearchField, EditorStyles.radioButton, EditorStyles.miniTextField })
                Patch(st, body);
            foreach (var st in new[] { EditorStyles.boldLabel, EditorStyles.miniBoldLabel, EditorStyles.foldout, EditorStyles.foldoutHeader })
                Patch(st, bold);
        }

        public void Dispose()
        {
            foreach (var (style, font, size, normal, hover, focused, onNormal, padding) in saved)
            { style.font = font; style.fontSize = size; style.padding = padding; style.normal.textColor = normal; style.hover.textColor = hover; style.focused.textColor = focused; style.onNormal.textColor = onNormal; }
            saved.Clear();
            GUI.skin = before;
            GUI.backgroundColor = background;
        }
    }

    // A thin divider line, as the game's panels use between groups.
    public static void Divider(Rect r) { Make(); GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1), line); }
}
