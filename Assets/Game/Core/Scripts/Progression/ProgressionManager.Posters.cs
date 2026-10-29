using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// Class posters (ClassPosterWall, over the writing desk by the bed): a class with no unlock flag has its poster up from
// the start; a class unlocked in play brings its poster home as a key item (the top row of icons, its portrait), the
// quest log asks the player to put it up, and using the wall puts it up while the Dungeon Master says the class's own
// line (AdventurerClass.posterLine). Flag poster.<class id>: 1 once up.
public partial class ProgressionManager
{
    public const string PosterPrefix = "poster.", PosterQuest = "hangPoster";

    [FoldoutGroup("Class posters"), Tooltip("The game's classes (their order is the key items' order). Each has a poster on the wall by the bed.")]
    public AdventureRules classRules;

    public static string PosterFlag(AdventurerClass c) => PosterPrefix + (string.IsNullOrEmpty(c.id) ? c.name : c.id);

    // On the wall: unlocked, and either there from the start or put up.
    public bool PosterUp(AdventurerClass c) => c != null && c.Unlocked && (string.IsNullOrEmpty(c.unlockFlag) || HasFlag(PosterFlag(c)));
    // Unlocked in play and not put up yet: a key item.
    public bool PosterCarried(AdventurerClass c) => c != null && !string.IsNullOrEmpty(c.unlockFlag) && c.Unlocked && !HasFlag(PosterFlag(c));

    // The posters carried, in the classes' order (filled into a list kept by the caller).
    public void CarriedPosters(List<AdventurerClass> into)
    {
        into.Clear();
        if (classRules == null || classRules.classes == null) return;
        foreach (var c in classRules.classes) if (PosterCarried(c)) into.Add(c);
    }

    public int PostersCarried
    {
        get
        {
            int n = 0;
            if (classRules != null && classRules.classes != null) foreach (var c in classRules.classes) if (PosterCarried(c)) n++;
            return n;
        }
    }

    public void PutUpPoster(AdventurerClass c)
    {
        if (!PosterCarried(c)) return;
        SetFlag(PosterFlag(c));
        MetaChanged?.Invoke();
    }

    // The dev console's addroomitem: the class unlocked and its poster carried home (taken down if it was up). False for a
    // class whose poster is up from the start.
    public bool CarryPoster(AdventurerClass c)
    {
        if (c == null || string.IsNullOrEmpty(c.unlockFlag)) return false;
        SetFlag(c.unlockFlag);
        SetFlag(PosterFlag(c), 0);
        MetaChanged?.Invoke();
        return true;
    }

    // The quest is open while a poster is carried and done once none is (given again for the next one).
    void UpdatePosterQuest()
    {
        if (Time.frameCount % 15 != 7) return;
        bool carrying = PostersCarried > 0;
        int state = QuestState(PosterQuest);
        if (carrying && state != 1) SetFlag(QuestFlag(PosterQuest), 1);
        else if (!carrying && state == 1) CompleteQuest(PosterQuest);
    }
}
