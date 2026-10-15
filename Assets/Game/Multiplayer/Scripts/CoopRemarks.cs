using System.Collections.Generic;
using UnityEngine;

// Co-op: the Dungeon Master talks to the party. On every machine (each says it to its own player) it watches the
// others' states and speaks when a run starts with more than one player, when a teammate falls (the first one of a run
// gets its own line), when a fallen teammate is back on a new floor and when the whole party is down. {name} is the
// teammate's name, {names} everyone in the run. Lines are picked at random; leave a list empty to stay quiet.
public class CoopRemarks : MonoBehaviour
{
    [SerializeField] DungeonMaster.Line[] partyStart =
    {
        new() { text = "{names}. Together, then." },
        new() { text = "Stay close, all of you." },
    };
    [Tooltip("The first teammate down in a run.")]
    [SerializeField] DungeonMaster.Line[] firstFall =
    {
        new() { text = "{name} first. I will remember that." },
        new() { text = "{name} is down already." },
    };
    [SerializeField] DungeonMaster.Line[] teammateFell =
    {
        new() { text = "{name} is down." },
        new() { text = "There goes {name}." },
    };
    [Tooltip("A fallen teammate up again on the next floor.")]
    [SerializeField] DungeonMaster.Line[] teammateBack =
    {
        new() { text = "{name} is back. Try to keep them this time." },
        new() { text = "{name} again. Up you get." },
    };
    [SerializeField] DungeonMaster.Line[] partyDown =
    {
        new() { text = "All of you. That was something." },
    };

    readonly Dictionary<CoopPlayer, (bool inRun, bool alive)> seen = new();
    bool anyFell, runOn;

    void Update()
    {
        if (!Coop.Running || !MultiplayerManager.HasInstance) { seen.Clear(); runOn = false; return; }
        var manager = MultiplayerManager.Instance;
        bool localRun = manager.LocalInRun;
        // A run starts: the others join it within a moment, so the party is counted a little later.
        if (localRun && !runOn) { anyFell = false; startSaid = false; runStarted = Time.time; }
        runOn = localRun;
        int inRun = 0, alive = 0;
        foreach (var p in manager.Players)
        {
            if (p == null) continue;
            var s = p.Current;
            if (s.inRun) { inRun++; if (s.alive) alive++; }
            if (!seen.TryGetValue(p, out var was)) { seen[p] = (s.inRun, s.alive); continue; }
            if (p.IsOwner && localRun && was.alive && !s.alive) anyFell = true;   // this player's own fall counts as the first too
            if (!p.IsOwner && localRun && s.inRun && was.inRun)
            {
                if (was.alive && !s.alive) { Say(anyFell ? teammateFell : firstFall, p.DisplayName, null); anyFell = true; }
                else if (!was.alive && s.alive) Say(teammateBack, p.DisplayName, null);
            }
            seen[p] = (s.inRun, s.alive);
        }
        if (localRun && !startSaid && Time.time > runStarted + 3f) { startSaid = true; if (inRun > 1) Say(partyStart, null, Names(manager)); }
        if (localRun && inRun > 1 && alive == 0 && !wiped) { wiped = true; Say(partyDown, null, null); }
        if (alive > 0 || !localRun) wiped = false;
    }

    bool wiped, startSaid;
    float runStarted;

    static string Names(MultiplayerManager manager)
    {
        var names = new List<string>();
        foreach (var p in manager.Players) if (p != null && p.Current.inRun) names.Add(p.DisplayName);
        return names.Count <= 1 ? string.Join("", names) : string.Join(", ", names.GetRange(0, names.Count - 1)) + " and " + names[names.Count - 1];
    }

    static void Say(DungeonMaster.Line[] lines, string name, string names)
    {
        if (lines == null || lines.Length == 0) return;
        var line = lines[Random.Range(0, lines.Length)];
        if (line == null || line.IsEmpty) return;
        DungeonMaster.Say(line.With(line.text.Replace("{name}", name ?? "").Replace("{names}", names ?? "")));
    }
}
