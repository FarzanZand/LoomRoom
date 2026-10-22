using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Asset Studio's library: every piece of the game's own content, sorted into categories, found from the assets
// themselves each refresh (nothing to keep in sync). An entry's permanent ID is ast_ + its Unity GUID, so renames and
// moves keep it. Retired content (_Archive folders) is left out. Export writes the whole catalog as JSON.
public static class AssetStudioCatalog
{
    public class Entry
    {
        public string id, guid, path, name, category, kind, description;
        public UnityEngine.Object asset;          // loaded on demand
        public UnityEngine.Object Load() => asset != null ? asset : asset = AssetDatabase.LoadMainAssetAtPath(path);
        public string Search;                     // lower-case text searched by the filter
        public bool inGame;                       // reached from a build scene or a Resources folder: it ships
    }

    public class Category
    {
        public string name;
        public string description;
        public readonly List<Entry> entries = new();
    }

    const string Root = "Assets/Game";

    public static string IdFor(string guid) => "ast_" + guid;

    static bool Retired(string path) => path.Contains("/_Archive/") || path.Contains("/Debug Dungeon/");

    public static List<Category> Build()
    {
        var cats = new List<Category>();
        Category Cat(string name, string description) { var c = new Category { name = name, description = description }; cats.Add(c); return c; }

        var items = Cat("Items", "Weapons, shields, armour, trinkets, consumables, spell tomes and keys (ItemData).");
        var enemies = Cat("Enemies", "Enemy prefabs, their EnemyData inside them.");
        var characters = Cat("Players & NPCs", "The two players and the people of the world.");
        var spells = Cat("Spells", "Spell definitions (SpellDefinition) and what they launch.");
        var rooms = Cat("Rooms", "Room templates: hand-built chunks, starting rooms, arenas and pockets.");
        var levels = Cat("Levels & biomes", "Table levels, their biomes and boss fights.");
        var props = Cat("Props", "Furniture, breakables, features and room props, dungeon and apartment.");
        var vfx = Cat("VFX", "Particle effects: spells, gore, hits, torches.");
        var sounds = Cat("Sounds", "Sound data (AudioData) and the clips they play.");
        var music = Cat("Music", "Music tracks.");
        var art = Cat("Painted art", "The painted pixel textures of items, props and characters.");
        var campaign = Cat("Campaign", "Places on the campaign map (CampaignEncounter) and campaign events (CampaignEvent). The World view shows how they hang together.");

        // Typed assets.
        AddTyped<ItemData>(items, i => i.itemType.ToString(), i => $"tier {i.tier} · {i.rarity}" + (string.IsNullOrEmpty(i.description) ? "" : " · " + i.description));
        AddTyped<SpellDefinition>(spells, s => "Spell", s => s.displayName);
        AddTyped<TableLevelData>(levels, l => $"Level · {l.kind}", l => l.name);
        AddTyped<DungeonBiome>(levels, b => "Biome", b => b.name);
        AddTyped<BossFight>(levels, b => "Boss fight", b => b.name);
        AddTyped<AudioData>(sounds, a => "Sound data", a => a.name);
        AddTyped<CampaignEncounter>(campaign, p => $"Place · Act {p.act}", p => p.displayName + " · " + p.KindName + (p.level != null ? " · " + p.level.name : ""));
        AddTyped<CampaignEvent>(campaign, e => $"Event · after Act {e.afterAct}", e => e.title);

        // Prefabs, sorted by what is on them.
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { Root }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Retired(path) || path.Contains("/Meshy/") && !path.Contains("/Meshy/Prefabs/")) continue;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) continue;
            if (go.GetComponent<DungeonRoomTemplate>() != null) { Add(rooms, guid, path, "Room template", Folder(path)); continue; }
            var character = go.GetComponent<Character>();
            if (character != null)
            {
                bool enemy = go.GetComponent<EnemyBrain>() != null;
                if (enemy && path.Contains("/Enemies/Base/")) continue;   // the shared bases, not enemies
                var data = AssetDatabase.LoadAllAssetsAtPath(path).OfType<CharacterData>().FirstOrDefault();
                Add(enemy ? enemies : characters, guid, path, enemy ? "Enemy" : go.GetComponent<Player>() != null ? "Player" : "Character",
                    data != null ? $"{data.name}" : Folder(path));
                continue;
            }
            if (path.Contains("/Items/Prefabs/") || path.Contains("/Combat/Spells/") && go.GetComponentInChildren<ParticleSystem>() == null) continue;   // shown through their item or spell
            if (go.GetComponentInChildren<ParticleSystem>(true) != null && (path.Contains("/Combat/") || path.Contains("/Effects/") || path.Contains("/Spells/")))
            { Add(vfx, guid, path, "Effect", Folder(path)); continue; }
            if (path.Contains("/Prefabs/") && (path.StartsWith(Root + "/Levels/") || path.StartsWith(Root + "/World/") || path.StartsWith(Root + "/Progression/")))
                Add(props, guid, path, "Prop", Folder(path));
        }

        // Clips: music by folder, the rest are sounds.
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Root + "/Audio", Root + "/UI/Audio", Root + "/Combat", Root + "/Levels" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Retired(path) || path.Contains("/Audio/Library/")) continue;   // the bought libraries are browsed in Unity
            bool isMusic = path.StartsWith(Root + "/Audio/Music/") || Path.GetDirectoryName(path).Replace('\\', '/') == Root + "/Audio";
            Add(isMusic ? music : sounds, guid, path, isMusic ? "Music" : "Clip", Folder(path));
        }

        // Painted textures (the house style's art), one per item, prop or character.
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Art/Synty Pixel" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.Contains("painted")) continue;
            Add(art, guid, path, "Painted texture", Folder(path));
        }

        foreach (var c in cats) c.entries.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        var shipped = InGame();
        foreach (var c in cats) foreach (var e in c.entries) e.inGame = shipped.Contains(e.path);
        return cats;
    }

    // What the game actually uses: everything the build's scenes and the Resources folders lead to, directly or not
    // (Unity's own dependency data), which is what a build takes in. A file only sitting in the folders is not in it.
    public static HashSet<string> InGame()
    {
        var roots = EditorBuildSettings.scenes.Where(s => s.enabled && !string.IsNullOrEmpty(s.path)).Select(s => s.path).ToList();
        foreach (var guid in AssetDatabase.FindAssets("", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/Resources/") && !AssetDatabase.IsValidFolder(path) && !path.Contains("/Editor/")) roots.Add(path);
        }
        return new HashSet<string>(AssetDatabase.GetDependencies(roots.ToArray(), true));
    }

    static void AddTyped<T>(Category c, Func<T, string> kind, Func<T, string> description) where T : UnityEngine.Object
    {
        foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { Root }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Retired(path)) continue;
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null) continue;
            string k = "", d = "";
            try { k = kind(a); d = description(a); } catch { }
            var e = Add(c, guid, path, k, d);
            e.asset = a;
            if (a is ItemData item && !string.IsNullOrEmpty(item.itemName)) { e.name = item.itemName; e.Search += " " + item.itemName.ToLowerInvariant() + " " + item.saveId; }
        }
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
