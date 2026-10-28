using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Builds the scripted event rooms from an eval: EventRoomsSetup.Build() (EventRoomsSetup.Build(true) builds them again
// from scratch, losing hand edits). Safe to run again: what exists is kept. Makes
// - the rooms in Levels/Shared/Rooms/Scripted/Events: Duel bridge, Bell room, Card table, Giant's hoard, Dice room, each a
//   room template with its ScriptedEventRoom and event script (Levels/Scripts/Events),
// - their enemies in Characters/Enemies/Events (Duellist, Sleeping giant, Card sharp: variants with their own data),
// - stand-ins for their props in Levels/Shared/Props/Events (DM die, Great bell) until the art replaces them,
// - house rule three (a duel is fought face to face),
// and deals the rooms into Dungeon1's walled biomes' room pools. Edit the prefabs by hand freely after.
public static class EventRoomsSetup
{
    const string RoomDir = "Assets/Game/Levels/Shared/Rooms/Scripted/Events";
    const string PropDir = "Assets/Game/Levels/Shared/Props/Events";
    const string EnemyDir = "Assets/Game/Characters/Enemies/Events";
    const string Shared = "Assets/Game/Levels/Shared/Props";
    static bool overwrite;
    const float Weight = .3f;
    static System.Text.StringBuilder log;

    public static string Build() => Build(false);

    public static string Build(bool replace)
    {
        overwrite = replace;
        log = new System.Text.StringBuilder();
        Folder(RoomDir); Folder(PropDir); Folder(EnemyDir);
        var duellist = Enemy("Duellist", "Crypt/Crypt Warden", 1f, (StatType.MaxHealth, 48f), (StatType.AttackDamage, 10f), (StatType.Armor, 10f));
        var giant = Enemy("Sleeping giant", "Crypt/Crypt Warden", 1.7f, (StatType.MaxHealth, 130f), (StatType.AttackDamage, 18f), (StatType.Armor, 12f), (StatType.MoveSpeed, .85f), (StatType.AttackSpeed, .8f));
        var sharp = Enemy("Card sharp", "Crypt/Crypt Soldier", -1f, (StatType.MaxHealth, 30f), (StatType.AttackDamage, 8f));
        var die = Die();
        var bell = Bell();
        var rule = Rule3();
        var rooms = new List<(GameObject room, DungeonRoomRoles roles, float weight)>
        {
            // About one event room a floor (measured over 36 floors of Dungeon1 at .3 each).
            (Duel(duellist, rule), DungeonRoomRoles.Combat, Weight),
            (BellRoom(bell), DungeonRoomRoles.Combat, Weight),
            (Wager(sharp), DungeonRoomRoles.Combat | DungeonRoomRoles.Rest | DungeonRoomRoles.Treasure | DungeonRoomRoles.Storage, Weight),
            (Giant(giant), DungeonRoomRoles.Treasure | DungeonRoomRoles.Combat, Weight),
            (Dice(die), DungeonRoomRoles.Combat | DungeonRoomRoles.Rest, Weight),
        };
        foreach (var biome in new[] { "Assets/Game/Levels/Shared/Biomes/Cellars1.asset", "Assets/Game/Levels/Shared/Biomes/Cellars2.asset", "Assets/Game/Levels/Shared/Biomes/Cellars3.asset",
                                      "Assets/Game/Levels/Dungeon1/Biomes/Ruins1.asset", "Assets/Game/Levels/Dungeon1/Biomes/Ruins2.asset", "Assets/Game/Levels/Dungeon1/Biomes/Ruins3.asset" })
            Pool(biome, rooms);
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        Folder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

    static T Load<T>(string path) where T : Object
    {
        var a = AssetDatabase.LoadAssetAtPath<T>(path);
        if (a == null) log.Append($"MISSING {path}; ");
        return a;
    }

    // ── Enemies ──────────────────────────────────────────────────────

    // A variant of an enemy with its own data (the New Enemy way), its stats set; scale > 0 sets its size.
    static GameObject Enemy(string name, string template, float scale, params (StatType stat, float value)[] stats)
    {
        string path = $"{EnemyDir}/{name}.prefab";
        var made = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (made != null && !overwrite) return made;
        var source = Load<GameObject>($"Assets/Game/Characters/Enemies/{template}.prefab");
        if (source == null) return null;
        if (made != null) AssetDatabase.DeleteAsset(path);
        made = NewCharacterWindow.Create(name, source, path);
        var root = PrefabUtility.LoadPrefabContents(path);
        if (scale > 0f) root.transform.localScale = Vector3.one * scale;
        // Held still until its event room takes it over.
        if (root.GetComponent<EventActor>() == null) root.AddComponent<EventActor>();
        var data = root.GetComponent<Character>().data;
        foreach (var (stat, value) in stats)
        {
            int i = data.stats.FindIndex(s => s.stat == stat);
            var entry = i >= 0 ? data.stats[i] : new StatEntry(stat, value);
            entry.baseValue = value;
            if (i >= 0) data.stats[i] = entry; else data.stats.Add(entry);
        }
        EditorUtility.SetDirty(data);
        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
        log.Append($"enemy {name}; ");
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    // ── Prop stand-ins ───────────────────────────────────────────────

    // The Dungeon Master's die: a stand-in cube with a pip on each face until its art is in.
    static GameObject Die()
    {
        string path = $"{PropDir}/DM die.prefab";
        var made = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (made != null) return made;
        var root = new GameObject("DM die");
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body"; body.transform.SetParent(root.transform, false); body.transform.localScale = Vector3.one * .6f;
        Object.DestroyImmediate(body.GetComponent<Collider>());
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(.86f, .82f, .7f) };
        AssetDatabase.CreateAsset(mat, $"{PropDir}/DM die stand-in.mat");
        body.GetComponent<Renderer>().sharedMaterial = mat;
        var pip = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(.15f, .1f, .08f) };
        AssetDatabase.CreateAsset(pip, $"{PropDir}/DM die stand-in pip.mat");
        // One dot per pip on each face: +Y 1, +X 2, +Z 3, -Z 4, -X 5, -Y 6.
        var faces = new (Vector3 axis, int n)[] { (Vector3.up, 1), (Vector3.right, 2), (Vector3.forward, 3), (Vector3.back, 4), (Vector3.left, 5), (Vector3.down, 6) };
        foreach (var (axis, n) in faces)
        {
            var rot = Quaternion.FromToRotation(Vector3.up, axis);
            for (int i = 0; i < n; i++)
            {
                float a = n == 1 ? 0 : i * Mathf.PI * 2f / n;
                var offset = n == 1 ? Vector2.zero : new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * .15f;
                var dot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                dot.name = "Pip"; Object.DestroyImmediate(dot.GetComponent<Collider>());
                dot.transform.SetParent(root.transform, false);
                dot.transform.localRotation = rot;
                dot.transform.localPosition = rot * new Vector3(offset.x, .301f, offset.y);
                dot.transform.localScale = new Vector3(.07f, .004f, .07f);
                dot.GetComponent<Renderer>().sharedMaterial = pip;
            }
        }
        var box = root.AddComponent<BoxCollider>(); box.size = Vector3.one * .6f;
        made = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        log.Append("die stand-in; ");
        return made;
    }

    // The great bell: a stand-in (the Synty bell, raw) hung from its pivot at the top until its art is in.
    static GameObject Bell()
    {
        string path = $"{PropDir}/Great bell.prefab";
        var made = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (made != null) return made;
        var root = new GameObject("Great bell");
        var synty = Load<GameObject>("Assets/Synty/PolygonDarkFantasy/Prefabs/Props/SM_Prop_Bell_Large_01.prefab");
        if (synty != null)
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(synty);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Bell"; model.transform.SetParent(root.transform, false);
            var b = Bounds(model);
            model.transform.localPosition = new Vector3(-b.center.x, -b.max.y, -b.center.z);
            foreach (var c in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        }
        made = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        log.Append("bell stand-in; ");
        return made;
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    // ── House rule three ─────────────────────────────────────────────

    static HouseRule Rule3()
    {
        const string path = "Assets/Game/Rules/Data/Rule 3.asset";
        var rule = AssetDatabase.LoadAssetAtPath<HouseRule>(path);
        if (rule == null)
        {
            rule = ScriptableObject.CreateInstance<HouseRule>();
            rule.id = "rule3";
            rule.text = "A duel is fought face to face.";
            rule.given = new DungeonMaster.Line { text = "*New rule added:* A duel is fought face to face.", mumble = true };
            rule.quote = new DungeonMaster.Line { text = "*Rule three.*", mumble = true };
            AssetDatabase.CreateAsset(rule, path);
            log.Append("rule 3; ");
        }
        // On the manager after rule two.
        var manager = Object.FindAnyObjectByType<RuleManager>(FindObjectsInactive.Include);
        var prefab = Load<GameObject>("Assets/Game/Rules/Prefabs/RuleManager.prefab");
        foreach (var m in new[] { manager, prefab != null ? prefab.GetComponent<RuleManager>() : null })
        {
            if (m == null || System.Array.IndexOf(m.rules, rule) >= 0) continue;
            var list = new List<HouseRule>(m.rules) { rule };
            m.rules = list.ToArray();
            EditorUtility.SetDirty(m);
            if (m == manager && PrefabUtility.IsPartOfPrefabInstance(m)) PrefabUtility.RecordPrefabInstancePropertyModifications(m);
        }
        if (manager != null) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        return rule;
    }

    // ── Rooms ────────────────────────────────────────────────────────

    // A room template: its painted footprint (rows[0] the south row), height in tiles, the event's ScriptedEventRoom and
    // a filler that adds the rest. Kept when it exists (unless overwriting).
    static GameObject Room(string name, string eventName, string summary, string[] rows, int heightTiles, System.Action<Transform, ScriptedEventRoom> fill,
        bool openCeiling = false, int skylight = 0, DungeonRoomLight light = DungeonRoomLight.Normal)
    {
        string path = $"{RoomDir}/{name}.prefab";
        var made = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (made != null && !overwrite) return made;
        var root = new GameObject(name);
        var t = root.AddComponent<DungeonRoomTemplate>();
        t.kind = DungeonRoomKind.Scripted;
        t.description = summary;
        t.paintFootprint = true; t.width = rows[0].Length; t.height = rows.Length; t.rows = rows;
        t.heightTiles = heightTiles; t.openCeiling = openCeiling; t.skylightRadius = skylight;
        t.replaceGeneratedProps = true; t.replaceGeneratedEnemies = true; t.socketEnemiesOnly = true;
        t.roomEvent = DungeonRoomEvent.None;
        if (light != DungeonRoomLight.Normal) root.AddComponent<DungeonRoomMood>().kind = light;
        var e = root.AddComponent<ScriptedEventRoom>();
        e.eventName = eventName; e.summary = summary;
        fill(root.transform, e);
        made = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        log.Append($"room {name}; ");
        return made;
    }

    static Transform Marker(Transform parent, string name, Vector3 at, float yaw = 0f)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = at; go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        return go.transform;
    }

    static GameObject Put(Transform parent, string prefabPath, Vector3 at, float yaw = 0f, string name = null)
    {
        var prefab = Load<GameObject>(prefabPath);
        if (prefab == null) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.localPosition = at; go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        if (name != null) go.name = name;
        return go;
    }

    static GameObject Put(Transform parent, GameObject prefab, Vector3 at, float yaw = 0f)
    {
        if (prefab == null) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.localPosition = at; go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        return go;
    }

    static void WallLight(Transform parent, Vector3 at, float facing)
    {
        var s = Marker(parent, "WallLight socket", at, facing).gameObject.AddComponent<DungeonSocket>();
        s.type = DungeonSocketType.WallLight;
    }

    // Rows (south first) of a w x h room, '+' on the given door cells, '*' in the middle.
    static string[] Rows(int w, int h, params Vector2Int[] doors)
    {
        var rows = new string[h];
        for (int y = 0; y < h; y++)
        {
            var row = new char[w];
            for (int x = 0; x < w; x++) row[x] = System.Array.IndexOf(doors, new Vector2Int(x, y)) >= 0 ? '+' : x == w / 2 && y == h / 2 ? '*' : '#';
            rows[y] = new string(row);
        }
        return rows;
    }

    static void Interactable(GameObject go, Vector3 centre, Vector3 size)
    {
        // A trigger: it marks what can be used, and never stops a blow or a step (a solid bell pull kept the player's
        // swings off an enemy beside it).
        var box = go.AddComponent<BoxCollider>(); box.center = centre; box.size = size; box.isTrigger = true;
        go.AddComponent<InteractableTrigger>();
    }

    // The duel: a 7 x 9 hall two tiles high, a chasm down each side, a bridge of three cells up the middle from the south
    // door to the north one, the duellist in the middle facing the south.
    static GameObject Duel(GameObject duellist, HouseRule rule) => Room("Duel bridge", "duel",
        "A duellist waits on a bridge. Face him one on one; a foul brings his friends.",
        Rows(7, 9, new Vector2Int(3, 0), new Vector2Int(3, 8)), 2, (root, e) =>
        {
            foreach (var x in new[] { -5f, 5f })
            {
                var cut = Marker(root, "Chasm", new Vector3(x, 0f, 0f)).gameObject.AddComponent<DungeonFloorCut>();
                cut.kind = DungeonFloorCut.Kind.Pit; cut.cells = new Vector2Int(2, 7);
            }
            var post = Marker(root, "Duellist post", Vector3.zero, 180f);
            Put(root, duellist, Vector3.zero, 180f);
            e.entrance = Marker(root, "Entrance", new Vector3(0f, 0f, -7f));
            Put(root, $"{Shared}/Furnishing/Hanging chain.prefab", new Vector3(-4.6f, 2.97f, 2.4f));
            Put(root, $"{Shared}/Furnishing/Hanging chain.prefab", new Vector3(5.2f, 2.97f, -1.6f));
            Put(root, $"{Shared}/Furnishing/Brazier.prefab", new Vector3(-2.4f, 0f, -7.2f));
            Put(root, $"{Shared}/Furnishing/Brazier.prefab", new Vector3(2.4f, 0f, 7.2f));
            // Fire either side of him, so he is seen from the way in.
            Put(root, $"{Shared}/Furnishing/Brazier.prefab", new Vector3(-2.3f, 0f, -1.4f));
            Put(root, $"{Shared}/Furnishing/Brazier.prefab", new Vector3(2.3f, 0f, -1.4f));
            WallLight(root, new Vector3(-6f, 0f, -8f), 270f); WallLight(root, new Vector3(6f, 0f, -8f), 90f);
            WallLight(root, new Vector3(-6f, 0f, 8f), 270f); WallLight(root, new Vector3(6f, 0f, 8f), 90f);
            var duel = root.gameObject.AddComponent<DuelEvent>();
            duel.post = post; duel.rule = rule;
            duel.salute = Load<AnimationClip>("Assets/Game/Characters/Animations/ThirdPerson/Gestures/RaiseArm.anim");
        });

    // The bell: a 7 x 7 room two tiles high, the bell over the middle with its pull beside it, a plain chest at the back.
    static GameObject BellRoom(GameObject bell) => Room("Bell room", "bell",
        "Pull the rope: the gates shut and a wave comes. Clear it and the chest gets better. Open the chest to stop.",
        Rows(7, 7, new Vector2Int(3, 0), new Vector2Int(0, 3)), 2, (root, e) =>
        {
            var b = Put(root, bell, new Vector3(0f, 5.99f, .4f));
            // The rope (its pivot at its top) hung from the ceiling down to about a metre off the floor; the chain stands in
            // without it.
            var ropePrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PropDir}/Bell rope.prefab");
            var pull = ropePrefab != null ? Put(root, ropePrefab, new Vector3(.9f, 5.99f, -.2f)) : Put(root, $"{Shared}/Furnishing/Hanging chain.prefab", new Vector3(.9f, 4.2f, -.2f), 0f, "Bell pull");
            if (pull != null)
            {
                foreach (var c in pull.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                float length = Mathf.Max(1f, pull.transform.position.y - Bounds(pull).min.y);
                // Tied to the clapper: from just under the bell's mouth down to about a metre off the floor.
                if (ropePrefab != null && b != null)
                {
                    pull.name = "Bell rope";
                    var mouth = Bounds(b);
                    pull.transform.localPosition = root.InverseTransformPoint(new Vector3(mouth.center.x, mouth.min.y + .03f, mouth.center.z));
                }
                Interactable(pull, new Vector3(0f, -length + 1.3f, 0f), new Vector3(.45f, 2.6f, .45f));
                pull.AddComponent<BellRope>();
            }
            var chest = Put(root, $"{Shared}/Chests/Supply chest.prefab", new Vector3(0f, 0f, 4.6f), 180f);
            Put(root, $"{Shared}/Furnishing/Candle cluster.prefab", new Vector3(-1.7f, 0f, 5.1f));
            Put(root, $"{Shared}/Furnishing/Candle cluster.prefab", new Vector3(1.7f, 0f, 5.1f), 140f);
            e.entrance = Marker(root, "Entrance", new Vector3(0f, 0f, -5f));
            WallLight(root, new Vector3(-6f, 0f, 6f), 0f); WallLight(root, new Vector3(6f, 0f, 6f), 0f);
            WallLight(root, new Vector3(6f, 0f, -4f), 90f);
            var ev = root.gameObject.AddComponent<BellEvent>();
            ev.bell = b != null ? b.transform : null;
            ev.chest = chest != null ? chest.GetComponent<DungeonContainer>() : null;
            ev.toll = Load<AudioClip>("Assets/Game/Audio/Clips/Events/Bell toll.wav");
        });

    // The wager: a small candlelit 7 x 5 room, a table in the middle with the dealer sitting behind it, three bowls and a
    // coin on it.
    static GameObject Wager(GameObject sharp) => Room("Card table", "wager",
        "A skeleton runs a shell game. Bet gold, find the coin. He cheats once you win twice; call him out with a blow.",
        Rows(7, 5, new Vector2Int(3, 0), new Vector2Int(6, 2)), 1, (root, e) =>
        {
            var table = Put(root, $"{Shared}/Tavern/Table.prefab", new Vector3(0f, 0f, .6f));
            var tb = table != null ? Bounds(table) : new Bounds(new Vector3(0, .8f, .6f), new Vector3(1.6f, 1.6f, 1f));
            float top = tb.max.y;
            // He is small (a skeleton at .83) and the table is high: he sits a little raised and close in, so his head and
            // hat show over the table.
            Put(root, $"{Shared}/Tavern/Chair.prefab", new Vector3(0f, 0f, 1.58f), 180f);
            var seat = Marker(root, "Dealer seat", new Vector3(0f, .12f, 1.45f), 180f);
            Put(root, sharp, seat.localPosition, 180f);
            var bet = Marker(root, "Bet", new Vector3(0f, 0f, .6f)).gameObject;
            Interactable(bet, new Vector3(0f, top * .5f, 0f), new Vector3(tb.size.x, top, tb.size.z));
            bet.AddComponent<WagerTable>();
            var cups = new Transform[3];
            for (int i = 0; i < 3; i++)
            {
                var bowl = Put(root, $"{Shared}/Tavern/Broth bowl.prefab", Vector3.zero, 0f, "Bowl " + (i + 1));
                if (bowl == null) continue;
                bowl.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
                var bb = Bounds(bowl);
                bowl.transform.localPosition = new Vector3((i - 1) * .42f, top + (bowl.transform.position.y - bb.min.y), .45f);
                foreach (var c in bowl.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                var size = Bounds(bowl).size;
                Interactable(bowl, Vector3.zero, new Vector3(size.x, size.y * 2f, size.z) * 1.1f);
                bowl.AddComponent<WagerCup>();
                cups[i] = bowl.transform;
            }
            var coin = Put(root, "Assets/Game/Items/Prefabs/Gold/Coin pile small.prefab", new Vector3(0f, top + .01f, .15f), 0f, "Coin");
            if (coin != null)
            {
                // Just the look: no pickup, no collider.
                foreach (var c in coin.GetComponents<MonoBehaviour>()) Object.DestroyImmediate(c);
                foreach (var c in coin.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                foreach (var rb in coin.GetComponentsInChildren<Rigidbody>()) Object.DestroyImmediate(rb);
                coin.transform.localScale = Vector3.one * .6f;
            }
            Put(root, $"{Shared}/Tavern/Table candle.prefab", new Vector3(-.85f, top, .9f));
            e.entrance = Marker(root, "Entrance", new Vector3(0f, 0f, -3f));
            WallLight(root, new Vector3(-6f, 0f, 2f), 270f);
            var ev = root.gameObject.AddComponent<WagerEvent>();
            ev.seat = seat; ev.cups = cups; ev.coin = coin != null ? coin.transform : null;
            ev.coinRest = Marker(root, "Coin rest", new Vector3(0f, top + .01f, .15f));
            ev.sitting = Load<AnimationClip>("Assets/Game/Characters/Animations/ThirdPerson/Sitting/Sitting Idle.anim");
            ev.cupKnock = Load<AudioClip>("Assets/Game/Cinematics/Intro/Tabletop/Dice knock 2.wav");
            ev.coinSound = Load<AudioClip>("Assets/ThirdParty/Sound Effects Library/SoundFX/Coins3.wav");
        }, light: DungeonRoomLight.Candlelit);

    // The giant: a 9 x 9 hall two tiles high, dim, the giant asleep in the middle of its hoard, a gold chest at its back.
    static GameObject Giant(GameObject giant) => Room("Giant's hoard", "giant",
        "A giant sleeps on its hoard. Crouch and take what you can; a walking step close by wakes it.",
        Rows(9, 9, new Vector2Int(4, 0), new Vector2Int(8, 4)), 2, (root, e) =>
        {
            var bed = Marker(root, "Bed", new Vector3(0f, 0f, .6f), 200f);
            Put(root, giant, bed.localPosition, 200f);
            var spots = new List<Transform>();
            var ring = new[] { (2.6f, 30f), (3.4f, 75f), (2.8f, 120f), (3.6f, 160f), (3.0f, 200f), (3.4f, 245f), (2.6f, 290f), (4.2f, 330f), (5.2f, 100f), (5.0f, 250f) };
            int n = 0;
            foreach (var (r, a) in ring)
            {
                var p = Quaternion.Euler(0f, a, 0f) * Vector3.forward * r + new Vector3(0f, 0f, .6f);
                spots.Add(Marker(root, "Hoard " + (++n), p));
            }
            Put(root, $"{Shared}/Chests/Gold chest.prefab", new Vector3(0f, 0f, 6.6f), 180f);
            // Candles round the giant, so it and its hoard can be seen from the door (their lights are theirs).
            foreach (var (x, z, yaw) in new[] { (-2.2f, -1.4f, 20f), (2.4f, -1.2f, 110f), (-2.6f, 2.8f, 200f), (2.6f, 3.0f, 300f) })
                Put(root, $"{Shared}/Furnishing/Candle cluster.prefab", new Vector3(x, 0f, z), yaw);
            Put(root, $"{Shared}/Furnishing/Bone heap.prefab", new Vector3(-5.6f, 0f, 5.4f), 30f);
            Put(root, $"{Shared}/Furnishing/Bone heap.prefab", new Vector3(5.8f, 0f, -5.2f), 200f);
            Put(root, $"{Shared}/Furnishing/Candle cluster.prefab", new Vector3(-2.2f, 0f, 7.2f));
            Put(root, $"{Shared}/Furnishing/Candle cluster.prefab", new Vector3(2.2f, 0f, 7.2f), 90f);
            e.entrance = Marker(root, "Entrance", new Vector3(0f, 0f, -7f));
            WallLight(root, new Vector3(-8f, 0f, -6f), 270f); WallLight(root, new Vector3(8f, 0f, 6f), 90f);
            var ev = root.gameObject.AddComponent<SleepingGiantEvent>();
            ev.bed = bed; ev.hoard = spots.ToArray();
        }, light: DungeonRoomLight.Candlelit);

    // The dice: a 7 x 7 room open to the sky in moonlight; the die lands in the middle.
    static GameObject Dice(GameObject die) => Room("Dice room", "dice",
        "Walk in and the Dungeon Master rolls his die into the room: 1 ambush, 2 a tough enemy, 3 nothing, 4 a rest, 5 an iron chest, 6 a gold chest.",
        Rows(7, 7, new Vector2Int(3, 0), new Vector2Int(6, 3)), 1, (root, e) =>
        {
            // The moonlight's shaft and motes, but not its night-sky card: the opening must show the Dungeon Master above.
            var moon = Put(root, $"{Shared}/Effects/Moonlight.prefab", Vector3.zero);
            var sky = moon != null ? moon.transform.Find("Night sky") : null;
            if (sky != null) sky.gameObject.SetActive(false);
            var landing = Marker(root, "Landing", new Vector3(0f, 0f, .5f));
            e.entrance = Marker(root, "Entrance", new Vector3(0f, 0f, -5f));
            WallLight(root, new Vector3(-6f, 0f, -4f), 270f); WallLight(root, new Vector3(-6f, 0f, 4f), 270f);
            var ev = root.gameObject.AddComponent<DiceEvent>();
            ev.diePrefab = die; ev.landing = landing;
            ev.knocks = new[]
            {
                Load<AudioClip>("Assets/Game/Cinematics/Intro/Tabletop/Dice knock 1.wav"),
                Load<AudioClip>("Assets/Game/Cinematics/Intro/Tabletop/Dice knock 2.wav"),
                Load<AudioClip>("Assets/Game/Cinematics/Intro/Tabletop/Dice knock 3.wav"),
            };
            ev.roll = Load<AudioClip>("Assets/ThirdParty/Sound Effects Library/SoundFX/DiceRollFast.wav");
            ev.toughEnemy = Load<GameObject>("Assets/Game/Characters/Enemies/Crypt/Demon.prefab");
            ev.healEffect = Load<GameObject>("Assets/Game/Combat/Spells/Heal/Heal Impact.prefab");
        }, openCeiling: true, skylight: 3, light: DungeonRoomLight.Moonlit);

    // ── Biome pools ──────────────────────────────────────────────────

    static void Pool(string biomePath, List<(GameObject room, DungeonRoomRoles roles, float weight)> rooms)
    {
        var biome = AssetDatabase.LoadAssetAtPath<DungeonBiome>(biomePath);
        if (biome == null) { log.Append($"no biome {biomePath}; "); return; }
        var list = new List<DungeonBiome.RoomTemplateChoice>(biome.roomTemplates ?? new DungeonBiome.RoomTemplateChoice[0]);
        foreach (var (room, roles, weight) in rooms)
        {
            if (room == null) continue;
            var entry = list.Find(c => c != null && c.template == room);
            if (entry != null && !overwrite) continue;
            if (entry == null) { entry = new DungeonBiome.RoomTemplateChoice { template = room }; list.Add(entry); }
            entry.weight = weight; entry.rooms = roles;
        }
        biome.roomTemplates = list.ToArray();
        EditorUtility.SetDirty(biome);
    }
}
