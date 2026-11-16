using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The character sheet's look: a printed record sheet (the lead, 2026-11-16: "the character sheet design and the paper
// is ugly, rethink and redo it"). The paper is pixel art with everything that is printed drawn on it (a ruled frame,
// the title, black bars over the skills and over each attribute, a box per icon, level and score, the portrait's card),
// two canvas units a painted pixel as the rest of the UI; what is written on it is the live part: levels as square
// boxes inked in, the next one bracketed in pen, Train as a red plate (grey while it can't be pressed).
//
// MetaProgressionSetup.Record() lays the open scene's sheet out this way, in place: it moves and restyles the objects
// BuildCharacterSheet and BuildSheetLesson made, by name, so every reference CharacterSheetUI holds stays (from an
// eval; safe to run again). BuildCharacterSheet and RefreshCharacterSheet run it last. The art is from
// Tools/sheet_record.py, whose LAY table holds the same numbers as the ones here: painted pixels from the sheet's top
// left corner (Put turns them into canvas units from its middle).
public static partial class MetaProgressionSetup
{
    const string RecordSprites = MetaSprites + "Record/";
    static readonly Color RecordInk = new(.15f, .13f, .12f), RecordPen = new(.67f, .13f, .1f), RecordFaint = new(.45f, .42f, .39f),
        RecordOnInk = new(.94f, .91f, .83f), RecordCream = new(.98f, .94f, .85f), RecordCard = new(.98f, .965f, .925f);
    const float SheetWidth = 860, SheetHeight = 480;

