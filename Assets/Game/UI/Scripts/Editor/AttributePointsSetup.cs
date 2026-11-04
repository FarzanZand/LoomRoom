using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Builds the attribute points panel (AttributePointsUI) in the open Room scene: under the inventory's panel, in the gap
// between the character panel and the bag, its four looks each under their own root (Stone rows, Ledger, Stone slots,
// Strip), with their sprites (Tools/points_sprites.py) and sounds (AudioData in Audio/Data/Attribute points). Run from
// an eval: AttributePointsSetup.Build(); safe to run again (it replaces the panel). Edit the built objects by hand after.
public static class AttributePointsSetup
{
    const string Inv = "Assets/Game/UI/Sprites/Inventory/", Own = "Assets/Game/UI/Sprites/Points/", Meta = "Assets/Game/UI/Sprites/Meta/";
    const string Lib = "Assets/ThirdParty/Sound Effects Library/", Sounds = "Assets/Game/Audio/Data/Attribute points/";
    const string CardSounds = "Assets/Game/Audio/Data/Level up cards/", Dice = "Assets/Game/Cinematics/Intro/Tabletop/Dice knock 1.wav";
    static readonly List<string> missing = new();
    static TMP_FontAsset bold, body;
    static readonly Color Gold = new(.86f, .7f, .38f), Ink = new(.75f, .73f, .69f), Gain = new(.84f, .84f, .82f), Faded = new(.55f, .51f, .45f);
    static readonly Color Tint = new(1f, .839f, .827f), Dark = new(.1f, .09f, .11f, .82f);
    static readonly Color PaperInk = new(.2f, .16f, .13f), PaperFaded = new(.33f, .28f, .23f), PaperGold = new(.55f, .3f, .12f);
    static readonly MetaAttribute[] Order = { MetaAttribute.Strength, MetaAttribute.Dexterity, MetaAttribute.Constitution, MetaAttribute.Intelligence };

    public static string Build()
    {
        missing.Clear();
        Sprites();
        bold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator-Bold.asset");
        body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator.asset");
        var inventory = Object.FindAnyObjectByType<InventoryUI>(FindObjectsInactive.Include);
        if (inventory == null) return "no InventoryUI in the open scene";
        var panel = (GameObject)new SerializedObject(inventory).FindProperty("panel").objectReferenceValue;
        if (panel == null) return "the InventoryUI has no panel";
        var parent = (RectTransform)panel.transform;
        var old = parent.Find("Attribute points");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var root = Rect("Attribute points", parent);
        root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
        root.sizeDelta = new Vector2(460f, 560f);
        // The panels' corners are only laid out while the bag is open; closed, the gap's middle measured in game is used.
        float x = GapCentre(inventory, parent);
        root.anchoredPosition = new Vector2(Mathf.Abs(x) < 1f ? -79f : x, 30f);
        var ui = root.gameObject.AddComponent<AttributePointsUI>();
        ui.designs = new[] { StoneRows(root), Ledger(root), StoneSlots(root), Strip(root) };
        ui.look = AttributePointsUI.Look.StoneRows;
        foreach (var d in ui.designs) d.root.gameObject.SetActive(false);
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
        float right = parent.InverseTransformPoint(a[2]).x, left = parent.InverseTransformPoint(b[0]).x;
        return (right + left) * .5f;
    }

    // ── The four looks ──────────────────────────────────────────

    // Stone rows: the inventory's warm grey stone panel, a gold title, four rows of grey slot stone, each with its icon
    // in a slot, the name in gold, the score now and after, what a point gives, and its key.
    static AttributePointsUI.Design StoneRows(RectTransform parent)
    {
        var d = Design(AttributePointsUI.Look.StoneRows, "Stone rows", parent, new Vector2(440f, 600f));
        Panel(d.root);
        d.title = Text("Title", d.root, bold, 26, Gold, new Vector2(0f, 262f), "1 POINT TO SPEND", 400);
        d.choices = new AttributePointsUI.Choice[4];
        // Each bonus on its own line (the user), so the rows are three lines of bonuses tall.
        for (int i = 0; i < 4; i++) d.choices[i] = Row(d.root, i, new Vector2(0f, 174f - i * 122f), new Vector2(392f, 114f), true);
        Colours(d, Ink, Gold, Faded);
        d.appear = Card("Tarot pop"); d.hover = Card("Tarot hover"); d.pick = Card("Tarot pick"); d.leave = Card("Tarot leave");
        return d;
    }

