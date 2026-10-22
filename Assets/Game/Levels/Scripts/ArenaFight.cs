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

    DungeonGenerator dungeon;
    int room = -1;
    readonly List<EnemyBrain> fighters = new();
    bool done;

    IEnumerator Start()
    {
        dungeon = GetComponentInParent<DungeonGenerator>();
        if (dungeon == null || dungeon.Layout == null) yield break;
        room = dungeon.Layout.RoomAt(dungeon.CellOf(transform.position));
        if (room < 0) yield break;
        // Enemies are put down after the room is built: gather them for a while.
        for (float t = 0; t < 3f; t += .25f) { Gather(); yield return new WaitForSeconds(.25f); }
        var wait = new WaitForSeconds(.25f);
        while (!done)
        {
            Gather();
            if (fighters.Count > 0 && fighters.TrueForAll(b => b == null || b.State == EnemyState.Dead)) yield return Won();
            yield return wait;
        }
    }

    void Gather()
    {
        // No way out but through the fight: the exit room's stairs stay hidden (a win ends the run on its own).
        foreach (var exit in dungeon.GetComponentsInChildren<DungeonExit>()) if (exit != null && exit.gameObject.activeSelf) exit.gameObject.SetActive(false);
        foreach (var brain in dungeon.GetComponentsInChildren<EnemyBrain>())
            if (brain != null && !fighters.Contains(brain) && brain.State != EnemyState.Dead && dungeon.Layout.RoomAt(dungeon.CellOf(brain.transform.position)) == room)
                fighters.Add(brain);
    }

    IEnumerator Won()
    {
        if (!RunManager.HasInstance || !RunManager.Instance.Running || RunManager.Instance.Ended) yield break;   // looked at again later
        if (wonLine != null && !wonLine.IsEmpty) DungeonMaster.Say(wonLine);
        yield return new WaitForSeconds(delay);
        // Dead in the meantime: not won yet. A revive goes on with the run, and the win is checked again.
        if (!RunManager.Instance.Running || RunManager.Instance.Ended || TableManager.Loader == null) yield break;
        done = true;
        TableManager.Loader.CompleteRun();
    }
}
