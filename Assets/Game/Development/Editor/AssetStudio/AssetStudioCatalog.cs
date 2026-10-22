using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Asset Studio's library: every piece of the game's own content, sorted into categories, found from the assets
// themselves (nothing to keep in sync). An entry's permanent ID is ast_ + its Unity GUID, so renames and moves keep it.
// Retired content (_Archive folders) is left out. Export writes the whole catalog as JSON.
// The window keeps the built catalog through recompiles (its entries are serializable); after that only the files
// changed since are looked at again (AssetStudioCatalogWatcher notes them, Apply sorts them in), so a recompile or Play
// never reads the whole project. Refresh library builds it all again, the in-game check marks included.
public static class AssetStudioCatalog
{
    [Serializable]
    public class Entry
    {
        public string id, guid, path, name, category, kind, description;
        [NonSerialized] public UnityEngine.Object asset;          // loaded on demand
        public UnityEngine.Object Load() => asset != null ? asset : asset = AssetDatabase.LoadMainAssetAtPath(path);
        public string Search;                     // lower-case text searched by the filter
        public bool inGame;                       // reached from a build scene or a Resources folder: it ships
        // Items only (for balancing): the main number (damage or armour), its slot and its price.
        public float itemMain;
        public string itemSlot;
        public int itemPrice, itemTier;
    }

    [Serializable]
    public class Category
    {
        public string name;
        public string description;
        public List<Entry> entries = new();
    }

    const string Root = "Assets/Game";

    public static string IdFor(string guid) => "ast_" + guid;

    static bool Retired(string path) => path.Contains("/_Archive/") || path.Contains("/Debug Dungeon/");

    static List<Category> NewCategories()
    {
        var cats = new List<Category>();
        void Cat(string name, string description) => cats.Add(new Category { name = name, description = description });
        Cat("Items", "Weapons, shields, armour, trinkets, consumables, spell tomes and keys (ItemData).");
        Cat("Enemies", "Enemy prefabs, their EnemyData inside them.");
        Cat("Players & NPCs", "The two players and the people of the world.");
        Cat("Spells", "Spell definitions (SpellDefinition) and what they launch.");
        Cat("Rooms", "Room templates: hand-built chunks, starting rooms, arenas and pockets.");
        Cat("Levels & biomes", "Table levels, their biomes, boss fights, map templates and the defaults every level shares.");
        Cat("Props", "Furniture, breakables, features and room props, dungeon and apartment.");
        Cat("VFX", "Particle effects: spells, gore, hits, torches.");
        Cat("Sounds", "Sound data (AudioData) and the clips they play.");
        Cat("Music", "Music tracks.");
        Cat("Painted art", "The painted pixel textures of items, props and characters.");
        Cat("Campaign", "Places on the campaign map (CampaignEncounter), campaign events (CampaignEvent) and the house rules (HouseRule). The World view shows how they hang together.");
        return cats;
    }

