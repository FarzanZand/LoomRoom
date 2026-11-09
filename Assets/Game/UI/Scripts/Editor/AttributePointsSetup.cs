using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Builds the attribute points panel (AttributePointsUI) in the open Room scene: under the inventory's panel, in the gap
// between the character panel and the bag, the inventory's warm grey stone panel with a gold title and four rows of grey
// slot stone, each with its icon in a slot, the name in gold, the score now and after, a line per bonus and its key.
// Sounds: the level-up cards' paper and knock, and the regular button click on a pick. Run from an eval:
// AttributePointsSetup.Build(); safe to run again (it replaces the panel). Edit the built objects by hand after.
public static class AttributePointsSetup
{
    const string Inv = "Assets/Game/UI/Sprites/Inventory/", CardSounds = "Assets/Game/Audio/Data/Level up cards/";
    static readonly List<string> missing = new();
    static TMP_FontAsset bold, body;
    static readonly Color Gold = new(.86f, .7f, .38f), Ink = new(.75f, .73f, .69f), Gain = new(.84f, .84f, .82f), Faded = new(.55f, .51f, .45f);
    static readonly Color Tint = new(1f, .839f, .827f), Dark = new(.1f, .09f, .11f, .82f);
    static readonly MetaAttribute[] Order = { MetaAttribute.Strength, MetaAttribute.Dexterity, MetaAttribute.Constitution, MetaAttribute.Intelligence };

    public static string Build()
    {
        missing.Clear();
        bold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator-Bold.asset");
        body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator.asset");
        var inventory = Object.FindAnyObjectByType<InventoryUI>(FindObjectsInactive.Include);
        if (inventory == null) return "no InventoryUI in the open scene";
        var holder = (GameObject)new SerializedObject(inventory).FindProperty("panel").objectReferenceValue;
        if (holder == null) return "the InventoryUI has no panel";
        var parent = (RectTransform)holder.transform;
        var old = parent.Find("Attribute points");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var root = Rect("Attribute points", parent);
        root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
        root.sizeDelta = new Vector2(440f, 600f);
        // The panels' corners are only laid out while the bag is open; closed, the gap's middle measured in game is used.
        float x = GapCentre(inventory, parent);
        root.anchoredPosition = new Vector2(Mathf.Abs(x) < 1f ? -79f : x, 30f);
        var ui = root.gameObject.AddComponent<AttributePointsUI>();

        var panel = Rect("Panel", root); panel.sizeDelta = root.sizeDelta;
        ui.panel = panel;
        ui.group = panel.gameObject.AddComponent<CanvasGroup>();
        var face = Box("Frame", panel, Sprite(Inv + "Plain frame grey.png"), panel.sizeDelta, Tint); face.type = Image.Type.Tiled;
        Box("Grunge", panel, Sprite("Assets/Game/UI/Sprites/Panel/Panel grunge.png"), panel.sizeDelta - new Vector2(28f, 28f), Color.white);
        ui.title = Text("Title", panel, bold, 26, Gold, new Vector2(0f, 262f), "1 POINT TO SPEND", 400);
        ui.choices = new AttributePointsUI.Choice[4];
        // Each bonus on its own line (the user), so the rows are three lines of bonuses tall.
        for (int i = 0; i < 4; i++) ui.choices[i] = Row(panel, i, new Vector2(0f, 174f - i * 122f), new Vector2(392f, 114f));
        ui.ink = Ink; ui.gold = Gold; ui.faded = Faded;
        ui.appear = Card("Tarot pop"); ui.hover = Card("Tarot hover"); ui.leave = Card("Tarot leave");
        ui.pickKey = AttributeCellUI.ClickKey;   // a pick is the regular button click (the user, 2026-11-04)
        panel.gameObject.SetActive(false);
        root.SetAsLastSibling();
        EditorUtility.SetDirty(ui);
        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        return "built" + (missing.Count > 0 ? "; missing: " + string.Join(", ", missing) : "");
    }

    // The middle of the gap between the character panel's right edge and the bag panel's left edge, in the panel's space.
    static float GapCentre(InventoryUI inv, RectTransform parent)
    {
        if (inv.statsPanel == null || inv.inventoryPanel == null) return 0f;
        var a = new Vector3[4]; var b = new Vector3[4];
        inv.statsPanel.GetWorldCorners(a); inv.inventoryPanel.GetWorldCorners(b);
        return (parent.InverseTransformPoint(a[2]).x + parent.InverseTransformPoint(b[0]).x) * .5f;
    }

