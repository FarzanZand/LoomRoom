using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// The kind of a hand-built room in Asset Studio (DungeonRoomTemplate.kind): Scripted, Active or Visual, set with a
// right-click on a room anywhere the studio shows one, with a small pixel icon for each and the rooms sortable by it.
// Guess reads a room's contents for a first sorting (its event scripts, then anything that does something in it).
public static class AssetStudioRoomKinds
{
    public static readonly DungeonRoomKind[] Order = { DungeonRoomKind.Scripted, DungeonRoomKind.Active, DungeonRoomKind.Visual, DungeonRoomKind.Unsorted };

    public static string Name(DungeonRoomKind k) => k switch
    {
        DungeonRoomKind.Scripted => "Scripted",
        DungeonRoomKind.Active => "Active",
        DungeonRoomKind.Visual => "Visual",
        _ => "Unsorted",
    };

    public static string Tip(DungeonRoomKind k) => k switch
    {
        DungeonRoomKind.Scripted => "Scripted: a room with an event of its own.",
        DungeonRoomKind.Active => "Active: something in it does something (spikes, a chest, a shrine, a pit, a portal).",
        DungeonRoomKind.Visual => "Visual: only decor.",
        _ => "Not sorted yet: right-click to give it a kind.",
    };

    public static DungeonRoomKind Of(GameObject room) => room != null && room.TryGetComponent<DungeonRoomTemplate>(out var t) ? t.kind : DungeonRoomKind.Unsorted;

    // Sorting rank: Scripted, Active, Visual, then the unsorted.
    public static int Rank(GameObject room) => Array.IndexOf(Order, Of(room));

    public static void Set(GameObject room, DungeonRoomKind kind)
    {
        if (room == null || !room.TryGetComponent<DungeonRoomTemplate>(out var t) || t.kind == kind) return;
        Undo.RecordObject(t, "Room kind");
        t.kind = kind;
        EditorUtility.SetDirty(t);
        AssetDatabase.SaveAssetIfDirty(room);
    }

    // Kind/Scripted, Kind/Active, ... on a room's right-click menu.
    public static void AddTo(GenericMenu menu, GameObject room, Action changed = null, string prefix = "Kind/")
    {
        var now = Of(room);
        foreach (var k in Order)
        {
            var kind = k;
            menu.AddItem(new GUIContent(prefix + Name(k)), now == k, () => { Set(room, kind); changed?.Invoke(); });
        }
    }

    // A first sorting from what the room holds: a script that runs an event of its own makes it Scripted; anything
    // that does something (a chest, spikes, a shrine or feature, a pit, a portal, a fixed ambush) Active; the rest Visual.
    public static DungeonRoomKind Guess(GameObject room)
    {
        if (room == null) return DungeonRoomKind.Unsorted;
        string path = AssetDatabase.GetAssetPath(room);
        if (path.Contains("/Rooms/Scripted/")) return DungeonRoomKind.Scripted;
        if (room.GetComponentInChildren<SkeletonParty>(true) || room.GetComponentInChildren<ArenaFight>(true) || room.GetComponentInChildren<TavernRoom>(true)
            || room.GetComponentInChildren<DungeonStage>(true) || room.GetComponentInChildren<WeepingEnemy>(true)) return DungeonRoomKind.Scripted;
        var t = room.GetComponent<DungeonRoomTemplate>();
        if (t != null && (t.roomEvent == DungeonRoomEvent.Ambush || t.roomEvent == DungeonRoomEvent.SleepingPack)) return DungeonRoomKind.Active;
        if (room.GetComponentsInChildren<DungeonSocket>(true).Any(s => s.type == DungeonSocketType.Chest || s.type == DungeonSocketType.Feature || s.type == DungeonSocketType.Boss || s.type == DungeonSocketType.Merchant))
            return DungeonRoomKind.Active;
        if (room.GetComponentsInChildren<DungeonContainer>(true).Any(c => c.chest) || room.GetComponentInChildren<DungeonSpikeTrap>(true) || room.GetComponentInChildren<DungeonShrine>(true)
            || room.GetComponentInChildren<PortalArch>(true) || room.GetComponentInChildren<DungeonFloorCut>(true) || room.GetComponentInChildren<DungeonFountain>(true)
            || room.GetComponentInChildren<DungeonAltar>(true) || room.GetComponentInChildren<DungeonGrave>(true) || room.GetComponentInChildren<DungeonBookshelf>(true))
            return DungeonRoomKind.Active;
        return DungeonRoomKind.Visual;
    }

