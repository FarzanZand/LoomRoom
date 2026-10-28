using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Puts the Spells Pack's particle effects (Assets/Spells Pack, its URP copies in LWRP(URP)) into the game's spell
// prefabs: Fireball (hand, projectile, impact), Fire Circle (marker, eruption) and Heal (hand, impact). Each effect is
// copied in unpacked under an "Art" child, without the pack's demo scripts and lights (SpellVisual keeps its own light),
// its systems set to scale with the hierarchy so the Art's scale sizes it. Run again to rebuild the Art from the pack
// (hand edits to the Art children are lost then; SpellVisual, lights and sounds on the prefabs are kept).
public static partial class SpellPackEffects
{
    const string Pack = "Assets/Spells Pack/LWRP(URP)/Particles_LWRP/";
    const string Spells = "Assets/Game/Combat/Spells/";
    // Pixel look: the effects draw with point-filtered, low-resolution copies of the pack's textures (in _Pack), about an
    // eighth of their size, so they sit with the game's pixel art. Off: the pack's own smooth materials.
    const bool PixelArt = true;
    const string PixelFolder = Spells + "_Pack/";
    const int PixelDivisor = 8;

    struct Art
    {
        public string prefab, parent, effect;
        public float scale;
        public Vector3 offset;
        public bool once, charge;
        public string[] remove, drop;
        public (string system, float size)[] sizes;   // a system's start size times this (sizes the parts of an effect apart)
        public float linger;
        public (string system, float life)[] lives;
        public string[] mute;
        public (string system, float rate)[] rates;   // a system's emission times this (after bursts became a steady rate)     // systems kept but no longer emitting (a root that can't be dropped)   // one system's lifetime times this, on top of Life
        public float life;        // every system's lifetime times this (0: as the pack has it)
        public float maxScreen;   // largest a particle may be on screen, as a share of the screen (0: the renderer's own); keeps a hit at point blank from filling the view
    }

    public static void Rebuild()
    {
        var arts = new[]
        {
            new Art { prefab = "Fireball/Fireball Hand.prefab", effect = "Prefabs/Projectiles/Casting/Casting_Fire_LWRP.prefab", scale = .11f, charge = true, linger = .35f, remove = new[] { "Flame" }, drop = new[] { "Expansive Wave" } },
            new Art { prefab = "Fireball/Fireball Projectile.prefab", parent = "Art", effect = "Prefabs/Projectiles/Projectiles/Projectile_Fire_LWRP.prefab", scale = .45f, remove = new[] { "Art/Core", "Art/Flame Tail", "Art/Embers" }, drop = new[] { "Expansive Wave" } },
            new Art { prefab = "Fireball/Fireball Impact.prefab", effect = "Prefabs/Projectiles/Explosion/Explosion _Fire_4_LWRP.prefab", scale = .8f, once = true, life = .45f, maxScreen = .08f, lives = new[] { ("Effect", .45f) }, remove = new[] { "Flash", "Flames", "Sparks", "Puff" }, drop = new[] { "Fireballs(Low Quality)" } },
            // Area Radius 2: the ground wave spreads about 4 m across, the burst stands a little over head height.
            new Art { prefab = "Fire Circle/Fire Circle Eruption.prefab", effect = "Prefabs/Spells/Spell_Fire_5_LWRP.prefab", scale = 1f, once = true, life = .7f, maxScreen = .07f, sizes = new[] { ("Effect", .26f), ("Explosion", .4f) }, lives = new[] { ("Explosion", .6f) } },
            // Held heal: a small steady green glow with a few motes, nothing more.
            new Art { prefab = "Heal/Heal Hand.prefab", effect = "Prefabs/Projectiles/Casting/Casting_Nature_2_LWRP.prefab", scale = .1f, charge = true, linger = .35f, remove = new[] { "Sparkles", "Motes" }, drop = new[] { "Thunder 3", "Spell 3" }, mute = new[] { "Effect" }, sizes = new[] { ("Energy", 1.6f), ("Glow", .6f) }, rates = new[] { ("Energy", .18f) } },
            // In first person the feet are out of view: the ring is set a little ahead, so it shows at the bottom of the screen.
            new Art { prefab = "Heal/Heal Impact.prefab", effect = "Prefabs/Spells/Spell_Nature_7_LWRP.prefab", scale = .6f, offset = new Vector3(0f, .05f, 1.1f), once = true, remove = new[] { "Sparkles", "Motes" }, maxScreen = .4f, sizes = new[] { ("Trail", .35f) } },
            // The Dungeon Master's beam in a boss fight (DungeonMasterBeam): the glow at his hand, and where it lands on an unguarded player.
            new Art { prefab = "Assets/Game/Combat/Effects/DM beam/DM beam charge.prefab", effect = "Prefabs/Projectiles/Casting/Casting_Light_LWRP.prefab", scale = 1.2f, maxScreen = .2f, charge = true, linger = .4f },
            new Art { prefab = "Assets/Game/Combat/Effects/DM beam/DM beam impact.prefab", effect = "Prefabs/Projectiles/Explosion/Explosion_Light_LWRP.prefab", scale = .14f, once = true, life = .6f, maxScreen = .2f },
        };
        // The eruption was a variant of the fireball's impact: it becomes its own prefab, keeping its SpellVisual,
        // impulse and light.
        Standalone(Spells + "Fire Circle/Fire Circle Eruption.prefab");
        foreach (var art in arts) Build(art);
        Marker();
        AssetDatabase.SaveAssets();
        Debug.Log("Spell effects rebuilt from the Spells Pack.");
    }

