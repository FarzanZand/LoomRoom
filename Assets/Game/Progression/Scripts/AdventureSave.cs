using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

public class AdventureSave : MonoBehaviour
{
    [Serializable] public class Stack { public string item; public int count; public int used; }
    [Serializable] public class Blessing { public StatType stat; public ModifierType type; public float value, duration; }
    [Serializable] public class Checkpoint
    {
        public string level, classId;
        public int floor, seed, characterLevel, gold, lootLevel = 1;
        public float xp, health, mana, stamina;
        public int[] ranks;
        public int[] attributePoints;   // points from levels put in this run, by MetaAttribute (empty in older checkpoints)
        public int unspentPoints;
        public float[] skillExperience;
        public Stack[] bag, hotbar;
        public string[] equipped;
        public LeftHandSpell leftHand;
        public Blessing[] blessings;
        public float foodSeconds, foodRate, runSeconds;
        public int kills, bosses, goldFound, healthSlain;
    }
    [Serializable] public class Memorial
    {
        public string id, classId, className, killer;
        public int floor, level;
        public int[] skills;
        [Tooltip("A blank record older saves started with (the retired memorial table's figures): class only, no details.")]
        public bool forgotten;
    }
    // JsonUtility fills a new Data (and Checkpoint) from its field initialisers before reading, so a field
    // missing from an older file keeps its default here (lootLevel 1, empty memorial and flag lists).
    [Serializable] public class Data
    {
        public const int Current = 5;   // 2: skills raised by use (seven ranks, no partial training). 3: Stealth skill (eight ranks). 4: bag grid (items stored at their top-left cell). 5: realms (2026-10-30: the run's floors, event rooms and seeds changed; the user: drop those saves, fresh start)
        // 0 for a file written before versions existed (it then keeps no checkpoint); Write stamps Current.
        public int version;
        public Checkpoint checkpoint;
        public List<Memorial> memorials = new();
        public List<ProgressionManager.FlagEntry> flags = new();
        [Tooltip("The name written on the character sheet.")] public string characterName = "";
        // When it was written (UTC ticks): the file's own time is wrong after a copy (the rename's move to a new folder
        // gave every slot the same time). 0 in a file written before this existed: the file's time is used.
        public long savedAt;
    }
    public Data Saved { get; private set; } = new();
    [Tooltip("In the Unity editor, every Play session starts as a new game: the save file is neither read nor written. Turn off to test saving and continuing.")]
    [SerializeField] bool freshStartInEditor = true;
    // Built games always save; the editor only when Fresh Start In Editor is off.
    bool UsesFile => !(Application.isEditor && freshStartInEditor && !CustomFolder) && !fileLocked;
    bool fileLocked;
    public bool HasCheckpoint => Saved.checkpoint != null;
    public event Action Changed;
    Player player; AdventurerProgress progress;
    const string AdventureFile = "loomroom-adventure.json", DebugFile = "loomroom-debug.json";
    string FilePath => Path.Combine(Folder, DebugDungeonSession.Active ? DebugFile : SlotFile(Slot));