    // ── The icons: small pixel pictures drawn here (a scroll, spikes, a candle), kept for the Editor session ──

    static readonly Dictionary<DungeonRoomKind, Texture2D> icons = new();

    // '.' clear, '#' outline; the rest per icon.
    static readonly string[] Scroll =
    {
        "............",
        ".##########.",
        "#pppppppppp#",
        ".#oooooooo#.",
        ".#o------o#.",
        ".#oooooooo#.",
        ".#o-----oo#.",
        ".#oooooooo#.",
        ".#o------o#.",
        ".#oooooooo#.",
        "#pppppppppp#",
        ".##########.",
    };
    static readonly string[] Spikes =
    {
        ".s...s...s..",
        ".s...s...s..",
        ".sd..sd..sd.",
        ".sd..sd..sd.",
        "ssd.ssd.ssd.",
        "ssd.ssd.ssd.",
        "ssddssddssdd",
        "############",
        "#pppppppppp#",
        "#p#pp#pp#pp#",
        "#pppppppppp#",
        "############",
    };
    static readonly string[] Candle =
    {
        ".....y......",
        "....yoy.....",
        "....oro.....",
        ".....|......",
        "....###.....",
        "....#w#.....",
        "....#w#.....",
        "....#w#.....",
        "....#w#.....",
        "..#######...",
        "..#bbbbb#...",
        "..#######...",
    };

    static readonly Dictionary<char, Color> Ink = new()
    {
        ['#'] = new Color(.08f, .07f, .06f),
        ['p'] = new Color(.62f, .45f, .22f),   // scroll rods, spike plate
        ['o'] = new Color(.93f, .84f, .62f),   // parchment; flame
        ['-'] = new Color(.45f, .36f, .26f),   // writing
        ['s'] = new Color(.82f, .84f, .88f),   // spike steel
        ['d'] = new Color(.42f, .44f, .5f),    // the spikes' shaded side
        ['y'] = new Color(1f, .9f, .45f),      // flame tip
        ['r'] = new Color(.95f, .45f, .12f),   // flame heart
        ['|'] = new Color(.15f, .12f, .1f),    // wick
        ['w'] = new Color(.9f, .86f, .74f),    // wax
        ['b'] = new Color(.72f, .56f, .26f),   // brass
    };

    public static Texture2D Icon(DungeonRoomKind kind)
    {
        if (kind == DungeonRoomKind.Unsorted) return null;
        if (icons.TryGetValue(kind, out var t) && t != null) return t;
        var rows = kind == DungeonRoomKind.Scripted ? Scroll : kind == DungeonRoomKind.Active ? Spikes : Candle;
        // The flame's own orange on the candle; the parchment colour elsewhere.
        var ink = new Dictionary<char, Color>(Ink);
        if (kind == DungeonRoomKind.Visual) ink['o'] = new Color(1f, .62f, .2f);
        t = new Texture2D(12, 12, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < 12; y++)
            for (int x = 0; x < 12; x++)
            {
                char c = rows[11 - y][x];   // rows top first; textures bottom first
                t.SetPixel(x, y, ink.TryGetValue(c, out var colour) ? colour : Color.clear);
            }
        t.Apply();
        icons[kind] = t;
        return t;
    }

    // The badge drawn on a room's picture or row: the icon on a dark plate, or a faint "?" while unsorted. Hovering shows
    // what the kind means.
    public static void Badge(Rect r, GameObject room)
    {
        var kind = Of(room);
        EditorGUI.DrawRect(r, new Color(.05f, .05f, .06f, kind == DungeonRoomKind.Unsorted ? .45f : .85f));
        var icon = Icon(kind);
        if (icon != null) GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), icon, ScaleMode.ScaleToFit);
        else GUI.Label(r, "?", new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(.6f, .58f, .55f) } });
        GUI.Label(r, new GUIContent("", Tip(kind)));
    }
}