    static AttributePointsUI.Choice Row(RectTransform parent, int i, Vector2 at, Vector2 size)
    {
        var c = new AttributePointsUI.Choice { attribute = Order[i] };
        c.root = Rect(Order[i].ToString(), parent); c.root.sizeDelta = size; c.root.anchoredPosition = at;
        c.root.gameObject.AddComponent<CanvasGroup>();
        c.hit = Box("Row", c.root, Sprite(Inv + "Plain slot grey.png"), size, Color.white); c.hit.type = Image.Type.Sliced;
        c.edge = Box("Edge", c.root, Sprite(Inv + "Plain slot frame.png"), size + new Vector2(6f, 6f), new Color(Gold.r, Gold.g, Gold.b, 0f)); c.edge.type = Image.Type.Sliced;
        float h = size.y;
        var slot = Box("Icon slot", c.root, Sprite(Inv + "Plain slot grey.png"), new Vector2(h - 18f, h - 18f), Dark); slot.type = Image.Type.Sliced;
        slot.rectTransform.anchoredPosition = new Vector2(-size.x * .5f + h * .5f, 0f);
        c.icon = Box("Icon", c.root, null, new Vector2(h - 34f, h - 34f), Color.white); c.icon.preserveAspect = true;
        c.icon.rectTransform.anchoredPosition = slot.rectTransform.anchoredPosition;
        float textX = -size.x * .5f + h + 8f;
        float textW = size.x - h - 22f;
        c.label = Text("Name", c.root, bold, 22, Gold, new Vector2(textX + 110f, size.y * .5f - 24f), "STRENGTH", 220, TextAlignmentOptions.Left);
        float gainsH = size.y - 46f;
        c.gains = Text("Gains", c.root, body, 15, Gain, new Vector2(textX + textW * .5f, size.y * .5f - 40f - gainsH * .5f), "+1 damage", textW, TextAlignmentOptions.TopLeft);
        c.gains.textWrappingMode = TextWrappingModes.Normal; c.gains.rectTransform.sizeDelta = new Vector2(textW, gainsH); c.gains.lineSpacing = -14f;
        // The score ends and the new one starts at the same place in every row, so the arrows line up.
        c.from = Text("Score", c.root, bold, 26, Ink, new Vector2(size.x * .5f - 98f, size.y * .5f - 24f), "8", 40, TextAlignmentOptions.Right);
        c.to = Text("After", c.root, bold, 26, Gold, new Vector2(size.x * .5f - 42f, size.y * .5f - 24f), "> 9", 64, TextAlignmentOptions.Left);
        c.key = Text("Key", c.root, body, 14, Faded, new Vector2(size.x * .5f - 18f, -size.y * .5f + 16f), "1", 20);
        return c;
    }

    static RectTransform Rect(string name, RectTransform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var r = (RectTransform)go.transform;
        r.SetParent(parent, false);
        return r;
    }

    static Image Box(string name, RectTransform parent, Sprite sprite, Vector2 size, Color colour)
    {
        var r = Rect(name, parent);
        r.sizeDelta = size;
        var image = r.gameObject.AddComponent<Image>();
        image.sprite = sprite; image.color = colour; image.raycastTarget = false;
        return image;
    }

    static TextMeshProUGUI Text(string name, RectTransform parent, TMP_FontAsset font, float size, Color colour, Vector2 at, string text, float width, TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var r = Rect(name, parent);
        r.sizeDelta = new Vector2(width, size * 1.4f);
        r.anchoredPosition = at;
        var t = r.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = font; t.fontSize = size; t.color = colour; t.text = text; t.alignment = align;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.raycastTarget = false;
        return t;
    }

    static Sprite Sprite(string path)
    {
        var s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (s == null) missing.Add(System.IO.Path.GetFileName(path));
        return s;
    }

    static AudioData Card(string name)
    {
        var data = AssetDatabase.LoadAssetAtPath<AudioData>(CardSounds + name + ".asset");
        if (data == null) missing.Add(name);
        return data;
    }
}
