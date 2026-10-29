using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Makes the spells added in October 2026 (Shadow Veil, Chain Lightning, Sweeping Arc, Molten Boulder, Hurled Stone, Frost
// Lance, Raise the Fallen): one folder each in Combat/Spells with the spell, its tome and its effect prefabs (art from the
// Spells Pack, SpellPackEffects.RebuildNewSpells), the bolt and beam materials, and the Necromancer enemy who raises
// skeletons and casts Frost Lance and Sweeping Arc. Run from an eval: NewSpellsSetup.Build(). Safe to run again: what
// exists is kept, and its numbers are only set again with Build(retune: true). Every run rebuilds the effects' art.
public static class NewSpellsSetup
{
    const string Spells = "Assets/Game/Combat/Spells/";
    const string Library = "Assets/ThirdParty/Sound Effects Library/";
    const string Necromancer = "Assets/Game/Characters/Enemies/Crypt/Necromancer.prefab";

    public static void Build(bool retune = false)
    {
        var fireball = Load<SpellDefinition>(Spells + "Fireball/Fireball.asset");
        var heal = Load<SpellDefinition>(Spells + "Heal/Heal.asset");

        // Prefabs first (copies of the Fireball's, whose SpellVisual, light and projectile setup they keep); the art goes in after.
        foreach (var name in new[] { "Shadow Veil", "Chain Lightning", "Sweeping Arc", "Molten Boulder", "Hurled Stone", "Frost Lance", "Raise the Fallen", "Flick", "Die Throw", "Helping Hand" })
        {
            Folder(Spells + name);
            Copy("Fireball/Fireball Hand.prefab", $"{name}/{name} Hand.prefab");
        }
        Copy("Fireball/Fireball Hand.prefab", "Shadow Veil/Shadow Veil Wisps.prefab");
        Copy("Fireball/Fireball Impact.prefab", "Chain Lightning/Chain Lightning Hit.prefab");
        Copy("Fireball/Fireball Hand.prefab", "Sweeping Arc/Sweeping Arc Tip.prefab");
        Copy("Fireball/Fireball Impact.prefab", "Sweeping Arc/Sweeping Arc Hit.prefab");
        Copy("Fireball/Fireball Projectile.prefab", "Molten Boulder/Molten Boulder Projectile.prefab");
        Copy("Fireball/Fireball Impact.prefab", "Molten Boulder/Molten Boulder Impact.prefab");
        Copy("Fireball/Fireball Hand.prefab", "Molten Boulder/Molten Boulder Fire.prefab");
        Copy("Fireball/Fireball Projectile.prefab", "Hurled Stone/Hurled Stone Projectile.prefab");
        Copy("Fireball/Fireball Impact.prefab", "Hurled Stone/Hurled Stone Impact.prefab");
        Copy("Fireball/Fireball Projectile.prefab", "Frost Lance/Frost Lance Projectile.prefab");
        Copy("Fireball/Fireball Impact.prefab", "Frost Lance/Frost Lance Impact.prefab");
        Copy("Fireball/Fireball Hand.prefab", "Frost Lance/Frost Lance Chill.prefab");
        Copy("Fireball/Fireball Hand.prefab", "Raise the Fallen/Raise the Fallen Circle.prefab");
        Copy("Fireball/Fireball Impact.prefab", "Raise the Fallen/Raise the Fallen Flash.prefab");
        Copy("Fireball/Fireball Hand.prefab", "Raise the Fallen/Raise the Fallen Aura.prefab");
        Copy("Fireball/Fireball Hand.prefab", "Flick/Flick Mark.prefab");
        Copy("Fireball/Fireball Hand.prefab", "Die Throw/Die Throw Mark.prefab");
        Copy("Fireball/Fireball Hand.prefab", "Helping Hand/Helping Hand Pour.prefab");
        Copy("Fireball/Fireball Impact.prefab", "Helping Hand/Helping Hand Burst.prefab");
        SpellPackEffects.RebuildNewSpells();

        // Lights in each effect's colour.
        Light("Shadow Veil/Shadow Veil Hand.prefab", new Color(.6f, .35f, 1f), 1.2f, 2f);
        Light("Shadow Veil/Shadow Veil Wisps.prefab", new Color(.5f, .3f, .9f), 0f, 1f);
        Light("Chain Lightning/Chain Lightning Hand.prefab", new Color(.55f, .85f, 1f), 1.6f, 2.5f);
        Light("Chain Lightning/Chain Lightning Hit.prefab", new Color(.55f, .85f, 1f), 1.2f, 3f);
        Light("Sweeping Arc/Sweeping Arc Hand.prefab", new Color(1f, .35f, 1f), .7f, 2f);
        Light("Sweeping Arc/Sweeping Arc Tip.prefab", new Color(1f, .35f, 1f), 0f, 1f);
        Light("Sweeping Arc/Sweeping Arc Hit.prefab", new Color(1f, .35f, 1f), 1.2f, 2.5f);
        Light("Molten Boulder/Molten Boulder Hand.prefab", new Color(1f, .45f, .15f), 1.5f, 2.5f);
        Light("Molten Boulder/Molten Boulder Impact.prefab", new Color(1f, .5f, .2f), 5f, 6f);
        Light("Molten Boulder/Molten Boulder Fire.prefab", new Color(1f, .45f, .15f), 2.5f, 4f);
        Light("Hurled Stone/Hurled Stone Hand.prefab", new Color(.7f, 1f, .5f), .8f, 2f);
        Light("Hurled Stone/Hurled Stone Impact.prefab", new Color(.9f, .85f, .7f), 0f, 1f);
        Light("Frost Lance/Frost Lance Hand.prefab", new Color(.6f, .85f, 1f), 1.3f, 2.5f);
        Light("Frost Lance/Frost Lance Impact.prefab", new Color(.6f, .85f, 1f), 1.2f, 2.5f);
        Light("Frost Lance/Frost Lance Chill.prefab", new Color(.6f, .85f, 1f), .6f, 1.5f);
        Light("Raise the Fallen/Raise the Fallen Hand.prefab", new Color(.4f, 1f, .45f), 1.4f, 2.5f);
        Light("Raise the Fallen/Raise the Fallen Circle.prefab", new Color(.35f, 1f, .4f), 3f, 5f);
        Light("Raise the Fallen/Raise the Fallen Flash.prefab", new Color(.4f, 1f, .45f), 4f, 5f);
        Light("Raise the Fallen/Raise the Fallen Aura.prefab", new Color(.4f, 1f, .45f), .8f, 2f);
        Light("Flick/Flick Hand.prefab", new Color(1f, .82f, .5f), 1.2f, 2.2f);
        Light("Flick/Flick Mark.prefab", new Color(1f, .85f, .55f), 3f, 4f);
        Light("Die Throw/Die Throw Hand.prefab", new Color(1f, .82f, .5f), 1.2f, 2.2f);
        Light("Die Throw/Die Throw Mark.prefab", new Color(1f, .85f, .55f), 3f, 4f);
        Light("Helping Hand/Helping Hand Hand.prefab", new Color(1f, .9f, .6f), 1.2f, 2.2f);
        Light("Helping Hand/Helping Hand Pour.prefab", new Color(1f, .9f, .6f), 4f, 5f);
        Light("Helping Hand/Helping Hand Burst.prefab", new Color(1f, .9f, .6f), 5f, 5f);
        // The hurled rock's material is full white: right by the eyes the player's key light burns it to a blank lump.
        var rock = Load<Material>(Spells + "_Pack/Materials/Rock_LWRP.mat");
        if (rock != null && rock.HasProperty("_BaseColor")) { rock.SetColor("_BaseColor", new Color(.58f, .55f, .5f, 1f)); EditorUtility.SetDirty(rock); }
        var fire = LoadPrefab("Molten Boulder/Molten Boulder Fire.prefab");
        // The pack's ember ring is 1 m across at this scale (its circle's radius 3.35 x .3).
        if (fire != null) Edit("Molten Boulder/Molten Boulder Fire.prefab", root => { var g = root.GetComponent<GroundFire>(); if (g == null) g = root.AddComponent<GroundFire>(); g.drawnRadius = 1f; });

        // Bolts and beams: the Dungeon Master's beam textures on their own copies.
        var boltCore = Material("Chain Lightning/Chain Lightning core.mat", "Assets/Game/Combat/Effects/DM beam/DM beam core.mat", new Color(1f, 1f, 1f, 1f));
        var boltGlow = Material("Chain Lightning/Chain Lightning glow.mat", "Assets/Game/Combat/Effects/DM beam/DM beam glow.mat", new Color(.6f, .85f, 1.6f, 1f));
        var beamCore = Material("Sweeping Arc/Sweeping Arc core.mat", "Assets/Game/Combat/Effects/DM beam/DM beam core.mat", new Color(1f, 1f, 1f, 1f));
        var beamGlow = Material("Sweeping Arc/Sweeping Arc glow.mat", "Assets/Game/Combat/Effects/DM beam/DM beam glow.mat", new Color(1.5f, .4f, 1.5f, 1f));

        var soldier = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Characters/Enemies/Crypt/Crypt Soldier.prefab");

        var veil = Spell("Shadow Veil", heal, retune, s =>
        {
            s.spell = LeftHandSpell.ShadowVeil; s.effect = SpellEffect.ShadowVeil;
            s.description = "Wrap yourself in shadow. No enemy sees you until you strike, and the first blow is a backstab.";
            s.manaCost = 20; s.power = 0; s.castSeconds = .5f; s.recoverySeconds = .3f; s.castMoveSpeed = .8f;
            s.veil.seconds = 8f; s.veil.revealDistance = 1.1f; s.veil.ambush = true;
            s.veil.wisps = Visual("Shadow Veil/Shadow Veil Wisps.prefab");
            s.veil.endSound = Clip("Magic/Mgc_Water_Hit_Short_01"); s.veil.endVolume = .6f;
            s.chargeSound = Clip("Magic/Mgc_Water_Cast_01"); s.releaseSound = Clip("Magic/Mgc_Fire_Whoosh_02"); s.impactSound = null;
        });
        var chain = Spell("Chain Lightning", fireball, retune, s =>
        {
            s.spell = LeftHandSpell.ChainLightning; s.effect = SpellEffect.ChainLightning;
            s.description = "A bolt that leaps from the enemy you aim at to the ones near it, weaker with every jump.";
            s.manaCost = 16; s.power = 14; s.castSeconds = .55f; s.recoverySeconds = .35f;
            var c = s.chain;
            c.range = 16; c.aimCone = 14; c.jumps = 3; c.jumpRange = 7; c.falloff = .75f; c.stunSeconds = .35f; c.jumpDelay = .09f;
            c.boltSeconds = .36f; c.coreWidth = .12f; c.glowWidth = .6f; c.kink = .45f; c.flicker = .04f;
            c.coreMaterial = boltCore; c.glowMaterial = boltGlow; c.color = new Color(.55f, .85f, 1.6f); c.lightIntensity = 2.5f;
            c.hitEffect = Visual("Chain Lightning/Chain Lightning Hit.prefab");
            c.jumpSound = Clip("Magic/Mgc_Electric_Hit_01"); c.jumpVolume = .7f;
            s.chargeSound = Clip("Magic/Mgc_Electric_Cast_Start_01"); s.releaseSound = Clip("Magic/Mgc_Electric_Throw_01"); s.impactSound = null; s.flightSound = null;
        });
        var sweep = Spell("Sweeping Arc", fireball, retune, s =>
        {
            s.spell = LeftHandSpell.SweepingArc; s.effect = SpellEffect.SweepingArc;
            s.description = "A beam that swings across everything in front of you.";
            s.manaCost = 18; s.power = 16; s.castSeconds = .6f; s.recoverySeconds = .5f;
            var w = s.sweep;
            w.arc = 120; w.sweepSeconds = .55f; w.length = 10; w.enemyHeight = 1.45f; w.warnSeconds = .45f;
            w.coreWidth = .09f; w.glowWidth = .5f; w.coreMaterial = beamCore; w.glowMaterial = beamGlow; w.color = new Color(1.4f, .35f, 1.5f); w.lightIntensity = 5f;
            w.tipEffect = Visual("Sweeping Arc/Sweeping Arc Tip.prefab"); w.hitEffect = Visual("Sweeping Arc/Sweeping Arc Hit.prefab");
            w.sweepSound = Clip("Magic/Mgc_Electric_Hold_Loop_01"); w.sweepVolume = .6f;
            s.chargeSound = Clip("Magic/Mgc_Electric_Cast_Start_02"); s.releaseSound = Clip("Magic/Mgc_Fire_Whoosh_01"); s.impactSound = null; s.flightSound = null;
        });
        var boulder = Spell("Molten Boulder", fireball, retune, s =>
        {
            s.spell = LeftHandSpell.MoltenBoulder; s.effect = SpellEffect.Standard;
            s.description = "A lump of glowing rock, thrown in an arc. It bursts where it lands and leaves the ground burning.";
            s.manaCost = 18; s.power = 26; s.castSeconds = .8f; s.recoverySeconds = .45f;
            s.projectile = Projectile("Molten Boulder/Molten Boulder Projectile.prefab"); s.radius = 2.2f; s.speed = 16; s.lifetime = 4f; s.gravity = 14f;
            s.groundFire = LoadPrefab("Molten Boulder/Molten Boulder Fire.prefab")?.GetComponent<GroundFire>();
            s.groundFireSeconds = 3f; s.groundFireRadius = 1.8f; s.groundFirePower = .2f;
            s.chargeSound = Clip("Magic/Mgc_Fire_Cast_Start_01"); s.releaseSound = Clip("Magic/Mgc_Fire_Throw_03");
            s.flightSound = Clip("Magic/Mgc_Fire_Hold_Loop_01"); s.impactSound = Clip("Magic/Mgc_Fire_Impact_02");
        });
        var stone = Spell("Hurled Stone", fireball, retune, s =>
        {
            s.spell = LeftHandSpell.HurledStone; s.effect = SpellEffect.Standard;
            s.description = "A stone torn from the ground and thrown. Cheap, and it knocks what it hits off balance.";
            s.manaCost = 8; s.power = 12; s.castSeconds = .45f; s.recoverySeconds = .3f;
            s.projectile = Projectile("Hurled Stone/Hurled Stone Projectile.prefab"); s.radius = .8f; s.speed = 20; s.lifetime = 3f; s.gravity = 12f; s.stunSeconds = .8f;
            s.chargeSound = Clip("Magic/Mgc_Water_Cast_02"); s.releaseSound = Clip("Magic/Mgc_Fire_Whoosh_03");
            s.flightSound = null; s.impactSound = Clip("Ambience/Amb_Rock_Drop_Impact_02");
        });
        var lance = Spell("Frost Lance", fireball, retune, s =>
        {
            s.spell = LeftHandSpell.FrostLance; s.effect = SpellEffect.Standard;
            s.description = "A shard of ice that flies straight through up to three enemies and slows them.";
            s.manaCost = 12; s.power = 13; s.castSeconds = .5f; s.recoverySeconds = .3f;
            s.projectile = Projectile("Frost Lance/Frost Lance Projectile.prefab"); s.radius = .5f; s.speed = 30; s.lifetime = 2f; s.gravity = 0f;
            s.pierce = 3; s.chill = .4f; s.chillSeconds = 3f; s.chillEffect = Visual("Frost Lance/Frost Lance Chill.prefab");
            s.chargeSound = Clip("Magic/Mgc_Ice_Cast_01"); s.releaseSound = Clip("Magic/Mgc_Ice_Throw_01");
            s.flightSound = null; s.impactSound = Clip("Magic/Mgc_Ice_Hit_01");
        });
        var raise = Spell("Raise the Fallen", heal, retune, s =>
        {
            s.spell = LeftHandSpell.RaiseTheFallen; s.effect = SpellEffect.RaiseTheFallen;
            s.description = "Pull a fallen skeleton's bones back together. It fights for you for half a minute.";
            s.manaCost = 25; s.power = 0; s.castSeconds = 1f; s.recoverySeconds = .4f; s.castMoveSpeed = .6f;
            var r = s.raise;
            r.reach = 10; r.raiseAs = soldier; r.gatherSeconds = 1.3f; r.riseSeconds = 1.4f;
            r.circle = Visual("Raise the Fallen/Raise the Fallen Circle.prefab"); r.flash = Visual("Raise the Fallen/Raise the Fallen Flash.prefab");
            var m = r.summon;
            m.lifetime = 30f; m.maxAtOnce = 2; m.end = SummonEnd.Collapse; m.followDistance = 3f; m.leash = 18f; m.creditSummoner = true; m.noLoot = true;
            m.aura = Visual("Raise the Fallen/Raise the Fallen Aura.prefab"); m.endEffect = null;
            r.gatherSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Game/Audio/Clips/Misc/BoneGrowth.wav"); r.gatherVolume = .9f;
            s.chargeSound = Clip("Magic/Mgc_Water_Cast_03"); s.releaseSound = Clip("Magic/Mgc_Fire_Whoosh_04"); s.impactSound = null;
        });

        var flick = Spell("Flick", fireball, retune, s =>
        {
            s.spell = LeftHandSpell.Flick; s.effect = SpellEffect.Flick;
            s.description = "Ask the Dungeon Master for a hand. He reaches in and flicks the enemy you aim at across the room.";
            s.manaCost = 30; s.power = 30; s.castSeconds = .6f; s.recoverySeconds = .4f;
            var f = s.flick;
            f.reach = FlickReach.Appear; f.appearDistance = 13f; f.appearAhead = .75f;
            f.range = 16; f.aimCone = 15; f.reachSeconds = 1.6f; f.windup = .35f;
            f.launchSpeed = 11; f.launchUp = 7; f.sideways = 70; f.walkSpeed = 45; f.gravity = 25; f.spin = 720; f.stunSeconds = 1.5f; f.bowlShare = .5f;
            f.die = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Levels/Shared/Props/Events/DM die.prefab"); f.dieScale = 1.5f; f.dieArc = 7f; f.maxWalk = 40f; f.dieGravity = 16f; f.dieLight = 12f; f.handRoom = 1.8f; f.fingerReach = 12f; f.mark = Visual("Flick/Flick Mark.prefab"); f.markLight = 1.5f; f.handLength = 5f;
            f.snapSound = Clip("Weapons/Wpn_Whoosh_02"); f.snapVolume = 1f;
            f.slamSound = Clip("SoundFX/HammerHit"); f.slamVolume = 1f;
            f.hitSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/ThirdParty/Generic Fantasy UI/Fantasy_UI_Gen_Select_Heavy_01_mono.wav"); f.hitVolume = 1f;
            f.hitShake = .6f;
            f.lid = Lid();
            f.dieKnocks = new[] { 1, 2, 3 }.Select(i => AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/Game/Cinematics/Intro/Tabletop/Dice knock {i}.wav")).Where(c => c != null).ToArray();
            f.dieRollSound = Clip("SoundFX/DiceRollFast"); f.dieVolume = 1f;
            s.chargeSound = Clip("Magic/Mgc_Water_Cast_01"); s.releaseSound = null; s.impactSound = null; s.flightSound = null;
        });

        // Flick's die on its own: from where he stands, whatever the distance.
        var die = Spell("Die Throw", fireball, retune, s =>
        {
            s.spell = LeftHandSpell.DieThrow; s.effect = SpellEffect.Flick;
            s.description = "Ask the Dungeon Master for a hand. He flicks a die at the enemy you aim at from where he stands, and it knocks it across the room.";
            s.manaCost = 24; s.power = 24; s.castSeconds = .6f; s.recoverySeconds = .4f;
            s.flick = JsonUtility.FromJson<FlickSettings>(JsonUtility.ToJson(flick.flick));
            s.flick.reach = FlickReach.DieOnly; s.flick.mark = Visual("Die Throw/Die Throw Mark.prefab");
            // Thrown quickly: his hand comes in faster and the die flies low and hard (it took about five seconds to land).
            s.flick.reachSeconds = .9f; s.flick.windup = .25f; s.flick.dieArc = 3.5f; s.flick.dieGravity = 40f;
            s.flick.lines = new[] { "Catch.", "Let's see what you roll.", "Allow me." };
            s.chargeSound = Clip("Magic/Mgc_Water_Cast_01"); s.releaseSound = null; s.impactSound = null; s.flightSound = null;
        });

        var help = Spell("Helping Hand", heal, retune, s =>
        {
            s.spell = LeftHandSpell.HelpingHand; s.effect = SpellEffect.HelpingHand;
            s.description = "Ask the Dungeon Master for a hand. He appears in front of you and holds it over you, and the light from it heals you.";
            s.manaCost = 28; s.power = 40; s.castSeconds = .7f; s.recoverySeconds = .4f; s.castMoveSpeed = .7f;
            var h = s.helpingHand;
            h.reach = HelpReach.Appear; h.appearDistance = 17f;
            h.reachSeconds = 1.4f; h.holdSeconds = 2.2f; h.handReach = 12f; h.handRoom = 1.5f; h.edgeGap = 1.5f; h.maxWalk = 40f; h.walkSpeed = 45f;
            h.palmHeight = 7f; h.palmAhead = 4.5f; h.palmLight = 8f; h.palmGlow = Visual("Helping Hand/Helping Hand Hand.prefab"); h.palmGlowScale = 12f; h.beam = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Levels/Dungeon1/Forest/Props/Moonbeam warm.prefab"); h.beamSource = new Vector3(-1.2f, 11f, -.6f); h.beamLight = .35f;
            h.pour = Visual("Helping Hand/Helping Hand Pour.prefab"); h.burst = Visual("Helping Hand/Helping Hand Burst.prefab");
            h.potion = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Items/Prefabs/Consumables/Potion.prefab");
            h.potionScale = 3f; h.potionArc = 6f; h.potionGravity = 16f; h.potionLight = 10f; h.landAhead = 1.2f; h.burstHealSeconds = 1f;
            h.smashSound = Clip("Magic/Mgc_Ice_Hit_Short_01"); h.smashVolume = .8f;
            h.lid = Lid();
            h.color = new Color(1f, .9f, .6f);
            s.chargeSound = Clip("Magic/Mgc_Water_Cast_01"); s.releaseSound = null; s.impactSound = Clip("Magic/Mgc_Water_Impact_Short_01"); s.impactVolume = .7f; s.flightSound = null;
        });

        // Hand effects every spell shows in the palm.
        foreach (var (spell, name) in new[] { (veil, "Shadow Veil"), (chain, "Chain Lightning"), (sweep, "Sweeping Arc"), (boulder, "Molten Boulder"), (stone, "Hurled Stone"), (lance, "Frost Lance"), (raise, "Raise the Fallen"), (flick, "Flick"), (die, "Die Throw"), (help, "Helping Hand") })
        {
            spell.heldVisual = Visual($"{name}/{name} Hand.prefab");
            spell.impact = name switch
            {
                "Molten Boulder" => Visual("Molten Boulder/Molten Boulder Impact.prefab"),
                "Hurled Stone" => Visual("Hurled Stone/Hurled Stone Impact.prefab"),
                "Frost Lance" => Visual("Frost Lance/Frost Lance Impact.prefab"),
                _ => null,
            };
            EditorUtility.SetDirty(spell);
            Tome(spell, name);
        }
        EnemyHand("Frost Lance"); EnemyHand("Sweeping Arc"); EnemyHand("Raise the Fallen"); EnemyHand("Chain Lightning");
        MakeNecromancer(raise, lance, sweep, retune);
        AssetDatabase.SaveAssets();
        Debug.Log($"[NewSpellsSetup] Spells, tomes and effects made; catalog {ItemDatabaseAuthoring.SyncCatalog()} items.");
    }

    // ── Spells and tomes ───────────────────────────────────────────────────────

    // The spell at Spells/<name>/<name>.asset: made from the template (its arm animation, timing and sounds) the first time.
    static SpellDefinition Spell(string name, SpellDefinition template, bool retune, System.Action<SpellDefinition> tune)
    {
        string path = $"{Spells}{name}/{name}.asset";
        var spell = Load<SpellDefinition>(path);
        bool made = spell == null;
        if (made)
        {
            spell = Object.Instantiate(template);
            spell.name = name;
            spell.projectile = null; spell.groundArea = false; spell.homing = false;
            AssetDatabase.CreateAsset(spell, path);
        }
        spell.displayName = name;
        if (made || retune) tune(spell);
        EditorUtility.SetDirty(spell);
        return spell;
    }

    // Spells/<name>/<name> Tome.asset, a copy of the Fireball Tome (a Rare tome for the left hand); its save id is made once.
    static void Tome(SpellDefinition spell, string name)
    {
        string path = $"{Spells}{name}/{name} Tome.asset";
        var tome = Load<ItemData>(path);
        if (tome == null)
        {
            AssetDatabase.CopyAsset(Spells + "Fireball/Fireball Tome.asset", path);
            tome = Load<ItemData>(path);
            tome.saveId = System.Guid.NewGuid().ToString("N");
        }
        tome.itemName = name + " Tome";
        tome.spell = spell;
        tome.description = "Hold it in your left hand to cast " + name + ". " + spell.description;
        string iconPath = $"{Spells}{name}/{name} Tome icon.png";
        if (AssetImporter.GetAtPath(iconPath) is TextureImporter importer && (importer.textureType != TextureImporterType.Sprite || importer.filterMode != FilterMode.Point))
        {
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        var icon = AssetDatabase.LoadAllAssetsAtPath(iconPath).OfType<Sprite>().FirstOrDefault();
        if (icon != null) tome.icon = icon;
        EditorUtility.SetDirty(tome);
    }

    // An enemy-sized copy of the hand effect (the player's is sized for their own palm, too small on a body).
    static void EnemyHand(string name)
    {
        string path = $"{Spells}{name}/{name} Enemy Hand.prefab";
        var hand = LoadPrefab($"{name}/{name} Hand.prefab");
        if (hand == null || LoadPrefab($"{name}/{name} Enemy Hand.prefab") != null) return;
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(hand);
        try
        {
            instance.transform.localScale = Vector3.one * 3f;
            var visual = instance.GetComponent<SpellVisual>();
            if (visual != null && visual.glow != null) { visual.glow.range = 3.5f; visual.intensity = 3f; }
            PrefabUtility.SaveAsPrefabAsset(instance, path);
        }
        finally { Object.DestroyImmediate(instance); }
    }

    // ── The Necromancer ────────────────────────────────────────────────────────

    // A copy of the Crypt Acolyte (a caster on the humanoid base) that raises skeletons from their bones, with Frost
    // Lance and Sweeping Arc to keep the player busy meanwhile. In no biome list yet.
    static void MakeNecromancer(SpellDefinition raise, SpellDefinition lance, SpellDefinition sweep, bool retune)
    {
        bool made = AssetDatabase.LoadAssetAtPath<GameObject>(Necromancer) == null;
        if (made) AssetDatabase.CopyAsset("Assets/Game/Characters/Enemies/Crypt/Crypt Acolyte.prefab", Necromancer);
        if (!made && !retune) return;
        var data = AssetDatabase.LoadAllAssetsAtPath(Necromancer).OfType<EnemyData>().FirstOrDefault();
        if (data == null) { Debug.LogError("[NewSpellsSetup] The Necromancer has no EnemyData."); return; }
        data.name = "Necromancer";
        data.characterName = "Necromancer";
        data.description = "Raises fallen skeletons from their bones. Kill it first.";
        data.caster = true;
        data.spells = new List<EnemySpell>
        {
            new EnemySpell { spell = raise, weight = 4f, handEffect = Visual("Raise the Fallen/Raise the Fallen Enemy Hand.prefab"), minRange = 0f, maxRange = 30f, cooldown = 7f, damageMultiplier = 0f },
            new EnemySpell { spell = lance, weight = 1.5f, handEffect = Visual("Frost Lance/Frost Lance Enemy Hand.prefab"), minRange = 3f, maxRange = 14f, cooldown = 4f, damageMultiplier = 1.1f },
            new EnemySpell { spell = sweep, weight = 1f, handEffect = Visual("Sweeping Arc/Sweeping Arc Enemy Hand.prefab"), minRange = 2f, maxRange = 9f, cooldown = 9f, damageMultiplier = 1.4f },
        };
        EditorUtility.SetDirty(data);
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(Necromancer);
        if (go != null) EditorUtility.SetDirty(go);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);
    static GameObject LoadPrefab(string relative) => AssetDatabase.LoadAssetAtPath<GameObject>(Spells + relative);
    static SpellVisual Visual(string relative) => LoadPrefab(relative)?.GetComponent<SpellVisual>();
    static SpellProjectile Projectile(string relative) => LoadPrefab(relative)?.GetComponent<SpellProjectile>();

    // The roof over the room coming off like a big chest's lid with grit falling as it breaks free, landing back on with a
    // stone thud and dust.
    static LidEffects Lid() => new()
    {
        open = Clip("Doors/Drs_Chest_Large_Open_02"), rising = Clip("Ambience/Amb_Dungeon_Cave_Crumble_01"),
        close = Clip("Doors/Drs_Chest_Large_Close_01"), land = Clip("Ambience/Amb_Rock_Drop_Impact_01"), volume = .8f, dust = true,
    };

    static AudioClip Clip(string name)
    {
        foreach (var ext in new[] { ".wav", ".ogg", ".mp3" })
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Library + name + ext);
            if (clip != null) return clip;
        }
        Debug.LogWarning("[NewSpellsSetup] No sound " + name);
        return null;
    }

    static void Copy(string from, string to)
    {
        if (LoadPrefab(to) != null) return;
        AssetDatabase.CopyAsset(Spells + from, Spells + to);
    }

    static void Edit(string relative, System.Action<GameObject> change)
    {
        var root = PrefabUtility.LoadPrefabContents(Spells + relative);
        try { change(root); PrefabUtility.SaveAsPrefabAsset(root, Spells + relative); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // The effect's light: colour, strength (0 switches it off) and reach.
    static void Light(string relative, Color color, float intensity, float range)
    {
        if (LoadPrefab(relative) == null) return;
        Edit(relative, root =>
        {
            var visual = root.GetComponent<SpellVisual>();
            if (visual == null || visual.glow == null) return;
            visual.glow.color = color; visual.glow.range = range;
            visual.intensity = intensity;
            visual.glow.enabled = intensity > 0f;
        });
    }

    static Material Material(string relative, string from, Color tint)
    {
        string path = Spells + relative;
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            AssetDatabase.CopyAsset(from, path);
            material = AssetDatabase.LoadAssetAtPath<Material>(path);
        }
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
        EditorUtility.SetDirty(material);
        return material;
    }

    static void Folder(string path)
    {
        path = path.TrimEnd('/');
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        Folder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
    }
}
