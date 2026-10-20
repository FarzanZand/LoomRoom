using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Lays the inventory screen out: the character's stats framed on the left, and on the right one tall
// framed panel with the equipment paper doll on top and the bag grid below. The frames, cell sockets and slot frames
// are painted pixel art (UI/Sprites/Inventory, loomroom-art pipeline), drawn at 2 canvas units per art pixel.
// Lays out the 40 bag cells already in the scene. Safe to run again.
public static class InventoryLayout
{
    const string Art = "Assets/Game/UI/Sprites/Inventory";
    const float Cell = 64f, Gap = 4f, Unit = 56f, UnitGap = 4f;
    static readonly Color Gold = new(.86f, .7f, .38f), Bone = new(.84f, .84f, .82f), Dim = new(.6f, .6f, .62f);   // grey panels, gold titles (the user's call)

    static void Record(Object o) { if (o != null && PrefabUtility.IsPartOfPrefabInstance(o)) PrefabUtility.RecordPrefabInstancePropertyModifications(o); }

    static Sprite Sprite(string name, int border)
    {
        string path = $"{Art}/{name}.png";
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single;
        ti.filterMode = FilterMode.Point; ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.npotScale = TextureImporterNPOTScale.None; ti.spritePixelsPerUnit = 50f; // 2 canvas units per art pixel
        ti.spriteBorder = new Vector4(border, border, border, border); ti.wrapMode = TextureWrapMode.Clamp;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static void Skin(Image image, Sprite sprite, bool tiled = false)
    {
        if (image == null) return;
        image.sprite = sprite; image.type = tiled ? Image.Type.Tiled : Image.Type.Sliced; image.fillCenter = true; image.pixelsPerUnitMultiplier = 1f;
        image.color = Color.white; image.material = null;
        Record(image);
    }

    static RectTransform Place(Transform t, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var r = (RectTransform)t;
        r.anchorMin = r.anchorMax = anchor; r.pivot = new Vector2(.5f, .5f);
        r.anchoredPosition = pos; r.sizeDelta = size;
        Record(r);
        return r;
    }

    static TMP_Text Label(Transform parent, string name, string text, float size, Color color, Vector2 pos, Vector2 box, TextAlignmentOptions align, TMP_FontAsset font)
    {
        var t = parent.Find(name)?.GetComponent<TMP_Text>();
        if (t == null) { t = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>(); t.transform.SetParent(parent, false); }
        t.text = text; t.fontSize = size; t.color = color; t.alignment = align; t.raycastTarget = false; if (font != null) t.font = font;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        Place(t.transform, new Vector2(.5f, 1f), pos, box);
        Record(t);
        return t;
    }

    // The item whose icon gives each slot its empty outline.
    static readonly Dictionary<EquipmentSlot, string> SilhouetteIcons = new()
    {
        { EquipmentSlot.RightHand, "Iron Sword" }, { EquipmentSlot.LeftHand, "Iron Shield" }, { EquipmentSlot.Head, "Iron Helm" },
        { EquipmentSlot.Body, "Iron Armor" }, { EquipmentSlot.Legs, "Iron Leggings" }, { EquipmentSlot.Gloves, "Iron Gloves" },
        { EquipmentSlot.Boots, "Iron Boots" }, { EquipmentSlot.Trinket1, "Copper ring" }, { EquipmentSlot.Trinket2, "Copper ring" },
    };
    static readonly Color SilhouetteColor = new(.82f, .82f, .86f, .1f);

    // A flat white shape from the icon's alpha (the Image's colour dims it), saved to UI/Sprites/Inventory/Silhouettes the
    // first time; an existing file is kept as it is.
    static Sprite Silhouette(EquipmentSlot slot)
    {
        if (!SilhouetteIcons.TryGetValue(slot, out var name)) return null;
        // Drawn once from the icon; after that the file is the art (edit it by hand), never overwritten here.
        string existing = $"{Art}/Silhouettes/{name}.png";
        if (System.IO.File.Exists(existing)) return AssetDatabase.LoadAssetAtPath<Sprite>(existing);
        string source = $"Assets/Game/Items/Icons/Generated/{name}.png";
        if (!System.IO.File.Exists(source)) return null;
        var icon = new Texture2D(2, 2); icon.LoadImage(System.IO.File.ReadAllBytes(source));
        var px = icon.GetPixels32();
        for (int i = 0; i < px.Length; i++) px[i] = px[i].a > 100 ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
        var shape = new Texture2D(icon.width, icon.height, TextureFormat.RGBA32, false); shape.SetPixels32(px); shape.Apply();
        string folder = $"{Art}/Silhouettes", path = $"{folder}/{name}.png";
        System.IO.Directory.CreateDirectory(folder);
        System.IO.File.WriteAllBytes(path, shape.EncodeToPNG());
        Object.DestroyImmediate(icon); Object.DestroyImmediate(shape);
        AssetDatabase.ImportAsset(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.alphaIsTransparency = true;
        ti.filterMode = FilterMode.Point; ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.npotScale = TextureImporterNPOTScale.None;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    public static string Run()
    {
        var log = new System.Text.StringBuilder();
        // Plain on purpose (the user found the ornate frames and gold slots too busy).
        var frame = Sprite("Plain frame grey", 8); var socket = Sprite("Plain slot grey", 4); var slotFrame = socket;
        var title = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator-Bold.asset");
        var body = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator.asset");

        var ui = Object.FindAnyObjectByType<InventoryUI>(FindObjectsInactive.Include);
        var screen = ui.transform.root.GetComponentInChildren<AdventureMenuTabs>(true).transform;
        var inv = (RectTransform)screen.Find("InventoryPanel");
        var stats = (RectTransform)screen.Find("Equipment and stats");

        // ── Right: inventory (paper doll + bag) ─────────────────────────
        const float W = 760f, H = 928f;   // clears the hotbar below it
        inv.anchorMin = inv.anchorMax = new Vector2(1f, .5f); inv.pivot = new Vector2(1f, .5f);
        inv.anchoredPosition = new Vector2(-36f, 68f); inv.sizeDelta = new Vector2(W, H); Record(inv);
        Skin(inv.GetComponent<Image>(), frame, true);
        foreach (var name in new[] { "InventoryTitle" }) { var t = inv.Find(name); if (t != null) t.gameObject.SetActive(false); }
        Label(inv, "Title", "INVENTORY", 30, Gold, new Vector2(0f, -44f), new Vector2(W - 80f, 40f), TextAlignmentOptions.Center, title);

        // The doll: equipment slots moved in from the stats panel.
        var doll = inv.Find("Doll");
        if (doll == null) { doll = new GameObject("Doll", typeof(RectTransform)).transform; doll.SetParent(inv, false); }
        const float dollH = 492f;
        Place(doll, new Vector2(.5f, 1f), new Vector2(0f, -78f - dollH * .5f), new Vector2(W - 80f, dollH));
        float S(int n) => n * Unit + (n - 1) * UnitGap;
        // The arrangement: helm on top; weapon, body armour and shield in a row with their bottoms level; under
        // them one row of gloves, ring, leggings, ring, boots. The side columns stand far enough out to
        // leave room for the rings.
        const float Ring = 64f;
        float side = S(2) * .5f + 12f + Ring + 12f + S(2) * .5f;   // centre of the side columns
        float ringX = S(2) * .5f + 12f + Ring * .5f;
        float top = dollH * .5f;                       // doll's top edge (relative to its centre)
        float armorTop = top - S(2) - 12f;
        float bottomRow = armorTop - S(3) - 12f;       // top of the gloves, rings, leggings and boots row
        float weaponTop = armorTop + (S(4) - S(3));    // tall slots end level with the armour
        var places = new Dictionary<string, (int w, int h, float x, float yTop)>
        {
            { "HELM",      (2, 2, 0f, top) },
            { "ARMOR",     (2, 3, 0f, armorTop) },
            { "MAINHAND",  (2, 4, -side, weaponTop) },
            { "OFFHAND",   (2, 4, side, weaponTop) },
            { "GLOVES",    (2, 2, -side, bottomRow) },
            { "TRINKET 2", (1, 1, -ringX, bottomRow - (S(2) - Ring) * .5f) },
            { "PANTS",     (2, 2, 0f, bottomRow) },
            { "TRINKET 1", (1, 1, ringX, bottomRow - (S(2) - Ring) * .5f) },
            { "BOOTS",     (2, 2, side, bottomRow) },
        };
        foreach (var pair in places)
        {
            var slot = stats.Find(pair.Key) ?? doll.Find(pair.Key);
            if (slot == null) { log.AppendLine("no slot " + pair.Key); continue; }
            slot.SetParent(doll, false);
            var size = pair.Key.StartsWith("TRINKET") ? new Vector2(Ring, Ring) : new Vector2(S(pair.Value.w), S(pair.Value.h));
            Place(slot, new Vector2(.5f, .5f), new Vector2(pair.Value.x, pair.Value.yTop - size.y * .5f), size);
            Skin(slot.GetComponent<Image>(), slotFrame);
            if (slot.TryGetComponent<UIColor>(out var tint)) { tint.enabled = false; Record(tint); }
            foreach (RectTransform child in slot)
            {
                child.anchorMin = child.anchorMax = new Vector2(.5f, .5f);
                switch (child.name)
                {
                    case "Frame": child.gameObject.SetActive(false); break;
                    case "Honed fill": child.sizeDelta = size - Vector2.one * 10f; child.anchoredPosition = Vector2.zero; break;
                    case "Item icon": child.sizeDelta = size - Vector2.one * 14f; child.anchoredPosition = Vector2.zero; break;
                    case "Slot label":
                        // No slot names; an empty slot is just its frame.
                        child.gameObject.SetActive(false); break;
                }
                Record(child); Record(child.gameObject);
            }
            // A dim outline of what goes in the slot while it is empty, cut from that kind of item's icon.
            var slotUi = slot.GetComponent<EquipmentSlotUI>();
            var shape = slotUi != null ? Silhouette(slotUi.slot) : null;
            if (slotUi != null && shape != null)
            {
                var image = slot.Find("Silhouette")?.GetComponent<Image>();
                if (image == null) { image = new GameObject("Silhouette", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>(); image.transform.SetParent(slot, false); }
                var icon = slot.Find("Item icon");
                image.transform.SetSiblingIndex(icon != null ? icon.GetSiblingIndex() : 0);
                image.sprite = shape; image.preserveAspect = true; image.raycastTarget = false; image.color = SilhouetteColor; image.material = null;
                Place(image.transform, new Vector2(.5f, .5f), Vector2.zero, size - Vector2.one * 22f);
                Record(image); Record(image.gameObject);
                slotUi.silhouette = image; EditorUtility.SetDirty(slotUi); Record(slotUi);
            }
        }
        log.AppendLine("doll laid out");

        // The bag below the doll.
        var grid = (RectTransform)inv.Find("Grid");
        var gridSize = new Vector2(10 * Cell + 9 * Gap, 4 * Cell + 3 * Gap);
        Place(grid, new Vector2(.5f, 1f), new Vector2(0f, -78f - dollH - 26f - gridSize.y * .5f), gridSize);
        var layout = grid.GetComponent<GridLayoutGroup>(); layout.cellSize = new Vector2(Cell, Cell); layout.spacing = new Vector2(Gap, Gap); Record(layout);
        grid.pivot = new Vector2(.5f, .5f); Record(grid);
        var items = (RectTransform)inv.Find("Items");
        if (items != null) { items.anchorMin = grid.anchorMin; items.anchorMax = grid.anchorMax; items.pivot = grid.pivot; items.anchoredPosition = grid.anchoredPosition; items.sizeDelta = grid.sizeDelta; Record(items); }
        foreach (Transform cell in grid)
        {
            var slot = cell.GetComponent<ItemSlotUI>();
            var so = new SerializedObject(slot); so.FindProperty("useAuthoredAppearance").boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); Record(slot);
            Skin(cell.GetComponent<Image>(), socket);
            var dungeonFrame = cell.Find("Dungeon frame"); if (dungeonFrame != null) { dungeonFrame.gameObject.SetActive(false); Record(dungeonFrame.gameObject); }
        }
        var hint = inv.Find("InventoryHint");
        if (hint != null)
        {
            var t = hint.GetComponent<TMP_Text>(); t.text = "Drag to move   Right-click for actions   Drag out to drop   I to close"; t.color = Dim; t.alignment = TextAlignmentOptions.Center; t.font = body; t.fontSize = 18; Record(t);
            Place(hint, new Vector2(.5f, 0f), new Vector2(0f, 44f), new Vector2(W - 80f, 24f));
        }
        log.AppendLine("bag laid out");

        // ── Left: character ─────────────────────────────────────────────
        stats.anchorMin = stats.anchorMax = new Vector2(0f, .5f); stats.pivot = new Vector2(0f, .5f);
        stats.anchoredPosition = new Vector2(36f, 70f); stats.sizeDelta = new Vector2(520f, 820f); Record(stats);
        Skin(stats.GetComponent<Image>(), frame, true);
        if (stats.TryGetComponent<UIColor>(out var statsTint)) { statsTint.enabled = false; Record(statsTint); }
        float y = -44f;
        void Row(string name, float size, Color color, TMP_FontAsset font, float step)
        {
            var t = stats.Find(name); if (t == null) return;
            var text = t.GetComponent<TMP_Text>(); text.color = color; text.font = font; text.fontSize = size; text.alignment = TextAlignmentOptions.Left; Record(text);
            Place(t, new Vector2(.5f, 1f), new Vector2(0f, y), new Vector2(520f - 100f, size + 10f));
            y -= step;
        }
        Row("Title", 30, Gold, title, 56f);
        var panelUi = stats.GetComponent<EquipmentPanelUI>();
        var identity = Label(stats, "Identity", "", 22, Bone, new Vector2(0f, y - 14f), new Vector2(420f, 60f), TextAlignmentOptions.Center, body);
        identity.lineSpacing = -8f; panelUi.identity = identity; y -= 84f;
        Row("Health", 24, Bone, body, 40f);
        Row("Stamina", 24, Bone, body, 40f);
        Row("Mana", 24, Bone, body, 56f);
        Row("Damage", 24, Bone, body, 40f);
        Row("Armor", 24, Bone, body, 56f);
        // The skills (the game has no STR/DEX to spend): icon, name and rank, hover for what each does.
        var attributes = stats.Find("Attributes"); if (attributes != null) { attributes.gameObject.SetActive(false); Record(attributes.gameObject); }
        panelUi.attributes = null;
        Label(stats, "Skills title", "SKILLS", 22, Gold, new Vector2(0f, y - 4f), new Vector2(420f, 30f), TextAlignmentOptions.Left, title);
        y -= 40f;
        var list = stats.Find("Skills");
        if (list == null) { list = new GameObject("Skills", typeof(RectTransform)).transform; list.SetParent(stats, false); }
        const float rowH = 34f;
        Place(list, new Vector2(.5f, 1f), new Vector2(0f, y - rowH * AdventureSkills.Count * .5f), new Vector2(420f, rowH * AdventureSkills.Count));
        var rows = new List<AdventureSkillRowUI>();
        for (int i = 0; i < AdventureSkills.Count; i++)
        {
            var skill = AdventureSkills.All[i];
            var row = list.Find(skill.ToString());
            if (row == null) { row = new GameObject(skill.ToString(), typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(AdventureSkillRowUI)).transform; row.SetParent(list, false); }
            Place(row, new Vector2(.5f, 1f), new Vector2(0f, -rowH * (i + .5f)), new Vector2(420f, rowH - 2f));
            var hit = row.GetComponent<Image>(); hit.color = new Color(1f, 1f, 1f, 0f); hit.raycastTarget = true; Record(hit);
            var rowUi = row.GetComponent<AdventureSkillRowUI>(); rowUi.skill = skill;
            var icon = row.Find("Icon")?.GetComponent<Image>();
            if (icon == null) { icon = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>(); icon.transform.SetParent(row, false); }
            icon.raycastTarget = false; icon.preserveAspect = true;
            var r = (RectTransform)icon.transform; r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, .5f); r.anchoredPosition = Vector2.zero; r.sizeDelta = new Vector2(28f, 28f); Record(r);
            rowUi.icon = icon;
            rowUi.label = Label(row, "Name", skill.ToString(), 22, Bone, Vector2.zero, new Vector2(300f, rowH), TextAlignmentOptions.Left, body);
            var lr = (RectTransform)rowUi.label.transform; lr.anchorMin = lr.anchorMax = lr.pivot = new Vector2(0f, .5f); lr.anchoredPosition = new Vector2(40f, 0f); Record(lr);
            rowUi.rank = Label(row, "Rank", "0", 22, Bone, Vector2.zero, new Vector2(80f, rowH), TextAlignmentOptions.Right, body);
            var rr = (RectTransform)rowUi.rank.transform; rr.anchorMin = rr.anchorMax = rr.pivot = new Vector2(1f, .5f); rr.anchoredPosition = Vector2.zero; Record(rr);
            EditorUtility.SetDirty(rowUi); Record(rowUi);
            rows.Add(rowUi);
        }
        panelUi.skills = rows.ToArray();
        y -= rowH * AdventureSkills.Count + 16f;
        EditorUtility.SetDirty(panelUi);
        Row("Food regeneration", 22, Dim, body, 60f);
        var controls = stats.Find("Controls");
        if (controls != null) { Place(controls, new Vector2(.5f, 0f), new Vector2(0f, 44f), new Vector2(420f, 24f)); var t = controls.GetComponent<TMP_Text>(); t.text = "Click or drag gear to take it off"; t.color = Dim; t.alignment = TextAlignmentOptions.Center; Record(t); }
        var titleText = stats.Find("Title")?.GetComponent<TMP_Text>(); if (titleText != null) { titleText.text = "CHARACTER"; titleText.alignment = TextAlignmentOptions.Center; Record(titleText); }
        log.AppendLine("character panel laid out");

        // The item tooltip keeps its original look for now (the user's call); TooltipUI is not touched here.

        // The hotbar keeps its own style (user's call): never touched here.
        EditorUtility.SetDirty(ui);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(ui.gameObject.scene);
        return log.ToString();
    }
}
