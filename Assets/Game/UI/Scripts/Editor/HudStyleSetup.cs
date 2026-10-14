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
                var rugged = Rugged(image.sprite != null ? image.sprite.name : "", slot, slotFrame);
                if (rugged == null) continue;
                // The current look is read before the component exists: adding it applies a look straight away.
                var pixel = new HudStyleImage.Look { sprite = image.sprite, type = image.type, pixelsPerUnitMultiplier = image.pixelsPerUnitMultiplier, material = image.material != null && image.material != Canvas.GetDefaultCanvasMaterial() ? image.material : null };
                var style = Undo.AddComponent<HudStyleImage>(image.gameObject);
                style.pixel = pixel;
                // A fill keeps its image type: a Filled bar still fills by fillAmount (UIBar), the others by size.
                if (rugged.sprite == null) rugged.type = image.type == Image.Type.Filled ? Image.Type.Filled : Image.Type.Simple;
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
    static HudStyleImage.Look Rugged(string sprite, Sprite slot, Sprite slotFrame) => sprite switch
    {
        "Hud slot" => new HudStyleImage.Look { sprite = slot, type = Image.Type.Sliced, pixelsPerUnitMultiplier = 1f },
        "Hud slot selected" => new HudStyleImage.Look { sprite = slotFrame, type = Image.Type.Sliced, pixelsPerUnitMultiplier = 1f, setColor = true, color = Gold },
        "Hotbar frame" => new HudStyleImage.Look { sprite = slotFrame, type = Image.Type.Sliced, pixelsPerUnitMultiplier = 1f },
        "Hud bar frame" or "Inset" => new HudStyleImage.Look { sprite = slot, type = Image.Type.Sliced, pixelsPerUnitMultiplier = 2f },
        // Bar fills: flat colour, so the stone frame is the only texture.
        "Hud bar fill" or "Bar" => new HudStyleImage.Look { sprite = null, type = Image.Type.Simple, pixelsPerUnitMultiplier = 1f },
        _ => null,
    };
}
