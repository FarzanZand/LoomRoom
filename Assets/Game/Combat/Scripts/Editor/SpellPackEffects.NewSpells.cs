using System.Linq;
using UnityEditor;
using UnityEngine;

// The Spells Pack art for the spells added in October 2026 (Shadow Veil, Chain Lightning, Sweeping Arc, Molten Boulder,
// Hurled Stone, Frost Lance, Raise the Fallen): hand, projectile, impact and the effects their scripts place. Made by
// NewSpellsSetup (which makes the prefabs first) and rebuilt with the rest by Rebuild. A hue, when given, recolours the
// pack's own colours (the Dark effects are green; the veil is a purple shadow; the sweep's cut and the lance's burst keep their colour).
public static partial class SpellPackEffects
{
    const string P = "Prefabs/Projectiles/";
    const string S = "Prefabs/Spells/";

    struct Hue { public string prefab; public float hue, saturation, value; }

    public static void RebuildNewSpells()
    {
        var arts = new[]
        {
            // Shadow Veil: a smoky purple dark gathered in the palm; wisps that curl round the player while it lasts.
            new Art { prefab = "Shadow Veil/Shadow Veil Hand.prefab", effect = P + "Casting/Casting_Arcane_LWRP.prefab", scale = .06f, charge = true, linger = .35f },
            new Art { prefab = "Shadow Veil/Shadow Veil Wisps.prefab", effect = P + "Casting/Casting_Arcane_2_LWRP.prefab", scale = .16f, charge = true, linger = .6f, maxScreen = .25f },
            // Chain Lightning: blue sparks circling the palm; a crack of light and smoke where a bolt strikes.
            new Art { prefab = "Chain Lightning/Chain Lightning Hand.prefab", effect = P + "Casting/Casting_Storm_2_LWRP.prefab", scale = .06f, charge = true, linger = .3f },
            new Art { prefab = "Chain Lightning/Chain Lightning Hit.prefab", effect = P + "Explosion/Explosion_Storm_LWRP.prefab", scale = .1f, once = true, life = .45f, maxScreen = .12f, drop = new[] { "Expansive Wave ", "Smoke" } },
            // Sweeping Arc: a magenta rune in the palm; sparks burning at the beam's end; a burst on what it cuts.
            new Art { prefab = "Sweeping Arc/Sweeping Arc Hand.prefab", effect = P + "Casting/Casting_Arcane_3_LWRP.prefab", scale = .045f, charge = true, linger = .3f },
            new Art { prefab = "Sweeping Arc/Sweeping Arc Tip.prefab", effect = P + "Casting/Casting_Arcane_LWRP.prefab", scale = .22f, charge = true, linger = .3f, maxScreen = .1f },
            new Art { prefab = "Sweeping Arc/Sweeping Arc Hit.prefab", effect = P + "Explosion/Explosion_Arcane_3_LWRP.prefab", scale = .09f, once = true, life = .4f, maxScreen = .07f },
            // Molten Boulder: a glowing lava rock in the hand and in the air; a burst of fire; embers left burning on the ground.
            new Art { prefab = "Molten Boulder/Molten Boulder Hand.prefab", effect = P + "Casting/Casting_Fire_4_LWRP.prefab", scale = .08f, charge = true, linger = .35f },
            new Art { prefab = "Molten Boulder/Molten Boulder Projectile.prefab", parent = "Art", effect = P + "Projectiles/Projectile_Fire_4_LWRP.prefab", scale = .26f },
            new Art { prefab = "Molten Boulder/Molten Boulder Impact.prefab", effect = P + "Explosion/Explosion _Fire_2_LWRP.prefab", scale = .55f, once = true, life = .5f, maxScreen = .12f },
            new Art { prefab = "Molten Boulder/Molten Boulder Fire.prefab", effect = S + "Spell_Fire_4_LWRP.prefab", scale = .3f, charge = true, linger = .8f, maxScreen = .2f, drop = new[] { "Expansive Wave", "Explosion", "Orb" } },
            // Hurled Stone: earth gathered in the palm; a mossy rock; a burst of brown dust.
            new Art { prefab = "Hurled Stone/Hurled Stone Hand.prefab", effect = P + "Casting/Casting_Nature_2_LWRP.prefab", scale = .055f, charge = true, linger = .3f },
            new Art { prefab = "Hurled Stone/Hurled Stone Projectile.prefab", parent = "Art", effect = P + "Projectiles/Projectile_Nature_LWRP.prefab", scale = .17f },
            new Art { prefab = "Hurled Stone/Hurled Stone Impact.prefab", effect = P + "Explosion/Explosion_Nature_LWRP.prefab", scale = .3f, once = true, life = .45f, maxScreen = .12f },
            // Frost Lance: white-blue frost in the palm; an ice shard; a burst of ice; frost clinging to what it chilled.
            new Art { prefab = "Frost Lance/Frost Lance Hand.prefab", effect = P + "Casting/Casting_Ice_LWRP.prefab", scale = .07f, charge = true, linger = .3f },
            new Art { prefab = "Frost Lance/Frost Lance Projectile.prefab", parent = "Art", effect = P + "Projectiles/Projectile_Ice_3_LWRP.prefab", scale = .45f },
            new Art { prefab = "Frost Lance/Frost Lance Impact.prefab", effect = P + "Explosion/Explosion_Ice_2_LWRP.prefab", scale = .12f, once = true, life = .45f, maxScreen = .07f },
            new Art { prefab = "Frost Lance/Frost Lance Chill.prefab", effect = P + "Casting/Casting_Ice_LWRP.prefab", scale = .22f, charge = true, linger = .4f, maxScreen = .15f },
            // Raise the Fallen: a green rune in the palm; the rune circle and its rising wind under the bones; the flash as
            // they close; a faint circle under a skeleton the player raised.
            new Art { prefab = "Raise the Fallen/Raise the Fallen Hand.prefab", effect = P + "Casting/Casting_Dark_3_LWRP.prefab", scale = .045f, charge = true, linger = .35f },
            new Art { prefab = "Raise the Fallen/Raise the Fallen Circle.prefab", effect = S + "Spell_Dark_9_LWRP.prefab", scale = .2f, charge = true, linger = .6f, maxScreen = .18f },
            new Art { prefab = "Raise the Fallen/Raise the Fallen Flash.prefab", effect = P + "Explosion/Explosion_Dark_3_LWRP.prefab", scale = .22f, once = true, life = .5f, maxScreen = .2f },
            new Art { prefab = "Raise the Fallen/Raise the Fallen Aura.prefab", effect = "Prefabs/Auras/Aura_Dark_LWRP.prefab", scale = .13f, charge = true, linger = .5f, maxScreen = .2f },
        };
        foreach (var art in arts) Build(art);
        var hues = new[]
        {
            new Hue { prefab = "Shadow Veil/Shadow Veil Hand.prefab", hue = .76f, saturation = .8f, value = .55f },
            new Hue { prefab = "Shadow Veil/Shadow Veil Wisps.prefab", hue = .76f, saturation = .6f, value = .4f },
            // The pack's arcane burst is white at its heart; the beam's cut stays magenta and doesn't blow out.
            new Hue { prefab = "Sweeping Arc/Sweeping Arc Hit.prefab", hue = .84f, saturation = 1f, value = .7f },
            new Hue { prefab = "Frost Lance/Frost Lance Impact.prefab", hue = .56f, saturation = .9f, value = .75f },
            new Hue { prefab = "Hurled Stone/Hurled Stone Impact.prefab", hue = .09f, saturation = .45f, value = .7f },
        };
        foreach (var h in hues) Recolour(Spells + h.prefab, h.hue, h.saturation, h.value);
        AssetDatabase.SaveAssets();
        Debug.Log("New spell effects rebuilt from the Spells Pack.");
    }

