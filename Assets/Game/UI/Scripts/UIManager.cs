using System;
using Sirenix.OdinInspector;
using UnityEngine;

// Named UI colours in one place. Everything is opt-in:
// - Kit art (panels, frames, buttons) follows the Theme when its Image uses the Kit material.
//   The kit sprites are greyscale value maps: black = shadow, dark grey = panel, light grey = frame,
//   white = frame highlight. For a UI that should look different, give it a copy of the material.
// - Text, bar fills and flat backgrounds follow a slot when they have a UIColor component.
[ExecuteAlways]
public class UIManager : Singleton<UIManager>
{
    [SerializeField, Required, Tooltip("The shared kit material. Its colours are written from the Theme below.")]
    Material kitMaterial;

    [Title("Preset")]
    [OnValueChanged(nameof(ApplyPreset)), Tooltip("Picking a preset copies its colours into the fields below. Custom leaves them alone; Saved uses a theme asset.")]
    public UIThemePreset preset = UIThemePreset.Custom;
    [ShowIf(nameof(preset), UIThemePreset.Saved), OnValueChanged(nameof(ApplyPreset))]
    [AssetSelector(Paths = ThemeFolder), Tooltip("A theme saved with Save As Theme.")]
    public UITheme savedTheme;
    const string ThemeFolder = "Assets/Game/UI/Themes";

    // Copies the chosen preset into the colour fields and pushes them to the kit material and UIColor slots.
    [Button("Reapply Preset"), ShowIf("@preset != UIThemePreset.Custom")]
    public void ApplyPreset()
    {
        var colors = preset == UIThemePreset.Saved ? savedTheme != null ? savedTheme.colors : null : UIThemeColors.Builtin(preset);
        if (colors == null) return;
#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(this, "Apply UI Preset");
#endif
        colors.CopyTo(this);
#if UNITY_EDITOR
        UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        UnityEditor.EditorUtility.SetDirty(this);
#endif
        Apply();
    }

#if UNITY_EDITOR
    // Saves the current colours as a new theme asset and selects it.
    [Button, PropertyOrder(-1)]
    void SaveAsTheme(string themeName = "New Theme")
    {
        if (string.IsNullOrWhiteSpace(themeName)) themeName = "New Theme";
        System.IO.Directory.CreateDirectory(ThemeFolder);
        var theme = ScriptableObject.CreateInstance<UITheme>();
        theme.colors = UIThemeColors.From(this);
        var path = UnityEditor.AssetDatabase.GenerateUniqueAssetPath($"{ThemeFolder}/{themeName.Trim()}.asset");
        UnityEditor.AssetDatabase.CreateAsset(theme, path);
        UnityEditor.AssetDatabase.SaveAssets();
        UnityEditor.Undo.RecordObject(this, "Save UI Theme");
        preset = UIThemePreset.Saved;
        savedTheme = theme;
        UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"Saved UI theme {path}", theme);
    }
#endif

    [Title("Theme")]
    [Tooltip("Outer outlines and the darkest edges.")] public Color shadow = new(.043f, .055f, .067f);
    [Tooltip("Panel and button fills.")] public Color panel = new(.114f, .145f, .173f);
    [Range(0, 1), Tooltip("How much the painted shading inside panels varies around the panel colour.")] public float panelShading = .25f;
    [Tooltip("Frame colour (the darker end of the frame shading).")] public Color frame = new(.64f, .34f, .24f);
    [Tooltip("Frame highlight (the brightest pixels of frames and bevels).")] public Color frameHighlight = new(.88f, .53f, .35f);
    public Color text = new(.902f, .902f, .902f);
    public Color dimText = new(.847f, .8f, .706f);

#if UNITY_EDITOR
    [OnInspectorGUI, PropertyOrder(1)]
    void DrawRamp()
    {
        var rect = UnityEditor.EditorGUILayout.GetControlRect(false, 18);
        int steps = Mathf.Max(1, (int)rect.width);
        for (int i = 0; i < steps; i++)
            UnityEditor.EditorGUI.DrawRect(new Rect(rect.x + i, rect.y, 1, rect.height), Ramp(i / (float)(steps - 1)));
        UnityEditor.EditorGUILayout.LabelField("Kit shading, from black (left) to white (right) in the sprites", UnityEditor.EditorStyles.miniLabel);
    }
