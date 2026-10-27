using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// The Arena, an act 1 campaign place, run from an eval: ArenaSetup.Build(). Safe to run again (it keeps what exists and
// rebuilds the room template and the map place).
//  - Characters/Enemies/Arena/Arena Champion: a copy of the Crypt Soldier wearing the Fantasy Kingdom headsman (a
//    bare-chested, hooded brute no other character wears), rebound onto the enemy rig by bone name, with a steel mace.
//  - Levels/Arena: a board-game level (printed tiles on the table, like the Old Wood): the Arena style (sand floor,
//    sandstone, from Tools/arena_style.py), one floor on the level's base biome (inside Arena.asset, no biome file: the
//    Cellars' enemies and loot with that style, no merchant or hand-built rooms; once it is set up its own content is
//    kept) and the Arena pit: a 13 x 13 board of sand ringed by three tiers of stands with a crowd of pewter
//    miniatures, the owner's box under an awning, banners and braziers, the champion waiting near the far side, the DM's
//    lines and ArenaFight. The level is a landing and the pit (the pit is the last room, its stairs hidden by ArenaFight).
//  - Campaign/Encounters/Act 1/The Arena, appended to the CampaignManager's places, with its place and road on the map.
public static partial class ArenaSetup
{
    const string Dir = "Assets/Game/Levels/Arena";
    const string EnemyPath = "Assets/Game/Characters/Enemies/Arena/Arena Champion.prefab";
    const float C = 2f;   // the level's cell size

