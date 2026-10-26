using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

// Sets up the tavern (the rest room every dungeon floor has), from an eval: TavernSetup.Build(). Safe to run again: what
// exists is kept (delete a prefab to make it afresh) and the settings are set again. Makes:
// - the dishes (Levels/Resources/Tavern dishes: Hearty stew, Herb broth);
// - the Tavern door (Levels/Shared/Props/Tavern/Tavern door.prefab): a variant of the dungeon door with a shield
//   hung on its outside face and a block that keeps enemies out once the floor is in play (DoorwayBlock);
// - the tavern room (Levels/Shared/Rooms/Scripted/Tavern.prefab, a copy of the Merchant shop): the bar and the keeper,
//   a fire pit to rest by (TavernHearth), tables and stools, its music (RoomMusic), the Dungeon Master's off-the-record
//   lines (TavernRoom) and its door on every way in (DungeonRoomTemplate.wayInDoor);
// - the keeper: the Dungeon merchant prefab with a TavernKeeper (his dishes, the rumour price) and its name;
// - every walled dungeon level's shop room set to the tavern, and every floor of every level a merchant (chance 1);
// - the Arena as the first rumour (CampaignEncounter.rumoured);
// - the tavern's tabs on the shop screen in Room.unity (TavernTabsUI);
// - a tavern behind a door in the Debug arena.
// Everything it makes is edited by hand after (in the Asset Studio: Rooms, Props, Items > Dish, Campaign).
public static class TavernSetup
{
    const string PropDir = "Assets/Game/Levels/Shared/Props/Tavern";
    const string RoomPath = "Assets/Game/Levels/Shared/Rooms/Scripted/Tavern.prefab";
    const string ShopPath = "Assets/Game/Levels/Shared/Rooms/Scripted/Merchant shop.prefab";
    const string DoorPath = PropDir + "/Tavern door.prefab";
    const string DungeonDoor = "Assets/Game/Levels/Shared/Doors/Door 128/Dungeon door 128.prefab";   // the door every walled level uses
    const string DishDir = "Assets/Game/Levels/Resources/Tavern dishes";
    const string Music = "Assets/Game/Audio/Music/Quirky Song 2.mp3";
    const float PropTexels = 128f;