#endif

    [PropertyOrder(2), Title("Status colours")] public Color health = new(.659f, .275f, .243f);
    [PropertyOrder(2)] public Color mana = new(.267f, .439f, .624f);
    [PropertyOrder(2)] public Color stamina = new(.549f, .604f, .384f);
    [PropertyOrder(2)] public Color experience = new(.722f, .565f, .243f);
    [PropertyOrder(2)] public Color gold = new(1f, .851f, .4f);
    [PropertyOrder(2)] public Color bossHealth = new(.722f, .122f, .122f);
    [PropertyOrder(2), Tooltip("The small bar over enemies you aim at or hit.")] public Color enemyHealth = new(.698f, .22f, .09f);
    [PropertyOrder(2)] public Color recentDamage = new(.949f, .62f, .298f);
    [PropertyOrder(2), Tooltip("Slot background, loot beam and name of Rare items (spell tomes). Not part of the theme.")] public Color rare = DefaultRare;
    public static readonly Color DefaultRare = new(.34f, .19f, .07f);
    [PropertyOrder(-1), Tooltip("The hotbar, health, mana, stamina and XP bars and buff frames: Pixel (the old pixel kit) or Rugged (the inventory's grey stone frames). Switches live, also outside Play mode (HudStyleImage).")]
    public HudStyle hudStyle = HudStyle.Rugged;
    public static HudStyle CurrentHudStyle => HasInstance ? Instance.hudStyle : HudStyle.Rugged;
    [PropertyOrder(2), Tooltip("Slot fill and border of Plain gear and consumables: a faint white (names stay white).")] public Color plain = DefaultPlain;
    public static readonly Color DefaultPlain = new(.9f, .88f, .84f);
    [PropertyOrder(2), Tooltip("Slot fill, loot beam and name of Fine gear (blue, the first step above Plain).")] public Color fine = DefaultFine;
    public static readonly Color DefaultFine = new(.12f, .3f, .64f);
    [PropertyOrder(2), Tooltip("Slot fill, loot beam and name of Superior gear (green, between Fine and Masterwork).")] public Color superior = DefaultSuperior;
    public static readonly Color DefaultSuperior = new(.09f, .42f, .29f);
    [PropertyOrder(2), Tooltip("Slot fill, loot beam and name of Masterwork gear (bronze-gold, the best rolled step).")] public Color masterwork = DefaultMasterwork;
    public static readonly Color DefaultMasterwork = new(.52f, .34f, .06f);
    [PropertyOrder(2), Tooltip("Slot fill, loot beam and name of Relics (hand-made items).")] public Color relic = DefaultRelic;
    public static readonly Color DefaultRelic = new(.58f, .17f, .06f);
    [PropertyOrder(2), Tooltip("Slot fill, loot beam and name of spoils (the meta currency: monster parts and baubles).")] public Color spoils = DefaultSpoils;
    public static readonly Color DefaultSpoils = new(.72f, .07f, .1f);

    // A rarity's colour, from the UIManager when there is one (Plain: the faint fill).
    public static Color RarityColor(ItemRarity rarity)
    {
        var ui = HasInstance ? Instance : null;
        return rarity switch
        {
            ItemRarity.Rare => ui != null ? ui.rare : DefaultRare,
            ItemRarity.Fine => ui != null ? ui.fine : DefaultFine,
            ItemRarity.Superior => ui != null ? ui.superior : DefaultSuperior,
            ItemRarity.Masterwork => ui != null ? ui.masterwork : DefaultMasterwork,
            ItemRarity.Relic => ui != null ? ui.relic : DefaultRelic,
            ItemRarity.Spoils => ui != null ? ui.spoils : DefaultSpoils,
            _ => ui != null ? ui.plain : DefaultPlain,
        };
    }

    public event Action Changed;

    public Color Get(UISlot slot) => slot switch
    {
        UISlot.Shadow => shadow,
        UISlot.Panel => panel,
        UISlot.Frame => frame,
        UISlot.FrameHighlight => frameHighlight,
        UISlot.Text => text,
        UISlot.DimText => dimText,
        UISlot.Health => health,
        UISlot.Mana => mana,
        UISlot.Stamina => stamina,
        UISlot.Experience => experience,
        UISlot.Gold => gold,
        UISlot.BossHealth => bossHealth,
        UISlot.EnemyHealth => enemyHealth,
        UISlot.RecentDamage => recentDamage,
        _ => Color.white,
    };

    // The colour a kit pixel of this grey value gets (mirrors the Copper Plum shader).
    public Color Ramp(float value)
    {
        if (value < .07f) return shadow;
        if (value < .3f) return panel * Mathf.Lerp(1 - .6f * panelShading, 1 + .4f * panelShading, (value - .07f) / .23f);
        return Color.Lerp(frame, frameHighlight, (value - .3f) / .62f);
    }

    void OnEnable() => Apply();
    void OnValidate() => Apply();

    public void Apply()
    {
        if (kitMaterial != null)
        {
            kitMaterial.SetColor("_ShadowColor", shadow);
            kitMaterial.SetColor("_PanelColor", panel);
            kitMaterial.SetFloat("_PanelShading", panelShading);
            kitMaterial.SetColor("_BorderColor", frame);
            kitMaterial.SetColor("_HighlightColor", frameHighlight);
        }
        Changed?.Invoke();
    }
}

// The HUD look (UIManager.hudStyle). Serialized by integer; append only.
public enum HudStyle { Pixel = 0, Rugged = 1 }
