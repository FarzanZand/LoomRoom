using System.Collections.Generic;
using UnityEngine;

// Floor loot: a pillar of light rises from an item lying on the floor, coloured by what it is
// (normal, a rarity, consumable), so loot reads from across a dark room. Added by WorldItem when
// InventoryManager.lootBeams is on; LootLabelsUI shows the item's name over the nearest ones.
public class LootBeam : MonoBehaviour
{
    public static readonly List<LootBeam> Active = new();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Active.Clear();

    public WorldItem Item { get; private set; }
    public Color Color { get; private set; }
    public Vector3 Base { get; private set; }
    public float Height { get; private set; }
    LineRenderer core, glow;
    LineRenderer[] lines;
    float glowWidth;
    Renderer body;
    float phase;

    public static Color ColorFor(ItemData item, InventoryManager config)
    {
        if (item == null) return config.beamNormal;
        // Fine, Superior, Masterwork and Relic: their rarity colour, brighter and a little stronger the rarer.
        if (item.rarity == ItemRarity.Fine || item.rarity == ItemRarity.Superior || item.rarity == ItemRarity.Masterwork || item.rarity == ItemRarity.Relic)
            return WithAlpha(Color.Lerp(item.RarityColour, Color.white, .3f), config.beamNormal.a * (item.rarity == ItemRarity.Fine ? 1.3f : item.rarity == ItemRarity.Superior ? 1.45f : item.rarity == ItemRarity.Masterwork ? 1.6f : 2f));
        // Spoils: a strong red beam, so the currency reads from across a dark room.
        if (item.rarity == ItemRarity.Spoils) return WithAlpha(Color.Lerp(item.RarityColour, Color.white, .25f), config.beamNormal.a * 1.8f);
        if (item.rarity == ItemRarity.Rare) return WithAlpha(Color.Lerp(item.RarityColour, new Color(1f, .62f, .25f), .5f), config.beamNormal.a * 1.3f);
        // Plain gear and consumables share the white beam.
        return config.beamNormal;
    }
    static Color WithAlpha(Color c, float a) { c.a = a; return c; }

    public void Init(WorldItem item, Bounds bounds, InventoryManager config)
    {
        Item = item;
        Color = ColorFor(item.Item, config);
        Base = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        Height = config.beamHeight;
        glow = Line("Beam glow", config.beamMaterial, config.beamWidth, Color);
        core = Line("Beam core", config.beamMaterial, config.beamWidth * .3f, Color.Lerp(Color, Color.white, .35f));
        glowWidth = config.beamWidth;
        lines = new[] { glow, core };
        phase = Random.value * 6f;
        foreach (var r in item.GetComponentsInChildren<Renderer>()) if (!(r is LineRenderer) && !(r is ParticleSystemRenderer)) { body = r; break; }
    }

    LineRenderer Line(string name, Material material, float width, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.SetPosition(0, Base);
        line.SetPosition(1, Base + Vector3.up * Height);
        line.widthMultiplier = width;
        line.alignment = LineAlignment.View;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        var top = color; top.a = 0f;
        line.colorGradient = new Gradient
        {
            colorKeys = new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(color.a, 0f), new GradientAlphaKey(color.a * .5f, .35f), new GradientAlphaKey(0f, 1f) },
        };
        return line;
    }

    void OnEnable() => Active.Add(this);
    void OnDisable() => Active.Remove(this);

    // Every beam is the same height above its item; it breathes a little in width, and follows the item if it
    // moves (a chest's pop-out).
    void Update()
    {
        if (glow == null) return;
        if (body != null) Base = new Vector3(body.bounds.center.x, body.bounds.min.y, body.bounds.center.z);
        float pulse = .9f + .1f * Mathf.Sin(Time.time * 2.4f + phase);
        glow.widthMultiplier = glowWidth * pulse;
        foreach (var line in lines)
        {
            line.SetPosition(0, Base);
            line.SetPosition(1, Base + Vector3.up * Height);
        }
    }
}