    // The files that can be in the library (Classify decides).
    static IEnumerable<string> Candidates()
    {
        var guids = new HashSet<string>();
        foreach (var t in new[] { "ItemData", "SpellDefinition", "TableLevelData", "DungeonBiome", "BossFight", "AudioData", "CampaignEncounter", "CampaignEvent", "TableLevelDefaults", "HouseRule", "Prefab" })
            guids.UnionWith(AssetDatabase.FindAssets("t:" + t, new[] { Root }));
        guids.UnionWith(AssetDatabase.FindAssets("t:AudioClip", new[] { Root + "/Audio", Root + "/UI/Audio", Root + "/Combat", Root + "/Levels" }.Where(AssetDatabase.IsValidFolder).ToArray()));
        if (AssetDatabase.IsValidFolder(Root + "/Art/Synty Pixel")) guids.UnionWith(AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Art/Synty Pixel" }));
        return guids.Select(AssetDatabase.GUIDToAssetPath);
    }

    public static List<Category> Build()
    {
        var cats = NewCategories();
        foreach (var path in Candidates()) Classify(cats, path);
        Sort(cats);
        shipped = InGame();
        foreach (var c in cats) foreach (var e in c.entries) e.inGame = shipped.Contains(e.path);
        return cats;
    }

    static void Sort(List<Category> cats)
    {
        foreach (var c in cats) c.entries.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
    }

    // Which category a file goes in, if any, and its entry there.
    static Entry Classify(List<Category> cats, string path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith(Root + "/") || Retired(path)) return null;
        var type = AssetDatabase.GetMainAssetTypeAtPath(path);
        if (type == null) return null;
        Category C(string name) => cats.First(c => c.name == name);
        string guid = AssetDatabase.AssetPathToGUID(path);

        if (typeof(ScriptableObject).IsAssignableFrom(type))
        {
            var a = AssetDatabase.LoadMainAssetAtPath(path);
            Entry e = null;
            try
            {
                switch (a)
                {
                    case ItemData i:
                        e = Add(C("Items"), guid, path, i.itemType.ToString(), "");
                        DescribeItem(e, i);
                        break;
                    case SpellDefinition s: e = Add(C("Spells"), guid, path, "Spell", s.displayName); break;
                    case TableLevelData l: e = Add(C("Levels & biomes"), guid, path, $"Level · {l.kind}", l.name); break;
                    case DungeonBiome b: e = Add(C("Levels & biomes"), guid, path, "Biome", b.name); break;
                    case BossFight f: e = Add(C("Levels & biomes"), guid, path, "Boss fight", f.name); break;
                    case TableLevelDefaults _: e = Add(C("Levels & biomes"), guid, path, "Level defaults", "Chests, the merchant, breakables, features, wall lights and HUD pieces every level shares."); break;
                    case AudioData d: e = Add(C("Sounds"), guid, path, "Sound data", d.name); break;
                    case CampaignEncounter p: e = Add(C("Campaign"), guid, path, $"Place · Act {p.act}", p.displayName + " · " + p.KindName + (p.level != null ? " · " + p.level.name : "") + (p.Locked ? $" · shut until {p.deathsToOpen} deaths in {p.deathsIn.displayName}" : "")); break;
                    case CampaignEvent ev: e = Add(C("Campaign"), guid, path, $"Event · after Act {ev.afterAct}", ev.title); break;
                    case HouseRule r: e = Add(C("Campaign"), guid, path, "House rule", r.text); break;
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[Asset Studio] {path}: {ex.Message}"); }
            if (e != null) e.asset = a;
            return e;
        }

        if (type == typeof(GameObject))
        {
            if (!path.EndsWith(".prefab") || path.Contains("/Meshy/") && !path.Contains("/Meshy/Prefabs/")) return null;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) return null;
            if (go.GetComponent<DungeonMapTemplate>() != null) return Add(C("Levels & biomes"), guid, path, "Map template", Folder(path));
            if (go.GetComponent<DungeonRoomTemplate>() != null) return Add(C("Rooms"), guid, path, "Room template", Folder(path));
            var character = go.GetComponent<Character>();
            if (character != null)
            {
                bool enemy = go.GetComponent<EnemyBrain>() != null;
                if (enemy && path.Contains("/Enemies/Base/")) return null;   // the shared bases, not enemies
                var data = AssetDatabase.LoadAllAssetsAtPath(path).OfType<CharacterData>().FirstOrDefault();
                return Add(enemy ? C("Enemies") : C("Players & NPCs"), guid, path, enemy ? "Enemy" : go.GetComponent<Player>() != null ? "Player" : "Character",
                    data != null ? $"{data.name}" : Folder(path));
            }
            if (path.Contains("/Items/Prefabs/") || path.Contains("/Combat/Spells/") && go.GetComponentInChildren<ParticleSystem>() == null) return null;   // shown through their item or spell
            if (go.GetComponentInChildren<ParticleSystem>(true) != null && (path.Contains("/Combat/") || path.Contains("/Effects/") || path.Contains("/Spells/")))
                return Add(C("VFX"), guid, path, "Effect", Folder(path));
            if (path.Contains("/Prefabs/") && (path.StartsWith(Root + "/Levels/") || path.StartsWith(Root + "/World/") || path.StartsWith(Root + "/Progression/")))
                return Add(C("Props"), guid, path, "Prop", Folder(path));
            return null;
        }

        // Clips: music by folder, the rest are sounds (the bought libraries are browsed in Unity).
        if (typeof(AudioClip).IsAssignableFrom(type))
        {
            if (!new[] { "/Audio/", "/UI/Audio/", "/Combat/", "/Levels/" }.Any(f => path.StartsWith(Root + f)) || path.Contains("/Audio/Library/")) return null;
            bool isMusic = path.StartsWith(Root + "/Audio/Music/") || Path.GetDirectoryName(path).Replace('\\', '/') == Root + "/Audio";
            return Add(isMusic ? C("Music") : C("Sounds"), guid, path, isMusic ? "Music" : "Clip", Folder(path));
        }

        // Painted textures (the house style's art), one per item, prop or character.
        if (typeof(Texture2D).IsAssignableFrom(type) && path.StartsWith(Root + "/Art/Synty Pixel/") && path.Contains("painted"))
            return Add(C("Painted art"), guid, path, "Painted texture", Folder(path));
        return null;
    }

    // An item's row: its main number (damage or armour), slot and price first, for balancing.
    static void DescribeItem(Entry e, ItemData i)
    {
        if (!string.IsNullOrEmpty(i.itemName)) { e.name = i.itemName; }
        string main = "";
        try { if (i.TryMainStat(out _, out float value, out string text)) { e.itemMain = value; main = (i.IsWeapon ? "Damage " : "Armour ") + text; } }
        catch { }
        e.itemSlot = i.spell != null ? "Spell tome" : i.IsWeapon || i.itemType == ItemType.Shield || i.canBeEquipped ? ItemData.SlotName(i.equipSlot) : i.itemType.ToString();
        e.itemPrice = i.value;
        e.itemTier = i.tier;
        e.description = string.Join(" · ", new[] { main, e.itemSlot, $"{i.value} gold", $"tier {i.tier}", i.rarity.ToString(), i.description }.Where(s => !string.IsNullOrWhiteSpace(s)));
        e.Search = $"{e.name} {e.id} {e.path} {e.kind} {e.description} {i.saveId}".ToLowerInvariant();
    }

    // ── Keeping a built catalog up to date ──

    static HashSet<string> shipped;   // the in-game set of the last full build (this session), for new entries

    // Files changed since the catalog was built (AssetStudioCatalogWatcher): their entries are made again. An entry
    // keeps its in-game mark; a new one reads it from the last full build's set (Refresh library brings it up to date).
    public static void Apply(List<Category> cats, ICollection<string> paths)
    {
        if (cats == null || paths.Count == 0) return;
        var all = cats.SelectMany(c => c.entries).ToList();
        foreach (var path in paths)
        {
            // The GUID only of a file that is there: a path moved or deleted can still answer with its old GUID, which
            // would take away the entry just made for the file's new place.
            string guid = File.Exists(path) ? AssetDatabase.AssetPathToGUID(path) : "";
            var old = all.Where(e => e.path == path || guid.Length > 0 && e.guid == guid).ToList();
            foreach (var c in cats) c.entries.RemoveAll(e => old.Contains(e));
            if (!File.Exists(path)) continue;
            var made = Classify(cats, path);
            if (made == null) continue;
            if (old.Count > 0) made.inGame = old[0].inGame;
            else { shipped ??= InGame(); made.inGame = shipped.Contains(path); }
        }
        Sort(cats);
    }

    // The library's file types under Assets/Game, and folders (anything else can't change it).
    static readonly string[] WatchedTypes = { ".asset", ".prefab", ".png", ".tga", ".psd", ".wav", ".ogg", ".mp3", ".aif", ".aiff" };
    public static bool Watched(string path) => path.StartsWith(Root + "/") && (IsFolderPath(path) || WatchedTypes.Any(t => path.EndsWith(t, StringComparison.OrdinalIgnoreCase)));
    static bool IsFolderPath(string path) => Path.GetExtension(path).Length == 0;

    const string ChangedKey = "LoomRoom.Studio.Changed", RebuildKey = "LoomRoom.Studio.Rebuild";
    public static event Action Changed;

    // Called by the watcher: kept in SessionState, so the paths outlive the recompile that may follow an import.
    // imported: files made or saved (a new folder adds nothing by itself: its files come as imports of their own);
    // gone: files and folders deleted or moved, from and to.
    public static void Note(IEnumerable<string> imported, IEnumerable<string> gone)
    {
        var goneList = gone.Where(Watched).ToList();
        var list = imported.Where(p => Watched(p) && !IsFolderPath(p)).Concat(goneList).ToList();
        if (list.Count == 0) return;
        var pending = new HashSet<string>(SessionState.GetString(ChangedKey, "").Split('\n').Where(s => s.Length > 0));
        pending.UnionWith(list);
        // A folder moved or deleted, or a great many files: build it all again instead.
        if (pending.Count > 400 || goneList.Any(IsFolderPath)) { SessionState.SetBool(RebuildKey, true); SessionState.EraseString(ChangedKey); }
        else SessionState.SetString(ChangedKey, string.Join("\n", pending));
        Changed?.Invoke();
    }

    // The files changed since the last call (null: so many, build it all again).
    public static List<string> TakeChanges()
    {
        if (SessionState.GetBool(RebuildKey, false)) { SessionState.SetBool(RebuildKey, false); SessionState.EraseString(ChangedKey); return null; }
        var list = SessionState.GetString(ChangedKey, "").Split('\n').Where(s => s.Length > 0).ToList();
        SessionState.EraseString(ChangedKey);
        return list;
    }
    public static bool HasChanges => SessionState.GetBool(RebuildKey, false) || SessionState.GetString(ChangedKey, "").Length > 0;
    public static void ClearChanges() { SessionState.SetBool(RebuildKey, false); SessionState.EraseString(ChangedKey); }

    // What the game actually uses: everything the build's scenes and the Resources folders lead to, directly or not
    // (Unity's own dependency data), which is what a build takes in. A file only sitting in the folders is not in it.
    public static HashSet<string> InGame()
    {
        var roots = EditorBuildSettings.scenes.Where(s => s.enabled && !string.IsNullOrEmpty(s.path)).Select(s => s.path).ToList();
        // Only inside Resources folders (outside Editor folders): every file in them ships.
        var resources = AssetDatabase.GetAllAssetPaths()
            .Where(p => p.StartsWith("Assets/") && Path.GetFileName(p) == "Resources" && !p.Contains("/Editor/") && AssetDatabase.IsValidFolder(p)).ToArray();
        if (resources.Length > 0)
            foreach (var guid in AssetDatabase.FindAssets("", resources))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!AssetDatabase.IsValidFolder(path) && !path.Contains("/Editor/")) roots.Add(path);
            }
        return new HashSet<string>(AssetDatabase.GetDependencies(roots.Distinct().ToArray(), true));
    }

    static Entry Add(Category c, string guid, string path, string kind, string description)
    {
        var e = new Entry
        {
            guid = guid, id = IdFor(guid), path = path, name = Path.GetFileNameWithoutExtension(path),
            category = c.name, kind = kind, description = description ?? "",
        };
        e.Search = $"{e.name} {e.id} {e.path} {e.kind} {e.description}".ToLowerInvariant();
        c.entries.Add(e);
        return e;
    }

    static string Folder(string path) => Path.GetDirectoryName(path).Replace('\\', '/').Replace(Root + "/", "");

    // The whole library as JSON (asset_studio_catalog.json), for Main or for review.
    [Serializable] class ExportEntry { public string id, name, category, kind, path, description; }
    [Serializable] class ExportFile { public string game = "The Table Awaits"; public string exported_at; public int total; public List<ExportEntry> entries = new(); }
    public static string Export(List<Category> cats, string file)
    {
        var x = new ExportFile { exported_at = DateTime.UtcNow.ToString("o") };
        foreach (var c in cats) foreach (var e in c.entries) x.entries.Add(new ExportEntry { id = e.id, name = e.name, category = e.category, kind = e.kind, path = e.path, description = e.description });
        x.total = x.entries.Count;
        File.WriteAllText(file, JsonUtility.ToJson(x, true));
        return file;
    }
}

// Notes every library file imported, deleted or moved, for AssetStudioCatalog.Apply.
class AssetStudioCatalogWatcher : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        AssetStudioCatalog.Note(importedAssets, deletedAssets.Concat(movedAssets).Concat(movedFromAssetPaths));
    }
}
