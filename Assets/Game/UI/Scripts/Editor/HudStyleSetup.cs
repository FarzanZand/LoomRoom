using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Gives the HUD's images (hotbar, vitals bars, XP bar, buff frames, the spell charge bar) both HUD looks: their current
// pixel-kit look becomes Pixel, the inventory's grey stone look Rugged (HudStyleImage). Keeps the looks of images that
// already have one, so hand edits survive; safe to run again.
public static class HudStyleSetup
{
    const string Slot = "Assets/Game/UI/Sprites/Diablo/Plain slot grey.png";
    const string SlotFrame = "Assets/Game/UI/Sprites/Diablo/Plain slot frame.png";
    static readonly string[] Roots = { "UI/Canvas/HotbarUI", "UI/Canvas/PlayerVitals", "UI/Canvas/Experience", "UI/Canvas/Left Hand HUD" };
    static readonly Color Gold = new(.86f, .7f, .38f);

    [MenuItem("Tools/LoomRoom/UI/Set Up HUD Styles")]
    public static void SetUp()
    {
        var frameImporter = (TextureImporter)AssetImporter.GetAtPath(SlotFrame);
        if (frameImporter != null && (frameImporter.textureType != TextureImporterType.Sprite || frameImporter.spriteBorder != new Vector4(4, 4, 4, 4)))
        {
            frameImporter.textureType = TextureImporterType.Sprite; frameImporter.spriteImportMode = SpriteImportMode.Single;
            frameImporter.filterMode = FilterMode.Point; frameImporter.mipmapEnabled = false; frameImporter.spritePixelsPerUnit = 50;
            frameImporter.textureCompression = TextureImporterCompression.Uncompressed; frameImporter.spriteBorder = new Vector4(4, 4, 4, 4);
            frameImporter.SaveAndReimport();
        }
        var slot = AssetDatabase.LoadAssetAtPath<Sprite>(Slot);
        var slotFrame = AssetDatabase.LoadAssetAtPath<Sprite>(SlotFrame);
        if (slot == null || slotFrame == null) { Debug.LogError("[HUD style] Plain slot sprites missing."); return; }

        int added = 0, kept = 0;
        foreach (var path in Roots)
        {
            var root = GameObject.Find(path);
            if (root == null) { Debug.LogWarning($"[HUD style] {path} not found."); continue; }
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (image.sprite == null && image.GetComponent<HudStyleImage>() == null) continue;
                if (image.GetComponent<HudStyleImage>() != null) { kept++; continue; }
                var rugged = Rugged(image.sprite != null ? image.sprite.name : "", slot, slotFrame, image.type);
                if (rugged == null) continue;
                // The current look is read before the component exists: adding it applies a look straight away.
                var pixel = new HudStyleImage.Look { sprite = image.sprite, type = image.type, pixelsPerUnitMultiplier = image.pixelsPerUnitMultiplier, material = image.material != null && image.material != Canvas.GetDefaultCanvasMaterial() ? image.material : null };
                var style = Undo.AddComponent<HudStyleImage>(image.gameObject);
                style.pixel = pixel;
                style.rugged = rugged;
                style.Apply();
                added++;
            }
        }
        if (UIManager.HasInstance) UIManager.Instance.Apply();
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log($"[HUD style] {added} images set up, {kept} kept their looks.");
    }

    // The Rugged look for a pixel-kit sprite, or null to leave the image alone.
    static HudStyleImage.Look Rugged(string sprite, Sprite slot, Sprite slotFrame, Image.Type type) => sprite switch
    {
        "Hud slot" => new HudStyleImage.Look { sprite = slot, type = Image.Type.Sliced, pixelsPerUnitMultiplier = 1f },
        "Hud slot selected" => new HudStyleImage.Look { sprite = slotFrame, type = Image.Type.Sliced, pixelsPerUnitMultiplier = 1f, setColor = true, color = Gold },
        "Hotbar frame" => new HudStyleImage.Look { sprite = slotFrame, type = Image.Type.Sliced, pixelsPerUnitMultiplier = 1f },
        "Hud bar frame" or "Inset" => new HudStyleImage.Look { sprite = slot, type = Image.Type.Sliced, pixelsPerUnitMultiplier = 2f },
        // Bar fills: the grainy bar strip, tinted by the bar's colour (Texture Bars).
        "Hud bar fill" or "Bar" => BarFill(type),
        _ => null,
    };

    // ── Bar texture ──────────────────────────────────────────────────

    const string BarFillPath = "Assets/Game/UI/Sprites/Diablo/Plain bar fill.png";   // painted by Tools/hud_bar_texture.py
    const string EnemyBarPath = "Assets/Game/UI/Prefabs/Dungeon/Enemy health bar.prefab";

    // A bar fill's Rugged look: the strip, tiled so its grain keeps its size however full the bar is. A Filled bar
    // (filled by fillAmount) stays Filled, stretching the strip.
    static HudStyleImage.Look BarFill(Image.Type current)
    {
        var sprite = BarSprite();
        if (sprite == null) return new HudStyleImage.Look { sprite = null, type = current == Image.Type.Filled ? Image.Type.Filled : Image.Type.Simple, pixelsPerUnitMultiplier = 1f };
        return new HudStyleImage.Look { sprite = sprite, type = current == Image.Type.Filled ? Image.Type.Filled : Image.Type.Tiled, pixelsPerUnitMultiplier = 2f };
    }

    static Sprite BarSprite()
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(BarFillPath);
        if (importer == null) { Debug.LogError($"[HUD style] {BarFillPath} is missing: run Tools/hud_bar_texture.py."); return null; }
        var border = new Vector4(0, 3, 0, 2);   // left, bottom, right, top: the shaded bottom and bright top stay put
        if (importer.textureType != TextureImporterType.Sprite || importer.spriteBorder != border || importer.filterMode != FilterMode.Point)
        {
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false; importer.spritePixelsPerUnit = 50;
            importer.wrapMode = TextureWrapMode.Repeat; importer.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect; importer.SetTextureSettings(settings);
            importer.spriteBorder = border;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(BarFillPath);
    }

    // The vitals bars (health, mana, stamina) and the enemy bar get the grainy strip instead of a flat fill; the enemy
    // bar also gets the inventory's grey slot as its frame. Safe to run again.
    [MenuItem("Tools/LoomRoom/UI/Texture Bars")]
    public static string TextureBars()
    {
        var bar = BarSprite();
        var slot = AssetDatabase.LoadAssetAtPath<Sprite>(Slot);
        if (bar == null || slot == null) return "sprites missing";
        int hud = 0;
        var vitals = GameObject.Find("UI/Canvas/PlayerVitals");
        if (vitals != null)
            foreach (var style in vitals.GetComponentsInChildren<HudStyleImage>(true))
            {
                if (style.rugged.sprite != null || style.GetComponent<UIColor>() == null) continue;   // only the colour-tinted fills
                Undo.RecordObject(style, "Texture bars");
                style.rugged = BarFill(style.rugged.type);
                style.Apply();
                hud++;
            }
        else Debug.LogWarning("[HUD style] UI/Canvas/PlayerVitals not found.");
        if (UIManager.HasInstance) UIManager.Instance.Apply();
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        var root = PrefabUtility.LoadPrefabContents(EnemyBarPath);
        try
        {
            var enemyBar = root.GetComponent<DungeonEnemyBar>();
            var frame = root.GetComponent<Image>();
            frame.sprite = slot; frame.type = Image.Type.Sliced; frame.pixelsPerUnitMultiplier = 2f; frame.material = null; frame.color = Color.white;
            // The frame's own grainy inside shows through where health is lost.
            var background = enemyBar.healthFill.transform.parent.GetComponent<Image>();
            if (background != null && background.gameObject != root) background.color = Color.clear;
            foreach (var fill in new[] { enemyBar.healthFill, enemyBar.recentDamageFill })
            {
                if (fill == null) continue;
                fill.sprite = bar; fill.type = fill.type == Image.Type.Filled ? Image.Type.Filled : Image.Type.Tiled; fill.pixelsPerUnitMultiplier = 2f; fill.material = null;
            }
            PrefabUtility.SaveAsPrefabAsset(root, EnemyBarPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return $"{hud} HUD bars and the enemy bar textured";
    }
}