    // Ledger: a page of the character sheet's paper laid on the table, written in ink: a heading, and per attribute its
    // name, what a point gives in a smaller hand, and its score at the right; a pick strikes the score out and writes the
    // new one beside it.
    static AttributePointsUI.Design Ledger(RectTransform parent)
    {
        var d = Design(AttributePointsUI.Look.Ledger, "Ledger", parent, new Vector2(440f, 560f));
        var paper = Box("Paper", d.root, Sprite(Meta + "Paper.png"), new Vector2(440f, 560f), Color.white);
        paper.type = Image.Type.Sliced;
        d.title = Text("Title", d.root, bold, 26, PaperInk, new Vector2(0f, 226f), "1 point to spend", 380);
        var rule = Box("Rule", d.root, Sprite(Own + "Ink stroke.png"), new Vector2(300f, 6f), new Color(1f, 1f, 1f, .7f));
        rule.rectTransform.anchoredPosition = new Vector2(0f, 202f);
        d.choices = new AttributePointsUI.Choice[4];
        for (int i = 0; i < 4; i++)
        {
            var c = new AttributePointsUI.Choice { attribute = Order[i] };
            c.root = Rect(Order[i].ToString(), d.root); c.root.sizeDelta = new Vector2(380f, 96f); c.root.anchoredPosition = new Vector2(0f, 136f - i * 104f);
            c.root.gameObject.AddComponent<CanvasGroup>();
            c.hit = Box("Hit", c.root, null, c.root.sizeDelta, new Color(0f, 0f, 0f, 0f));
            c.label = Text("Name", c.root, bold, 24, PaperInk, new Vector2(-70f, 30f), "Strength", 240, TextAlignmentOptions.Left);
            // A bonus a line, as on the stone rows (small enough to read was the fault of one long line).
            c.gains = Text("Gains", c.root, body, 16, PaperFaded, new Vector2(-40f, -12f), "+1 damage", 300, TextAlignmentOptions.TopLeft);
            c.gains.textWrappingMode = TextWrappingModes.Normal; c.gains.rectTransform.sizeDelta = new Vector2(300f, 56f); c.gains.lineSpacing = -14f;
            c.from = Text("Score", c.root, bold, 28, PaperInk, new Vector2(128f, 6f), "8", 50);
            c.strike = Box("Strike", c.root, Sprite(Own + "Ink stroke.png"), new Vector2(40f, 7f), Color.white);
            c.strike.type = Image.Type.Filled; c.strike.fillMethod = Image.FillMethod.Horizontal; c.strike.fillAmount = 0f;
            c.strike.rectTransform.anchoredPosition = new Vector2(128f, 6f);
            c.to = Text("New score", c.root, bold, 28, PaperGold, new Vector2(170f, 10f), "9", 50);
            c.key = Text("Key", c.root, body, 14, PaperFaded, new Vector2(182f, 38f), "1", 20);
            d.choices[i] = c;
        }
        Colours(d, PaperInk, PaperGold, PaperFaded);
        d.appear = Sound("Ledger appear", .9f, .03f, Lib + "Items/Itm_Note_Open_01.wav", Lib + "Items/Itm_Note_Open_02.wav");
        d.hover = Sound("Ledger hover", .25f, .03f, Lib + "Items/Itm_Note_Pageturn_01.wav");
        d.pick = null;
        d.extra = Sound("Ledger write", .8f, .05f, "Assets/Game/Audio/Clips/UI/Pen scratch.wav");
        d.leave = Card("Tarot leave");
        return d;
    }

