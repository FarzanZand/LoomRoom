using UnityEngine;

// On the tavern's room template: what the Dungeon Master says, in person, the first time the table player walks in on a
// floor (DungeonGenerator.RoomLines asks here before the template's own Enter Lines). Off the record: a hint about what
// lies ahead when there is one to give (a guardian further down this run, a portal on this floor), else one of his
// plain lines. All of them are edited here (and in the Asset Studio's Dialogue view).
public class TavernRoom : MonoBehaviour
{
    [Tooltip("Said when a guardian waits further down this run. {Floors} is how many floors down.")]
    public DungeonMaster.Line[] bossAhead =
    {
        new() { text = "There's a boss {Floors} down. Eat first.", inPerson = true, mumble = true },
        new() { text = "Rest here. It gets worse below.", inPerson = true, mumble = true },
    };
    [Tooltip("Said when the guardian is on the next floor.")]
    public DungeonMaster.Line[] bossNext =
    {
        new() { text = "The next stairs go down to a boss.", inPerson = true, mumble = true },
    };
    [Tooltip("Said when this floor has a portal.")]
    public DungeonMaster.Line[] portalHere =
    {
        new() { text = "There's a portal somewhere on this floor.", inPerson = true, mumble = true },
    };
    [Tooltip("Said otherwise.")]
    public DungeonMaster.Line[] plain =
    {
        new() { text = "Nothing follows you in here.", inPerson = true, mumble = true },
        new() { text = "Even I need a break from you sometimes.", inPerson = true, mumble = true },
        new() { text = "The stew is better than it looks.", inPerson = true, mumble = true },
    };
    [Range(0, 1), Tooltip("Chance a hint is given when there is one (else a plain line).")]
    public float hintChance = .7f;
    [Tooltip("Said the first time a tavern is walked into (once a save) while the keeper has a rumour to sell; the quest to buy one opens with it.")]
    public DungeonMaster.Line rumourLine = new() { text = "The keeper sells rumours. Buy one and see what turns up on the map.", inPerson = true, mumble = true };
    public const string RumourToldFlag = "story.rumourTold";

    public DungeonMaster.Line Pick(DungeonGenerator floor)
    {
        // The first tavern of a save, with a rumour on sale: the quest to buy one, and his word on it.
        var pm = ProgressionManager.HasInstance ? ProgressionManager.Instance : null;
        if (pm != null && ProgressionManager.MetaActive && !pm.HasFlag(RumourToldFlag) && CampaignManager.HasInstance && CampaignManager.Instance.Rumours().Count > 0)
        {
            pm.SetFlag(RumourToldFlag);
            pm.GiveQuest(ProgressionManager.RumourQuest);
            if (rumourLine != null && !rumourLine.IsEmpty) return rumourLine;
        }
        if (floor != null && Random.value < hintChance)
        {
            var level = floor.LevelData;
            int here = floor.FloorNumber, count = level != null ? level.FloorCount : here;
            for (int f = here + 1; level != null && f <= count; f++)
                if (level.Milestone(f) != null)
                {
                    int down = f - here;
                    if (down == 1 && Any(bossNext)) return One(bossNext, down);
                    if (Any(bossAhead)) return One(bossAhead, down);
                    break;
                }
            if (Any(portalHere) && floor.GetComponentInChildren<PortalArch>(true) != null) return One(portalHere, 0);
        }
        return Any(plain) ? One(plain, 0) : null;
    }

    static bool Any(DungeonMaster.Line[] lines) => lines != null && lines.Length > 0;

    static DungeonMaster.Line One(DungeonMaster.Line[] lines, int floors)
    {
        var line = lines[Random.Range(0, lines.Length)];
        if (line == null || line.IsEmpty) return null;
        string count = floors == 1 ? "one floor" : floors + " floors";
        return new DungeonMaster.Line { text = line.text.Replace("{Floors}", count), inPerson = line.inPerson, mumble = line.mumble, voice = line.voice };
    }
}
