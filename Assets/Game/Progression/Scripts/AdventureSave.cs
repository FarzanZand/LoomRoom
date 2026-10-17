using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

public class AdventureSave : MonoBehaviour
{
    [Serializable] public class Stack { public string item; public int count; }
    [Serializable] public class Blessing { public StatType stat; public ModifierType type; public float value, duration; }
    [Serializable] public class Checkpoint
    {
        public string level, classId;
        public int floor, seed, characterLevel, gold, lootLevel = 1;
        public float xp, health, mana, stamina;
        public int[] ranks, growth;
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
        [Tooltip("A starting memorial: a figure from before the player remembers, with no details.")]
        public bool forgotten;
    }
    // JsonUtility fills a new Data (and Checkpoint) from its field initialisers before reading, so a field
    // missing from an older file keeps its default here (lootLevel 1, empty memorial and flag lists).
    [Serializable] public class Data
    {
        public const int Current = 4;   // 2: Barony skills (seven ranks, no partial training). 3: Stealth skill (eight ranks). 4: Diablo bag grid (items stored at their top-left cell)
        public int version = Current;
        public Checkpoint checkpoint;
        public List<Memorial> memorials = new();
        public List<ProgressionManager.FlagEntry> flags = new();
        [Tooltip("The name written on the character sheet.")] public string characterName = "";
    }
    public Data Saved { get; private set; } = new();
    [Tooltip("In the Unity editor, every Play session starts as a new game: the save file is neither read nor written. Turn off to test saving and continuing.")]
    [SerializeField] bool freshStartInEditor = true;
    // Built games always save; the editor only when Fresh Start In Editor is off.
    bool UsesFile => !(Application.isEditor && freshStartInEditor);
    public bool HasCheckpoint => Saved.checkpoint != null;
    public event Action Changed;
    Player player; AdventurerProgress progress;
    const string AdventureFile = "loomroom-adventure.json", DebugFile = "loomroom-debug.json";
    string FilePath => Path.Combine(Application.persistentDataPath, DebugDungeonSession.Active ? DebugFile : SlotFile(Slot));