    // Stone slots: the stone panel with four square slots in a cross of two by two, each the attribute's icon over its
    // name, score and gains; a pick presses the slot in and lets a few embers
    // rise from it.
    static AttributePointsUI.Design StoneSlots(RectTransform parent)
    {
        var d = Design(AttributePointsUI.Look.StoneSlots, "Stone slots", parent, new Vector2(440f, 540f));
        Panel(d.root);
        d.title = Text("Title", d.root, bold, 26, Gold, new Vector2(0f, 232f), "1 POINT TO SPEND", 400);
        d.choices = new AttributePointsUI.Choice[4];
        for (int i = 0; i < 4; i++)
        {
            var at = new Vector2(i % 2 == 0 ? -100f : 100f, i < 2 ? 90f : -128f);
            var c = new AttributePointsUI.Choice { attribute = Order[i] };
            c.root = Rect(Order[i].ToString(), d.root); c.root.sizeDelta = new Vector2(192f, 208f); c.root.anchoredPosition = at;
            c.root.gameObject.AddComponent<CanvasGroup>();
            c.hit = Box("Slot", c.root, Sprite(Inv + "Plain slot grey.png"), c.root.sizeDelta, Color.white); c.hit.type = Image.Type.Sliced;
            c.edge = Box("Edge", c.root, Sprite(Inv + "Plain slot frame.png"), c.root.sizeDelta + new Vector2(6f, 6f), new Color(Gold.r, Gold.g, Gold.b, 0f)); c.edge.type = Image.Type.Sliced;
            c.icon = Box("Icon", c.root, null, new Vector2(56f, 56f), Color.white); c.icon.rectTransform.anchoredPosition = new Vector2(0f, 58f); c.icon.preserveAspect = true;
            c.label = Text("Name", c.root, bold, 20, Gold, new Vector2(0f, 12f), "STRENGTH", 180);
            c.from = Text("Score", c.root, bold, 24, Ink, new Vector2(-28f, -16f), "8", 40, TextAlignmentOptions.Right);
            c.to = Text("After", c.root, bold, 24, Gold, new Vector2(26f, -16f), "> 9", 64, TextAlignmentOptions.Left);
            c.gains = Text("Gains", c.root, body, 14, Gain, new Vector2(0f, -64f), "+1 damage", 180);
            c.gains.textWrappingMode = TextWrappingModes.Normal; c.gains.rectTransform.sizeDelta = new Vector2(176f, 60f); c.gains.lineSpacing = -12f;
            c.key = Text("Key", c.root, body, 14, Faded, new Vector2(78f, 88f), "1", 20);
            d.choices[i] = c;
        }
        d.spark = Box("Ember", d.root, Sprite(Own + "Ember.png"), new Vector2(8f, 8f), Color.white);
        d.spark.gameObject.SetActive(false);
        Colours(d, Ink, Gold, Faded);
        d.appear = Card("Tarot pop"); d.hover = Card("Tarot hover"); d.leave = Card("Tarot leave");
        d.pick = Sound("Slot press", .8f, .04f, Lib + "Items/Itm_Generic_Armor_Down_01.wav", Lib + "Items/Itm_Generic_Armor_Down_02 1.wav");
        d.pick.pitch = .82f; EditorUtility.SetDirty(d.pick); AssetDatabase.SaveAssetIfDirty(d.pick);
        d.extra = Sound("Slot embers", .3f, .06f, Lib + "Crafting/Crt_Matches_01.wav", Lib + "Crafting/Crt_Matches_02.wav");   // a short spark, not a fire tail
        return d;
    }

    // Strip: a short stone panel with the four attributes as slots side by side, each its icon, name and score now and
    // after; what a point gives is one line under them for the slot the pointer is on. A pick presses the slot in.
    static AttributePointsUI.Design Strip(RectTransform parent)
    {
        var d = Design(AttributePointsUI.Look.Strip, "Strip", parent, new Vector2(440f, 300f));
        Panel(d.root);
        d.title = Text("Title", d.root, bold, 26, Gold, new Vector2(0f, 112f), "1 POINT TO SPEND", 400);
        d.choices = new AttributePointsUI.Choice[4];
        for (int i = 0; i < 4; i++)
        {
            var c = new AttributePointsUI.Choice { attribute = Order[i] };
            c.root = Rect(Order[i].ToString(), d.root); c.root.sizeDelta = new Vector2(96f, 138f); c.root.anchoredPosition = new Vector2(-153f + i * 102f, 4f);
            c.root.gameObject.AddComponent<CanvasGroup>();
            c.hit = Box("Slot", c.root, Sprite(Inv + "Plain slot grey.png"), c.root.sizeDelta, Color.white); c.hit.type = Image.Type.Sliced;
            c.edge = Box("Edge", c.root, Sprite(Inv + "Plain slot frame.png"), c.root.sizeDelta + new Vector2(6f, 6f), new Color(Gold.r, Gold.g, Gold.b, 0f)); c.edge.type = Image.Type.Sliced;
            c.icon = Box("Icon", c.root, null, new Vector2(46f, 46f), Color.white); c.icon.rectTransform.anchoredPosition = new Vector2(0f, 34f); c.icon.preserveAspect = true;
            c.label = Text("Name", c.root, bold, 14, Gold, new Vector2(0f, -8f), "STR", 92);
            c.label.enableAutoSizing = true; c.label.fontSizeMin = 10; c.label.fontSizeMax = 14;
            c.from = Text("Score", c.root, bold, 22, Ink, new Vector2(-22f, -38f), "8", 32, TextAlignmentOptions.Right);
            c.to = Text("After", c.root, bold, 22, Gold, new Vector2(20f, -38f), "> 9", 52, TextAlignmentOptions.Left);
            c.key = Text("Key", c.root, body, 13, Faded, new Vector2(38f, 58f), "1", 18);
            d.choices[i] = c;
        }
        d.detail = Text("Gives", d.root, body, 16, Gain, new Vector2(0f, -100f), "", 400);
        d.detail.textWrappingMode = TextWrappingModes.Normal; d.detail.rectTransform.sizeDelta = new Vector2(400f, 44f); d.detail.lineSpacing = -14f;
        Colours(d, Ink, Gold, Faded);
        d.appear = Card("Tarot pop"); d.hover = Card("Tarot hover"); d.leave = Card("Tarot leave");
        d.pick = AssetDatabase.LoadAssetAtPath<AudioData>(Sounds + "Slot press.asset");
        return d;
    }