    // Every colour the prefab's systems start with or fade through, given this hue, its saturation times s and its
    // value times v (alpha kept).
    static void Recolour(string path, float hue, float s, float v)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.startColor = Shift(main.startColor, hue, s, v);
                var col = ps.colorOverLifetime;
                if (col.enabled) col.color = Shift(col.color, hue, s, v);
            }
            var visual = root.GetComponent<SpellVisual>();
            if (visual != null && visual.glow != null) visual.glow.color = Color.HSVToRGB(hue, .6f, 1f);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static ParticleSystem.MinMaxGradient Shift(ParticleSystem.MinMaxGradient g, float hue, float s, float v)
    {
        switch (g.mode)
        {
            case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(Shift(g.color, hue, s, v));
            case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(Shift(g.colorMin, hue, s, v), Shift(g.colorMax, hue, s, v));
            case ParticleSystemGradientMode.Gradient: return new ParticleSystem.MinMaxGradient(Shift(g.gradient, hue, s, v));
            case ParticleSystemGradientMode.TwoGradients: return new ParticleSystem.MinMaxGradient(Shift(g.gradientMin, hue, s, v), Shift(g.gradientMax, hue, s, v));
            default: return g;
        }
    }

    static Gradient Shift(Gradient g, float hue, float s, float v)
    {
        if (g == null) return null;
        var shifted = new Gradient { mode = g.mode };
        shifted.SetKeys(g.colorKeys.Select(k => new GradientColorKey(Shift(k.color, hue, s, v), k.time)).ToArray(), g.alphaKeys);
        return shifted;
    }

    static Color Shift(Color c, float hue, float s, float v)
    {
        Color.RGBToHSV(c, out _, out float sat, out float val);
        var o = Color.HSVToRGB(hue, Mathf.Clamp01(sat * s), val * v, true);
        o.a = c.a;
        return o;
    }
}
