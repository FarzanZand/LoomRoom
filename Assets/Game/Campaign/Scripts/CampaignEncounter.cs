using Sirenix.OdinInspector;
using UnityEngine;

// One place on the campaign map: what it is, what it loads, and what the Dungeon Master says about it. A CampaignPoint
// on the map shows it; CampaignManager decides when it opens. Kinds are saved by number: append only.
public enum CampaignEncounterKind { Level = 0, Placeholder = 1, Home = 2 }

[CreateAssetMenu(menuName = "LoomRoom/Campaign Encounter", fileName = "New encounter")]
public class CampaignEncounter : ScriptableObject
{
    [Tooltip("Saved in the progress flags (campaign.beaten.<id>): never change it once players have saves.")]
    public string id;
    public string displayName = "New place";
    public CampaignEncounterKind kind = CampaignEncounterKind.Placeholder;
    [ShowIf(nameof(kind), CampaignEncounterKind.Level), Tooltip("The table level this place plays.")]
    public TableLevelData level;
    [Min(1), Tooltip("Which act of the campaign it belongs to (1 is home; any number of acts). Its act must be open for it to be picked.")]
    public int act = 1;
    [TextArea(2, 4), Tooltip("The card's line under the name.")]
    public string description;
    [Tooltip("Said when the player picks this place.")]
    public DungeonMaster.Line pickedLine;

    [Title("Shut until", HorizontalLine = false), Min(0), Tooltip("The place stays off the map (its road too) until the player has died this many times playing Deaths In; then it rises into view the next time the map is seen. 0: it opens with its act.")]
    public int deathsToOpen;
    [ShowIf("@deathsToOpen > 0"), Tooltip("The place whose deaths count (campaign.deaths.<its id>).")]
    public CampaignEncounter deathsIn;
    [ShowIf("@deathsToOpen > 0"), Tooltip("Said as it rises into view.")]
    public DungeonMaster.Line openedLine;
    [ShowIf("@deathsToOpen > 0"), Tooltip("Said once, back in the room, after the first death counted toward this place while it is still shut.")]
    public DungeonMaster.Line shutHint;
    public bool Locked => deathsToOpen > 0 && deathsIn != null;

    public string KindName => kind switch
    {
        CampaignEncounterKind.Level => level != null && level.kind == TableLevelKind.Boardgame ? "Board" : "Dungeon",
        CampaignEncounterKind.Home => "Home",
        _ => "Unknown",
    };
}
