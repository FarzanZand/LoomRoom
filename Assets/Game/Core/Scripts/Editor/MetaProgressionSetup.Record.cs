using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The character sheet's look: a sheet kept by hand (the lead, 2026-11-16: the first record sheet, all ruled boxes, black
// bars and red slabs, "looks messy, cramped and very digital-button square"). The paper is calm pixel art with little
// printed on it (two headings on their rules, a faint rule under each skill, a ring for each attribute's score, the
// portrait's card), two canvas units a painted pixel as the rest of the UI. What is written on it is the live part:
// levels as round bubbles inked in, the next one ringed in pen, a larger bubble at each milestone, and Train as an oval
// drawn round the price in red pen (in pencil while it can't be bought). Nothing is boxed, and what an attribute gives
// is on its hover card, not in small print on the sheet.
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
    // A button that is an oval drawn round its words: in pen, fuller under the pointer or the selection.
    static void Tag(Button b, Sprite sprite, Sprite hot, float fontSize, Color label)
    {
        var rt = (RectTransform)b.transform;
        Sliced(b, sprite, Color.white);
        b.transition = Selectable.Transition.SpriteSwap;
        b.spriteState = new SpriteState { highlightedSprite = hot, pressedSprite = hot, selectedSprite = hot };
        var text = b.transform.Find("Label");
        if (text == null) return;
        Fill(text, rt.sizeDelta);
        var tmp = text.GetComponent<TMP_Text>();
        tmp.fontSize = fontSize; tmp.color = label; tmp.alignment = TextAlignmentOptions.Center;
    }
    // A button that is only its mark (the portrait's arrows): nothing drawn behind it, the mark brighter under the pointer.
    static void Bare(Button b, float fontSize)
    {
        var img = b.GetComponent<Image>(); img.sprite = null; img.color = new Color(1, 1, 1, 0); img.raycastTarget = true;
        var text = b.transform.Find("Label");
        if (text == null) return;
        Fill(text, ((RectTransform)b.transform).sizeDelta);
        var tmp = text.GetComponent<TMP_Text>();
        tmp.fontSize = fontSize; tmp.color = RecordPen; tmp.alignment = TextAlignmentOptions.Center;
        b.transition = Selectable.Transition.ColorTint; b.targetGraphic = tmp;
        var colors = b.colors;
        colors.normalColor = new Color(.42f, .42f, .42f); colors.highlightedColor = Color.white; colors.selectedColor = Color.white;
        colors.pressedColor = new Color(.7f, .7f, .7f); colors.disabledColor = new Color(.4f, .4f, .4f, .4f);
        b.colors = colors;
    }
    // A bubble: a round drawing at its own size, tinted in ink.
    static void Round(Component c, Sprite sprite)
    {
        var img = c.GetComponent<Image>();
        img.enabled = true; img.sprite = sprite; img.color = RecordInk; img.type = Image.Type.Simple;
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
        Sprite pipEmpty = RecordSprite("Record pip empty"), pipFilled = RecordSprite("Record pip filled"), pipNext = RecordSprite("Record pip next");
        Sprite ringEmpty = RecordSprite("Record ring empty"), ringFilled = RecordSprite("Record ring filled"), ringNext = RecordSprite("Record ring next");
        Sprite oval = RecordSprite("Record button", 11), ovalHot = RecordSprite("Record button hot", 11), ovalOff = RecordSprite("Record button off", 11);
        Sprite note = RecordSprite("Record note", 6), bannerCard = RecordSprite("Record banner", 7), tape = RecordSprite("Record tape");
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

        // The portrait: a square drawing on its card, a strip of tape over each upper corner, an arrow either side.
        var ring = Need(p, "Portrait ring");
        ring.GetComponent<Image>().enabled = false;
        Put(ring, 59, 44, 142, 142);
        var mask = Need(ring, "Portrait mask");
        var maskImage = mask.GetComponent<Image>(); maskImage.sprite = null; maskImage.color = Color.white;
        Fill(mask, new Vector2(284, 284));
        Fill(Need(mask, "Portrait"), new Vector2(284, 284));
        var tapeLeft = Extra("Tape left", p, tape); Put(tapeLeft, 43, 28, 34, 34);
        var tapeRight = Extra("Tape right", p, tape); Put(tapeRight, 183, 28, 34, 34); tapeRight.rectTransform.localScale = new Vector3(-1, 1, 1);
        tapeLeft.transform.SetSiblingIndex(ring.GetSiblingIndex() + 1); tapeRight.transform.SetSiblingIndex(ring.GetSiblingIndex() + 2);
        var previous = Need(p, "Previous portrait").GetComponent<Button>(); Put(previous, 28, 102, 22, 26); Bare(previous, 32);
        var next = Need(p, "Next portrait").GetComponent<Button>(); Put(next, 210, 102, 22, 26); Bare(next, 32);
        previous.transform.SetSiblingIndex(tapeRight.transform.GetSiblingIndex() + 1); next.transform.SetSiblingIndex(tapeRight.transform.GetSiblingIndex() + 2);

        // The name on its line.
        var name = Need(p, "Name"); Put(name, 48, 196, 164, 24);
        var nameField = name.GetComponent<TMP_InputField>();
        if (nameField != null)
        {
            if (nameField.textComponent != null) { nameField.textComponent.fontSize = 32; nameField.textComponent.color = RecordPen; }
            if (nameField.placeholder is TMP_Text hint) { hint.fontSize = 28; hint.color = new Color(RecordFaint.r, RecordFaint.g, RecordFaint.b, .6f); }
            nameField.caretColor = RecordPen; nameField.selectionColor = new Color(RecordPen.r, RecordPen.g, RecordPen.b, .25f);
        }

        // Spoils: the purse and its number over the printed words, Refund all under them.
        var purse = Need(p, "Purse");
        purse.GetComponent<Image>().enabled = false;
        Put(purse, 48, 232, 164, 34);
        Hide(purse, "Spoils label");
        Fill(Need(purse, "Spoils icon"), new Vector2(46, 46), new Vector2(-52, 0));
        var spoils = Need(purse, "Spoils"); Fill(spoils, new Vector2(150, 60), new Vector2(60, 0));
        var spoilsText = spoils.GetComponent<TMP_Text>(); spoilsText.fontSize = 48; spoilsText.color = RecordPen; spoilsText.alignment = TextAlignmentOptions.Left;
        var refund = Need(purse, "Refund all").GetComponent<Button>();
        Fill(refund.transform, new Vector2(224, 48), new Vector2(0, -98)); Tag(refund, oval, ovalHot, 32, RecordPen);

        // Header: what the sheet says back, and Put down.
        var feedback = Need(p, "Feedback"); Put(feedback, 340, 20, 396, 24);
        feedback.GetComponent<TMP_Text>().fontSize = 28;
        var close = Need(p, "Put down").GetComponent<Button>(); Put(close, 746, 16, 92, 24); Tag(close, oval, ovalHot, 32, RecordPen);

        // Skills: a row each under the heading. Nothing boxed: the icon, the name, the level, its bubbles, Train.
        const float kx = 262, rowsY = 54, rowHeight = 36;
        string[] skills = { "blocking", "swords", "maces", "sorcery", "thaumaturgy", "athletics", "stealth", "trading" };
        for (int i = 0; i < skills.Length; i++)
        {
            string id = skills[i]; float mid = rowsY + i * rowHeight + rowHeight / 2f;
            var frame = Need(p, $"{id} icon frame");
            frame.GetComponent<Image>().enabled = false;
            Put(frame, kx + 3, mid - 15, 30, 30);
            Fill(Need(frame, $"{id} icon"), new Vector2(56, 56));
            var title = Need(p, $"{id} name"); Put(title, kx + 40, mid - 12, 146, 24);
            title.GetComponent<TMP_Text>().fontSize = 32;
            Hide(p, $"{id} level label"); Hide(p, $"{id} rule");
            var level = Need(p, $"{id} level"); Put(level, kx + 186, mid - 12, 36, 24);
            var levelText = level.GetComponent<TMP_Text>(); levelText.fontSize = 32; levelText.alignment = TextAlignmentOptions.Center;
            float x = kx + 232;
            for (int k = 1; k <= 10; k++)
            {
                var pip = Need(p, $"{id} level {k}");
                var mark = pip.Find("Mark");
                float size = mark != null ? 22 : 12;
                Put(pip, x, mid - size / 2f, size, size);
                Round(pip, mark != null ? ringEmpty : pipEmpty);
                if (mark != null)
                {
                    Fill(mark, new Vector2(44, 44), new Vector2(0, 1)); mark.GetComponent<TMP_Text>().fontSize = 26;
                    var unlock = pip.Find("Unlock icon"); if (unlock != null) Fill(unlock, new Vector2(28, 28));
                    var value = pip.Find("Value");
                    if (value != null) { Fill(value, new Vector2(60, 18), new Vector2(0, 29)); var vt = value.GetComponent<TMP_Text>(); vt.fontSize = 16; vt.color = RecordFaint; }
                }
                x += size + 5;
            }
            var buy = Need(p, $"{id} buy").GetComponent<Button>(); Put(buy, kx + 472, mid - 12, 104, 24); Tag(buy, oval, ovalHot, 32, RecordPen);
        }

        // Attributes: four along the foot under their heading. The score in its printed ring, the name, its bubbles,
        // Train. What a point gives is on the hover card (three gains in small print under each made the foot a thicket).
        string[] attributes = { "strength", "dexterity", "constitution", "intelligence" };
        for (int i = 0; i < attributes.Length; i++)
        {
            string id = attributes[i]; float x0 = 30 + i * 202, y0 = 376;
            var crest = Need(p, $"{id} crest");                         // the score's ring is printed: this is what is hovered
            var crestImage = crest.GetComponent<Image>(); crestImage.sprite = null; crestImage.color = new Color(1, 1, 1, 0); crestImage.raycastTarget = true;
            Put(crest, x0 + 2, y0 + 8, 46, 46);
            var score = Need(crest, "Score"); Fill(score, new Vector2(92, 92), new Vector2(0, -1));
            var scoreText = score.GetComponent<TMP_Text>(); scoreText.fontSize = 48; scoreText.alignment = TextAlignmentOptions.Center;
            Need(p, $"{id} bonus").gameObject.SetActive(false);
            Put(Need(p, $"{id} icon"), x0 + 58, y0 + 2, 16, 16);
            var title = Need(p, $"{id} name"); Put(title, x0 + 78, y0 - 2, 122, 24);
            var titleText = title.GetComponent<TMP_Text>(); titleText.fontSize = 32; titleText.color = RecordInk; titleText.alignment = TextAlignmentOptions.Left;
            for (int k = 1; k <= 8; k++)
            {
                var pip = Need(p, $"{id} level {k}");
                Put(pip, x0 + 58 + (k - 1) * 17, y0 + 26, 12, 12);
                Round(pip, pipEmpty);
            }
            var buy = Need(p, $"{id} buy").GetComponent<Button>(); Put(buy, x0 + 58, y0 + 46, 132, 24); Tag(buy, oval, ovalHot, 32, RecordPen);
        }

        // A bought milestone's banner: a card edged in pen over the skills. The hover card: a pale card.
        var banner = Need(p, "Unlock banner");
        Sliced(banner, bannerCard, Color.white);
        ((RectTransform)banner).anchoredPosition = new Vector2(240, 40);
        Hide(banner, "Fill");
        var bannerTitle = banner.Find("Title"); if (bannerTitle != null) bannerTitle.GetComponent<TMP_Text>().color = RecordPen;
        var bannerSub = banner.Find("Sub"); if (bannerSub != null) bannerSub.GetComponent<TMP_Text>().color = RecordInk;
        var card = so.FindProperty("tip").objectReferenceValue as RectTransform;
        if (card != null) { Sliced(card, note, Color.white); Hide(card, "Fill"); }

        // The first lesson's pieces follow the new rows: the marker behind a row, the washes (windows onto the blank
        // paper, so the print fades with the ink), the quest's line under the purse.
        var highlight = p.Find("Lesson highlight");
        if (highlight != null) { var rt = (RectTransform)highlight; rt.sizeDelta = new Vector2(1076, 62); rt.anchoredPosition = new Vector2(278, rt.anchoredPosition.y); }
        foreach (var washName in new[] { "Lesson wash above", "Lesson wash below", "Lesson wash attributes" })
        {
            var wash = p.Find(washName) as RectTransform;
            if (wash == null) continue;
            if (washName.EndsWith("attributes")) Put(wash, 24, 346, 812, 118);
            else { wash.sizeDelta = new Vector2(1152, wash.sizeDelta.y); wash.anchoredPosition = new Vector2(240, wash.anchoredPosition.y); }
            var copy = wash.Find("Paper");
            if (copy == null) continue;
            var image = copy.GetComponent<Image>(); image.sprite = blank; image.color = new Color(1, 1, 1, WashAlpha);
            ((RectTransform)copy).sizeDelta = ((RectTransform)p).sizeDelta; copy.position = p.position;
        }
        var tip = p.Find("Lesson tip");
        // Its window is the banner's card, rounded and edged in pen (it was a square box: a frame with a fill inside it).
        var tipFrame = tip != null ? tip.Find("Frame") : null;
        if (tipFrame != null) Sliced(tipFrame, bannerCard, Color.white);
        var tipFill = tip != null ? tip.Find("Fill") : null;
        if (tipFill != null) tipFill.GetComponent<Image>().enabled = false;
        var quest = so.FindProperty("questNote").objectReferenceValue as TMP_Text;
        if (quest != null)
        {
            quest.rectTransform.pivot = new Vector2(.5f, .5f);
            Put(quest, 30, 314, 212, 30);
            quest.alignment = TextAlignmentOptions.Center; quest.textWrappingMode = TextWrappingModes.Normal; quest.fontSize = 22; quest.color = RecordPen;
        }

        // Keys and a pad: a frame round the selected button, and the way from button to button set by hand (left to
        // itself the way up from an attribute skipped five skills: it goes by distance, and the foot's blocks are far
        // from the Train column). Skills run up and down their column, Put down above them; the attributes run along
        // the foot and go up to the last skill (Strength, under the purse, to Refund all, and on up through the
        // portrait's two arrows to Put down); the arrows and Refund all hang off the column's left.
        var select = Extra("Selection mark", p, RecordSprite("Record select", 12));
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
        so.FindProperty("ringFilled").objectReferenceValue = ringFilled;
        so.FindProperty("ringOutline").objectReferenceValue = ringEmpty;
        so.FindProperty("ringDashed").objectReferenceValue = ringNext;
        so.FindProperty("buyPlate").objectReferenceValue = oval;
        so.FindProperty("buyPlateOff").objectReferenceValue = ovalOff;
        so.FindProperty("buyPlateHot").objectReferenceValue = ovalHot;
        so.FindProperty("buyText").colorValue = RecordPen;
        so.FindProperty("buyTextOff").colorValue = RecordFaint;
        so.FindProperty("ink").colorValue = RecordInk;
        so.FindProperty("pen").colorValue = RecordPen;
        so.FindProperty("faint").colorValue = RecordFaint;
        so.FindProperty("lessonRowHalfHeight").floatValue = rowHeight;
        so.FindProperty("veilSpan").vector2Value = new Vector2((SheetHeight / 2f - (rowsY + skills.Length * rowHeight)) * 2f, (SheetHeight / 2f - rowsY) * 2f);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(ui);
        EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
        return "character sheet laid out as the hand-kept sheet";
    }
}