    static Sprite RecordSprite(string name, int border = 0, bool smooth = false)
    {
        string path = RecordSprites + name + ".png";
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) { AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport); importer = AssetImporter.GetAtPath(path) as TextureImporter; }
        if (importer == null) throw new System.Exception($"{path} is missing: run python -I Tools/sheet_record.py Assets/Game/UI/Sprites/Meta/Record");
        var b = new Vector4(border, border, border, border);
        var filter = smooth ? FilterMode.Bilinear : FilterMode.Point;
        if (importer.textureType != TextureImporterType.Sprite || importer.filterMode != filter || importer.spriteBorder != b || importer.mipmapEnabled
            || !Mathf.Approximately(importer.spritePixelsPerUnit, 50f) || importer.textureCompression != TextureImporterCompression.Uncompressed)
        {
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = filter; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false; importer.spritePixelsPerUnit = 50f; importer.spriteBorder = b; importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // A rect given in painted pixels from the sheet's top left corner, for a child of the paper.
    static void Put(Component c, float x, float y, float w, float h)
    {
        var rt = (RectTransform)c.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
        rt.sizeDelta = new Vector2(w * 2f, h * 2f);
        var middle = new Vector2((x + w / 2f - SheetWidth / 2f) * 2f, (SheetHeight / 2f - (y + h / 2f)) * 2f);
        rt.anchoredPosition = middle + Vector2.Scale(rt.pivot - new Vector2(.5f, .5f), rt.sizeDelta);
    }
    static Transform Need(Transform p, string name)
    {
        var t = p.Find(name);
        if (t == null) throw new System.Exception($"The character sheet has no '{name}': build it first (MetaProgressionSetup.BuildCharacterSheet).");
        return t;
    }
    static void Hide(Transform p, string name) { var t = p.Find(name); if (t != null) t.gameObject.SetActive(false); }
    static void Fill(Transform child, Vector2 size, Vector2 at = default) { var rt = (RectTransform)child; rt.sizeDelta = size; rt.anchoredPosition = at; }
    static Image Sliced(Component c, Sprite sprite, Color color)
    {
        var img = c.GetComponent<Image>();
        img.enabled = true; img.sprite = sprite; img.color = color; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 1f;
        return img;
    }
    static Image Extra(string name, Transform parent, Sprite sprite)
    {
        var t = parent.Find(name);
        var img = t != null ? t.GetComponent<Image>() : Img(name, parent, Vector2.zero, Vector2.zero, sprite, Color.white);
        img.sprite = sprite; img.raycastTarget = false;
        return img;
    }
    static void Plate(Button b, Sprite sprite, float fontSize, Color label)
    {
        var rt = (RectTransform)b.transform;
        Sliced(b, sprite, Color.white);
        var colors = b.colors;
        colors.normalColor = new Color(.9f, .9f, .9f); colors.highlightedColor = Color.white; colors.selectedColor = Color.white;
        colors.pressedColor = new Color(.72f, .72f, .72f); colors.disabledColor = new Color(1, 1, 1, .45f);
        b.colors = colors;
        var text = b.transform.Find("Label");
        if (text == null) return;
        Fill(text, rt.sizeDelta);
        var tmp = text.GetComponent<TMP_Text>();
        tmp.fontSize = fontSize; tmp.color = label; tmp.alignment = TextAlignmentOptions.Center;
    }
    static void Stamp(Button b, Sprite sprite, float fontSize)
    {
        Sliced(b, sprite, RecordPen);
        var text = b.transform.Find("Label");
        if (text == null) return;
        Fill(text, ((RectTransform)b.transform).sizeDelta);
        var tmp = text.GetComponent<TMP_Text>();
        tmp.fontSize = fontSize; tmp.color = RecordPen; tmp.alignment = TextAlignmentOptions.Center;
    }

    public static string Record()
    {
        var ui = Object.FindAnyObjectByType<CharacterSheetUI>(FindObjectsInactive.Include);
        if (ui == null) return "no character sheet in the open scene";
        var so = new SerializedObject(ui);
        var root = so.FindProperty("root").objectReferenceValue as GameObject;
        var p = root != null ? root.transform.Find("Paper") : null;
        if (p == null) return "the sheet has no Paper";
        Undo.RegisterFullObjectHierarchyUndo(ui.gameObject, "Record sheet");

        Sprite paper = RecordSprite("Record paper"), blank = RecordSprite("Record paper blank");
        Sprite pipEmpty = RecordSprite("Record pip empty", 5), pipFilled = RecordSprite("Record pip filled", 5), pipNext = RecordSprite("Record pip next", 5);
        Sprite plate = RecordSprite("Record button", 5), plateOff = RecordSprite("Record button off", 5);
        Sprite stamp = RecordSprite("Record stamp", 4), note = RecordSprite("Record note", 5), tape = RecordSprite("Record tape");
        Sprite shadow = RecordSprite("Record shadow", 24, true);

        // Every text takes the record's inks (the old sheet's were a little greyer); the ones that differ are set below.
        foreach (var t in p.GetComponentsInChildren<TMP_Text>(true))
        {
            var c = t.color;
            bool red = c.r > c.g * 2f, pale = c.r > .4f && !red;
            var to = red ? RecordPen : pale ? RecordFaint : RecordInk;
            t.color = new Color(to.r, to.g, to.b, c.a);
        }

        // The desk under it, the sheet's shadow, the sheet.
        var dim = root.GetComponent<Image>();
        if (dim != null) dim.color = new Color(.035f, .028f, .024f, .88f);
        var cast = Extra("Desk shadow", root.transform, shadow);
        cast.type = Image.Type.Sliced; cast.pixelsPerUnitMultiplier = 1f; cast.color = new Color(0, 0, 0, .8f);
        cast.rectTransform.anchorMin = cast.rectTransform.anchorMax = new Vector2(.5f, .5f);
        Fill(cast.transform, new Vector2(SheetWidth * 2 + 76, SheetHeight * 2 + 76), new Vector2(10, -14));
        cast.transform.SetSiblingIndex(0);
        var sheet = p.GetComponent<Image>();
        sheet.sprite = paper; sheet.color = Color.white; sheet.type = Image.Type.Simple;
        ((RectTransform)p).sizeDelta = new Vector2(SheetWidth * 2, SheetHeight * 2);
        // At its own size (the old sheet was enlarged by hand to 1.13): on a 1080 line screen a painted pixel is then
        // exactly two screen pixels, and the pixel font at 32 lands on its own grid.
        p.localScale = Vector3.one;
        Hide(p, "Border"); Hide(p, "Divider"); Hide(p, "Title"); Hide(p, "Line");

        // The portrait: a square drawing on its card, a strip of tape over each upper corner, the arrows on its edges.
        var ring = Need(p, "Portrait ring");
        ring.GetComponent<Image>().enabled = false;
        Put(ring, 41, 63, 190, 190);
        var mask = Need(ring, "Portrait mask");
        var maskImage = mask.GetComponent<Image>(); maskImage.sprite = null; maskImage.color = Color.white;
        Fill(mask, new Vector2(380, 380));
        Fill(Need(mask, "Portrait"), new Vector2(380, 380));
        var tapeLeft = Extra("Tape left", p, tape); Put(tapeLeft, 24, 46, 34, 34);
        var tapeRight = Extra("Tape right", p, tape); Put(tapeRight, 214, 46, 34, 34); tapeRight.rectTransform.localScale = new Vector3(-1, 1, 1);
        tapeLeft.transform.SetSiblingIndex(ring.GetSiblingIndex() + 1); tapeRight.transform.SetSiblingIndex(ring.GetSiblingIndex() + 2);
        var previous = Need(p, "Previous portrait").GetComponent<Button>(); Put(previous, 28, 148, 16, 20); Plate(previous, plateOff, 24, RecordInk);
        var next = Need(p, "Next portrait").GetComponent<Button>(); Put(next, 228, 148, 16, 20); Plate(next, plateOff, 24, RecordInk);
        previous.transform.SetSiblingIndex(tapeRight.transform.GetSiblingIndex() + 1); next.transform.SetSiblingIndex(tapeRight.transform.GetSiblingIndex() + 2);

        // The name on its printed line.
        var name = Need(p, "Name"); Put(name, 40, 259, 192, 26);
        var nameField = name.GetComponent<TMP_InputField>();
        if (nameField != null)
        {
            if (nameField.textComponent != null) { nameField.textComponent.fontSize = 40; nameField.textComponent.color = RecordPen; }
            if (nameField.placeholder is TMP_Text hint) { hint.fontSize = 28; hint.color = new Color(RecordFaint.r, RecordFaint.g, RecordFaint.b, .6f); }
            nameField.caretColor = RecordPen; nameField.selectionColor = new Color(RecordPen.r, RecordPen.g, RecordPen.b, .25f);
        }

        // Spoils: the purse under its printed bar.
        var purse = Need(p, "Purse");
        purse.GetComponent<Image>().enabled = false;
        Put(purse, 30, 308, 212, 33);
        Hide(purse, "Spoils label");
        Fill(Need(purse, "Spoils icon"), new Vector2(46, 46), new Vector2(-178, 0));
        var spoils = Need(purse, "Spoils"); Fill(spoils, new Vector2(120, 56), new Vector2(-100, 0));
        spoils.GetComponent<TMP_Text>().fontSize = 48; spoils.GetComponent<TMP_Text>().color = RecordPen;
        var refund = Need(purse, "Refund all").GetComponent<Button>();
        Fill(refund.transform, new Vector2(164, 40), new Vector2(118, 0)); Stamp(refund, stamp, 20);

        // Header: what the sheet says back (in the middle), and Put down.
        var feedback = Need(p, "Feedback"); Put(feedback, 300, 22, 430, 22);
        feedback.GetComponent<TMP_Text>().fontSize = 26;
        var close = Need(p, "Put down").GetComponent<Button>(); Put(close, 742, 21, 96, 24); Stamp(close, stamp, 26);

        // Skills: a row each under the bar.
        const float kx = 262, rowsY = 74, rowHeight = 38;
        string[] skills = { "blocking", "swords", "maces", "sorcery", "thaumaturgy", "athletics", "stealth", "trading" };
        for (int i = 0; i < skills.Length; i++)
        {
            string id = skills[i]; float y = rowsY + i * rowHeight;
            var frame = Need(p, $"{id} icon frame");
            frame.GetComponent<Image>().enabled = false;
            Put(frame, kx + 2, y + 1, 36, 36);
            Fill(Need(frame, $"{id} icon"), new Vector2(64, 64));
            var title = Need(p, $"{id} name"); Put(title, kx + 46, y + 7, 144, 24);
            title.GetComponent<TMP_Text>().fontSize = 32;
            Hide(p, $"{id} level label"); Hide(p, $"{id} rule");
            var level = Need(p, $"{id} level"); Put(level, 456, y + 7, 32, 24);
            var levelText = level.GetComponent<TMP_Text>(); levelText.fontSize = 32; levelText.alignment = TextAlignmentOptions.Center;
            float x = kx + 240;
            for (int k = 1; k <= 10; k++)
            {
                var pip = Need(p, $"{id} level {k}");
                var mark = pip.Find("Mark");
                float size = mark != null ? 22 : 14;
                Put(pip, x, y + 19 - size / 2f, size, size);
                Sliced(pip, pipEmpty, RecordInk);
                if (mark != null)
                {
                    Fill(mark, new Vector2(44, 44), new Vector2(0, 1)); mark.GetComponent<TMP_Text>().fontSize = 26;
                    var unlock = pip.Find("Unlock icon"); if (unlock != null) Fill(unlock, new Vector2(32, 32));
                    var value = pip.Find("Value");
                    if (value != null) { Fill(value, new Vector2(60, 18), new Vector2(0, 30)); value.GetComponent<TMP_Text>().fontSize = 16; }
                }
                x += size + 4;
            }
            var buy = Need(p, $"{id} buy").GetComponent<Button>(); Put(buy, kx + 468, y + 6, 104, 26); Plate(buy, plate, 28, RecordCream);
        }

        // Attributes: four blocks along the foot, each under its own bar.
        string[] attributes = { "strength", "dexterity", "constitution", "intelligence" };
        for (int i = 0; i < attributes.Length; i++)
        {
            string id = attributes[i]; float x0 = 22 + i * 205, y0 = 384;
            var crest = Need(p, $"{id} crest");                         // the score's box is printed: this is what is hovered
            var crestImage = crest.GetComponent<Image>(); crestImage.sprite = null; crestImage.color = new Color(1, 1, 1, 0); crestImage.raycastTarget = true;
            Put(crest, x0 + 6, y0 + 21, 34, 32);
            var score = Need(crest, "Score"); Fill(score, new Vector2(68, 64), new Vector2(0, -1));
            var scoreText = score.GetComponent<TMP_Text>(); scoreText.fontSize = 48; scoreText.alignment = TextAlignmentOptions.Center;
            var gains = Need(p, $"{id} bonus"); Put(gains, x0 + 6, y0 + 56, 191, 19);
            var gainsText = gains.GetComponent<TMP_Text>();
            // Two lines at the pixel font's own size (the same in all four blocks), not one line shrunk to fit.
            gainsText.enableAutoSizing = false; gainsText.fontSize = 16;
            gainsText.textWrappingMode = TextWrappingModes.Normal; gainsText.alignment = TextAlignmentOptions.TopLeft; gainsText.lineSpacing = -10;
            Put(Need(p, $"{id} icon"), x0 + 2, y0, 16, 16);
            var title = Need(p, $"{id} name"); Put(title, x0 + 24, y0, 160, 16);
            var titleText = title.GetComponent<TMP_Text>(); titleText.fontSize = 24; titleText.color = RecordOnInk; titleText.alignment = TextAlignmentOptions.Left;
            for (int k = 1; k <= 8; k++)
            {
                var pip = Need(p, $"{id} level {k}");
                Put(pip, x0 + 46 + (k - 1) * 18, y0 + 21, 14, 14);
                Sliced(pip, pipEmpty, RecordInk);
            }
            var buy = Need(p, $"{id} buy").GetComponent<Button>(); Put(buy, x0 + 46, y0 + 38, 140, 16); Plate(buy, plate, 22, RecordCream);
        }

        // A bought milestone's banner: a red plate over the skills. The hover card: a pale card.
        var banner = Need(p, "Unlock banner");
        Sliced(banner, plate, Color.white);
        ((RectTransform)banner).anchoredPosition = new Vector2(240, 40);
        Hide(banner, "Fill");
        var bannerTitle = banner.Find("Title"); if (bannerTitle != null) bannerTitle.GetComponent<TMP_Text>().color = RecordCream;
        var bannerSub = banner.Find("Sub"); if (bannerSub != null) bannerSub.GetComponent<TMP_Text>().color = new Color(1f, .88f, .8f);
        var card = so.FindProperty("tip").objectReferenceValue as RectTransform;
        if (card != null) { Sliced(card, note, Color.white); Hide(card, "Fill"); }

        // The first lesson's pieces follow the new rows: the marker behind a row, the washes (windows onto the blank
        // paper, so the print fades with the ink), the quest's line under the purse.
        var highlight = p.Find("Lesson highlight");
        if (highlight != null) { var rt = (RectTransform)highlight; rt.sizeDelta = new Vector2(1068, 70); rt.anchoredPosition = new Vector2(278, rt.anchoredPosition.y); }
        foreach (var washName in new[] { "Lesson wash above", "Lesson wash below", "Lesson wash attributes" })
        {
            var wash = p.Find(washName) as RectTransform;
            if (wash == null) continue;
            if (washName.EndsWith("attributes")) Put(wash, 22, 384, 816, 76);
            else { wash.sizeDelta = new Vector2(1152, wash.sizeDelta.y); wash.anchoredPosition = new Vector2(240, wash.anchoredPosition.y); }
            var copy = wash.Find("Paper");
            if (copy == null) continue;
            var image = copy.GetComponent<Image>(); image.sprite = blank; image.color = new Color(1, 1, 1, WashAlpha);
            ((RectTransform)copy).sizeDelta = ((RectTransform)p).sizeDelta; copy.position = p.position;
        }
        var tip = p.Find("Lesson tip");
        var tipFill = tip != null ? tip.Find("Fill") : null;
        if (tipFill != null) tipFill.GetComponent<Image>().color = RecordCard;
        var quest = so.FindProperty("questNote").objectReferenceValue as TMP_Text;
        if (quest != null)
        {
            quest.rectTransform.pivot = new Vector2(.5f, .5f);
            Put(quest, 30, 345, 212, 36);
            quest.alignment = TextAlignmentOptions.Center; quest.textWrappingMode = TextWrappingModes.Normal; quest.fontSize = 22; quest.color = RecordPen;
        }

        // Keys and a pad: a frame round the selected button, and the way from button to button set by hand (left to
        // itself the way up from an attribute skipped five skills: it goes by distance, and the foot's blocks are far
        // from the Train column). Skills run up and down their column, Put down above them; the attributes run along
        // the foot and go up to the last skill (Strength, under the purse, to Refund all, and on up through the
        // portrait's two arrows to Put down); the arrows and Refund all hang off the column's left.
        var select = Extra("Selection mark", p, RecordSprite("Record select", 9));
        select.type = Image.Type.Sliced; select.pixelsPerUnitMultiplier = 1f; select.color = Color.white;
        select.rectTransform.anchorMin = select.rectTransform.anchorMax = new Vector2(.5f, .5f);
        select.gameObject.SetActive(false);
        var questRing = so.FindProperty("questRing").objectReferenceValue as Image;
        select.transform.SetSiblingIndex(questRing != null ? questRing.transform.GetSiblingIndex() : p.childCount - 1);
        so.FindProperty("selectionMark").objectReferenceValue = select;
        Button Buy(string id) => Need(p, id + " buy").GetComponent<Button>();
        void Way(Selectable from, Selectable up, Selectable down, Selectable left, Selectable right)
        {
            from.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = up, selectOnDown = down, selectOnLeft = left, selectOnRight = right };
        }
        for (int i = 0; i < skills.Length; i++)
            Way(Buy(skills[i]), i > 0 ? Buy(skills[i - 1]) : close, i < skills.Length - 1 ? Buy(skills[i + 1]) : Buy(attributes[3]),
                i <= 3 ? next : refund, null);
        for (int i = 0; i < attributes.Length; i++)
            Way(Buy(attributes[i]), i == 0 ? refund : Buy(skills[skills.Length - 1]), null, i > 0 ? Buy(attributes[i - 1]) : null, i < attributes.Length - 1 ? Buy(attributes[i + 1]) : null);
        Way(close, null, Buy(skills[0]), next, null);
        Way(previous, next, refund, null, next);
        Way(next, close, refund, previous, Buy(skills[2]));
        Way(refund, previous, Buy(attributes[0]), null, Buy(skills[6]));

        so.FindProperty("bubbleFilled").objectReferenceValue = pipFilled;
        so.FindProperty("bubbleOutline").objectReferenceValue = pipEmpty;
        so.FindProperty("bubbleDashed").objectReferenceValue = pipNext;
        so.FindProperty("buyPlate").objectReferenceValue = plate;
        so.FindProperty("buyPlateOff").objectReferenceValue = plateOff;
        so.FindProperty("buyText").colorValue = RecordCream;
        so.FindProperty("buyTextOff").colorValue = RecordFaint;
        so.FindProperty("ink").colorValue = RecordInk;
        so.FindProperty("pen").colorValue = RecordPen;
        so.FindProperty("faint").colorValue = RecordFaint;
        so.FindProperty("lessonRowHalfHeight").floatValue = rowHeight;
        so.FindProperty("veilSpan").vector2Value = new Vector2((SheetHeight / 2f - (rowsY + skills.Length * rowHeight)) * 2f, (SheetHeight / 2f - rowsY) * 2f);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(ui);
        EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
        return "character sheet laid out as the record sheet";
    }
}
