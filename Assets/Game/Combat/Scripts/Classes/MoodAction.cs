using UnityEngine;

// The Pianist's Mood (the user, 2026-11-25): a toggle on the class's own button (X). On, the dungeon is seen through the
// class's own filter (AdventurerClass.dungeonFilter, Noir); off, through the scene's (ScreenManager's Camera filter).
// The choice is remembered across runs and sessions (PlayerPrefs, Prefs Key), on by default. ClassRunLook reads it.
[CreateAssetMenu(menuName = "LoomRoom/Class Action/Mood")]
public class MoodAction : ClassAction
{
    [Tooltip("The PlayerPrefs key the choice is kept under (1 on, 0 off).")]
    public string prefsKey = "class_mood";
    [Tooltip("Said in the feed as it is turned on and off. Empty: nothing.")]
    public string onLine = "Mood on.", offLine = "Mood off.";

    public bool On
    {
        get { try { return PlayerPrefs.GetInt(prefsKey, 1) != 0; } catch { return true; } }
        set { PlayerPrefs.SetInt(prefsKey, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    public override bool IsToggle => true;
    public override bool IsOn(Player player) => On;

    public override void Use(Player player)
    {
        On = !On;
        var line = On ? onLine : offLine;
        if (!string.IsNullOrWhiteSpace(line)) MessageLog.Post(line);
        RaiseChanged();
    }
}
