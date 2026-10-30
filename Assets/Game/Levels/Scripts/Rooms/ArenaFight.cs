using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// On an arena room template's root: the fight in this room decides the run. Once every enemy standing in the room has
// fallen, the Dungeon Master says the win line and the run ends in victory, as the last floor's stairs would
// (TableLevelLoader.CompleteRun); the floor's exit stairs are hidden, so the fight can't be skipped. The room's
// enemies are the ones in its cells, found like SkeletonParty finds its dancers.
public class ArenaFight : MonoBehaviour
{
    [Tooltip("Said once the last enemy in the room falls.")]
    public DungeonMaster.Line wonLine = new() { text = "That's the fight. Take your bow.", inPerson = true };
    [Min(0), Tooltip("Seconds between the last enemy falling and the run ending (the body falls, the line is read).")]
    public float delay = 3.5f;
    [Tooltip("Unlocked by the win the first time (its New class card shows as the line is said; its poster comes home). The Arcanist.")]
    public AdventurerClass unlocks;

    DungeonGenerator dungeon;
    int room = -1;
    readonly List<EnemyBrain> fighters = new();
    readonly List<DungeonExit> exits = new();
    bool done;

    bool started, watching;
    // Watched by the floor itself (DungeonGenerator calls this as it is built and again each time the floor is switched
    // back on), so the fight counts whether or not its room is on the table yet: a board level keeps the rooms behind a
    // closed door off the table until it opens, and a room switched off then would never start its own watch. Switching
    // the floor off (a build slice, the map still sinking) stops the watch, so the floor starts it again.
    public void Watch(DungeonGenerator floor)
    {
        if (watching || done || !Application.isPlaying || floor == null) return;
        started = watching = true;
        floor.StartCoroutine(Run(floor));
    }
    // The floor was switched off: its coroutines, the watch among them, are gone.
    public void WatchStopped() => watching = false;

    IEnumerator Start()
    {
        if (started) yield break;
        started = true;
        yield return Run(GetComponentInParent<DungeonGenerator>());
    }

    IEnumerator Run(DungeonGenerator floor)
    {
        dungeon = floor;
        if (dungeon == null || dungeon.Layout == null) yield break;
        room = dungeon.Layout.RoomAt(dungeon.CellOf(transform.position));
        if (room < 0) yield break;
        // Enemies are put down after the room is built: gather them for a while, then keep to the ones found.
        for (float t = 0; t < 3f; t += .25f) { Gather(true); yield return new WaitForSeconds(.25f); }
        var wait = new WaitForSeconds(.25f);
        while (!done && this != null)
        {
            Gather(false);
            if (fighters.Count > 0 && fighters.TrueForAll(Down)) yield return Won();
            yield return wait;
        }
    }

    // Dead by its brain or by its health (an enemy killed while its room was off the table never ran its death). One that
    // never started (its room still behind a closed door) reads 0 health but stands: it counts as up.
    static bool Down(EnemyBrain b) => b == null || b.State == EnemyState.Dead
        || b.TryGetComponent<CharacterStats>(out var s) && s.Initialised && !s.IsAlive;

    // scan: look through the whole floor (only while it is being set up); otherwise only the exits already found.
    void Gather(bool scan)
    {
        if (scan)
        {
            // Only the way on: the ladder the player came down stays in the landing (it leads nowhere).
            foreach (var exit in dungeon.GetComponentsInChildren<DungeonExit>(true)) if (exit != null && !exit.entrance && !exits.Contains(exit)) exits.Add(exit);
            // Also the ones still off the table (behind the closed door): they count from the start.
            foreach (var brain in dungeon.GetComponentsInChildren<EnemyBrain>(true))
                if (brain != null && !fighters.Contains(brain) && brain.State != EnemyState.Dead && dungeon.Layout.RoomAt(dungeon.CellOf(brain.transform.position)) == room)
                    fighters.Add(brain);
        }
        // No way out but through the fight: the exit room's stairs stay hidden (a win ends the run on its own), also
        // when a room dealt onto the table switches them on again.
        foreach (var exit in exits) if (exit != null && exit.gameObject.activeSelf) exit.gameObject.SetActive(false);
    }

    IEnumerator Won()
    {
        if (!RunManager.HasInstance || !RunManager.Instance.Running || RunManager.Instance.Ended) yield break;   // looked at again later
        if (wonLine != null && !wonLine.IsEmpty) DungeonMaster.Say(wonLine);
        if (unlocks != null && ProgressionManager.HasInstance) ProgressionManager.Instance.UnlockClass(unlocks);
        yield return new WaitForSeconds(delay);
        // Dead in the meantime: not won yet. A revive goes on with the run, and the win is checked again.
        if (!RunManager.Instance.Running || RunManager.Instance.Ended || TableManager.Loader == null) yield break;
        done = true;
        TableManager.Loader.CompleteRun();
    }
}
