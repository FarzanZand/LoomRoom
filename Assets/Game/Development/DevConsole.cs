#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

// § (the key left of 1; ` on a US keyboard), Editor and development builds only: a console for testing.
//   god [on|off]    no damage, health, mana and stamina kept full (same as F8)
//   ghost [on|off]  fly through walls along the view (sprint is faster), enemies don't see you; off puts you back on
//                   the nearest floor
//   revive          back on your feet after dying; in a run the run goes on. Once the console has been opened, a death
//                   waits for the recap before it is banked (and while the console is open it waits on), so a revive
//                   before then never counts; normal play banks it on the frame of death as always
//   loadtemplate <name>  a new run on floor 1 of a level that uses that room template, opening in it (the template is
//                   the run's starting room, the rest of the floor is generated around it as usual; no reveal). The
//                   level is the one whose biomes or starting room use the template (its biome first), else the level
//                   being played, else the Dungeon button's pick. It is a run like any other: it saves its checkpoint.
//   additem <name> [count] [rarity]  an item from the catalog into the table player's pack (gear rolled to that rarity)
//   addspell <name>|all  a spell's tome into the table player's pack
//   addroomitem <name>|all  carry home any room key item (ProgressionManager.RoomKeyItems: trophies, class posters, the
//                   puzzle's tile...), to use in the room
//   help, clear
// Tab completes the command or the template's name; the names that match what is typed are listed under the line.
// While it is open the game is in the Menu state, so typing never moves the player or fires hotbar keys.
// The window is Development/Resources/Dev Console.prefab (Tools > LoomRoom > Dev > Build Dev Console; edit it freely).
public class DevConsole : MonoBehaviour
{
    [SerializeField] GameObject panel;
    [SerializeField] TMP_InputField input;
    [SerializeField] TMP_Text log;
    [SerializeField, Tooltip("Under the line: the commands or template names that match what is typed.")] TMP_Text suggestions;
    [SerializeField, Tooltip("Shown while there are suggestions (their backing); empty = the suggestions text itself.")] GameObject suggestionBox;
    [SerializeField, Min(4)] int lines = 14;
    [SerializeField, Min(1)] int suggestionLines = 8;
    [SerializeField, Tooltip("Every room template prefab, for loadtemplate in development builds (the Editor finds them itself). Filled by Tools > LoomRoom > Dev > Build Dev Console (and DevConsoleBuilder.RefreshTemplates from an eval).")]
    GameObject[] templates = new GameObject[0];

    static readonly string[] Commands = { "god", "ghost", "revive", "die", "loadtemplate", "event", "meddle", "learnspells", "additem", "addspell", "addroomitem", "help", "clear" };
    const string TemplateCommand = "loadtemplate", EventCommand = "event";
    List<(string name, GameObject prefab)> roomTemplates;

    // The scripted event rooms by their event name (ScriptedEventRoom.eventName), and the Dungeon Master's cheating.
    public const string CheatsEvent = "cheats";
    IEnumerable<(string name, GameObject prefab)> EventRooms =>
        RoomTemplates.Where(t => t.prefab.TryGetComponent<ScriptedEventRoom>(out var e) && !string.IsNullOrWhiteSpace(e.eventName))
            .Select(t => (t.prefab.GetComponent<ScriptedEventRoom>().eventName.Trim().ToLowerInvariant(), t.prefab));
    IEnumerable<string> EventNames => EventRooms.Select(e => e.name).Append(CheatsEvent).Distinct().OrderBy(n => n);