    // ── Pieces ──────────────────────────────────────────────────

    static AttributePointsUI.Design Design(AttributePointsUI.Look look, string name, RectTransform parent, Vector2 size)
    {
        var r = Rect(name, parent); r.sizeDelta = size;
        return new AttributePointsUI.Design { look = look, root = r, group = r.gameObject.AddComponent<CanvasGroup>() };
    }

    static void Colours(AttributePointsUI.Design d, Color ink, Color gold, Color faded) { d.ink = ink; d.gold = gold; d.faded = faded; }

    // The inventory's panel: the grey stone frame tiled with the panels' warm tint, and their grime over it.
    static void Panel(RectTransform r)
    {
        var face = Box("Frame", r, Sprite(Inv + "Plain frame grey.png"), r.sizeDelta, Tint); face.type = Image.Type.Tiled;
        var grime = Box("Grunge", r, Sprite("Assets/Game/UI/Sprites/Panel/Panel grunge.png"), r.sizeDelta - new Vector2(28f, 28f), Color.white);
        grime.raycastTarget = false;
    }

    static AttributePointsUI.Choice Row(RectTransform parent, int i, Vector2 at, Vector2 size, bool key)
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
        // What a point gives, wrapped to at most two lines under the name.
        float gainsH = size.y - 46f;
        c.gains = Text("Gains", c.root, body, 15, Gain, new Vector2(textX + textW * .5f, size.y * .5f - 40f - gainsH * .5f), "+1 damage", textW, TextAlignmentOptions.TopLeft);
        c.gains.textWrappingMode = TextWrappingModes.Normal; c.gains.rectTransform.sizeDelta = new Vector2(textW, gainsH); c.gains.lineSpacing = -14f;
        // The score ends and the new one starts at the same place in every row, so the arrows line up.
        c.from = Text("Score", c.root, bold, 26, Ink, new Vector2(size.x * .5f - 98f, size.y * .5f - 24f), "8", 40, TextAlignmentOptions.Right);
        c.to = Text("After", c.root, bold, 26, Gold, new Vector2(size.x * .5f - 42f, size.y * .5f - 24f), "> 9", 64, TextAlignmentOptions.Left);
        if (key) c.key = Text("Key", c.root, body, 14, Faded, new Vector2(size.x * .5f - 18f, -size.y * .5f + 16f), "1", 20);
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

    // The own sprites: point filtered and uncompressed like the inventory's art, two canvas units a texel.
    static void Sprites()
    {
        foreach (var name in new[] { "Ink stroke.png", "Ember.png" })
        {
            if (AssetImporter.GetAtPath(Own + name) is not TextureImporter ti) { missing.Add(name); continue; }
            ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single;
            ti.filterMode = FilterMode.Point; ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.spritePixelsPerUnit = 50; ti.alphaIsTransparency = true;
            ti.SaveAndReimport();
        }
        if (AssetImporter.GetAtPath(Meta + "Paper.png") is TextureImporter paper && paper.spriteBorder == Vector4.zero)
        { paper.spriteBorder = new Vector4(40f, 40f, 40f, 40f); paper.SaveAndReimport(); }
    }

    static AudioData Card(string name)
    {
        var data = AssetDatabase.LoadAssetAtPath<AudioData>(CardSounds + name + ".asset");
        if (data == null) missing.Add(name);
        return data;
    }

    static AudioData Sound(string name, float volume, float variance, params string[] clips)
    {
        if (!AssetDatabase.IsValidFolder(Sounds.TrimEnd('/'))) AssetDatabase.CreateFolder("Assets/Game/Audio/Data", "Attribute points");
        string path = Sounds + name + ".asset";
        var data = AssetDatabase.LoadAssetAtPath<AudioData>(path);
        if (data == null) { data = ScriptableObject.CreateInstance<AudioData>(); AssetDatabase.CreateAsset(data, path); }
        var list = new List<AudioClip>();
        foreach (var c in clips)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(c);
            if (clip != null) list.Add(clip); else missing.Add(System.IO.Path.GetFileName(c));
        }
        data.clips = list.ToArray(); data.volume = volume; data.pitchVariance = variance;
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssetIfDirty(data);
        return data;
    }
}