    // Where saves go: the game's own folder, or another one given with -savedir <folder> (two copies on one computer,
    // each with its own save).
    public static string Folder
    {
        get
        {
            if (folder != null) return folder;
            folder = Application.persistentDataPath;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i].Equals("-savedir", StringComparison.OrdinalIgnoreCase)) { folder = args[i + 1]; customFolder = true; }
#if UNITY_EDITOR
            var test = UnityEditor.SessionState.GetString(TestFolderKey, "");
            if (!string.IsNullOrEmpty(test)) { folder = test; customFolder = true; }
#endif
            return folder;
        }
    }
    static string folder;
    static bool customFolder;
    // Editor tests: a save folder for this editor session only (SessionState). Saves there are read and written even
    // with Fresh Start In Editor on, and the real save is never touched.
    public const string TestFolderKey = "LoomRoom.Save.TestFolder";

    // ── Save slots ───────────────────────────────────────────────────
    // Three saves side by side; the main menu picks one (New game, Continue, Load game) before the game's scene loads,
    // and the last one played is remembered, so Play straight from the Room scene uses it too. Slot 1 is the file
    // from before slots existed.
    public const int SlotCount = 3;
    const string SlotPref = "LoomRoom.SaveSlot";
    // A copy with its own folder (-savedir) keeps its slot to itself, so it never changes the game's remembered one.
    public static int Slot
    {
        get => CustomFolder ? customSlot : Mathf.Clamp(PlayerPrefs.GetInt(SlotPref, 1), 1, SlotCount);
        set
        {
            if (CustomFolder) { customSlot = Mathf.Clamp(value, 1, SlotCount); return; }
            PlayerPrefs.SetInt(SlotPref, Mathf.Clamp(value, 1, SlotCount)); PlayerPrefs.Save();
        }
    }
    static int customSlot = 1;
    // Domain reload is off in this project: the folder is found again at every Play (an editor test folder set in
    // between must take effect, and the real one must never be kept by mistake).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetFolder() { folder = null; customFolder = false; customSlot = 1; }
    static bool CustomFolder { get { _ = Folder; return customFolder; } }
    static string SlotFile(int slot) => slot <= 1 ? AdventureFile : $"loomroom-adventure-{slot}.json";
    static string SlotPath(int slot) => Path.Combine(Folder, SlotFile(slot));
    public static bool SlotExists(int slot) => File.Exists(SlotPath(slot)) || File.Exists(SlotPath(slot) + ".bak");

    // The name the player gives a new game on the main menu: the save is known by it (the save list, the character
    // sheet). Written into the new save when the game's scene makes it.
    public static string NewCharacterName;
    // Domain reload is off: a name typed on the menu must not carry over to the next Play session.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() => NewCharacterName = null;
    // The name a new game gets when none is typed (a pad has no keyboard); it can be changed on the character sheet.
    public const string DefaultName = "Adventurer";
    public const int NameLength = 16;
    // A name as it is kept: spaces run together and trimmed, at most NameLength letters.
    public static string CleanName(string text)
    {
        text = string.Join(" ", (text ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length > NameLength ? text.Substring(0, NameLength).TrimEnd() : text;
    }

    // Whether any game has been saved (the main menu offers Continue and Load game).
    public static bool SaveExists { get { for (int i = 1; i <= SlotCount; i++) if (SlotExists(i)) return true; return false; } }
    // The save played last (newest readable file), or 0 when there is none. An unreadable save is left to the save list.
    public static int LatestSlot
    {
        get
        {
            int best = 0; DateTime newest = DateTime.MinValue;
            for (int i = 1; i <= SlotCount; i++) { var s = Summarize(i); if (s.readable && s.lastPlayed > newest) { newest = s.lastPlayed; best = i; } }
            return best;
        }
    }
    // The first slot with no save, or 0 when all are taken.
    public static int FreeSlot { get { for (int i = 1; i <= SlotCount; i++) if (!SlotExists(i)) return i; return 0; } }
    static DateTime? LastWritten(int slot)
    {
        foreach (var path in new[] { SlotPath(slot), SlotPath(slot) + ".bak" }) if (File.Exists(path)) return File.GetLastWriteTime(path);
        return null;
    }

    // Removes a save for good (its backup too).
    public static void DeleteSlot(int slot)
    {
        var paths = new List<string> { SlotPath(slot), SlotPath(slot) + ".bak", SlotPath(slot) + ".tmp" };
        // Unreadable copies set aside earlier (KeepCorrupt) go with the save.
        try { var dir = Path.GetDirectoryName(SlotPath(slot)); if (Directory.Exists(dir)) paths.AddRange(Directory.GetFiles(dir, Path.GetFileName(SlotPath(slot)) + "*.corrupt-*")); } catch (Exception) { }
        foreach (var path in paths)
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception e) { Debug.LogError($"Could not delete save {path}: {e.Message}"); }
    }

    // What the main menu shows of a save without loading the game.
    public struct Summary
    {
        public bool exists, readable;
        public string name;
        public int runs, trophies, spoils, runFloor;
        public DateTime lastPlayed;
    }
    public static Summary Summarize(int slot)
    {
        var summary = new Summary { exists = SlotExists(slot) };
        if (!summary.exists) return summary;
        summary.lastPlayed = LastWritten(slot) ?? DateTime.MinValue;
        foreach (var path in new[] { SlotPath(slot), SlotPath(slot) + ".bak" })
        {
            if (!File.Exists(path)) continue;
            try
            {
                var data = JsonUtility.FromJson<Data>(File.ReadAllText(path));
                if (data == null) continue;
                summary.readable = true;
                summary.name = data.characterName;
                summary.runs = data.memorials != null ? data.memorials.Count(m => m != null && !m.forgotten) : 0;
                if (data.flags != null)
                    foreach (var flag in data.flags)
                    {
                        if (flag.key == ProgressionManager.SpoilsFlag) summary.spoils = flag.value;
                        else if (flag.key.StartsWith(ProgressionManager.TrophyPrefix) && flag.value == (int)ProgressionManager.TrophyState.Mounted) summary.trophies++;
                    }
                summary.runFloor = data.version == Data.Current && data.checkpoint != null ? data.checkpoint.floor : 0;
                if (data.savedAt > 0) summary.lastPlayed = new DateTime(data.savedAt, DateTimeKind.Utc).ToLocalTime();
                return summary;
            }
            catch (Exception e) { Debug.LogWarning($"Could not read save {path}: {e.Message}"); }
        }
        return summary;
    }

    // The game was called LoomRoom until 2026-10-13 and The Table Waits until 2026-10-22; the save folder follows the
    // product name, so saves from before a rename sit in the old folder beside the new one (the newest name first). Brought over once ever (a PlayerPrefs marker, also set by any save
    // written here), and only into a folder with no saves yet: a save deleted later must stay deleted. Called by the main
    // menu as it opens and by Initialize.
    const string MigratedPref = "LoomRoom.SavesMigrated";
    static readonly string[] OldProductNames = { "The Table Waits", "LoomRoom" };
    static void MarkMigrated()
    {
        if (PlayerPrefs.GetInt(MigratedPref, 0) == 1) return;
        PlayerPrefs.SetInt(MigratedPref, 1); PlayerPrefs.Save();
    }
    public static void MigrateOldSaves()
    {
        if (PlayerPrefs.GetInt(MigratedPref, 0) == 1 || CustomFolder) return;   // a copy with its own folder (-savedir) leaves the game's alone
        MarkMigrated();
        try
        {
            var current = Application.persistentDataPath;
            if (Directory.Exists(current) && Directory.GetFiles(current, "loomroom-*.json*").Length > 0) return;
            foreach (var oldName in OldProductNames)
            {
                var old = Path.Combine(Path.GetDirectoryName(current) ?? "", oldName);
                if (!Directory.Exists(old) || string.Equals(Path.GetFullPath(old), Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase)) continue;
                var files = Directory.GetFiles(old, "loomroom-*.json*");
                if (files.Length == 0) continue;
                Directory.CreateDirectory(current);
                // Every save slot, the debug save and their backups.
                foreach (var from in files)
                {
                    var to = Path.Combine(current, Path.GetFileName(from));
                    if (!File.Exists(to)) { File.Copy(from, to); File.SetLastWriteTime(to, File.GetLastWriteTime(from)); }   // keeps "last played" true
                }
                return;
            }
        }
        catch (Exception e) { Debug.LogWarning($"Could not bring over saves from before the rename: {e.Message}"); }
    }
    bool loading, initialized;
    void Start() => Initialize();
    public void Initialize()
    {
        if (initialized) return;
        initialized = true;
        player = GetComponent<Player>(); progress = GetComponent<AdventurerProgress>();
        if (UsesFile) MigrateOldSaves();
        bool existed = UsesFile && Read();
        Saved.memorials ??= new(); Saved.flags ??= new();
        bool named = !existed && !string.IsNullOrEmpty(NewCharacterName);
        if (named) Saved.characterName = CleanName(NewCharacterName);
        NewCharacterName = null;
        if (ProgressionManager.HasInstance)
        {
            loading = true;
            foreach (var flag in Saved.flags) ProgressionManager.Instance.SetFlag(flag.key, flag.value);
            loading = false; ProgressionManager.Instance.FlagChanged += FlagChanged;
        }
        if (RunManager.HasInstance) { RunManager.Instance.RunEnded += Ended; RunManager.Instance.Revived += Revived; }
        if (named || ProgressionManager.HasInstance && ProgressionManager.Instance.IntroSkipped) Write();
    }

    void OnDestroy()
    {
        if (ProgressionManager.HasInstance) ProgressionManager.Instance.FlagChanged -= FlagChanged;
        if (RunManager.HasInstance) { RunManager.Instance.RunEnded -= Ended; RunManager.Instance.Revived -= Revived; }
    }
    // False when there is no save yet, or none could be read (the unreadable files are then kept aside).
    bool Read()
    {
        bool unreadable = false;
        foreach (var path in new[] { FilePath, FilePath + ".bak" })
        {
            if (!File.Exists(path)) continue;
            try { var data = JsonUtility.FromJson<Data>(File.ReadAllText(path)); if (data == null) { unreadable = true; continue; }
                // Older saves keep their memorials and unlocks; a run checkpoint from another format is dropped.
                if (data.version != Data.Current) { data.checkpoint = null; data.version = Data.Current; }
                // Read from the backup: set the broken save aside, or the next write would move it over the good backup.
                if (unreadable) KeepCorrupt(FilePath);
                Saved = data; return true; }
            // A file that is there but can't be opened (locked by a virus scanner or cloud sync) isn't broken: leave it,
            // and don't write over it this session.
            catch (IOException e) { fileLocked = true; Debug.LogWarning($"Adventure save is locked, not used this session: {e.Message}"); return false; }
            catch (Exception e) { unreadable = true; Debug.LogWarning($"Could not read adventure save: {e.Message}"); }
        }
        if (unreadable) KeepCorrupt(FilePath, FilePath + ".bak");
        return false;
    }

    // An unreadable save (or its backup too): move it to "<name>.corrupt-<time>" so the next write doesn't overwrite
    // it and it can still be looked at or recovered.
    void KeepCorrupt(params string[] paths)
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            try { File.Move(path, $"{path}.corrupt-{stamp}"); Debug.LogWarning($"Unreadable adventure save kept as {path}.corrupt-{stamp}"); }
            catch (Exception e) { Debug.LogError($"Could not keep the unreadable adventure save {path}: {e.Message}"); }
        }
    }
    void FlagChanged(string key, int value) { if (!loading) Write(); }
    public void Write()
    {
        // Playing in someone else's co-op game: this player's save is left as it was (MultiplayerManager.partyProgression
        // adds what was earned when the game ends).
        // Their own meta progression (spoils, the sheet, the trophies) is still written at once (GuestSaveFlags).
        if (!UsesFile) { Changed?.Invoke(); return; }
        Saved.version = Data.Current;
        Saved.savedAt = DateTime.UtcNow.Ticks;
        if (MultiplayerManager.HasInstance && MultiplayerManager.Instance.HoldsSave)
        {
            var own = MultiplayerManager.Instance.GuestSaveFlags();
            if (own == null) { Changed?.Invoke(); return; }
            Saved.flags = own;
        }
        else if (ProgressionManager.HasInstance) Saved.flags = ProgressionManager.Instance.AllFlags.Select(x => new ProgressionManager.FlagEntry { key = x.Key, value = x.Value }).ToList();
        try
        {
            Directory.CreateDirectory(Folder);
            if (!CustomFolder) MarkMigrated();   // this folder has saves of its own now
            File.WriteAllText(FilePath + ".tmp", JsonUtility.ToJson(Saved, true));
            if (File.Exists(FilePath)) File.Replace(FilePath + ".tmp", FilePath, FilePath + ".bak");
            else File.Move(FilePath + ".tmp", FilePath);
        }
        catch (Exception e) { Debug.LogError($"Adventure save failed: {e.Message}"); NotificationUI.Show("Could not save the adventure"); }
        Changed?.Invoke();
    }
    Stack[] Capture(Inventory inventory)
    {
        var result = new Stack[inventory.SlotCount];
        for (int i = 0; i < result.Length; i++) if (inventory.ItemAt(i) != null) result[i] = new Stack { item = inventory.ItemAt(i).saveId, count = inventory.CountAt(i), used = inventory.UsedAt(i) };
        return result;
    }
    public void CaptureFloor()
    {
        if (!progress.InRun || IntroController.PracticeRunning) return;
        if (MultiplayerManager.HasInstance && MultiplayerManager.Instance.HoldsSave) return;
        var run = RunManager.Instance;
        Saved.checkpoint = new Checkpoint { level = run.Level.name, floor = run.Floor, seed = run.Seed, classId = progress.selectedClass.id,
            characterLevel = progress.Level, lootLevel = TableManager.Loader.GenerationLevel, xp = progress.Experience, skillExperience = (float[])progress.SkillExperience.Clone(), ranks = (int[])progress.Ranks.Clone(), attributePoints = (int[])progress.AttributePoints.Clone(), unspentPoints = progress.UnspentPoints,
            health = player.Stats.CurrentHealth, mana = player.Stats.CurrentMana, stamina = player.Stats.CurrentStamina, gold = player.Wallet.Gold,
            bag = Capture(player.Bag), hotbar = Capture(player.Hotbar), equipped = player.Equipment.EquippedItems.Select(x => x.saveId).ToArray(), leftHand = GetComponent<PlayerSpellcasting>().Equipped,
            blessings = player.Stats.Modifiers.Where(m => ReferenceEquals(m.Source, RunManager.BlessingSource)).Select(m => new Blessing { stat=m.Stat, type=m.Type, value=m.Value, duration=m.Duration }).ToArray(),
            foodSeconds=player.Stats.FoodRemaining, foodRate=player.Stats.FoodHealingPerSecond, runSeconds=run.Seconds, kills=run.Kills, bosses=run.BossesSlain, goldFound=run.GoldFound, healthSlain=run.HealthSlain };
        Write();
    }
    public bool CanRestore(Checkpoint checkpoint)
    {
        if (checkpoint == null || checkpoint.bag == null || checkpoint.hotbar == null || checkpoint.equipped == null || checkpoint.ranks?.Length != AdventureSkills.Count || checkpoint.floor < 1 || progress.rules == null || !progress.rules.classes.Any(x => x != null && x.id == checkpoint.classId)) return false;
        // Items that no longer exist (retired to _Archive, dropped from the catalog) don't block the run: Restore leaves them out.
        return true;
    }
    public void Restore(Checkpoint checkpoint)
    {
        progress.selectedClass = progress.rules.classes.First(x => x != null && x.id == checkpoint.classId);
        player.Equipment.UnequipAll(); player.Bag.Clear(); player.Hotbar.Clear();
        // Fine and Masterwork gear is saved with its rolls ("<saveId>~2:...") and comes back as the same copy; the "+1" of retired Honed gear is dropped on load.
        // An item that no longer resolves (retired since the save) is left out and logged; the rest of the run comes back.
        var missing = new List<string>();
        ItemData Item(string id) { var item = ItemData.FromSaveId(id, InventoryManager.Instance.itemCatalog); if (item == null) missing.Add(id); return item; }
        // An item whose bag footprint grew since the save (tomes became 2x2) and no longer fits at its cell goes to the
        // first free spot afterwards, then the hotbar, then the floor, so nothing overlaps and nothing is lost.
        var homeless = new List<ItemStack>();
        void Fill(Inventory inventory, Stack[] stacks) { for (int i = 0; i < stacks.Length && i < inventory.SlotCount; i++) if (stacks[i] != null && stacks[i].count > 0 && Item(stacks[i].item) is ItemData item) { if (!inventory.IsGrid || inventory.Fits(item, i)) inventory.Set(i, new ItemStack(item, stacks[i].count) { used = stacks[i].used }); else homeless.Add(new ItemStack(item, stacks[i].count)); } }
        Fill(player.Bag, checkpoint.bag); Fill(player.Hotbar, checkpoint.hotbar);
        foreach (var stack in homeless)
            if (!player.Bag.TryAdd(stack.item, stack.count) && !player.Hotbar.TryAdd(stack.item, stack.count))
                for (int n = 0; n < stack.count; n++) InventoryManager.Instance.DropFromPlayer(stack.item, player);
        foreach (var id in checkpoint.equipped) if (Item(id) is ItemData worn) player.Equipment.Equip(worn, false, grant: true);
        if (missing.Count > 0) Debug.LogWarning("[AdventureSave] Left out items that no longer exist: " + string.Join(", ", missing));
        progress.Restore(checkpoint.characterLevel, checkpoint.xp, (int[])checkpoint.ranks.Clone(), checkpoint.skillExperience, (int[])checkpoint.attributePoints?.Clone(), checkpoint.unspentPoints);
        player.Stats.RemoveAllFromSource(RunManager.BlessingSource);
        if (checkpoint.blessings != null) foreach(var b in checkpoint.blessings) player.Stats.AddModifier(new StatModifier(b.stat,b.value,b.type,RunManager.BlessingSource,b.duration));
        player.Stats.ClearFood();
        if (checkpoint.foodSeconds > 0) player.Stats.EatFood(checkpoint.foodRate, checkpoint.foodSeconds);
        player.Stats.RestorePools(checkpoint.health, checkpoint.mana, checkpoint.stamina);
        player.Wallet.Clear(); player.Wallet.Add(checkpoint.gold);
        RunManager.Instance.RestoreTotals(checkpoint.kills, checkpoint.bosses, checkpoint.goldFound, checkpoint.runSeconds, checkpoint.healthSlain);
        if (!player.Equipment.Has(EquipmentSlot.LeftHand) && checkpoint.leftHand != LeftHandSpell.Shield)
            GetComponent<PlayerSpellcasting>().ResetHand(checkpoint.leftHand);
        else GetComponent<PlayerSpellcasting>().RestoreEquippedHand();
    }
    // The memorial added for the last death, until the next run ends: a revive takes it back.
    string deathMemorial;
    void Ended(bool victory)
    {
        if (MultiplayerManager.HasInstance && MultiplayerManager.Instance.HoldsSave) return;
        deathMemorial = null;
        if (!victory)
        {
            var memorial = new Memorial { id = Guid.NewGuid().ToString("N"), classId = progress.selectedClass.id, className = progress.selectedClass.displayName, killer = RunManager.Instance.Killer, floor = RunManager.Instance.Floor, level = progress.Level, skills = (int[])progress.Ranks.Clone() };
            Saved.memorials.Add(memorial);
            deathMemorial = memorial.id;
        }
        Saved.checkpoint = null; Write();
    }
    // Back on your feet after a death that was already saved: that death's memorial goes and the run gets its
    // checkpoint again, taken where the player stands now (the spoils were banked, so none are carried).
    void Revived(bool counted)
    {
        if (!counted) return;
        if (deathMemorial != null) { Saved.memorials.RemoveAll(m => m != null && m.id == deathMemorial); deathMemorial = null; }
        if (progress != null && progress.InRun && !IntroController.PracticeRunning) CaptureFloor();
        else Write();
    }
}