    // ── Save slots ───────────────────────────────────────────────────
    // Three saves side by side; the main menu picks one (New game, Continue, Load game) before the game's scene loads,
    // and the last one played is remembered, so Play straight from the Room scene uses it too. Slot 1 is the file
    // from before slots existed.
    public const int SlotCount = 3;
    const string SlotPref = "LoomRoom.SaveSlot";
    public static int Slot
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(SlotPref, 1), 1, SlotCount);
        set { PlayerPrefs.SetInt(SlotPref, Mathf.Clamp(value, 1, SlotCount)); PlayerPrefs.Save(); }
    }
    static string SlotFile(int slot) => slot <= 1 ? AdventureFile : $"loomroom-adventure-{slot}.json";
    static string SlotPath(int slot) => Path.Combine(Application.persistentDataPath, SlotFile(slot));
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
            for (int i = 1; i <= SlotCount; i++) { var time = LastWritten(i); if (time.HasValue && time.Value > newest && Summarize(i).readable) { newest = time.Value; best = i; } }
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
        foreach (var path in new[] { SlotPath(slot), SlotPath(slot) + ".bak", SlotPath(slot) + ".tmp" })
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
                return summary;
            }
            catch (Exception e) { Debug.LogWarning($"Could not read save {path}: {e.Message}"); }
        }
        return summary;
    }

    // The game was called LoomRoom until 2026-10-13; the save folder follows the product name, so saves from before the
    // rename sit in the old folder beside the new one. Brought over once ever (a PlayerPrefs marker, also set by any save
    // written here), and only into a folder with no saves yet: a save deleted later must stay deleted. Called by the main
    // menu as it opens and by Initialize.
    const string MigratedPref = "LoomRoom.SavesMigrated";
    static void MarkMigrated()
    {
        if (PlayerPrefs.GetInt(MigratedPref, 0) == 1) return;
        PlayerPrefs.SetInt(MigratedPref, 1); PlayerPrefs.Save();
    }
    public static void MigrateOldSaves()
    {
        if (PlayerPrefs.GetInt(MigratedPref, 0) == 1) return;
        MarkMigrated();
        try
        {
            var current = Application.persistentDataPath;
            var old = Path.Combine(Path.GetDirectoryName(current) ?? "", "LoomRoom");
            if (!Directory.Exists(old) || string.Equals(Path.GetFullPath(old), Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase)) return;
            if (Directory.Exists(current) && Directory.GetFiles(current, "loomroom-*.json*").Length > 0) return;
            Directory.CreateDirectory(current);
            foreach (var name in new[] { AdventureFile, AdventureFile + ".bak", DebugFile, DebugFile + ".bak" })
            {
                string from = Path.Combine(old, name), to = Path.Combine(current, name);
                if (File.Exists(from) && !File.Exists(to)) File.Copy(from, to);
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
        if (RunManager.HasInstance) RunManager.Instance.RunEnded += Ended;
        if (!existed) SeedStartingMemorials();
        if (named || ProgressionManager.HasInstance && ProgressionManager.Instance.IntroSkipped) Write();
    }

    // A new save starts with the rules' blank figures on the memorial table.
    void SeedStartingMemorials()
    {
        var starting = progress != null && progress.rules != null ? progress.rules.startingMemorials : null;
        if (starting == null || starting.Length == 0) return;
        foreach (var c in starting)
            if (c != null) Saved.memorials.Add(new Memorial { id = Guid.NewGuid().ToString("N"), classId = c.id, className = c.displayName, forgotten = true });
        Write();
    }
    void OnDestroy()
    {
        if (ProgressionManager.HasInstance) ProgressionManager.Instance.FlagChanged -= FlagChanged;
        if (RunManager.HasInstance) RunManager.Instance.RunEnded -= Ended;
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
        if (!UsesFile) { Changed?.Invoke(); return; }
        if (ProgressionManager.HasInstance) Saved.flags = ProgressionManager.Instance.AllFlags.Select(x => new ProgressionManager.FlagEntry { key = x.Key, value = x.Value }).ToList();
        try
        {
            Directory.CreateDirectory(Application.persistentDataPath);
            MarkMigrated();   // this folder has saves of its own now
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
        for (int i = 0; i < result.Length; i++) if (inventory.ItemAt(i) != null) result[i] = new Stack { item = inventory.ItemAt(i).saveId, count = inventory.CountAt(i) };
        return result;
    }
    public void CaptureFloor()
    {
        if (!progress.InRun || IntroController.PracticeRunning) return;
        var run = RunManager.Instance;
        Saved.checkpoint = new Checkpoint { level = run.Level.name, floor = run.Floor, seed = run.Seed, classId = progress.selectedClass.id,
            characterLevel = progress.Level, lootLevel = TableManager.Loader.GenerationLevel, xp = progress.Experience, skillExperience = (float[])progress.SkillExperience.Clone(), ranks = (int[])progress.Ranks.Clone(), growth = (int[])progress.Growth.Clone(), attributePoints = (int[])progress.AttributePoints.Clone(), unspentPoints = progress.UnspentPoints,
            health = player.Stats.CurrentHealth, mana = player.Stats.CurrentMana, stamina = player.Stats.CurrentStamina, gold = player.Wallet.Gold,
            bag = Capture(player.Bag), hotbar = Capture(player.Hotbar), equipped = player.Equipment.EquippedItems.Select(x => x.saveId).ToArray(), leftHand = GetComponent<PlayerSpellcasting>().Equipped,
            blessings = player.Stats.Modifiers.Where(m => ReferenceEquals(m.Source, RunManager.BlessingSource)).Select(m => new Blessing { stat=m.Stat, type=m.Type, value=m.Value, duration=m.Duration }).ToArray(),
            foodSeconds=player.Stats.FoodRemaining, foodRate=player.Stats.FoodHealingPerSecond, runSeconds=run.Seconds, kills=run.Kills, bosses=run.BossesSlain, goldFound=run.GoldFound, healthSlain=run.HealthSlain };
        Write();
    }
    public bool CanRestore(Checkpoint checkpoint)
    {
        if (checkpoint == null || checkpoint.bag == null || checkpoint.hotbar == null || checkpoint.equipped == null || checkpoint.ranks?.Length != AdventureSkills.Count || checkpoint.growth?.Length != AdventurerProgress.StatNames.Length || checkpoint.floor < 1 || progress.rules == null || !progress.rules.classes.Any(x => x != null && x.id == checkpoint.classId)) return false;
        var catalog = InventoryManager.Instance.itemCatalog;
        return checkpoint.equipped.All(id => ItemData.FromSaveId(id, catalog) != null) && checkpoint.bag.Concat(checkpoint.hotbar).All(x => x == null || x.count <= 0 || ItemData.FromSaveId(x.item, catalog) != null);
    }
    public void Restore(Checkpoint checkpoint)
    {
        progress.selectedClass = progress.rules.classes.First(x => x != null && x.id == checkpoint.classId);
        player.Equipment.UnequipAll(); player.Bag.Clear(); player.Hotbar.Clear();
        // Fine and Masterwork gear is saved with its rolls ("<saveId>~2:...") and comes back as the same copy; the "+1" of retired Honed gear is dropped on load.
        ItemData Item(string id) => ItemData.FromSaveId(id, InventoryManager.Instance.itemCatalog);
        void Fill(Inventory inventory, Stack[] stacks) { for (int i = 0; i < stacks.Length && i < inventory.SlotCount; i++) if (stacks[i] != null && stacks[i].count > 0) inventory.Set(i, new ItemStack(Item(stacks[i].item), stacks[i].count)); }
        Fill(player.Bag, checkpoint.bag); Fill(player.Hotbar, checkpoint.hotbar);
        foreach (var id in checkpoint.equipped) player.Equipment.Equip(Item(id), false, grant: true);
        progress.Restore(checkpoint.characterLevel, checkpoint.xp, (int[])checkpoint.ranks.Clone(), (int[])checkpoint.growth.Clone(), checkpoint.skillExperience, checkpoint.attributePoints, checkpoint.unspentPoints);
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
    void Ended(bool victory)
    {
        if (!victory) Saved.memorials.Add(new Memorial { id = Guid.NewGuid().ToString("N"), classId = progress.selectedClass.id, className = progress.selectedClass.displayName, killer = RunManager.Instance.Killer, floor = RunManager.Instance.Floor, level = progress.Level, skills = (int[])progress.Ranks.Clone() });
        Saved.checkpoint = null; Write();
    }
}
