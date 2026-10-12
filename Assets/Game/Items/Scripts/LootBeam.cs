using System.Collections.Generic;
using UnityEngine;

// Heathen/Diablo floor loot: a pillar of light rises from an item lying on the floor, coloured by what it is
// (normal, Honed, consumable), so loot reads from across a dark room. Added by WorldItem when
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
    Renderer body;
    float phase;

    public static Color ColorFor(ItemData item, InventoryManager config)
    {
        if (item == null) return config.beamNormal;
        if (item.honed > 0) return UIManager.HasInstance ? WithAlpha(UIManager.Instance.honed, config.beamNormal.a) : config.beamHoned;
        return item.itemType == ItemType.Consumable ? config.beamConsumable : config.beamNormal;
    }
    static Color WithAlpha(Color c, float a) { c.a = a; return c; }

    public void Init(WorldItem item, Bounds bounds, InventoryManager config)
    {
        Item = item;
        Color = ColorFor(item.Item, config);
        Base = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        Height = config.beamHeight;
        glow = Line("Beam glow", config.beamMaterial, config.beamWidth, Color);
        core = Line("Beam core", config.beamMaterial, config.beamWidth * .3f, Color.Lerp(Color, Color.white, .6f));
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

    // A slow breath, and the beam follows the item if it moves (a chest's pop-out).
    void Update()
    {
        if (glow == null) return;
        if (body != null) Base = new Vector3(body.bounds.center.x, body.bounds.min.y, body.bounds.center.z);
        float pulse = .8f + .2f * Mathf.Sin(Time.time * 2.4f + phase);
        foreach (var line in new[] { glow, core })
        {
            line.SetPosition(0, Base);
            line.SetPosition(1, Base + Vector3.up * Height * pulse);
        }
    }
}
