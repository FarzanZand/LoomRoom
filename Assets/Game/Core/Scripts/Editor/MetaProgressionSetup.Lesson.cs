using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The character sheet's first lesson (CharacterSheetUI, "First lesson"): what points the player at the one skill that
// must be trained first. A marker stroke behind that skill's row, paper-coloured washes over the rest of the sheet, a
// pen ring round its Train button, the quest's line at the foot, and a tutorial window with a tail pointing at the
// button. BuildSheetLesson() makes them in the open scene's sheet (from an eval; safe to run again: it replaces its own
// pieces and keeps the ring and the note when they are there); BuildCharacterSheet runs it too. The sprites are from
// Tools/meta_sheet_lesson.py. CharacterSheetUI lays the pieces out against the lesson skill's row at run time.
public static partial class MetaProgressionSetup
{
    // How much of the ink a wash takes away (a wash is the paper drawn again over it, this opaque).
    const float WashAlpha = .62f;

    static Sprite LessonSprite(string name, float pixelsPerUnit, Vector4 border = default)
    {
        string path = MetaSprites + name + ".png";
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) { AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport); importer = AssetImporter.GetAtPath(path) as TextureImporter; }
        if (importer == null) throw new System.Exception($"{path} is missing: run python -I Tools/meta_sheet_lesson.py Assets/Game/UI/Sprites/Meta");
        if (importer.textureType != TextureImporterType.Sprite || importer.filterMode != FilterMode.Bilinear || importer.spriteBorder != border
            || !Mathf.Approximately(importer.spritePixelsPerUnit, pixelsPerUnit) || importer.textureCompression != TextureImporterCompression.Uncompressed)
        {
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Bilinear; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = true; importer.spritePixelsPerUnit = pixelsPerUnit; importer.spriteBorder = border; importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    public static string BuildSheetLesson()
    {
        var ui = Object.FindAnyObjectByType<CharacterSheetUI>(FindObjectsInactive.Include);
        if (ui == null) return "no character sheet in the open scene";
        var so = new SerializedObject(ui);
        var root = so.FindProperty("root").objectReferenceValue as GameObject;
        var p = root != null ? root.transform.Find("Paper") : null;
        if (p == null) return "the sheet has no Paper";
        var paper = p.GetComponent<Image>();
        var body = Font("PixelOperator"); var bold = Font("PixelOperator-Bold");

        foreach (var name in new[] { "Lesson highlight", "Lesson wash above", "Lesson wash below", "Lesson wash attributes", "Lesson tip" })
        {
            var old = p.Find(name);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }

        // The marker lies under the row: drawn straight after the paper's border, before anything of the rows.
        var highlight = Img("Lesson highlight", p, new Vector2(224, 0), new Vector2(1110, 84), LessonSprite("Marker stroke", 100), new Color(1f, .80f, .16f, .55f));
        var border = p.Find("Border");
        highlight.transform.SetSiblingIndex(border != null ? border.GetSiblingIndex() + 1 : 0);
        highlight.gameObject.SetActive(false);

        // The washes lie over everything on the paper but the lesson's own pieces. The skill rows' column runs from the
        // divider to the border; the attributes' block from the border to the divider, below the name.
        // Each is a rect mask onto a copy of the paper, half clear, kept exactly on the paper (CharacterSheetUI.AlignPaper):
        // paper over paper shows no edge, so only the ink under it fades.
        RectTransform Wash(string name, Vector2 pos, Vector2 size, Vector2 pivot)
        {
            var rt = Rect(name, p, pos, size, null, pivot);
            rt.gameObject.AddComponent<RectMask2D>();
            var tint = paper != null ? paper.color : Color.white;
            var copy = Img("Paper", rt, Vector2.zero, ((RectTransform)p).rect.size, paper != null ? paper.sprite : null, new Color(tint.r, tint.g, tint.b, WashAlpha));
            copy.transform.position = p.position;
            rt.gameObject.SetActive(false);
            return rt;
        }
        var above = Wash("Lesson wash above", new Vector2(221, 0), new Vector2(1134, 10), new Vector2(.5f, 0));
        var below = Wash("Lesson wash below", new Vector2(221, 0), new Vector2(1134, 10), new Vector2(.5f, 1));
        var side = Wash("Lesson wash attributes", new Vector2(-570, -223), new Vector2(436, 390), new Vector2(.5f, .5f));

        // The ring and the quest's line: kept as they are when the sheet has them.
        var ring = so.FindProperty("questRing").objectReferenceValue as Image;
        if (ring == null)
        {
            ring = Img("Quest ring", p, Vector2.zero, new Vector2(100, 100), AssetDatabase.LoadAssetAtPath<Sprite>(MetaSprites + "Pen ring.png"), Pen);
            ring.gameObject.SetActive(false);
        }
        var note = so.FindProperty("questNote").objectReferenceValue as TMP_Text;
        if (note == null)
        {
            note = Text("Quest note", p, new Vector2(565, -392), new Vector2(820, 40), "", bold, 22, Pen, TextAlignmentOptions.Right);
            note.rectTransform.pivot = new Vector2(1, .5f); note.rectTransform.anchoredPosition = new Vector2(565, -392);
            note.gameObject.SetActive(false);
        }
        ring.transform.SetAsLastSibling();

        // The tutorial window: a shadow, the pen's frame, a fill, a title and two lines, and the tail. Its pivot is its top
        // right corner (the corner by the button), its height follows its text.
        // 448 wide: from just right of the Train buttons to the gap between a row's sixth and seventh bubble.
        var tip = Rect("Lesson tip", p, new Vector2(770, 80), new Vector2(448, 10), new Vector2(.5f, .5f), new Vector2(1, 1));
        var shadow = Img("Shadow", tip, Vector2.zero, Vector2.zero, LessonSprite("Tip shadow", 100, new Vector4(40, 40, 40, 40)), new Color(0, 0, 0, .38f), Image.Type.Sliced);
        Stretch(shadow.rectTransform, new Vector2(-22, -30), new Vector2(26, 18));
        var frame = Img("Frame", tip, Vector2.zero, Vector2.zero, null, Pen);   // a plain 3 unit line in the pen's red, as the tail's
        Stretch(frame.rectTransform, Vector2.zero, Vector2.zero);
        var fill = Img("Fill", tip, Vector2.zero, Vector2.zero, null, new Color(.985f, .978f, .955f));
        Stretch(fill.rectTransform, new Vector2(3, 3), new Vector2(-3, -3));
        var tail = Img("Tail", tip, new Vector2(-85, 3), new Vector2(36, 24), LessonSprite("Tip tail", 200), Color.white);
        tail.rectTransform.anchorMin = tail.rectTransform.anchorMax = new Vector2(1, 0); tail.rectTransform.pivot = new Vector2(.5f, 0);
        tail.rectTransform.anchoredPosition = new Vector2(-85, 3); tail.rectTransform.localScale = new Vector3(1, -1, 1);
        foreach (var g in new[] { shadow, frame, fill, tail }) g.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var layout = tip.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 14, 16); layout.spacing = 6; layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.MiddleLeft;
        tip.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        // At least this tall: over a row it then covers the right end of the two rows above it whole, their milestone
        // numbers too, and ends in the gap under the third (no sliver of a bubble or a Train button at its edges).
        tip.gameObject.AddComponent<LayoutElement>().minHeight = 164;
        var title = Text("Title", tip, Vector2.zero, new Vector2(408, 34), "Train Athletics first", bold, 32, Pen);
        var text = Text("Body", tip, Vector2.zero, new Vector2(408, 30), "It costs 10 spoils and teaches the dash.\nThe rest of the sheet opens after.", body, 24, Ink);
        text.textWrappingMode = TextWrappingModes.Normal;
        tip.gameObject.SetActive(false);

        // The banner of a bought milestone goes over all of it.
        var banner = so.FindProperty("unlockBanner").objectReferenceValue as RectTransform;
        if (banner != null) banner.SetAsLastSibling();

        so.FindProperty("questRing").objectReferenceValue = ring;
        so.FindProperty("questNote").objectReferenceValue = note;
        so.FindProperty("lessonTip").objectReferenceValue = tip;
        so.FindProperty("lessonTitle").objectReferenceValue = title;
        so.FindProperty("lessonBody").objectReferenceValue = text;
        so.FindProperty("lessonTail").objectReferenceValue = tail.rectTransform;
        so.FindProperty("lessonHighlight").objectReferenceValue = highlight;
        so.FindProperty("veilAbove").objectReferenceValue = above;
        so.FindProperty("veilBelow").objectReferenceValue = below;
        so.FindProperty("veilSide").objectReferenceValue = side;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(ui);
        EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
        return "sheet lesson built: highlight, three washes, tutorial window";
    }

    // Stretched over its parent, its lower left and upper right corners moved by these offsets.
    static void Stretch(RectTransform rt, Vector2 lowerLeft, Vector2 upperRight)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(.5f, .5f);
        rt.offsetMin = lowerLeft; rt.offsetMax = upperRight;
    }
}
