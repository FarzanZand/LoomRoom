using UnityEngine;

// What a run's class changes about how its dungeon looks and sounds (the Pianist, 2026-11-25): its own track on dungeon
// floors in place of the level's (AdventurerClass.dungeonMusic), and its own camera filter there (ownDungeonFilter,
// dungeonFilter) while its Mood is on. In the room, on the intro's practice board and for every other class nothing
// changes: the level's music and the scene's filter (ScreenManager's Camera filter). Read where the floor's track is
// picked (TableLevelLoader, the boss fights' way back to the floor's track) and by ScreenManager every frame.
public static class ClassRunLook
{
    // The table player's class in the run going on (null outside a run, and on the intro's practice board).
    public static AdventurerClass RunClass => RunManager.HasInstance && RunManager.Instance.Ended ? null : FloorClass;

    // The same, still after the run has ended in a death or a win, until it is put away as the view leaves the floor:
    // the class's look stays on the floor under the recap and goes with the flight out (ScreenManager).
    static AdventurerClass FloorClass
    {
        get
        {
            if (!RunManager.HasInstance || !RunManager.Instance.Running || !ProgressionManager.MetaActive) return null;
            var player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
            var progress = player != null ? player.GetComponent<AdventurerProgress>() : null;
            return progress != null ? progress.selectedClass : null;
        }
    }

    // The track for a dungeon floor: the run's class's own when it has one, else the level's (TableLevelData.MusicFor).
    public static AudioClip FloorMusic(TableLevelData level, int floor, out float volume)
    {
        var cls = level != null && level.IsDungeon ? RunClass : null;
        if (cls != null && cls.dungeonMusic != null) { volume = cls.dungeonMusicVolume; return cls.dungeonMusic; }
        if (level == null) { volume = 1f; return null; }
        return level.MusicFor(floor, out volume);
    }

    // The table player is on a dungeon floor of a run (not in the room, not on the practice board).
    public static bool OnDungeonFloor =>
        PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table
        && TableManager.HasInstance && TableManager.Loader != null && TableManager.Loader.Dungeon != null
        && TableManager.Loader.Dungeon.LevelData != null && TableManager.Loader.Dungeon.LevelData.IsDungeon;

    // The camera filter the run's class asks for right now, if any: on a dungeon floor, with its Mood on.
    public static bool DungeonFilter(out ScreenFilter filter)
    {
        filter = ScreenFilter.Standard;
        if (!Application.isPlaying || !OnDungeonFloor) return false;
        var cls = FloorClass;
        if (cls == null || !cls.ownDungeonFilter) return false;
        // A class whose action is a Mood: its filter only while the Mood is on.
        if (cls.action is MoodAction mood && !mood.On) return false;
        filter = cls.dungeonFilter;
        return true;
    }
}