    public static string Build()
    {
        var log = new System.Text.StringBuilder();
        var dishes = Dishes(log);
        var door = Door(log);
        var room = Room(door, log);
        Keeper(dishes, log);
        Levels(room, log);
        Arena(log);
        Ui(log);
        DebugArena(room, log);
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    // ── Dishes ──

    static TavernDish[] Dishes(System.Text.StringBuilder log)
    {
        Folder(DishDir);
        var stew = Dish("Hearty stew", "stew", 30, "+1 Strength, Dexterity, Constitution and Intelligence", "Thick, brown and still bubbling.", 1, null, "Assets/Game/Items/Icons/Generated/Cooked meat.png");
        var broth = Dish("Herb broth", "broth", 30, "+25% mana regeneration", "Green, bitter and good for you.", 0,
            new StatModifierEntry { stat = StatType.ManaRegen, value = .25f, type = ModifierType.PercentAdd }, "Assets/Game/Items/Icons/Generated/Mana Potion.png");
        log.Append("dishes; ");
        return new[] { stew, broth };
    }

    static TavernDish Dish(string name, string id, int price, string effect, string flavour, int attributes, StatModifierEntry modifier, string icon)
    {
        string path = $"{DishDir}/{name}.asset";
        var d = AssetDatabase.LoadAssetAtPath<TavernDish>(path);
        if (d != null) return d;
        d = ScriptableObject.CreateInstance<TavernDish>();
        d.id = id; d.displayName = name; d.price = price; d.effectText = effect; d.flavour = flavour; d.attributes = attributes;
        if (modifier != null) d.modifiers.Add(modifier);
        d.icon = AssetDatabase.LoadAllAssetsAtPath(icon).OfType<Sprite>().FirstOrDefault();
        AssetDatabase.CreateAsset(d, path);
        return d;
    }

    // ── The door ──

    // The door alone (made again when it was made from an older door), from an eval.
    public static string RemakeDoor() { var log = new System.Text.StringBuilder(); Door(log); return log.ToString(); }

    static GameObject Door(System.Text.StringBuilder log)
    {
        Folder(PropDir);
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(DoorPath);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(DungeonDoor);
        // Kept while it is made from the current dungeon door; made again in place (same file, so the room's
        // reference stays) when it was made from another one.
        if (existing != null && PrefabUtility.GetCorrespondingObjectFromSource(existing) == source) { log.Append("door kept; "); return existing; }
        var door = (GameObject)PrefabUtility.InstantiatePrefab(source);
        door.name = "Tavern door";
        try
        {
            // The leaf: a mesh of its own on the old door, a holder of its planks and iron on the 128 door.
            var leaf = door.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Door leaf");
            var shieldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Items/Prefabs/Shields/Bronze Shield.prefab");
            if (leaf != null && shieldPrefab != null)
            {
                var shield = (GameObject)PrefabUtility.InstantiatePrefab(shieldPrefab, leaf);
                PrefabUtility.UnpackPrefabInstance(shield, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
                shield.name = "Shield trophy";
                foreach (var c in shield.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                // The leaf's bounds in its own space, from every mesh under it.
                var lb = new Bounds(); bool any = false;
                foreach (var f in leaf.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (f.sharedMesh == null || f.transform.IsChildOf(shield.transform)) continue;
                    var mb = f.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = leaf.InverseTransformPoint(f.transform.TransformPoint(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                        if (!any) { lb = new Bounds(corner, Vector3.zero); any = true; } else lb.Encapsulate(corner);
                    }
                }
                // On the outside face (+z, the corridor side), in the middle of the upper panel, between the middle and top bands.
                shield.transform.localRotation = Quaternion.identity;
                shield.transform.localPosition = new Vector3(lb.center.x, 2.0f, lb.max.z + .06f);
            }
            var block = new GameObject("Keeps enemies out");
            block.transform.SetParent(door.transform, false);
            var obstacle = block.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box; obstacle.size = new Vector3(2.2f, 2.6f, .6f); obstacle.center = new Vector3(0, 1.3f, 0);
            obstacle.carving = true; obstacle.carveOnlyStationary = false; obstacle.enabled = false;
            block.AddComponent<DoorwayBlock>().obstacle = obstacle;
            PrefabUtility.SaveAsPrefabAsset(door, DoorPath);
            log.Append("door made; ");
        }
        finally { Object.DestroyImmediate(door); }
        return AssetDatabase.LoadAssetAtPath<GameObject>(DoorPath);
    }

    // ── The room ──

    static GameObject Room(GameObject door, System.Text.StringBuilder log)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(RoomPath) == null) AssetDatabase.CopyAsset(ShopPath, RoomPath);
        var root = PrefabUtility.LoadPrefabContents(RoomPath);
        try
        {
            root.name = "Tavern";
            var template = root.GetComponent<DungeonRoomTemplate>();
            template.wayInDoor = door;
            // A fixed room (14 x 10 m) with one way in, in the middle of its front (south) wall, so its walls are known
            // and can be furnished; the generator turns it to face its corridor.
            template.paintFootprint = true; template.allowRotation = true;
            template.width = 7; template.height = 5;
            template.rows = new[] { "###+###", "#######", "###*###", "#######", "#######" };
            template.enterLines = new string[0];
            template.enterInPerson = true;
            if (root.GetComponent<TavernRoom>() == null) root.AddComponent<TavernRoom>();
            var music = root.GetComponent<RoomMusic>() ?? root.AddComponent<RoomMusic>();
            music.music = AssetDatabase.LoadAssetAtPath<AudioClip>(Music); music.volume = .7f;
            var mood = root.GetComponent<DungeonRoomMood>() ?? root.AddComponent<DungeonRoomMood>();
            mood.kind = DungeonRoomLight.Candlelit;
            // The shop's weapon rack goes; the bread crate moves aside for the tables.
            foreach (var t in root.GetComponentsInChildren<Transform>(true).ToArray())
            {
                if (t == null) continue;
                if (t.name == "SM_Prop_Weapon_Rack_01") Object.DestroyImmediate(t.gameObject);
                else if (t.name == "SM_Prop_Bread_Crate_01") t.localPosition = new Vector3(-4.3f, 0, .8f);
            }
            if (root.transform.Find("Hearth") == null) Hearth(root.transform);
            if (root.transform.Find("Tables") == null) Tables(root.transform);
            PrefabUtility.SaveAsPrefabAsset(root, RoomPath);
            log.Append("room; ");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return AssetDatabase.LoadAssetAtPath<GameObject>(RoomPath);
    }

    // The fire pit with its spit: rest by it once (TavernHearth), a warm flickering light and flames.
    static void Hearth(Transform room)
    {
        var hearth = new GameObject("Hearth");
        hearth.transform.SetParent(room, false);
        hearth.transform.localPosition = new Vector3(-2.8f, 0, -2.4f);
        var pit = Restyled("Assets/Synty/PolygonDarkFantasy/Prefabs/Props/SM_Prop_Fire_Pit_01_Rotisserie_01.prefab", "Fire pit");
        if (pit != null) { var p = (GameObject)PrefabUtility.InstantiatePrefab(pit, hearth.transform); p.AddComponent<DungeonTemplatePiece>(); }
        var light = new GameObject("Fire light").AddComponent<Light>();
        light.transform.SetParent(hearth.transform, false); light.transform.localPosition = new Vector3(0, .9f, 0);
        light.type = LightType.Point; light.color = new Color(1f, .62f, .32f); light.intensity = 6f; light.range = 7f; light.shadows = LightShadows.Soft;
        light.gameObject.AddComponent<TorchFlicker>();
        var flames = new List<ParticleSystem>();
        var flame = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Synty/PolygonDarkFantasy/Prefabs/FX/FX_Candle_Flame_01.prefab");
        if (flame != null)
            foreach (var (x, z, s) in new[] { (0f, 0f, 9f), (.18f, .1f, 7f), (-.16f, -.08f, 7f) })
            {
                var f = (GameObject)PrefabUtility.InstantiatePrefab(flame, hearth.transform);
                f.transform.localPosition = new Vector3(x, .25f, z); f.transform.localScale = Vector3.one * s;
                flames.Add(f.GetComponent<ParticleSystem>());
            }
        var rest = hearth.AddComponent<TavernHearth>();
        rest.lights = new[] { light }; rest.flames = flames.Where(f => f != null).ToArray();
        var interaction = new GameObject("Interaction");
        interaction.transform.SetParent(hearth.transform, false);
        var box = interaction.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = new Vector3(1.8f, 1.2f, 1.8f); box.center = new Vector3(0, .6f, 0);
        interaction.AddComponent<InteractableTrigger>();
        // Two stools drawn up to the fire.
        var stool = Restyled("Assets/Synty/PolygonDarkFantasy/Prefabs/Props/SM_Prop_Stool_Bar_01.prefab", "Stool");
        foreach (var (x, z) in new[] { (-1.25f, .35f), (.9f, -1.0f) }) Place(stool, hearth.transform, new Vector3(x, 0, z), 0);
    }

    // Two tables with stools and mugs, in front of the bar.
    static void Tables(Transform room)
    {
        var tables = new GameObject("Tables");
        tables.transform.SetParent(room, false);
        var table = Restyled("Assets/Synty/PolygonDarkFantasy/Prefabs/Props/SM_Prop_Table_01.prefab", "Table");
        var stool = Restyled("Assets/Synty/PolygonDarkFantasy/Prefabs/Props/SM_Prop_Stool_Bar_01.prefab", "Stool");
        var mug = Restyled("Assets/Synty/PolygonFantasyKingdom/Prefabs/Items/SM_Item_Mug_Tankard_01.prefab", "Mug");
        foreach (var (x, z, turn) in new[] { (2.6f, -2.6f, 0f), (3.2f, 1.6f, 90f) })
        {
            var t = Place(table, tables.transform, new Vector3(x, 0, z), turn);
            if (t == null) continue;
            var top = Top(t);
            foreach (var (dx, dz) in new[] { (-1.1f, 0f), (1.1f, 0f) }) Place(stool, tables.transform, new Vector3(x, 0, z) + Quaternion.Euler(0, turn, 0) * new Vector3(dx, 0, dz), 0);
            foreach (var (dx, dz) in new[] { (-.3f, .15f), (.35f, -.1f) }) Place(mug, tables.transform, new Vector3(x, 0, z) + Quaternion.Euler(0, turn, 0) * new Vector3(dx, 0, dz) + Vector3.up * top, 30 * dx);
        }
    }

    static float Top(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return .8f;
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        return b.max.y - go.transform.position.y;
    }

    static GameObject Place(GameObject prefab, Transform parent, Vector3 at, float turn)
    {
        if (prefab == null) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.localPosition = at; go.transform.localRotation = Quaternion.Euler(0, turn, 0);
        if (go.GetComponent<DungeonTemplatePiece>() == null) go.AddComponent<DungeonTemplatePiece>();
        return go;
    }

    // A pixel copy of a Synty prefab at the dungeon props' 128 texels a metre (PixelRestyle), saved in the tavern's folder.
    static GameObject Restyled(string syntyPath, string name)
    {
        string path = $"{PropDir}/{name}.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;
        var synty = AssetDatabase.LoadAssetAtPath<GameObject>(syntyPath);
        if (synty == null) { Debug.LogWarning("[Tavern] no Synty prefab " + syntyPath); return null; }
        var copy = (GameObject)PrefabUtility.InstantiatePrefab(synty);
        PrefabUtility.UnpackPrefabInstance(copy, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        copy.name = name;
        try
        {
            foreach (var filter in copy.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (filter.sharedMesh == null || renderer == null) continue;
                string meshName = filter.sharedMesh.name;
                string dir = $"{PropDir}/Pixel/{meshName}";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{dir}/{meshName}.asset");
                var mat = AssetDatabase.LoadAssetAtPath<Material>($"{dir}/{meshName}.mat");
                if (mesh == null || mat == null) (mesh, mat) = PixelRestyle.Build(filter.sharedMesh, renderer.sharedMaterials, "Props", meshName, PropTexels * filter.transform.lossyScale.x, dir);
                if (mesh == null) continue;
                filter.sharedMesh = mesh;
                renderer.sharedMaterials = new[] { mat };
                // Walkers go round it (the NavMesh is built from colliders).
                var col = filter.GetComponent<Collider>();
                if (col == null) { var mc = filter.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mesh; }
                else if (col is MeshCollider existingMc) existingMc.sharedMesh = mesh;
            }
            return PrefabUtility.SaveAsPrefabAsset(copy, path);
        }
        finally { Object.DestroyImmediate(copy); }
    }

    // ── The keeper ──

    static void Keeper(TavernDish[] dishes, System.Text.StringBuilder log)
    {
        var defaults = AssetDatabase.LoadAssetAtPath<TableLevelDefaults>("Assets/Game/Levels/Resources/Table level defaults.asset");
        var prefab = defaults != null ? defaults.merchantPrefab : null;
        if (prefab == null) { log.Append("NO merchant prefab; "); return; }
        string path = AssetDatabase.GetAssetPath(prefab);
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            // On the object that carries the Merchant (a child of the prefab's root).
            var merchant = root.GetComponentInChildren<Merchant>(true);
            var host = merchant != null ? merchant.gameObject : root;
            foreach (var stray in root.GetComponentsInChildren<TavernKeeper>(true)) if (stray.gameObject != host) Object.DestroyImmediate(stray);
            var keeper = host.GetComponent<TavernKeeper>() ?? host.AddComponent<TavernKeeper>();
            keeper.dishes = dishes;
            if (merchant != null)
            {
                merchant.merchantName = "Tavern keeper";
                merchant.musicKey = "";   // the tavern's own music plays (RoomMusic)
                merchant.greetings = new[] { "\"Sit down. Eat something.\"", "\"Food, a fire, and things I've heard.\"", "\"Nothing comes through that door. Mostly.\"" };
            }
            var provokable = root.GetComponentInChildren<ProvokableNpc>(true);
            if (provokable != null)
            {
                provokable.hitsToTurn = 3;
                provokable.wrathLines = new[] { new DungeonMaster.Line { text = "I did warn you.", mumble = true }, new DungeonMaster.Line { text = "Some people only learn the hard way.", mumble = true } };
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
            log.Append("keeper; ");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        foreach (var data in AssetDatabase.LoadAllAssetsAtPath(path).OfType<CharacterData>())
        {
            var so = new SerializedObject(data);
            var n = so.FindProperty("characterName"); if (n != null) n.stringValue = "tavern keeper";
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    // ── The levels ──

    static void Levels(GameObject room, System.Text.StringBuilder log)
    {
        int set = 0;
        foreach (var g in AssetDatabase.FindAssets("t:TableLevelData", new[] { "Assets/Game/Levels" }))
        {
            var level = AssetDatabase.LoadAssetAtPath<TableLevelData>(AssetDatabase.GUIDToAssetPath(g));
            if (level == null || !level.IsDungeon || level.name == "IntroDungeon" || level.name == "Arena" || level.name == "Pocket") continue;
            var biomes = new List<DungeonBiome>();
            if (level.baseBiome != null) biomes.Add(level.baseBiome);
            if (level.stages != null) foreach (var s in level.stages) if (s?.biome != null) biomes.Add(s.biome);
            foreach (var b in biomes.Distinct()) { Undo.RecordObject(b, "Tavern"); b.merchantChance = 1; EditorUtility.SetDirty(b); }
            // Walled levels get the tavern; a board keeps its own camp (no walls for a door), its keeper the same.
            if (!level.IsBoard && level.baseBiome != null && level.layoutMode != DungeonLayoutMode.Authored) { level.baseBiome.shopRoom = room; EditorUtility.SetDirty(level.baseBiome); EditorUtility.SetDirty(level); }
            set++;
        }
        log.Append($"{set} levels; ");
    }

    // ── The Arena as the first rumour ──

    static void Arena(System.Text.StringBuilder log)
    {
        var arena = AssetDatabase.FindAssets("t:CampaignEncounter", new[] { "Assets/Game/Campaign/Encounters" })
            .Select(g => AssetDatabase.LoadAssetAtPath<CampaignEncounter>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(e => e != null && e.id == "arena");
        if (arena == null) { log.Append("NO arena place; "); return; }
        arena.rumoured = true;
        if (string.IsNullOrEmpty(arena.rumour)) arena.rumour = "Some who wander off past the Crypt come back with sand in their boots. Some come back with a crowd still cheering in their ears.";
        if (string.IsNullOrEmpty(arena.rumourTold)) arena.rumourTold = "There's a pit not far from the Crypt where they fight for a crowd. A champion waits in it. Nobody has thrown him out yet.";
        if (arena.openedLine == null || arena.openedLine.IsEmpty) arena.openedLine = new DungeonMaster.Line { text = "The Arena. You paid to hear about it. Now go and earn it.", inPerson = true, mumble = true };
        EditorUtility.SetDirty(arena);
        log.Append("arena rumour; ");
    }

    // ── The screen (Room.unity): Wares, Food and Rumours ──

    static void Ui(System.Text.StringBuilder log)
    {
        var shop = Object.FindAnyObjectByType<ShopUI>(FindObjectsInactive.Include);
        if (shop == null) { log.Append("NO ShopUI in the open scene; "); return; }
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var screen = (GameObject)typeof(ShopUI).GetField("root", flags).GetValue(shop);
        var panel = screen.transform.Find("Panel");
        var old = panel.Find("Tavern"); if (old != null) Object.DestroyImmediate(old.gameObject);
        var title = panel.Find("Title") as RectTransform;
        title.sizeDelta = new Vector2(440, title.sizeDelta.y); title.anchoredPosition = new Vector2(-402, title.anchoredPosition.y);
        var closeTemplate = panel.Find("Close").gameObject;
        var header = panel.Find("Supplies").GetComponent<TMP_Text>();
        var rowTemplate = panel.Find("Supply row 1");
        var nameStyle = rowTemplate.Find("Name").GetComponent<TMP_Text>();
        var infoStyle = rowTemplate.Find("Info").GetComponent<TMP_Text>();
        var priceStyle = rowTemplate.Find("Price").GetComponent<TMP_Text>();
        var slotSprite = rowTemplate.GetComponent<Image>().sprite;
        var coinSprite = rowTemplate.Find("Coin").GetComponent<Image>().sprite;

        var group = new GameObject("Tavern", typeof(RectTransform)).GetComponent<RectTransform>();
        group.SetParent(panel, false); Stretch(group);
        var tabs = UI.New("Tabs", group, new Vector2(110, 346), new Vector2(400, 52));
        var ui = screen.GetComponent<TavernTabsUI>() ?? screen.AddComponent<TavernTabsUI>();
        TavernTabsUI.Tab Tab(string label, float x)
        {
            var b = Object.Instantiate(closeTemplate, tabs);
            b.name = label + " tab";
            var rt = (RectTransform)b.transform; rt.anchoredPosition = new Vector2(x, 0); rt.sizeDelta = new Vector2(124, 48);
            var text = b.transform.Find("Label").GetComponent<TMP_Text>(); text.text = label; text.fontSize = 22;
            var frame = b.transform.Find("Frame");
            return new TavernTabsUI.Tab { button = b.GetComponent<Button>(), label = text, picked = frame != null ? frame.gameObject : null };
        }
        var wares = Tab("Wares", -130); var food = Tab("Food", 0); var rumours = Tab("Rumours", 130);

        // Food: two dish cards in the wares' column.
        var foodPanel = UI.New("Food", group, new Vector2(-340, 30), new Vector2(600, 560));
        Header("FOOD", foodPanel, new Vector2(-150, 266), header);
        Note("Eat once each run. It lasts until the run ends.", foodPanel, new Vector2(0, 232), infoStyle);
        var dishCards = new List<TavernTabsUI.Card>();
        for (int i = 0; i < 2; i++) dishCards.Add(Card($"Dish {i + 1}", foodPanel, new Vector2(0, 136 - i * 160), slotSprite, coinSprite, nameStyle, infoStyle, priceStyle, closeTemplate));
        // Rumours: three cards.
        var rumourPanel = UI.New("Rumours", group, new Vector2(-340, 30), new Vector2(600, 560));
        Header("RUMOURS", rumourPanel, new Vector2(-150, 266), header);
        Note("Places you haven't found. Pay to hear where.", rumourPanel, new Vector2(0, 232), infoStyle);
        var rumourCards = new List<TavernTabsUI.Card>();
        for (int i = 0; i < 3; i++) rumourCards.Add(Card($"Rumour {i + 1}", rumourPanel, new Vector2(0, 136 - i * 160), slotSprite, coinSprite, nameStyle, infoStyle, priceStyle, closeTemplate));
        var empty = Note("No more rumours for now. Come back when more of the map is open.", rumourPanel, new Vector2(0, 30), infoStyle);

        var so = new SerializedObject(ui);
        so.FindProperty("tabRow").objectReferenceValue = tabs.gameObject;
        SetTab(so.FindProperty("wares"), wares); SetTab(so.FindProperty("food"), food); SetTab(so.FindProperty("rumours"), rumours);
        var parts = new List<GameObject>();
        foreach (Transform t in panel)
            if (t.name.StartsWith("Supply row") || t.name.StartsWith("Equipment row") || t.name == "Supplies" || t.name == "Equipment" || t.name == "Price header" || t.name.EndsWith("sold out"))
                parts.Add(t.gameObject);
        var partsProp = so.FindProperty("waresParts"); partsProp.arraySize = parts.Count;
        for (int i = 0; i < parts.Count; i++) partsProp.GetArrayElementAtIndex(i).objectReferenceValue = parts[i];
        so.FindProperty("foodPanel").objectReferenceValue = foodPanel.gameObject;
        so.FindProperty("rumourPanel").objectReferenceValue = rumourPanel.gameObject;
        SetCards(so.FindProperty("dishCards"), dishCards); SetCards(so.FindProperty("rumourCards"), rumourCards);
        so.FindProperty("rumourEmpty").objectReferenceValue = empty;
        var scroll = AssetDatabase.LoadAllAssetsAtPath("Assets/ThirdParty/Pixel Items/Librarium/Singles/43_Scroll_Arcane.png").OfType<Sprite>().FirstOrDefault();
        so.FindProperty("rumourIcon").objectReferenceValue = scroll;
        so.ApplyModifiedPropertiesWithoutUndo();
        foodPanel.gameObject.SetActive(false); rumourPanel.gameObject.SetActive(false);
        var shopSo = new SerializedObject(shop); shopSo.FindProperty("tavern").objectReferenceValue = ui; shopSo.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(shop.gameObject.scene);
        EditorSceneManager.SaveScene(shop.gameObject.scene);
        log.Append(scroll == null ? "ui (no scroll icon); " : "ui; ");
    }

    static TavernTabsUI.Card Card(string name, RectTransform parent, Vector2 at, Sprite slot, Sprite coin, TMP_Text nameStyle, TMP_Text infoStyle, TMP_Text priceStyle, GameObject buttonTemplate)
    {
        var card = UI.New(name, parent, at, new Vector2(600, 150));
        var bg = card.gameObject.AddComponent<Image>(); bg.sprite = slot; bg.type = Image.Type.Sliced;
        var cell = UI.New("Icon cell", card, new Vector2(-250, 0), new Vector2(84, 84)); var cellImage = cell.gameObject.AddComponent<Image>(); cellImage.sprite = slot; cellImage.type = Image.Type.Sliced;
        var icon = UI.New("Icon", card, new Vector2(-250, 0), new Vector2(72, 72)).gameObject.AddComponent<Image>(); icon.preserveAspect = true;
        var n = Text("Name", card, new Vector2(-30, 52), new Vector2(340, 30), nameStyle, 24);
        var info = Text("Info", card, new Vector2(-30, 6), new Vector2(340, 64), infoStyle, 19); info.textWrappingMode = TextWrappingModes.Normal; info.alignment = TextAlignmentOptions.TopLeft;
        info.rectTransform.anchoredPosition = new Vector2(-30, 4);
        var note = Text("Note", card, new Vector2(-30, -55), new Vector2(340, 26), infoStyle, 18);
        var price = Text("Price", card, new Vector2(196, 44), new Vector2(80, 32), priceStyle, 24); price.alignment = TextAlignmentOptions.Right;
        var coinImage = UI.New("Coin", card, new Vector2(252, 44), new Vector2(26, 26)).gameObject.AddComponent<Image>(); coinImage.sprite = coin;
        var b = Object.Instantiate(buttonTemplate, card); b.name = "Button";
        var brt = (RectTransform)b.transform; brt.anchoredPosition = new Vector2(220, -26); brt.sizeDelta = new Vector2(124, 46);
        var label = b.transform.Find("Label").GetComponent<TMP_Text>(); label.fontSize = 22;
        var hover = card.gameObject.AddComponent<HoverText>();
        return new TavernTabsUI.Card { root = card.gameObject, icon = icon, name = n, info = info, note = note, price = price, coin = coinImage.gameObject, button = b.GetComponent<Button>(), buttonLabel = label, hover = hover };
    }

    static TMP_Text Text(string name, RectTransform parent, Vector2 at, Vector2 size, TMP_Text style, float fontSize)
    {
        var t = UI.New(name, parent, at, size).gameObject.AddComponent<TextMeshProUGUI>();
        t.font = style.font; t.fontSharedMaterial = style.fontSharedMaterial; t.fontSize = fontSize; t.color = style.color;
        t.alignment = TextAlignmentOptions.MidlineLeft; t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis; t.raycastTarget = false;
        return t;
    }

    static void Header(string text, RectTransform parent, Vector2 at, TMP_Text style)
    {
        var t = Text(text, parent, at, new Vector2(300, 30), style, style.fontSize); t.text = text; t.color = style.color;
    }

    static TMP_Text Note(string text, RectTransform parent, Vector2 at, TMP_Text style)
    {
        var t = Text("Note", parent, at, new Vector2(600, 28), style, 20); t.text = text;
        return t;
    }

    static void SetTab(SerializedProperty p, TavernTabsUI.Tab t)
    {
        p.FindPropertyRelative("button").objectReferenceValue = t.button;
        p.FindPropertyRelative("label").objectReferenceValue = t.label;
        p.FindPropertyRelative("picked").objectReferenceValue = t.picked;
    }

    static void SetCards(SerializedProperty p, List<TavernTabsUI.Card> cards)
    {
        p.arraySize = cards.Count;
        for (int i = 0; i < cards.Count; i++)
        {
            var e = p.GetArrayElementAtIndex(i); var c = cards[i];
            e.FindPropertyRelative("root").objectReferenceValue = c.root;
            e.FindPropertyRelative("icon").objectReferenceValue = c.icon;
            e.FindPropertyRelative("name").objectReferenceValue = c.name;
            e.FindPropertyRelative("info").objectReferenceValue = c.info;
            e.FindPropertyRelative("note").objectReferenceValue = c.note;
            e.FindPropertyRelative("price").objectReferenceValue = c.price;
            e.FindPropertyRelative("coin").objectReferenceValue = c.coin;
            e.FindPropertyRelative("button").objectReferenceValue = c.button;
            e.FindPropertyRelative("buttonLabel").objectReferenceValue = c.buttonLabel;
            e.FindPropertyRelative("hover").objectReferenceValue = c.hover;
        }
    }

    static void Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }

    static class UI
    {
        public static RectTransform New(string name, Transform parent, Vector2 at, Vector2 size)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
            rt.anchoredPosition = at; rt.sizeDelta = size;
            return rt;
        }
    }

    // ── The Debug arena: a tavern behind a door, west of the fighting floor ──

    static void DebugArena(GameObject room, System.Text.StringBuilder log)
    {
        var level = AssetDatabase.LoadAssetAtPath<TableLevelData>("Assets/Game/Development/Debug Dungeon/Debug Dungeon.asset");
        if (level == null) { log.Append("NO debug arena; "); return; }
        var rows = new List<string>();
        rows.Add(new string('#', 28));
        for (int i = 0; i < 3; i++) rows.Add("############1111############");
        rows.Add("#############..#############");
        for (int r = 0; r < 24; r++)
        {
            // The tavern (room 2, 7 x 5 like its footprint) on rows 8-12, a one-cell passage to the arena (room 0) on row 10.
            bool tavern = r >= 3 && r <= 7;
            string west = tavern ? "##2222222" : "#########";
            string gap = tavern && r == 5 ? "." : "#";
            rows.Add(west + gap + new string('0', 16) + "##");
        }
        rows.Add(new string('#', 28));
        Undo.RecordObject(level, "Debug tavern");
        level.authoredMap = string.Join("\n", rows);
        var list = level.authoredRooms != null ? level.authoredRooms.ToList() : new List<TableLevelData.AuthoredRoom>();
        while (list.Count < 3) list.Add(new TableLevelData.AuthoredRoom());
        // Turned so the front (its door wall) faces east, toward the arena.
        list[2].label = "Tavern"; list[2].role = DungeonGenerator.RoomRole.Rest; list[2].template = room; list[2].merchant = true; list[2].turns = 3;
        level.authoredRooms = list.ToArray();
        if (level.baseBiome != null) { level.baseBiome.merchantChance = 1; EditorUtility.SetDirty(level.baseBiome); }
        EditorUtility.SetDirty(level);
        log.Append("debug arena; ");
    }

    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        Folder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