    readonly List<string> history = new();
    bool open;
    int openedFrame = -10;
    Vector3 ghostFrom;   // where the player stood when ghost went on: the way back if nothing below is reachable

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        RunManager.DeferDeath = RunManager.HoldDeath = false;
        if (!Debug.isDebugBuild || FindAnyObjectByType<DevConsole>() != null) return;
        var prefab = Resources.Load<GameObject>("Dev Console");
        if (prefab == null) { Debug.LogWarning("[DevConsole] Development/Resources/Dev Console.prefab is missing."); return; }
        var go = Instantiate(prefab);
        go.name = "Dev Console";
        DontDestroyOnLoad(go);
    }

    void Awake()
    {
        panel.SetActive(false);
        // It lives across scenes: open when a scene goes, it closes there, so its Menu push isn't popped on the next
        // scene's GameManager.
        UnityEngine.SceneManagement.SceneManager.sceneUnloaded += CloseOnUnload;
        // The key that opens it must not end up in the line, whatever it types on this keyboard layout: nothing typed on
        // the frame it opens counts, nor the characters that key gives on common layouts.
        input.onValidateInput = (text, index, c) =>
            Time.frameCount <= openedFrame + 1 || c == '§' || c == '½' || c == '`' || c == '~' || c == '|' || c == '\t' ? '\0' : c;
        input.onSubmit.AddListener(Submit);
        input.onValueChanged.AddListener(ShowSuggestions);
        ShowSuggestions("");
        Print("Type help for the commands.");
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.backquoteKey.wasPressedThisFrame) { if (open) Close(); else Open(); }
        else if (open && keyboard.escapeKey.wasPressedThisFrame) Close();
        else if (open && keyboard.tabKey.wasPressedThisFrame) Complete();
    }

    // ── Completion ───────────────────────────────────────────────────

    // What is typed: the command so far (no space yet), or the command and the start of its argument.
    static void Split(string text, out string command, out string arg, out bool hasArg)
    {
        text = text.TrimStart();
        int space = text.IndexOf(' ');
        hasArg = space >= 0;
        command = hasArg ? text.Substring(0, space) : text;
        arg = hasArg ? text.Substring(space + 1).TrimStart() : "";
    }

    // The names that start with what is typed (ignoring case); for a command with an argument, its argument's names.
    List<string> Matches(string text, out bool forArg)
    {
        Split(text, out var command, out var arg, out forArg);
        if (!forArg) return Commands.Where(c => c.StartsWith(command.ToLowerInvariant())).ToList();
        if (command.ToLowerInvariant() == EventCommand) return EventNames.Where(n => n.StartsWith(arg, System.StringComparison.OrdinalIgnoreCase)).ToList();
        if (command.ToLowerInvariant() == "additem") return DevGive.CatalogItems(false).Select(i => i.itemName).Where(n => n.StartsWith(arg, System.StringComparison.OrdinalIgnoreCase)).Distinct().OrderBy(n => n).ToList();
        if (command.ToLowerInvariant() == "addspell") return SpellNames.Where(n => n.StartsWith(arg, System.StringComparison.OrdinalIgnoreCase)).ToList();
        if (command.ToLowerInvariant() == "addroomitem") return RoomItems.Select(r => r.name).Where(n => n.StartsWith(arg, System.StringComparison.OrdinalIgnoreCase)).ToList();
        if (command.ToLowerInvariant() == "meddle") return new[] { "add", "steal" }.Where(n => n.StartsWith(arg, System.StringComparison.OrdinalIgnoreCase)).ToList();
        if (command.ToLowerInvariant() != TemplateCommand) return new List<string>();
        return RoomTemplates.Select(t => t.name).Where(n => n.StartsWith(arg, System.StringComparison.OrdinalIgnoreCase)).ToList();
    }

    void ShowSuggestions(string text)
    {
        if (suggestions == null) return;
        var matches = string.IsNullOrWhiteSpace(text) ? new List<string>() : Matches(text, out _);
        // Nothing to list once the line is exactly the one match.
        Split(text, out var command, out var arg, out bool forArg);
        if (matches.Count == 1 && string.Equals(matches[0], forArg ? arg : command, System.StringComparison.OrdinalIgnoreCase)) matches.Clear();
        var shown = matches.Take(suggestionLines).ToList();
        if (matches.Count > shown.Count) shown.Add($"... and {matches.Count - shown.Count} more");
        suggestions.text = string.Join("\n", shown);
        (suggestionBox != null ? suggestionBox : suggestions.gameObject).SetActive(shown.Count > 0);
    }

    // Tab: the one match in full, or as much as all the matches share.
    void Complete()
    {
        var text = input.text;
        var matches = Matches(text, out bool forArg);
        if (matches.Count == 0) return;
        Split(text, out var command, out var arg, out _);
        string shared = matches[0];
        foreach (var m in matches)
        {
            int n = 0;
            while (n < shared.Length && n < m.Length && char.ToLowerInvariant(shared[n]) == char.ToLowerInvariant(m[n])) n++;
            shared = shared.Substring(0, n);
        }
        if (shared.Length < (forArg ? arg : command).Length) return;
        input.text = forArg ? command + " " + shared : shared + (matches.Count == 1 && (shared == TemplateCommand || shared == EventCommand || shared == "meddle" || shared == "additem" || shared == "addspell" || shared == "addroomitem") ? " " : "");
        input.MoveTextEnd(false);
        input.ActivateInputField();
    }

    // Every room template by its prefab's name; a name used twice gets its level's folder after it.
    List<(string name, GameObject prefab)> RoomTemplates
    {
        get
        {
            if (roomTemplates != null) return roomTemplates;
            IEnumerable<GameObject> found = templates;
#if UNITY_EDITOR
            found = FindTemplatePrefabs();
#endif
            var list = found.Where(p => p != null).Distinct().ToList();
            var counts = list.GroupBy(p => p.name).ToDictionary(g => g.Key, g => g.Count());
            roomTemplates = list.Select(p => (counts[p.name] > 1 ? $"{p.name} ({LevelFolder(p)})" : p.name, p))
                .OrderBy(t => t.Item1, System.StringComparer.OrdinalIgnoreCase).ToList();
            return roomTemplates;
        }
    }

    static string LevelFolder(GameObject prefab)
    {
#if UNITY_EDITOR
        var parts = UnityEditor.AssetDatabase.GetAssetPath(prefab).Split('/');
        int i = System.Array.IndexOf(parts, "Levels");
        if (i >= 0 && i + 1 < parts.Length) return parts[i + 1];
#endif
        return prefab.GetHashCode().ToString();
    }