    static void Build(Art art)
    {
        string path = art.prefab.StartsWith("Assets/") ? art.prefab : Spells + art.prefab;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var parent = string.IsNullOrEmpty(art.parent) ? root.transform : root.transform.Find(art.parent);
            if (art.remove != null)
                foreach (var name in art.remove)
                {
                    var old = root.transform.Find(name);
                    if (old != null && !PrefabUtility.IsPartOfPrefabInstance(old)) Object.DestroyImmediate(old.gameObject);
                }
            var previous = parent.Find("Effect");
            if (previous != null) Object.DestroyImmediate(previous.gameObject);

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Pack + art.effect);
            var effect = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(effect, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            effect.name = "Effect";
            effect.transform.SetParent(parent, false);
            effect.transform.localPosition = art.offset;
            effect.transform.localRotation = source.transform.localRotation;
            effect.transform.localScale = Vector3.one * art.scale;
            // The pack's demo scripts, physics and lights stay behind: SpellVisual drives the light, SpellProjectile the flight.
            foreach (var c in effect.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(c);
            foreach (var c in effect.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var c in effect.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(c);
            foreach (var l in effect.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l.gameObject.GetComponent<ParticleSystem>() != null ? (Object)l : l.gameObject);
            if (art.drop != null)
                foreach (var name in art.drop)
                {
                    var t = effect.transform.Find(name) ?? effect.GetComponentsInChildren<Transform>(true).FirstOrDefault(c => c.name == name);
                    if (t != null && t != effect.transform) Object.DestroyImmediate(t.gameObject);
                }
            if (PixelArt) Pixelate(effect);
            var systems = effect.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in systems)
            {
                var main = ps.main;
                if (art.sizes != null)
                    foreach (var (system, size) in art.sizes)
                        if (ps.name == system) main.startSizeMultiplier *= size;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.playOnAwake = true;
                // The pack's systems pause while off screen: a heal ring at the feet would never start.
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                // Colours that darken to black on an alpha-blended material read as black smoke in a dark dungeon: fade out instead.
                if (art.once) FadeInsteadOfBlack(ps);
                if (art.once) { main.loop = false; main.stopAction = ParticleSystemStopAction.None; }
                if (art.life > 0) main.startLifetimeMultiplier *= art.life;
                // Held effects burn steadily: the pack's casting effects come in bursts on a loop, which flickers on and off.
                if (art.charge) Steady(ps);
                if (art.rates != null)
                    foreach (var (system, rate) in art.rates)
                        if (ps.name == system) { var emission = ps.emission; emission.rateOverTimeMultiplier *= rate; }
                if (art.mute != null && art.mute.Contains(ps.name)) { var emission = ps.emission; emission.enabled = false; }
                if (art.lives != null)
                    foreach (var (system, life) in art.lives)
                        if (ps.name == system) main.startLifetimeMultiplier *= life;
                var r = ps.GetComponent<ParticleSystemRenderer>();
                if (r != null)
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                    if (art.maxScreen > 0) r.maxParticleSize = Mathf.Min(r.maxParticleSize, art.maxScreen);
                }
            }
            var visual = root.GetComponent<SpellVisual>();
            if (visual != null)
            {
                if (art.charge) visual.chargeScaled = systems.Where(s => s.emission.enabled).ToArray();
                if (art.once) visual.autoDestroySeconds = Longest(systems);
                visual.stopLinger = art.linger;
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static void FadeInsteadOfBlack(ParticleSystem ps)
    {
        var col = ps.colorOverLifetime;
        if (!col.enabled) return;
        var color = col.color;
        if (color.mode == ParticleSystemGradientMode.Gradient) col.color = new ParticleSystem.MinMaxGradient(Faded(color.gradient));
        else if (color.mode == ParticleSystemGradientMode.TwoGradients) col.color = new ParticleSystem.MinMaxGradient(Faded(color.gradientMin), Faded(color.gradientMax));
    }

    static Gradient Faded(Gradient gradient)
    {
        if (gradient == null) return null;
        float dark = -1;
        foreach (var key in gradient.colorKeys)
            if (key.time > .02f && Mathf.Max(key.color.r, key.color.g, key.color.b) < .12f) { dark = key.time; break; }
        if (dark < 0) return gradient;
        var colors = gradient.colorKeys.Where(k => k.time < dark).ToList();
        if (colors.Count == 0) colors.Add(gradient.colorKeys[0]);
        colors.Add(new GradientColorKey(colors[colors.Count - 1].color, 1f));
        var fade = new List<GradientAlphaKey> { new(gradient.Evaluate(0f).a, 0f), new(gradient.Evaluate(dark * .55f).a, dark * .55f), new(0f, dark), new(0f, 1f) };
        var replaced = new Gradient();
        replaced.SetKeys(colors.ToArray(), fade.ToArray());
        return replaced;
    }

    // Bursts become the same number of particles per second; a single burst keeps that many alive over its lifetime.
    static void Steady(ParticleSystem ps)
    {
        var emission = ps.emission;
        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        if (emission.burstCount == 0) return;
        float rate = 0;
        for (int i = 0; i < emission.burstCount; i++)
        {
            var burst = emission.GetBurst(i);
            float count = burst.count.constantMax;
            rate += burst.cycleCount > 1 && burst.repeatInterval > .001f ? count / burst.repeatInterval : count / Mathf.Max(.1f, main.startLifetime.constantMax);
        }
        emission.burstCount = 0;
        emission.rateOverTimeMultiplier = rate;
    }

    static float Longest(ParticleSystem[] systems)
    {
        float longest = 0;
        foreach (var ps in systems)
        {
            var m = ps.main;
            longest = Mathf.Max(longest, m.startDelay.constantMax + (m.loop ? 0 : m.duration * .5f) + m.startLifetime.constantMax);
        }
        return Mathf.Min(longest, 5f);
    }

    // A variant becomes a plain prefab at the same path (same GUID), its inherited particle art left out.
    static void Standalone(string path)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null || PrefabUtility.GetPrefabAssetType(asset) != PrefabAssetType.Variant) return;
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        try
        {
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var name in new[] { "Flash", "Flames", "Sparks", "Puff", "Effect" })
            {
                var t = instance.transform.Find(name);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }
            PrefabUtility.SaveAsPrefabAsset(instance, path);
        }
        finally { Object.DestroyImmediate(instance); }
    }

    // The ground mark: the pack's rune circle as the ring, its soft glow as the heat that grows over the delay.
    static void Marker()
    {
        Retexture(Spells + "Fire Circle/Fire Circle ring.mat", Pack + "Materials/Spell-Casting_LWRP.mat", new Color(1f, .5f, .15f, 1f));
        Retexture(Spells + "Fire Circle/Fire Circle fill.mat", Pack + "Materials/Glow_LWRP.mat", new Color(2.2f, .7f, .12f, 1f));
    }

    static void Pixelate(GameObject effect)
    {
        foreach (var r in effect.GetComponentsInChildren<Renderer>(true))
        {
            r.sharedMaterials = r.sharedMaterials.Select(m => PixelMaterial(m, PixelDivisor)).ToArray();
            if (r is ParticleSystemRenderer p && p.trailMaterial != null) p.trailMaterial = PixelMaterial(p.trailMaterial, PixelDivisor);
        }
    }

    // A copy of a pack material in _Pack/Materials whose textures are the pixel copies; refreshed from the pack each run.
    public static Material PixelMaterial(Material source, int divisor)
    {
        if (source == null || !AssetDatabase.GetAssetPath(source).StartsWith("Assets/Spells Pack/")) return source;
        string path = PixelFolder + "Materials/" + source.name + ".mat";
        var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (copy == null)
        {
            Folder(PixelFolder + "Materials");
            copy = new Material(source);
            AssetDatabase.CreateAsset(copy, path);
        }
        copy.shader = source.shader;
        copy.CopyPropertiesFromMaterial(source);
        copy.shaderKeywords = source.shaderKeywords;
        copy.renderQueue = source.renderQueue;
        PixelTextures(copy, divisor);
        EditorUtility.SetDirty(copy);
        return copy;
    }

    static void PixelTextures(Material material, int divisor)
    {
        foreach (var name in material.GetTexturePropertyNames())
        {
            var texture = material.GetTexture(name);
            if (texture != null) material.SetTexture(name, PixelTexture(texture, divisor));
        }
    }

    // A copy of the texture file in _Pack/Textures, imported point-filtered at about 1/divisor of its size.
    static Texture PixelTexture(Texture source, int divisor)
    {
        string from = AssetDatabase.GetAssetPath(source);
        if (string.IsNullOrEmpty(from) || from.StartsWith(PixelFolder)) return source;
        if (!from.StartsWith("Assets/Spells Pack/") || source is not Texture2D) return source;
        if (from.Contains("_Normal")) return source;
        string path = PixelFolder + "Textures/" + System.IO.Path.GetFileName(from);
        if (AssetDatabase.LoadAssetAtPath<Texture>(path) == null) { Folder(PixelFolder + "Textures"); AssetDatabase.CopyAsset(from, path); }
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        int size = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(source.width, source.height) / divisor), 32, 512);
        if (importer.filterMode != FilterMode.Point || importer.maxTextureSize != size || importer.textureCompression != TextureImporterCompression.Uncompressed)
        {
            importer.filterMode = FilterMode.Point;
            importer.maxTextureSize = size;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture>(path);
    }

    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        Folder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
    }

    static void Retexture(string path, string from, Color tint)
    {
        var target = AssetDatabase.LoadAssetAtPath<Material>(path);
        var source = AssetDatabase.LoadAssetAtPath<Material>(from);
        if (target == null || source == null) { Debug.LogWarning($"Missing material {path} or {from}"); return; }
        target.shader = source.shader;
        target.CopyPropertiesFromMaterial(source);
        target.shaderKeywords = source.shaderKeywords;
        target.renderQueue = source.renderQueue;
        if (PixelArt) PixelTextures(target, 4);   // the runes need more of their texels than flames do
        if (target.HasProperty("_BaseColor")) target.SetColor("_BaseColor", tint);
        EditorUtility.SetDirty(target);
    }
}
