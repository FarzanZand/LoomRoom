using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The character sheet (CharacterSheetUI), the key items column (KeyItemsUI) and the tally rows on the run recap,
// authored under UI/Canvas. Rebuilding replaces the sheet and the key items, and the recap's tally rows.
public static partial class MetaProgressionSetup
{
    const string MetaSprites = "Assets/Game/UI/Sprites/Meta/";
    static readonly Color Ink = new(.17f, .15f, .14f), Pen = new(.58f, .16f, .12f), Faint = new(.47f, .45f, .42f);

    // The sheet's shapes drawn smooth at four times their old size (Tools/meta_sheet_smooth.py): imported at four times
    // the pixels per unit with bilinear filtering and mipmaps, so they keep their size and stay crisp; the paper is
    // smooth too; anything else stays point-filtered.
    static readonly string[] SmoothShapes = { "Bubble outline", "Bubble filled", "Bubble dashed", "Ring", "Ring mask", "Icon frame", "Crest", "Pen frame" };

    static Sprite MetaSprite(string name, Vector4 border = default)
    {
        string path = MetaSprites + name + ".png";
        bool smooth = System.Array.IndexOf(SmoothShapes, name) >= 0, paper = name == "Paper";
        float ppu = smooth ? 200f : 50f;
        if (smooth) border *= 4f;
        var filter = smooth || paper ? FilterMode.Bilinear : FilterMode.Point;
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null && (importer.textureType != TextureImporterType.Sprite || importer.filterMode != filter || importer.spriteBorder != border || !Mathf.Approximately(importer.spritePixelsPerUnit, ppu)))
        {
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = filter; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = smooth || paper; importer.spritePixelsPerUnit = ppu; importer.spriteBorder = border; importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // A pixel icon's smooth copy for the sheet (Sheet icons/, Tools/meta_sheet_smooth.py), or the icon itself.
    public static Sprite SheetIcon(Sprite pixel)
    {
        if (pixel == null) return null;
        string path = MetaSprites + "Sheet icons/" + System.IO.Path.GetFileName(AssetDatabase.GetAssetPath(pixel));
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return pixel;
        if (importer.textureType != TextureImporterType.Sprite || importer.filterMode != FilterMode.Bilinear || !importer.mipmapEnabled)
        {
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Bilinear; importer.mipmapEnabled = true; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.spritePixelsPerUnit = 128; importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static TMP_FontAsset Font(string name) => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"Assets/Game/UI/Fonts/{name}.asset");

    static RectTransform Rect(string name, Transform parent, Vector2 pos, Vector2 size, Vector2? anchor = null, Vector2? pivot = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor ?? new Vector2(.5f, .5f);
        rt.pivot = pivot ?? new Vector2(.5f, .5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }
    static Image Img(string name, Transform parent, Vector2 pos, Vector2 size, Sprite sprite, Color color, Image.Type type = Image.Type.Simple, bool raycast = false)
    {
        var img = Rect(name, parent, pos, size).gameObject.AddComponent<Image>();
        img.sprite = sprite; img.color = color; img.type = type; img.raycastTarget = raycast;
        return img;
    }
    static TMP_Text Text(string name, Transform parent, Vector2 pos, Vector2 size, string text, TMP_FontAsset font, float fontSize, Color color, TextAlignmentOptions align = TextAlignmentOptions.Left)
    {
        var t = Rect(name, parent, pos, size).gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text; t.font = font; t.fontSize = fontSize; t.color = color; t.alignment = align; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }
    // Buy buttons are inked price tags, the others rubber stamps (sprites from Tools/meta_sheet_buttons.py), not boxes.
    static Button PenButton(string name, Transform parent, Vector2 pos, Vector2 size, string label, float fontSize, out TMP_Text text)
    {
        bool buy = name.EndsWith(" buy");
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Game/UI/Sprites/Meta/{(buy ? "Ink tag" : "Ink stamp")}.png");
        var img = Img(name, parent, pos, size, sprite, buy ? Color.white : Pen, Image.Type.Sliced, true);
        img.pixelsPerUnitMultiplier = 1f;
        var b = img.gameObject.AddComponent<Button>();
        var colors = b.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1f, .9f, .82f); colors.pressedColor = new Color(.85f, .78f, .72f); colors.disabledColor = new Color(1, 1, 1, .45f); b.colors = colors;
        b.targetGraphic = img;
        text = Text("Label", img.transform, buy ? new Vector2(8, 0) : Vector2.zero, buy ? new Vector2(size.x - 22, size.y) : size, label, Font("PixelOperator-Bold"), fontSize, Pen, TextAlignmentOptions.Center);
        return b;
    }

    static Transform HudCanvas()
    {
        var canvas = GameObject.Find("UI/Canvas");
        if (canvas == null) throw new System.Exception("No UI/Canvas in the open scene.");
        return canvas.transform;
    }

    // ── The character sheet ───────────────────────────────────────────

    [MenuItem("Tools/LoomRoom/Meta/Build Character Sheet", priority = 312)]
    public static void BuildCharacterSheet()
    {
        var canvas = HudCanvas();
        var old = canvas.Find("Character sheet");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var holder = Rect("Character sheet", canvas, Vector2.zero, Vector2.zero, Vector2.zero);
        holder.anchorMin = Vector2.zero; holder.anchorMax = Vector2.one; holder.sizeDelta = Vector2.zero;
        Undo.RegisterCreatedObjectUndo(holder.gameObject, "Build character sheet");
        var ui = holder.gameObject.AddComponent<CharacterSheetUI>();
        var root = Rect("Sheet", holder, Vector2.zero, Vector2.zero, Vector2.zero);
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.sizeDelta = Vector2.zero;
        var dim = root.gameObject.AddComponent<Image>(); dim.color = new Color(0, 0, 0, .55f); dim.raycastTarget = true;
        var paper = Img("Paper", root, Vector2.zero, new Vector2(1640, 900), MetaSprite("Paper"), Color.white);
        var p = paper.transform;
        var border = Img("Border", p, Vector2.zero, new Vector2(1580, 840), MetaSprite("Pen frame", new Vector4(3, 3, 3, 3)), new Color(Ink.r, Ink.g, Ink.b, .55f), Image.Type.Sliced);
        var body = Font("PixelOperator"); var bold = Font("PixelOperator-Bold"); var title = Font("PixelOperator-Bold");   // one font everywhere

        // You: portrait, name, attributes.
        var ring = Img("Portrait ring", p, new Vector2(-580, 230), new Vector2(260, 260), MetaSprite("Ring"), Color.white);
        var maskImg = Img("Portrait mask", ring.transform, Vector2.zero, new Vector2(216, 216), MetaSprite("Ring mask"), Color.white);
        maskImg.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        var portrait = Img("Portrait", maskImg.transform, Vector2.zero, new Vector2(176, 216), MetaSprite("Portrait placeholder"), Color.white);
        portrait.preserveAspect = true;
        var prev = PenButton("Previous portrait", p, new Vector2(-760, 230), new Vector2(52, 52), "<", 30, out _);
        var next = PenButton("Next portrait", p, new Vector2(-400, 230), new Vector2(52, 52), ">", 30, out _);
        var portraitLabel = Text("Portrait label", p, new Vector2(-580, 78), new Vector2(360, 30), "Portrait 1 of 3", body, 22, Faint, TextAlignmentOptions.Center);
        // Name: an input on a pencil line.
        var nameRect = Rect("Name", p, new Vector2(-580, 22), new Vector2(340, 56));
        var nameArea = Rect("Text Area", nameRect, Vector2.zero, Vector2.zero, Vector2.zero);
        nameArea.anchorMin = Vector2.zero; nameArea.anchorMax = Vector2.one; nameArea.sizeDelta = Vector2.zero;
        nameArea.gameObject.AddComponent<RectMask2D>();
        var nameText = Text("Text", nameArea, Vector2.zero, Vector2.zero, "", bold, 38, Pen, TextAlignmentOptions.Center);
        ((RectTransform)nameText.transform).anchorMin = Vector2.zero; ((RectTransform)nameText.transform).anchorMax = Vector2.one;
        var placeholder = Text("Placeholder", nameArea, Vector2.zero, Vector2.zero, "Your name", body, 32, new Color(Faint.r, Faint.g, Faint.b, .6f), TextAlignmentOptions.Center);
        ((RectTransform)placeholder.transform).anchorMin = Vector2.zero; ((RectTransform)placeholder.transform).anchorMax = Vector2.one;
        var input = nameRect.gameObject.AddComponent<TMP_InputField>();
        input.textViewport = nameArea; input.textComponent = nameText; input.placeholder = placeholder; input.characterLimit = 16;
        input.caretColor = Pen; input.selectionColor = new Color(Pen.r, Pen.g, Pen.b, .25f); input.customCaretColor = true;
        var nameHit = nameRect.gameObject.AddComponent<Image>(); nameHit.color = new Color(1, 1, 1, 0); input.targetGraphic = nameHit;
        Img("Line", p, new Vector2(-580, -8), new Vector2(340, 2), null, Ink);
        Text("Name label", p, new Vector2(-580, -28), new Vector2(340, 26), "NAME", body, 20, Faint, TextAlignmentOptions.Center);

        var rows = new List<CharacterSheetUI.Row>();
        string[] attributes = { "constitution", "intelligence", "strength" };
        for (int i = 0; i < attributes.Length; i++)
        {
            float y = -120 - i * 105;
            var row = new CharacterSheetUI.Row { trackId = attributes[i] };
            var crest = Img($"{attributes[i]} crest", p, new Vector2(-735, y), new Vector2(64, 72), MetaSprite("Crest"), Color.white);
            row.value = Text("Score", crest.transform, new Vector2(0, 6), new Vector2(64, 40), "8", bold, 32, Ink, TextAlignmentOptions.Center);
            row.unit = Text($"{attributes[i]} bonus", p, new Vector2(-735, y - 48), new Vector2(140, 24), "+0 health", body, 20, Faint, TextAlignmentOptions.Center);
            row.icon = crest;
            var attrIcon = Img($"{attributes[i]} icon", p, new Vector2(-670, y + 18), new Vector2(32, 32), AttributeIcon(attributes[i]), Color.white);
            attrIcon.preserveAspect = true;
            row.title = Text($"{attributes[i]} name", p, new Vector2(-532, y + 18), new Vector2(230, 36), attributes[i], bold, 28, Ink);
            row.title.raycastTarget = true;
            var bubbles = new List<Image>();
            for (int k = 0; k < 8; k++) bubbles.Add(Img($"{attributes[i]} level {k + 1}", p, new Vector2(-672 + k * 26, y - 22), new Vector2(20, 20), MetaSprite("Bubble outline"), Ink, Image.Type.Simple, true));
            row.bubbles = bubbles.ToArray();
            row.buy = PenButton($"{attributes[i]} buy", p, new Vector2(-412, y - 22), new Vector2(104, 36), "Train 10", 18, out row.buyLabel);
            rows.Add(row);
        }
        Img("Divider", p, new Vector2(-350, 0), new Vector2(2, 780), null, new Color(Ink.r, Ink.g, Ink.b, .35f));

        // Skills.
        Text("Title", p, new Vector2(-320, 382), new Vector2(700, 64), "Character sheet", title, 40, Ink).rectTransform.pivot = new Vector2(0, .5f);
        ((RectTransform)p.Find("Title")).anchoredPosition = new Vector2(-320, 382);
        var purse = Img("Purse", p, new Vector2(555, 382), new Vector2(430, 64), MetaSprite("Pen frame", new Vector4(3, 3, 3, 3)), Ink, Image.Type.Sliced);
        var purseIcon = Img("Spoils icon", purse.transform, new Vector2(-180, 0), new Vector2(46, 46), null, Color.white);
        var gem = AssetDatabase.LoadAssetAtPath<ItemData>($"{SpoilsData}/Grand spoils.asset");   // spoils as a currency: the gem
        purseIcon.sprite = gem != null ? gem.icon : null; purseIcon.preserveAspect = true;
        var spoilsText = Text("Spoils", purse.transform, new Vector2(-100, 0), new Vector2(110, 50), "0", bold, 40, Pen, TextAlignmentOptions.Center);
        Text("Spoils label", purse.transform, new Vector2(35, -2), new Vector2(170, 30), "SPOILS TO SPEND", body, 20, Ink);
        var refund = PenButton("Refund all", purse.transform, new Vector2(150, 0), new Vector2(104, 38), "Refund all", 20, out _);
        AdventureSkill[] order = { AdventureSkill.Blocking, AdventureSkill.Swords, AdventureSkill.Maces, AdventureSkill.Sorcery, AdventureSkill.Thaumaturgy, AdventureSkill.Athletics, AdventureSkill.Stealth, AdventureSkill.Trading };
        int[][] milestoneLevels = new int[order.Length][];
        for (int i = 0; i < order.Length; i++)
        {
            float y = 285 - i * 86;
            string id = order[i].ToString().ToLowerInvariant();
            var row = new CharacterSheetUI.Row { trackId = id };
            var frame = Img($"{id} icon frame", p, new Vector2(-280, y), new Vector2(72, 72), MetaSprite("Icon frame", new Vector4(4, 4, 4, 4)), Color.white, Image.Type.Sliced);
            row.icon = Img($"{id} icon", frame.transform, Vector2.zero, new Vector2(56, 56), null, Color.white, Image.Type.Simple, true);
            row.icon.preserveAspect = true;
            row.title = Text($"{id} name", p, new Vector2(-110, y + 12), new Vector2(260, 40), order[i].ToString(), bold, 34, Ink);
            row.title.raycastTarget = true;
            Text($"{id} level label", p, new Vector2(-152, y - 22), new Vector2(170, 28), "LEVEL", body, 20, Faint);
            row.value = Text($"{id} level", p, new Vector2(-48, y - 21), new Vector2(80, 30), "10", bold, 28, Ink);
            int firstAt = order[i] == AdventureSkill.Blocking ? 15 : 25;
            var bubbles = new List<Image>(); var icons = new List<Image>(); var marks = new List<TMP_Text>();
            float x = 60;
            for (int k = 0; k < 10; k++)
            {
                int value = (k + 1) * 5;
                bool milestone = value == firstAt || value == 50;
                float size = milestone ? 46 : 30;
                var b = Img($"{id} level {k + 1}", p, new Vector2(x + size / 2, y), new Vector2(size, size), MetaSprite("Bubble outline"), Ink, Image.Type.Simple, true);
                bubbles.Add(b);
                if (milestone)
                {
                    var mark = Text("Mark", b.transform, new Vector2(0, 1), new Vector2(size, size), "?", bold, 26, Ink, TextAlignmentOptions.Center);
                    var icon = Img("Unlock icon", b.transform, Vector2.zero, new Vector2(30, 30), null, Color.white);
                    icon.preserveAspect = true; icon.enabled = false;
                    Text("Value", b.transform, new Vector2(0, size / 2 + 12), new Vector2(60, 22), value.ToString(), bold, 18, Ink, TextAlignmentOptions.Center);
                    marks.Add(mark); icons.Add(icon);
                }
                x += size + 12;
            }
            row.bubbles = bubbles.ToArray(); row.milestoneIcons = icons.ToArray(); row.milestoneMarks = marks.ToArray();
            row.buy = PenButton($"{id} buy", p, new Vector2(685, y), new Vector2(150, 48), "Train 10", 28, out row.buyLabel);
            if (i < order.Length - 1) Img($"{id} rule", p, new Vector2(230, y - 43), new Vector2(1060, 2), null, new Color(Ink.r, Ink.g, Ink.b, .12f));
            rows.Add(row);
        }
        var feedback = Text("Feedback", p, new Vector2(-40, -398), new Vector2(900, 34), "", bold, 26, Pen, TextAlignmentOptions.Center);
        var close = PenButton("Put down", p, new Vector2(680, -392), new Vector2(170, 46), "Put down", 26, out _);
        var banner = Img("Unlock banner", p, new Vector2(230, 20), new Vector2(480, 96), MetaSprite("Pen frame", new Vector4(3, 3, 3, 3)), Pen, Image.Type.Sliced);
        var bannerFill = Img("Fill", banner.transform, Vector2.zero, new Vector2(472, 88), null, new Color(.985f, .978f, .955f, .97f));
        var unlockTitle = Text("Title", banner.transform, new Vector2(0, 12), new Vector2(460, 44), "Unlocked: Shield bash", title, 34, Pen, TextAlignmentOptions.Center);
        var unlockSub = Text("Sub", banner.transform, new Vector2(0, -24), new Vector2(460, 28), "A trophy for the wall", body, 22, Ink, TextAlignmentOptions.Center);
        banner.gameObject.SetActive(false);

        // Hover card.
        var tip = Rect("Hover card", root, Vector2.zero, new Vector2(420, 10), new Vector2(.5f, .5f), new Vector2(0, 1));
        var tipBg = tip.gameObject.AddComponent<Image>(); tipBg.sprite = MetaSprite("Pen frame", new Vector4(3, 3, 3, 3)); tipBg.type = Image.Type.Sliced; tipBg.color = Ink; tipBg.raycastTarget = false;
        var fill = Img("Fill", tip, Vector2.zero, Vector2.zero, null, new Color(.985f, .978f, .955f)); fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = Vector2.one; fill.rectTransform.sizeDelta = new Vector2(-4, -4);
        fill.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var layout = tip.gameObject.AddComponent<VerticalLayoutGroup>(); layout.padding = new RectOffset(18, 18, 14, 16); layout.spacing = 6; layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false;
        tip.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var tipTitle = Text("Title", tip, Vector2.zero, new Vector2(384, 34), "Blocking", bold, 30, Ink);
        var tipBody = Text("Body", tip, Vector2.zero, new Vector2(384, 30), "", body, 22, Ink); tipBody.textWrappingMode = TextWrappingModes.Normal;
        tip.gameObject.SetActive(false);

        var so = new SerializedObject(ui);
        so.FindProperty("root").objectReferenceValue = root.gameObject;
        so.FindProperty("portrait").objectReferenceValue = portrait;
        so.FindProperty("previousPortrait").objectReferenceValue = prev;
        so.FindProperty("nextPortrait").objectReferenceValue = next;
        so.FindProperty("portraitLabel").objectReferenceValue = portraitLabel;
        so.FindProperty("nameField").objectReferenceValue = input;
        so.FindProperty("spoilsText").objectReferenceValue = spoilsText;
        so.FindProperty("refundButton").objectReferenceValue = refund;
        so.FindProperty("closeButton").objectReferenceValue = close;
        so.FindProperty("feedback").objectReferenceValue = feedback;
        so.FindProperty("tip").objectReferenceValue = tip;
        so.FindProperty("unlockBanner").objectReferenceValue = banner.rectTransform;
        so.FindProperty("unlockTitle").objectReferenceValue = unlockTitle;
        so.FindProperty("unlockSub").objectReferenceValue = unlockSub;
        so.FindProperty("tipTitle").objectReferenceValue = tipTitle;
        so.FindProperty("tipBody").objectReferenceValue = tipBody;
        so.FindProperty("bubbleFilled").objectReferenceValue = MetaSprite("Bubble filled");
        so.FindProperty("bubbleOutline").objectReferenceValue = MetaSprite("Bubble outline");
        so.FindProperty("bubbleDashed").objectReferenceValue = MetaSprite("Bubble dashed");
        so.FindProperty("portraitPlaceholder").objectReferenceValue = MetaSprite("Portrait placeholder");
        so.ApplyModifiedPropertiesWithoutUndo();
        // The rows (a list of plain classes) are set straight on the component.
        var field = typeof(CharacterSheetUI).GetField("rows", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field.SetValue(ui, rows);
        EditorUtility.SetDirty(ui);
        root.gameObject.SetActive(false);
        var announcement = canvas.Find("Announcement");
        if (announcement != null) holder.SetSiblingIndex(announcement.GetSiblingIndex()); else holder.SetAsLastSibling();
        BuildKeyItems(canvas);
        EditorSceneManager.MarkSceneDirty(holder.gameObject.scene);
        Debug.Log("Meta: character sheet built under UI/Canvas");
    }

    static Sprite AttributeIcon(string id) => SheetIcon(AssetDatabase.LoadAssetAtPath<Sprite>(SkillIcons + (id == "constitution" ? "102_Passive_Male_Vitality.png" : id == "intelligence" ? "157_Mastery_Staff.png" : "134_Strength.png")));

    static void BuildKeyItems(Transform canvas)
    {
        var old = canvas.Find("Key items");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var column = Rect("Key items", canvas, Vector2.zero, Vector2.zero, Vector2.zero);
        column.anchorMin = Vector2.zero; column.anchorMax = Vector2.one; column.sizeDelta = Vector2.zero;
        Undo.RegisterCreatedObjectUndo(column.gameObject, "Key items");
        var ui = column.gameObject.AddComponent<KeyItemsUI>();
        var slotSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/UI/Sprites/Diablo/Plain slot grey.png");
        var slot = Rect("Key item", column, new Vector2(-70, -260), new Vector2(92, 92), new Vector2(1, 1));
        var bg = slot.gameObject.AddComponent<Image>(); bg.sprite = slotSprite; bg.type = Image.Type.Sliced; bg.raycastTarget = false; bg.pixelsPerUnitMultiplier = 1f;
        var icon = Img("Icon", slot, Vector2.zero, new Vector2(72, 72), null, Color.white);
        icon.preserveAspect = true;
        var so = new SerializedObject(ui);
        so.FindProperty("template").objectReferenceValue = slot;
        so.FindProperty("templateIcon").objectReferenceValue = icon;
        so.ApplyModifiedPropertiesWithoutUndo();
        slot.gameObject.SetActive(false);
    }

    // ── The tally rows on the run recap ───────────────────────────────

    [MenuItem("Tools/LoomRoom/Meta/Build Tally", priority = 313)]
    public static void BuildTally()
    {
        var recap = Object.FindAnyObjectByType<RunRecapUI>(FindObjectsInactive.Include);
        if (recap == null) throw new System.Exception("No RunRecapUI in the open scene.");
        var panel = recap.transform.Find("Recap screen/Panel") as RectTransform;
        Undo.RecordObject(panel, "Build tally");
        panel.sizeDelta = new Vector2(860, 940);
        var old = panel.Find("Tally");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        void Move(string name, float y) { var t = panel.Find(name) as RectTransform; if (t != null) { Undo.RecordObject(t, "Build tally"); t.anchoredPosition = new Vector2(t.anchoredPosition.x, y); } }
        Move("Title", 395); Move("Cause", 320); Move("Stats", 40); Move("Seed", -322);
        foreach (var b in new[] { "New run", "Adventures", "Return to room" }) Move(b, -390);
        var tally = Rect("Tally", panel, Vector2.zero, new Vector2(780, 600));
        Undo.RegisterCreatedObjectUndo(tally.gameObject, "Build tally");
        var body = Font("PixelOperator"); var bold = Font("PixelOperator-Bold"); var title = Font("PixelOperator-Bold");   // one font everywhere
        var gold = new Color(.86f, .7f, .38f); var text = new Color(.9f, .88f, .84f); var dim = new Color(.6f, .64f, .68f);
        var slotSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/UI/Sprites/Diablo/Plain slot grey.png");
        TMP_Text Label(string n, float y, string s, Color c, TMP_FontAsset f, float size, bool right = false) =>
            Text(n, tally, new Vector2(right ? 380 : -380, y), new Vector2(560, 40), s, f, size, c, right ? TextAlignmentOptions.Right : TextAlignmentOptions.Left).Pivot(right ? 1 : 0);
        Label("Spoils label", 225, "Spoils carried", gold, bold, 30);
        var spoilsValue = Label("Spoils value", 225, "0", text, bold, 32, true);
        var strip = Img("Spoils strip", tally, new Vector2(0, 168), new Vector2(760, 76), slotSprite, Color.white, Image.Type.Sliced).rectTransform;
        AddRowLayout(strip, 10, 14);
        var spoilsIcon = Img("Spoils icon", strip, Vector2.zero, new Vector2(52, 52), null, Color.white); spoilsIcon.preserveAspect = true;
        spoilsIcon.gameObject.AddComponent<LayoutElement>().preferredWidth = 52;
        Label("Kills label", 100, "Enemies slain", gold, bold, 30);
        var killsValue = Label("Kills value", 100, "0", text, bold, 32, true);
        var kills = Img("Kills strip", tally, new Vector2(0, 40), new Vector2(760, 76), slotSprite, Color.white, Image.Type.Sliced).rectTransform;
        var grid = kills.gameObject.AddComponent<GridLayoutGroup>(); grid.cellSize = new Vector2(28, 26); grid.spacing = new Vector2(4, 4); grid.padding = new RectOffset(14, 14, 10, 8);
        var skull = Img("Skull", kills, Vector2.zero, new Vector2(28, 26), AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/Art/PixelItems/01_Skull_Human.png"), new Color(.92f, .88f, .8f));
        skull.preserveAspect = true;
        var healthLine = Label("Health line", -32, "Health slain  0 / 50", dim, body, 26);
        var healthValue = Label("Health value", -32, "0", text, bold, 32, true);
        Img("Rule", tally, new Vector2(0, -72), new Vector2(760, 3), null, new Color(.25f, .25f, .28f));
        Label("Total label", -112, "TOTAL", gold, title, 40);
        var total = Label("Total value", -112, "0", new Color(1f, .85f, .4f), bold, 46, true);
        Label("Bank label", -165, "Spoils to spend", dim, body, 28);
        var bank = Label("Bank value", -165, "0", text, bold, 34, true);
        var help = Text("Help", tally, new Vector2(0, -205), new Vector2(760, 30), "", body, 24, new Color(.5f, .89f, .9f), TextAlignmentOptions.Center);
        var summary = Text("Summary", panel, new Vector2(0, -275), new Vector2(760, 30), "", body, 24, dim, TextAlignmentOptions.Center);
        var oldSummary = panel.Find("Summary"); if (oldSummary != null && oldSummary != summary.transform) Undo.DestroyObjectImmediate(oldSummary.gameObject);
        tally.SetSiblingIndex(panel.Find("Cause") != null ? panel.Find("Cause").GetSiblingIndex() + 1 : 0);

        var so = new SerializedObject(recap);
        so.FindProperty("tallyRoot").objectReferenceValue = tally.gameObject;
        so.FindProperty("spoilsStrip").objectReferenceValue = strip;
        so.FindProperty("spoilsIconTemplate").objectReferenceValue = spoilsIcon;
        so.FindProperty("spoilsValue").objectReferenceValue = spoilsValue;
        so.FindProperty("killsStrip").objectReferenceValue = kills;
        so.FindProperty("skullTemplate").objectReferenceValue = skull;
        so.FindProperty("killsValue").objectReferenceValue = killsValue;
        so.FindProperty("healthLine").objectReferenceValue = healthLine;
        so.FindProperty("healthValue").objectReferenceValue = healthValue;
        so.FindProperty("totalValue").objectReferenceValue = total;
        so.FindProperty("bankValue").objectReferenceValue = bank;
        so.FindProperty("helpLine").objectReferenceValue = help;
        so.FindProperty("summaryLine").objectReferenceValue = summary;
        so.ApplyModifiedProperties();
        spoilsIcon.gameObject.SetActive(false); skull.gameObject.SetActive(false);
        EditorSceneManager.MarkSceneDirty(recap.gameObject.scene);
        Debug.Log("Meta: tally rows built on the run recap");
    }

    static void AddRowLayout(RectTransform strip, float spacing, int padding)
    {
        var h = strip.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = spacing; h.padding = new RectOffset(padding, padding, 10, 10); h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = false; h.childControlHeight = false; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
    }

    static TMP_Text Pivot(this TMP_Text t, float x)
    {
        var rt = t.rectTransform; var pos = rt.anchoredPosition;
        rt.pivot = new Vector2(x, .5f); rt.anchoredPosition = pos;
        return t;
    }
}