#if UNITY_EDITOR
    // Every prefab under Assets/Game/Levels with a DungeonRoomTemplate (variants included).
    public static GameObject[] FindTemplatePrefabs() => UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Levels" })
        .Select(UnityEditor.AssetDatabase.GUIDToAssetPath)
        .Select(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>)
        .Where(p => p != null && p.GetComponent<DungeonRoomTemplate>() != null)
        .ToArray();
#endif

    void Open()
    {
        open = true;
        openedFrame = Time.frameCount;
        RunManager.DeferDeath = RunManager.HoldDeath = true;
        panel.SetActive(true);
        if (GameManager.HasInstance) GameManager.Instance.Push(GameState.Menu);
        input.text = "";
        input.ActivateInputField();
    }

    void Close()
    {
        open = false;
        RunManager.HoldDeath = false;
        input.DeactivateInputField();
        panel.SetActive(false);
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Menu);
    }

    void CloseOnUnload(UnityEngine.SceneManagement.Scene scene) { if (open) Close(); }

    void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= CloseOnUnload;
        if (open && GameManager.HasInstance) GameManager.Instance.Pop(GameState.Menu);
        if (open) RunManager.HoldDeath = false;
    }

    void Submit(string text)
    {
        if (!open) return;
        text = text.Trim();
        input.text = "";
        input.ActivateInputField();
        if (text.Length == 0) return;
        Print("> " + text);
        Run(text);
    }

    // A command from code (Asset Studio's "Play from this room"), printed in the log as if typed.
    public static bool RunCommand(string line)
    {
        var console = FindAnyObjectByType<DevConsole>();
        if (console == null) return false;
        console.Print("> " + line);
        console.Run(line);
        return true;
    }

    void Run(string line)
    {
        var parts = line.ToLowerInvariant().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        string command = parts[0], arg = parts.Length > 1 ? parts[1] : null;
        switch (command)
        {
            case "help":
                Print("god [on|off]: no damage, health, mana and stamina kept full");
                Print("ghost [on|off]: fly through walls, enemies don't see you");
                Print("revive: back on your feet after dying");
                Print("die: the table player dies at once (god mode goes off)");
                Print("loadtemplate <name>: a new run opening in that room template (Tab completes the name)");
                Print("event [name] [face]: a new run starting at the way into a scripted event room; event alone lists them; event dice 6 lands the die on 6; event cheats: a run where the Dungeon Master meddles at the first chance");
                Print("meddle add|steal: the Dungeon Master adds an enemy in front of you or takes the nearest chest, now");
                Print("learnspells: a tome of every spell into the table player's pack");
                Print("additem <name> [count] [rarity]: an item into the table player's pack; gear can be uncommon, rare, unique or legendary (Tab completes the name)");
                Print("addspell <name>|all: that spell's tome into the table player's pack (Tab completes the name)");
                Print("addroomitem <name>|all: carry home a room key item (a trophy, a class poster, the puzzle tile...) to use in the room (Tab completes; addroomitem alone lists them)");
                Print("clear: empty this window");
                break;
            case EventCommand:
                RunEvent(arg, parts.Length > 2 ? parts[2] : null);
                break;
            case "meddle":
                Meddle(arg);
                break;
            case "god":
                DevGodMode.Set(Switch(arg, DevGodMode.On));
                Print(DevGodMode.On ? "God mode on." : "God mode off.");
                break;
            case "ghost":
                Ghost(arg);
                break;
            case "revive":
                Revive();
                break;
            case "die":
                Die();
                break;
            case TemplateCommand:
                Split(line, out _, out var name, out _);
                LoadTemplate(name.Trim());
                break;
            case "learnspells":
                LearnSpells();
                break;
            case "additem":
                Split(line, out _, out var itemArgs, out _);
                AddItem(itemArgs.Trim());
                break;
            case "addroomitem":
                Split(line, out _, out var roomItem, out _);
                AddRoomItem(roomItem.Trim());
                break;
            case "addspell":
                Split(line, out _, out var spellName, out _);
                AddSpell(spellName.Trim());
                break;
            case "clear":
                history.Clear();
                Refresh();
                break;
            default:
                Print($"Unknown command: {command}. Type help.");
                break;
        }
    }

    // Instant death for the table player, as a real death: god mode goes off, the hit can't be blocked.
    void Die()
    {
        var player = Active;
        if (player == null || player.Stats == null) { Print("No player."); return; }
        if (!player.IsAlive) { Print("Already dead."); return; }
        if (PlayerManager.Instance.ActiveKind != PlayerKind.Table) { Print("Only the table player can die."); return; }
        if (DevGodMode.On) DevGodMode.Set(false);
        player.Stats.BlockBlessing = false;
        var hit = DamageInfo.Simple(player.Stats.CurrentHealth * 100f + 100000f);
        hit.Magic = true; hit.SourceName = "the console";
        player.Stats.TakeDamage(hit);
        Print(player.IsAlive ? "Could not die." : "You die.");
    }

    static bool Switch(string arg, bool current) => arg == "on" ? true : arg == "off" ? false : !current;

    static Player Active => PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;

    // A tome of every spell in the catalog the table player doesn't carry yet, into the hotbar while it has room, then the bag.
    void LearnSpells()
    {
        var player = TablePlayer;
        if (player == null) return;
        int given = 0;
        foreach (var item in InventoryManager.Instance.itemCatalog)
        {
            if (item == null || item.spell == null) continue;
            if (player.Hotbar.TotalCount(item) > 0 || player.Bag.TotalCount(item) > 0) continue;
            if (InventoryManager.Instance.Pickup(item, player, 1, true, false, false)) given++;
            else Print($"No room for {item.DisplayName}.");
        }
        Print(given > 0 ? $"{given} spell tomes given. Put one in your left hand from the hotbar." : "You have every spell tome already.");
    }

    void Ghost(string arg)
    {
        var player = Active;
        if (player == null) { Print("No player."); return; }
        bool on = Switch(arg, player.Ghost);
        if (on == player.Ghost) { Print(on ? "Already a ghost." : "Not a ghost."); return; }
        if (on)
        {
            ghostFrom = player.transform.position;
            player.Ghost = true;
            Print("Ghost on: fly where you look, through walls. Enemies don't see you.");
            return;
        }
        player.Ghost = false;
        player.Warp(LandingFor(player.transform.position));
        Print("Ghost off.");
    }

    // Where a ghost lands: the first floor straight below that can be walked to from where ghost went on (so never the
    // top of a ceiling or a closed-off pocket), else where ghost went on.
    Vector3 LandingFor(Vector3 at)
    {
        bool haveStart = NavMesh.SamplePosition(ghostFrom, out var start, 3f, NavMesh.AllAreas);
        var hits = Physics.RaycastAll(at + Vector3.up * .5f, Vector3.down, 200f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
        var path = new NavMeshPath();
        foreach (var h in hits)
        {
            if (!NavMesh.SamplePosition(h.point, out var spot, 1.5f, NavMesh.AllAreas)) continue;
            if (!haveStart) return spot.position;
            if (NavMesh.CalculatePath(start.position, spot.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
                return spot.position;
        }
        return ghostFrom;
    }

    static IEnumerable<string> SpellNames => DevGive.CatalogItems(true).Select(DevGive.SpellName).Append("all").Distinct().OrderBy(n => n);

    // The one entry whose name is this (ignoring case), else the one that starts with it, else contains it; null with a
    // message otherwise.
    ItemData Find(IEnumerable<ItemData> items, System.Func<ItemData, string> nameOf, string name, string kind)
    {
        var list = items.ToList();
        var found = list.Where(i => string.Equals(nameOf(i), name, System.StringComparison.OrdinalIgnoreCase)).ToList();
        if (found.Count == 0) found = list.Where(i => nameOf(i).StartsWith(name, System.StringComparison.OrdinalIgnoreCase)).ToList();
        if (found.Count == 0) found = list.Where(i => nameOf(i).IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        if (found.Count == 1 || found.Count > 1 && found.All(i => nameOf(i) == nameOf(found[0]))) return found[0];
        Print(found.Count == 0 ? $"No {kind} called {name}." : $"{found.Count} {kind}s match {name}: " + string.Join(", ", found.Select(nameOf).Distinct().Take(12)));
        return null;
    }

    Player TablePlayer
    {
        get
        {
            var player = DevGive.TablePlayer(out var problem);
            if (player == null) Print(problem);
            return player;
        }
    }

    // What addroomitem can carry home: every room key item (ProgressionManager.RoomKeyItems), and all of them at once.
    List<(string name, System.Func<string> give)> RoomItems
    {
        get
        {
            var list = new List<(string, System.Func<string>)>();
            if (!ProgressionManager.HasInstance) return list;
            var keys = ProgressionManager.Instance.RoomKeyItems();
            foreach (var k in keys)
            {
                var key = k;
                list.Add((key.name, () => DevGive.RoomItem(key)));
            }
            if (keys.Count > 1)
                list.Add(("all", () => { foreach (var k in keys) k.carry(); return $"{keys.Count} key items carried home."; }));
            return list;
        }
    }

    void AddRoomItem(string name)
    {
        var items = RoomItems;
        if (name.Length == 0)
        {
            Print(items.Count == 0 ? "Nothing to carry home here." : string.Join(", ", items.Select(r => r.name)));
            return;
        }
        var found = items.Where(r => string.Equals(r.name, name, System.StringComparison.OrdinalIgnoreCase)).ToList();
        if (found.Count == 0) found = items.Where(r => r.name.StartsWith(name, System.StringComparison.OrdinalIgnoreCase)).ToList();
        if (found.Count == 0) found = items.Where(r => r.name.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        if (found.Count == 0) { Print($"Nothing called {name}. addroomitem alone lists them."); return; }
        if (found.Count > 1) { Print($"{found.Count} match {name}: " + string.Join(", ", found.Select(r => r.name))); return; }
        Print(found[0].give());
    }

    // additem <name> [count] [rarity]: the count and rarity are read off the end, so names with spaces need no quotes.
    void AddItem(string args)
    {
        if (args.Length == 0) { Print("additem <name> [count] [rarity]: Tab completes the name."); return; }
        var words = args.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries).ToList();
        int count = 1;
        ItemRarity? rarity = null;
        for (int n = 0; n < 2 && words.Count > 1; n++)
        {
            var last = words[words.Count - 1];
            if (int.TryParse(last, out int c) && c > 0) { count = c; words.RemoveAt(words.Count - 1); }
            else if (System.Enum.TryParse(last, true, out ItemRarity r) && !int.TryParse(last, out _) && r != ItemRarity.Relic && r != ItemRarity.Spoils) { rarity = r; words.RemoveAt(words.Count - 1); }
        }
        var item = Find(DevGive.CatalogItems(false), i => i.itemName, string.Join(" ", words), "item");
        if (item != null) Print(DevGive.Item(item, count, rarity));
    }

    void AddSpell(string name)
    {
        if (name.Length == 0) { Print("addspell <name>|all: Tab completes the name."); return; }
        if (name.Equals("all", System.StringComparison.OrdinalIgnoreCase)) { LearnSpells(); return; }
        var tome = Find(DevGive.CatalogItems(true), DevGive.SpellName, name, "spell");
        if (tome != null) Print(DevGive.Spell(tome));
    }

    // event: lists the events; event <name>: a run opening at the way into that event's room (its ScriptedEventRoom
    // entrance); event dice <1-6>: the die lands on that face; event cheats: a run where he meddles at the first chance.
    void RunEvent(string name, string extra)
    {
        if (string.IsNullOrEmpty(name))
        {
            foreach (var (n, prefab) in EventRooms.OrderBy(e => e.name))
                Print($"{n}: {prefab.GetComponent<ScriptedEventRoom>().summary}");
            Print($"{CheatsEvent}: the Dungeon Master meddles (an enemy after an easy fight, a chest taken)");
            return;
        }
        if (name == CheatsEvent)
        {
            var loader = FindAnyObjectByType<TableLevelLoader>();
            if (loader == null) { Print("No table here (the Room scene has it)."); return; }
            if (loader.Busy) { Print("A floor is being built. Try again in a moment."); return; }
            // A level whose floors have the event rooms (Dungeon1), from its first floor; else the Dungeon button's pick.
            var events = EventRooms.Select(e => e.prefab).ToList();
            bool HasEvents(TableLevelData l) => l != null && l.IsDungeon && l.HasStages && l.stages.Any(st => st?.biome != null && st.biome.roomTemplates != null && st.biome.roomTemplates.Any(c => c != null && events.Contains(c.template)));
            var level = loader.Catalog != null && loader.Catalog.levels != null ? loader.Catalog.levels.FirstOrDefault(HasEvents) : null;
            if (level == null) level = DebugDungeonSession.SelectedDungeon != null ? DebugDungeonSession.SelectedDungeon : DebugDungeonSession.DebugDungeons.FirstOrDefault(l => l != null && l.IsDungeon);
            if (level == null) { Print("No dungeon level to play."); return; }
            DungeonMasterMeddling.Forced = true;
            var copy = Instantiate(level);
            copy.name = level.name; copy.fixedSeed = 0;
            Print($"Loading {level.displayName}: win the first fight quickly and he adds an enemy; walk up to a chest and he takes it. meddle add|steal does it at once.");
            Close();
            loader.Load(copy, skipReveal: true);
            return;
        }
        var found = EventRooms.Where(e => e.name.StartsWith(name)).ToList();
        if (found.Count == 0) { Print($"No event called {name}. Type event for the list."); return; }
        if (found.Count > 1 && !found.Any(e => e.name == name)) { Print($"{found.Count} events start with {name}: " + string.Join(", ", found.Select(e => e.name))); return; }
        var pick = found.FirstOrDefault(e => e.name == name);
        if (pick.prefab == null) pick = found[0];
        if (pick.name == "dice" && int.TryParse(extra, out int face) && face >= 1 && face <= 6) { DiceEvent.ForcedNext = face; Print($"The die will land on {face}."); }
        var entry = RoomTemplates.FirstOrDefault(t => t.prefab == pick.prefab);
        LoadTemplate(entry.name ?? pick.prefab.name);
    }

    void Meddle(string what)
    {
        if (what != "add" && what != "steal") { Print("meddle add|steal"); return; }
        var loader = FindAnyObjectByType<TableLevelLoader>();
        var dungeon = loader != null ? loader.Dungeon : null;
        if (dungeon == null || PlayerManager.Instance.ActiveKind != PlayerKind.Table) { Print("Only on a dungeon floor."); return; }
        var meddling = dungeon.GetComponent<DungeonMasterMeddling>();
        if (meddling == null) meddling = dungeon.gameObject.AddComponent<DungeonMasterMeddling>();
        var said = meddling.Now(what);
        Print(said);
        if (said.StartsWith("He ")) Close();
    }

    void LoadTemplate(string name)
    {
        if (name.Length == 0) { Print("loadtemplate <name>: Tab completes the name."); return; }
        var exact = RoomTemplates.Where(t => string.Equals(t.name, name, System.StringComparison.OrdinalIgnoreCase)).ToList();
        var found = exact.Count > 0 ? exact : RoomTemplates.Where(t => t.name.StartsWith(name, System.StringComparison.OrdinalIgnoreCase)).ToList();
        if (found.Count == 0) { Print($"No room template called {name}."); return; }
        if (found.Count > 1) { Print($"{found.Count} templates start with {name}: " + string.Join(", ", found.Select(t => t.name))); return; }
        if (StartRun(found[0].prefab, found[0].name, out var said)) { Print(said); if (open) Close(); }
        else Print(said);
    }

    // A new run opening in this room template (loadtemplate, the spawn window's Rooms): false with why when it can't.
    // The loader takes over at once; the caller closes its window.
    public static bool StartRun(GameObject template, string label, out string said)
    {
        var loader = FindAnyObjectByType<TableLevelLoader>();
        if (loader == null) { said = "No table here (the Room scene has it)."; return false; }
        if (loader.Busy) { said = "A floor is being built. Try again in a moment."; return false; }
        var level = LevelFor(template, loader, out int stage);
        if (level == null) { said = "No dungeon level to build it in."; return false; }
        // A copy of the level that opens in this template, its run starting at the biome that uses it. The copy keeps
        // the level's name, so a saved checkpoint resumes the level itself.
        var copy = Instantiate(level);
        copy.name = level.name;
        copy.startingRoom = template;
        copy.fixedSeed = 0;
        // An event room opens the run as itself: no starting chest in the middle of the event.
        if (template.GetComponent<ScriptedEventRoom>() != null) copy.startingChest = false;
        if (stage > 0) copy.stages = copy.stages.Skip(stage).ToArray();
        DevGive.GoldAtNextRunStart();
        said = $"Loading {label} in {level.displayName}{(stage > 0 && copy.stages[0].biome != null ? $" ({copy.stages[0].biome.name})" : "")}.";
        loader.Load(copy, skipReveal: true);
        return true;
    }

    // The level to build a template in: one whose starting room it is or whose biomes pick it (the level being played
    // first), else the level being played, else the Dungeon button's pick, else the first generated dungeon in the
    // catalog. stage: the run stage whose biome picks it.
    public static TableLevelData LevelFor(GameObject template, TableLevelLoader loader, out int stage)
    {
        stage = 0;
        var candidates = new List<TableLevelData> { loader.Current };
        if (loader.Catalog != null && loader.Catalog.levels != null) candidates.AddRange(loader.Catalog.levels);
        candidates.AddRange(DebugDungeonSession.DebugDungeons);
        candidates = candidates.Where(l => l != null && l.IsDungeon && l.layoutMode != DungeonLayoutMode.Authored).Distinct().ToList();
        foreach (var level in candidates)
        {
            if (level.startingRoom == template) return level;
            for (int i = 0; level.stages != null && i < level.stages.Length; i++)
            {
                var biome = level.stages[i]?.biome;
                if (biome != null && biome.roomTemplates != null && biome.roomTemplates.Any(c => c != null && c.template == template)) { stage = i; return level; }
            }
            // A level without stages: its base biome's pool.
            var own = !level.HasStages ? level.baseBiome : null;
            if (own != null && own.roomTemplates != null && own.roomTemplates.Any(c => c != null && c.template == template)) return level;
        }
        var selected = DebugDungeonSession.SelectedDungeon;
        return candidates.FirstOrDefault(l => l == loader.Current)
            ?? (selected != null && candidates.Contains(selected) ? selected : candidates.FirstOrDefault());
    }

    void Revive()
    {
        var player = Active;
        if (player == null) { Print("No player."); return; }
        if (player.IsAlive) { Print("You're not dead."); return; }
        RunManager.RevivePlayer(player, out bool run, out bool counted);
        Print(!run ? "Revived." : counted
            ? "Revived. The run goes on, but that death was already counted: it is in the save and the run's spoils were banked."
            : "Revived. The run goes on; the death was not counted.");
    }

    void Print(string line)
    {
        history.Add(line);
        if (history.Count > 200) history.RemoveRange(0, history.Count - 200);
        Refresh();
    }

    void Refresh()
    {
        if (log == null) return;
        int from = Mathf.Max(0, history.Count - lines);
        log.text = string.Join("\n", history.GetRange(from, history.Count - from));
    }
}
#endif
