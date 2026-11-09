// E at the table plays straight away, with no map view (TableManager.playWithoutMap; R opens the map instead): the run
// saved on quit is continued, else the place the figure stands at is played as the class last picked, and the map
// sinks into the table as it does after a click on a place.
public partial class CampaignManager
{
    // The place E would play now, and whether that continues the saved run. False when there is none (the map not
    // seen yet, a level loading, no open level place to stand at).
    bool QuickPlace(out CampaignEncounter place, out bool resume, out AdventurerClass savedClass)
    {
        place = null; resume = false; savedClass = null;
        if (!Active || PM == null || !PM.HasFlag(SeenFlag) || loader == null || loader.Busy || InMapView) return false;
        if (SavedRun(out var saved, out savedClass, out _) && savedClass != null && loader.CanResumeAdventure())
        {
            place = saved; resume = true;
            return true;
        }
        bool Playable(CampaignEncounter e) => e != null && e.kind == CampaignEncounterKind.Level && e.level != null && IsOpen(e);
        place = Playable(At) ? At : Playable(home) ? home : null;
        return place != null && progress != null && progress.selectedClass != null;
    }

    public bool CanQuickPlay => QuickPlace(out _, out _, out _);

    // E continues the saved run rather than starting one (the table's prompt says so).
    public bool QuickPlayResumes => QuickPlace(out _, out bool resume, out _) && resume;

    public bool QuickPlay(Player player)
    {
        if (player == null || !QuickPlace(out var e, out bool resume, out var savedClass)) return false;
        // A continued run is played as the class it was started with.
        if (resume && progress.selectedClass != savedClass) { progress.SelectClass(savedClass); RememberClass(); }
        // Its line is for a new run; a continued one picks up deeper, where it wouldn't fit.
        if (!resume && e.pickedLine != null && !e.pickedLine.IsEmpty) DungeonMaster.Say(e.pickedLine);
        At = e;
        Current = e;
        MarkVisited(e);
        if (Map != null && Map.gameObject.activeSelf) PlaceFigure(e);
        if (player.Look != null) player.Look.HeightOverride = null;
        if (resume) loader.ResumeAdventure(); else loader.Load(e.level);
        return true;
    }
}