    public static string Build()
    {
        var log = new System.Text.StringBuilder();
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Game/Levels", "Arena");
        if (!AssetDatabase.IsValidFolder(Dir + "/Rooms")) AssetDatabase.CreateFolder(Dir, "Rooms");
        var champion = Champion(log);
        var style = Style();
        var room = Room(champion, PitStyle(style));
        var level = Level(room, style, log);
        var encounter = Encounter(level, log);
        MapPlace(encounter, log);
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    // ── The enemy ──

    const string BodySource = "Assets/Synty/PolygonFantasyKingdom/Prefabs/Characters/SM_Chr_Headsman_01.prefab";
    const string BodyArt = "Assets/Game/Art/Synty Pixel/Characters/SM_Chr_Headsman_01/SM_Chr_Headsman_01 PolygonFantasyKingdom_Mat_01_A";

    static GameObject Champion(System.Text.StringBuilder log)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Game/Characters/Enemies/Arena")) AssetDatabase.CreateFolder("Assets/Game/Characters/Enemies", "Arena");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPath) == null)
        {
            NewCharacterWindow.Create("Arena Champion", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Characters/Enemies/Crypt/Crypt Soldier.prefab"), EnemyPath);
            log.Append("made the Arena Champion; ");
        }
        var root = PrefabUtility.LoadPrefabContents(EnemyPath);
        try
        {
            // Every body the rig carries off, and the soldier's helmet.
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.gameObject.SetActive(false);
            foreach (var mesh in root.GetComponentsInChildren<MeshRenderer>(true))
                if (mesh.name.Contains("Helmet")) mesh.gameObject.SetActive(false);
            var model = root.transform.Find("Model");
            var old = model.Find("Champion body");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            // The headsman (already restyled in the pixel look) on the rig's own bones, matched by name.
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(BodySource).GetComponentInChildren<SkinnedMeshRenderer>(true);
            var bones = new Dictionary<string, Transform>();
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (!bones.ContainsKey(t.name)) bones[t.name] = t;
            var body = new GameObject("Champion body");
            body.transform.SetParent(model, false);
            var skin = body.AddComponent<SkinnedMeshRenderer>();
            var mapped = new Transform[source.bones.Length];
            int missing = 0;
            for (int i = 0; i < mapped.Length; i++) if (source.bones[i] == null || !bones.TryGetValue(source.bones[i].name, out mapped[i])) missing++;
            skin.bones = mapped;
            skin.rootBone = bones.TryGetValue(source.rootBone != null ? source.rootBone.name : "Hips", out var rb) ? rb : bones["Hips"];
            skin.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(BodyArt + ".asset");
            skin.sharedMaterials = new[] { AssetDatabase.LoadAssetAtPath<Material>(BodyArt + ".mat") };
            skin.localBounds = source.localBounds;
            skin.updateWhenOffscreen = false;
            if (missing > 0) log.Append($"{missing} bones not found; ");
            foreach (var burst in root.GetComponentsInChildren<DeathBurst>(true)) Object.DestroyImmediate(burst);
            root.transform.localScale = Vector3.one * 1.12f;
            // The headsman's restyle was made for an NPC's size: baked again for the champion's (128 texels a unit).
            int rebaked = MatchTexels(root, "champion");
            if (rebaked > 0) log.Append($"champion body baked to the world's pixel size; ");
            var loadout = root.GetComponent<EnemyWeaponLoadout>();
            if (loadout != null) loadout.weapon = AssetDatabase.LoadAssetAtPath<ItemData>("Assets/Game/Items/Data/Weapons/Steel Mace.asset");
            var data = root.GetComponent<Character>().data as EnemyData;
            data.characterName = "Arena Champion";
            data.creatureKind = CreatureKind.Humanoid;
            data.gore ??= new GoreProfile();
            data.gore.kind = BloodKind.Blood; data.gore.color = new Color(.72f, .05f, .04f); data.gore.burstOnDeath = false;
            void Stat(StatType type, float value)
            {
                int i = data.stats.FindIndex(x => x.stat == type);
                var e = i >= 0 ? data.stats[i] : new StatEntry { stat = type };
                e.baseValue = value;
                if (i >= 0) data.stats[i] = e; else data.stats.Add(e);
            }
            Stat(StatType.MaxHealth, 64); Stat(StatType.AttackDamage, 9); Stat(StatType.Armor, 4);
            data.experience = 40;
            EditorUtility.SetDirty(data);
            PrefabUtility.SaveAsPrefabAsset(root, EnemyPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPath);
    }

    // ── The look ──

    // The Arena style: the textures Tools/arena_style.py paints (grey stone with moss since 2026-10-25), on copies of the
    // Cellar1 style's materials (same shader), point filtered, never scaled to a power of two, each tiled to the Cellar1's
    // 384 texels an architecture tile: the 512 floor at .75 (it repeats every two board cells), the 256 x 192 wall at
    // 1.5 x 2, the 256 trim at 1.5.
    static DungeonRoomStyle Style()
    {
        string dir = Dir + "/Styles";
        var church = AssetDatabase.LoadAssetAtPath<DungeonRoomStyle>("Assets/Game/Levels/Shared/Room styles/Cellar1.asset");
        Material Mat(string name, Material like, string texture, Vector2 tiling)
        {
            string tex = $"{dir}/{texture}.png", path = $"{dir}/{name}.mat";
            var importer = (TextureImporter)AssetImporter.GetAtPath(tex);
            if (importer != null && (importer.filterMode != FilterMode.Point || importer.npotScale != TextureImporterNPOTScale.None))
            {
                importer.filterMode = FilterMode.Point; importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.npotScale = TextureImporterNPOTScale.None; importer.SaveAndReimport();
            }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(like) { name = name }; AssetDatabase.CreateAsset(m, path); }
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tex));
            m.SetTextureScale("_BaseMap", tiling);
            m.color = Color.white;
            EditorUtility.SetDirty(m);
            return m;
        }
        string stylePath = dir + "/Arena.asset";
        var style = AssetDatabase.LoadAssetAtPath<DungeonRoomStyle>(stylePath);
        if (style == null) { style = ScriptableObject.CreateInstance<DungeonRoomStyle>(); AssetDatabase.CreateAsset(style, stylePath); }
        style.floor = Mat("Arena floor", church.floor, "Arena floor", new Vector2(.75f, .75f));
        Mat("Arena sand", church.floor, "Arena sand", new Vector2(.75f, .75f));
        style.bottomWall = style.upperWall = Mat("Arena wall", church.bottomWall, "Arena wall", new Vector2(1.5f, 2f));
        style.trim = style.ceiling = Mat("Arena trim", church.trim, "Arena trim", new Vector2(1.5f, 1.5f));
        style.wood = church.wood; style.metal = church.metal;
        style.wallVariants = new DungeonSurfaceVariant[0]; style.floorVariants = new DungeonSurfaceVariant[0];
        style.wallDecor = new DungeonWeightedPrefab[0];
        EditorUtility.SetDirty(style);
        return style;
    }

    // The pit's style: the Arena style with the sand floor (the landing and the way up are stone paving).
    static DungeonRoomStyle PitStyle(DungeonRoomStyle arena)
    {
        string path = Dir + "/Styles/Arena pit.asset";
        var pit = AssetDatabase.LoadAssetAtPath<DungeonRoomStyle>(path);
        if (pit == null) { pit = Object.Instantiate(arena); AssetDatabase.CreateAsset(pit, path); }
        EditorUtility.CopySerialized(arena, pit);
        pit.name = "Arena pit";
        pit.floor = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/Styles/Arena sand.mat");
        EditorUtility.SetDirty(pit);
        return pit;
    }

    // ── The biome ──

    // The level's base biome is the arena's only biome. Still with stages (a fresh copy of Dungeon1, or a level from before
    // base biomes): the content of its old biome file in the Arena folder, else the Cellars', folded into the base.
    static DungeonBiome Biome(TableLevelData level, DungeonRoomStyle style, System.Text.StringBuilder log)
    {
        var b = level.MakeBaseBiome();
        if (level.HasStages)
        {
            DungeonBiome old = null;
            foreach (var s in level.stages) if (s != null && s.biome != null) { old = s.biome; break; }
            var from = old != null && AssetDatabase.GetAssetPath(old).StartsWith(Dir + "/") ? old : AssetDatabase.LoadAssetAtPath<DungeonBiome>("Assets/Game/Levels/Shared/Biomes/Cellars1.asset");
            b.FoldIn(from);
            log.Append($"the base biome takes {(from != null ? from.name : "nothing")}'s enemies, loot and look; ");
        }
        b.displayName = "The Arena";
        // Its own words (the copy came with the Cellars'): the pit's template has the Dungeon Master's lines on the way in.
        b.entryMessage = "You step into the arena.";
        b.entryLines = new DungeonMaster.Line[0];
        b.merchantChance = 0f;
        b.roomTemplates = new DungeonBiome.RoomTemplateChoice[0];
        b.templateChance = 0f;
        b.guardian.enabled = false;
        b.roomStyles = new[] { new DungeonStyleChoice { style = style, weight = 1f } };
        b.destructiblesPerRoom = Vector2Int.zero;
        // A board on the table, lit and edged as the Old Wood's was (the Old Wood is gone since 2026-10-27).
        b.boardLightColor = new Color(1f, .86f, .68f); b.boardLightIntensity = 2600f * .6f;   // pale sand takes less light than a forest floor
        b.boardGridMaterial = null; b.boardEdgeMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Levels/Shared/Board/Board edge.mat");
        EditorUtility.SetDirty(b);
        return b;
    }

    // ── The pit ──

    // 13 x 13 cells, the way in at the south. Three tiers of sandstone stands step up round a 7 x 7 sand floor (open
    // only at the way in), a crowd of pewter miniatures on the upper two, the owner's box on the north stands under an
    // awning with a throne and banners, braziers at the sand's corners, weapon racks by the way in, and the champion
    // waiting near the far side, facing the way in.
    static GameObject Room(GameObject champion, DungeonRoomStyle style)
    {
        const int n = 13, mid = 6;
        Vector3 Cell(float x, float y) => new((x - mid) * C, 0f, (y - mid) * C);
        var root = new GameObject("Arena pit");
        try
        {
            var t = root.AddComponent<DungeonRoomTemplate>();
            var rows = new string[n];
            for (int y = 0; y < n; y++)
            {
                var row = new char[n];
                for (int x = 0; x < n; x++) row[x] = x == mid && y == 0 ? '+' : x == mid && y == mid ? '*' : '#';
                rows[y] = new string(row);
            }
            t.paintFootprint = true; t.width = n; t.height = n; t.rows = rows; t.allowRotation = false;
            t.style = style;
            t.replaceGeneratedProps = true; t.replaceGeneratedEnemies = true; t.socketEnemiesOnly = true;
            t.replaceGeneratedTorches = true;
            t.roomEvent = DungeonRoomEvent.None;
            t.enterLines = new[]
            {
                "The arena. One of you walks out of here. I'd like it to be you.",
                "Here's the arena. The crowd paid to see blood. Theirs or yours, they don't mind.",
                "Sand, stands and one very bored champion. Go and wake him up.",
            };
            t.enterChance = 1f; t.enterInPerson = true;
            root.AddComponent<ArenaFight>();

            // The stands: rings 6 (outer, tallest), 5 and 4 of cells round the centre, cut at the way in (south).
            float[] tier = { 0f, 0f, 0f, 0f, .55f, 1.1f, 1.65f };
            void Block(string name, float x0, float x1, float y0, float y1, float h)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = Cell((x0 + x1) * .5f, (y0 + y1) * .5f);
                var b = go.AddComponent<DungeonKitBlock>();
                b.size = new Vector3((x1 - x0 + 1) * C, h, (y1 - y0 + 1) * C);
                b.sides = DungeonKitBlock.Surface.Wall; b.top = DungeonKitBlock.Surface.Trim;
                b.UpdatePreview();
            }
            for (int k = 4; k <= 6; k++)
            {
                int a = mid - k, z = mid + k;
                Block($"Stands {k} south west", a, mid - 1, a, a, tier[k]);
                Block($"Stands {k} south east", mid + 1, z, a, a, tier[k]);
                Block($"Stands {k} north", a, z, z, z, tier[k]);
                Block($"Stands {k} west", a, a, a + 1, z - 1, tier[k]);
                Block($"Stands {k} east", z, z, a + 1, z - 1, tier[k]);
            }
            // The owner's box: a raised stone floor on the north stands.
            Block("Owner's box", mid - 2, mid + 2, n - 2, n - 1, 2.3f);
            // Its front and back would lie in the same planes as the north stands' faces (they flickered): 2 cm deeper each way.
            root.transform.Find("Owner's box").GetComponent<DungeonKitBlock>().size += new Vector3(0f, 0f, .04f);

            GameObject Load(string p) => AssetDatabase.LoadAssetAtPath<GameObject>(p);
            Transform Put(GameObject prefab, Vector3 pos, float yaw = 0f, float scale = 1f)
            {
                if (prefab == null) return null;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                go.transform.localPosition = pos; go.transform.localEulerAngles = new Vector3(0, yaw, 0);
                if (scale != 1f) go.transform.localScale = Vector3.one * scale;
                return go.transform;
            }
            Vector3 Up(float h) => Vector3.up * h;

            // The crowd: the classes' pewter miniatures at a person's height, on tiers 5 and 6, turned to the sand.
            var minis = new List<GameObject>();
            foreach (var g in AssetDatabase.FindAssets("t:AdventurerClass", new[] { "Assets/Game/Progression/Data/Classes" }))
            {
                var c = AssetDatabase.LoadAssetAtPath<AdventurerClass>(AssetDatabase.GUIDToAssetPath(g));
                if (c != null && c.miniature != null) minis.Add(c.miniature);
            }
            float miniScale = 1f;
            if (minis.Count > 0)
            {
                var probe = (GameObject)PrefabUtility.InstantiatePrefab(minis[0]);
                var bounds = new Bounds(probe.transform.position, Vector3.zero);
                foreach (var r in probe.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
                miniScale = bounds.size.y > .01f ? 1.65f / bounds.size.y * probe.transform.localScale.x : 1f;
                Object.DestroyImmediate(probe);
            }
            var rnd = new System.Random(5);
            int placed = 0;
            for (int k = 5; k <= 6; k++)
            {
                int a = mid - k, z = mid + k;
                var spots = new List<Vector2>();
                for (int x = a; x <= z; x += 2) { if (x != mid) spots.Add(new Vector2(x, a)); if (x < mid - 2 || x > mid + 2) spots.Add(new Vector2(x, z)); }
                for (int y = a + 2; y <= z - 2; y += 2) { spots.Add(new Vector2(a, y)); spots.Add(new Vector2(z, y)); }
                foreach (var spot in spots)
                {
                    if (rnd.NextDouble() < .25 || minis.Count == 0) continue;
                    var toMiddle = new Vector3(mid - spot.x, 0, mid - spot.y);
                    float yaw = Mathf.Atan2(toMiddle.x, toMiddle.z) * Mathf.Rad2Deg + (float)(rnd.NextDouble() - .5) * 30f;
                    var jitter = new Vector3((float)(rnd.NextDouble() - .5) * .5f, 0, (float)(rnd.NextDouble() - .5) * .5f);
                    var fig = Put(minis[rnd.Next(minis.Count)], Cell(spot.x, spot.y) + jitter + Up(tier[k]), yaw, miniScale);
                    if (fig != null)
                    {
                        fig.name = "Spectator";
                        foreach (var anim in fig.GetComponentsInChildren<Animator>()) anim.enabled = false;
                        foreach (var col in fig.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
                        // Miniatures are painted for their tiny size: baked again for a spectator's (128 texels a unit).
                        MatchTexels(fig.gameObject, "spectator");
                        placed++;
                    }
                }
            }

            // The owner's box: a throne under an awning, banners either side.
            Put(Restyled("Assets/Synty/PolygonFantasyKingdom/Prefabs/Props/Furniture/SM_Prop_Throne_01.prefab", "Arena throne", 1f), Cell(mid, n - 1.6f) + Up(2.3f), 180f);
            Put(Restyled("Assets/Synty/PolygonFantasyKingdom/Prefabs/Props/SM_Prop_Awning_01.prefab", "Arena awning", 1f), Cell(mid, n - 1.5f) + Up(2.3f), 180f);
            var banner = Restyled("Assets/Synty/PolygonFantasyKingdom/Prefabs/Props/Banners/SM_Prop_Banner_01.prefab", "Arena banner", 1f);
            foreach (var x in new[] { mid - 2.6f, mid + 2.6f })
                Put(banner, Cell(x, n - 1.6f) + Up(2.3f + 2.2f), 180f);
            // Banners on the stands' corners, facing in.
            foreach (var (x, y) in new[] { (0.5f, 0.5f), (n - 1.5f, 0.5f), (0.5f, n - 1.5f), (n - 1.5f, n - 1.5f) })
            {
                var toMiddle = new Vector3(mid - x, 0, mid - y);
                Put(banner, Cell(x, y) + Up(tier[6] + 2.2f), Mathf.Atan2(toMiddle.x, toMiddle.z) * Mathf.Rad2Deg);
            }
            // Braziers at the sand's corners, weapon racks either side of the way in.
            var brazier = Load("Assets/Game/Levels/Shared/Props/Furnishing/Brazier.prefab");
            foreach (var (x, y) in new[] { (3.2f, 3.2f), (n - 4.2f, 3.2f), (3.2f, n - 4.2f), (n - 4.2f, n - 4.2f) }) Put(brazier, Cell(x, y));
            var rack = Restyled("Assets/Synty/PolygonFantasyKingdom/Prefabs/Props/BattleGround/SM_Prop_Weapon_Rack_01.prefab", "Arena weapon rack", 1f);
            Put(rack, Cell(mid - 2.2f, 3.3f), 0f); Put(rack, Cell(mid + 2.2f, 3.3f), 0f);

            // The champion, near the far side of the sand, facing the way in.
            var socket = new GameObject("Champion socket");
            socket.transform.SetParent(root.transform, false);
            socket.transform.localPosition = Cell(mid, mid + 1.6f); socket.transform.localEulerAngles = new Vector3(0, 180f, 0);
            var s = socket.AddComponent<DungeonSocket>(); s.type = DungeonSocketType.Enemy; s.chance = 1f; s.overridePrefab = champion;
            // Every prop at the world's pixel size (the weapon racks, throne, banners and awning: their Synty sizes vary).
            MatchTexels(root, "arena");
            string path = Dir + "/Rooms/Scripted/Arena pit.prefab";
            Debug.Log($"[Arena] pit built: {placed} spectators at scale {miniScale:F2}");
            return PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { Object.DestroyImmediate(root); }
    }

    // ── The level ──

    static TableLevelData Level(GameObject room, DungeonRoomStyle style, System.Text.StringBuilder log)
    {
        string path = Dir + "/Arena.asset";
        if (AssetDatabase.LoadAssetAtPath<TableLevelData>(path) == null) { AssetDatabase.CopyAsset("Assets/Game/Levels/Dungeon1/Dungeon1.asset", path); log.Append("copied Dungeon1 as the Arena level; "); }
        var level = AssetDatabase.LoadAssetAtPath<TableLevelData>(path);
        level.displayName = "The Arena";
        level.description = "One fight, in the sand.";
        level.layoutMode = DungeonLayoutMode.Authored;
        level.kind = TableLevelKind.Boardgame;
        Biome(level, style, log);
        // North at the top: the pit (1, the last room, so its stairs are hidden by ArenaFight), the landing (0) where the
        // player starts. At least 14 cells each way.
        var rows = new[]
        {
            "#################",
            "##1111111111111##",
            "##1111111111111##",
            "##1111111111111##",
            "##1111111111111##",
            "##1111111111111##",
            "##1111111111111##",
            "##1111111111111##",
            "##1111111111111##",
            "##1111111111111##",
            "##1111111111111##",
            "##1111111111111##",
            "##1111111111111##",
            "##1111111111111##",
            "########+########",
            "########.########",
            "######00000######",
            "######00000######",
            "######00000######",
            "######00000######",
            "######00000######",
            "#################",
        };
        // One map template holds both rooms (Arena map.prefab): the landing, furnished by the generator, and the pit.
        level.mapTemplate = MapTemplate(rows, room, level);
        level.authoredMap = ""; level.authoredRooms = new TableLevelData.AuthoredRoom[0];
        level.authoredContent = "";
        level.SyncMapSize();
        level.authoredMarkers = new TableLevelData.AuthoredMarker[0];
        // One floor on the base biome, no boss (the champion is the pit's own).
        level.stages = new TableLevelData.BiomeStage[0];
        level.floors = 1; level.bossOnLastFloor = false;
        EditorUtility.SetDirty(level);
        // The catalog (a saved run finds its level here by name) and the toolbar's Dungeon list.
        var catalog = AssetDatabase.LoadAssetAtPath<TableLevelCatalog>("Assets/Game/Levels/Resources/TableLevels.asset");
        if (System.Array.IndexOf(catalog.levels, level) < 0) { var l = new List<TableLevelData>(catalog.levels) { level }; catalog.levels = l.ToArray(); EditorUtility.SetDirty(catalog); }
        var world = WorldManager.Instance;
        if (world != null && System.Array.IndexOf(world.debugDungeons, level) < 0)
        {
            var l = new List<TableLevelData>(world.debugDungeons) { level }; world.debugDungeons = l.ToArray(); EditorUtility.SetDirty(world);
        }
        return level;
    }

    // ── The campaign place ──

    static CampaignEncounter Encounter(TableLevelData level, System.Text.StringBuilder log)
    {
        string path = "Assets/Game/Campaign/Encounters/Act 1/The Arena.asset";
        var e = AssetDatabase.LoadAssetAtPath<CampaignEncounter>(path);
        if (e == null) { e = ScriptableObject.CreateInstance<CampaignEncounter>(); AssetDatabase.CreateAsset(e, path); log.Append("made the Arena encounter; "); }
        e.id = "arena"; e.displayName = "The Arena"; e.kind = CampaignEncounterKind.Level; e.level = level; e.act = 1;
        e.description = "A ring of stone and sand. Someone is always waiting in it.";
        e.pickedLine = new DungeonMaster.Line { text = "The arena, then. Try not to bore him.", inPerson = true };
        EditorUtility.SetDirty(e);
        // Appended to the places (their order is saved: append only), on the prefab and in the scene.
        const string managerPath = "Assets/Game/Campaign/Prefabs/CampaignManager.prefab";
        var root = PrefabUtility.LoadPrefabContents(managerPath);
        var cm = root.GetComponent<CampaignManager>();
        if (System.Array.IndexOf(cm.encounters, e) < 0) { var l = new List<CampaignEncounter>(cm.encounters) { e }; cm.encounters = l.ToArray(); }
        PrefabUtility.SaveAsPrefabAsset(root, managerPath);
        PrefabUtility.UnloadPrefabContents(root);
        return e;
    }

    // Its place on the map near the Crypt: a low stone ring round a sand floor on its base,
    // banners at the way in, a glow over it, and a road to the Crypt.
    static void MapPlace(CampaignEncounter e, System.Text.StringBuilder log)
    {
        var root = PrefabUtility.LoadPrefabContents(CampaignMapBuilder.PrefabPath);
        try
        {
            var map = root.GetComponent<CampaignMap>();
            var old = map.points.Find("The Arena");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            foreach (Transform p in map.paths) if (p.name == "Road The Arena - The Old Wood") { Object.DestroyImmediate(p.gameObject); break; }
            var go = new GameObject("The Arena");
            go.transform.SetParent(map.points, false);
            go.transform.localPosition = new Vector3(-20.5f, 0f, 30.5f);
            var point = go.AddComponent<CampaignPoint>();
            point.radius = 2.6f; point.encounter = e;
            GameObject Load(string p) => AssetDatabase.LoadAssetAtPath<GameObject>(p);
            void Put(string p, Vector3 pos, float yaw, float scale)
            {
                var prefab = Load(p); if (prefab == null) { log.Append("missing " + p + "; "); return; }
                var g = (GameObject)PrefabUtility.InstantiatePrefab(prefab, go.transform);
                g.transform.localPosition = pos; g.transform.localEulerAngles = new Vector3(0, yaw, 0); g.transform.localScale = Vector3.one * scale;
                foreach (var c in g.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            }
            // A low ring wall of stone panels with a gap toward the road, a sand floor, banners either side of the gap.
            const int pieces = 12; const float ring = 1.95f;
            Vector3 At(int i) { float a = (i + .5f) / pieces * Mathf.PI * 2; return new Vector3(Mathf.Cos(a) * ring, 0, Mathf.Sin(a) * ring); }
            for (int i = 0; i < pieces; i++)
            {
                if (i == 2) continue;   // the way in, facing the road (north, +z)
                Vector3 p0 = At(i), p1 = At(i + 1), dir = (p1 - p0).normalized;
                float chord = Vector3.Distance(p0, p1);
                var prefab = Load("Assets/Synty/PolygonFantasyKingdom/Prefabs/Castle/SM_Bld_Castle_Wall_Panel_Stone_01.prefab");
                if (prefab == null) continue;
                var g = (GameObject)PrefabUtility.InstantiatePrefab(prefab, go.transform);
                // The panel runs from its pivot along -x: turn that along the ring.
                // Scaled evenly (a stretched model would stretch its pixels too); neighbouring panels overlap a little.
                float panel = Mathf.Max(.3f, chord * 1.1f / 5f);
                g.transform.localScale = Vector3.one * panel;
                g.transform.localPosition = p0 + Vector3.up * (.86f * panel);   // the panel's mesh hangs .86 under its pivot
                g.transform.localEulerAngles = new Vector3(0, Mathf.Atan2(dir.z, -dir.x) * Mathf.Rad2Deg, 0);
                foreach (var c in g.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            }
            Put("Assets/Synty/PolygonDarkFantasy/Prefabs/Environment/SM_Env_Ground_Dirt_Round_01.prefab", new Vector3(0, .02f, 0), 0f, .46f);
            Vector3 gapA = At(2), gapB = At(3);
            Put("Assets/Synty/PolygonDarkFantasy/Prefabs/Base/SM_Bld_Base_Pillar_01.prefab", gapA, 0f, .34f);
            Put("Assets/Synty/PolygonDarkFantasy/Prefabs/Base/SM_Bld_Base_Pillar_01.prefab", gapB, 0f, .34f);
            Put("Assets/Synty/PolygonFantasyKingdom/Prefabs/Props/Banners/SM_Prop_Banner_01.prefab", gapA + Vector3.up * 1.05f + (gapA.normalized * .06f), Mathf.Atan2(gapA.x, gapA.z) * Mathf.Rad2Deg, .4f);
            Put("Assets/Synty/PolygonFantasyKingdom/Prefabs/Props/Banners/SM_Prop_Banner_02.prefab", gapB + Vector3.up * 1.05f + (gapB.normalized * .06f), Mathf.Atan2(gapB.x, gapB.z) * Mathf.Rad2Deg, .4f);
            // The same glow the other places have.
            var crypt = map.points.Find("The Crypt");
            var glow = crypt != null ? crypt.GetComponent<CampaignPoint>().glow : null;
            if (glow != null)
            {
                var light = Object.Instantiate(glow.gameObject, go.transform);
                light.name = glow.name; light.transform.localPosition = new Vector3(0, 9f, 0);
                point.glow = light.GetComponent<Light>();
            }
            point.enabled = false; point.enabled = true;   // snaps to the ground and builds its base
            point.EnsurePickCollider();
            var cryptPoint = map.points.Find("The Crypt")?.GetComponent<CampaignPoint>();
            if (cryptPoint != null && map.paths.Find("Road The Arena - The Crypt") == null)
            {
                var road = new GameObject("Road The Arena - The Crypt");
                road.transform.SetParent(map.paths, false);
                var path = road.AddComponent<CampaignPath>();
                path.from = point; path.to = cryptPoint; path.width = 1.1f; path.wobble = .3f; path.bend = 0f;
                path.Rebuild();
            }
            map.ClearScatterUnderRoads();
            PrefabUtility.SaveAsPrefabAsset(root, CampaignMapBuilder.PrefabPath);
            log.Append("placed the Arena on the map");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        log.Append("; " + CampaignMapBuilder.PixelProps());
    }
}
