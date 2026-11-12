using System.Collections.Generic;

// The dice of the first morning's character sheet (IntroController.SheetAndDice) count for one run (the user, 2026-11-12:
// "make the stats you have represent the value of the dice for your first dungeon run, but only the first"). Each rolled
// attribute's total (3d6: STR, INT, CON; Dexterity is not rolled and keeps the sheet's score) takes the place of the
// sheet's score in the next new run, and the class's own points still go on top (AdventurerProgress.Score), so health,
// mana and stamina and the class card's numbers follow (StartingPools). Kept in flags, so a run continued after a quit
// keeps them: story.firstRoll is 1 while rolled and no run has started, 2 while the run that plays them is going (a
// continued run starts no new run, TableLevelLoader), and 0 once a later run starts; story.firstRoll.<Attribute> holds
// each roll. An older save that rolled before this never set them and plays as before.
public partial class ProgressionManager
{
    public const string FirstRollFlag = "story.firstRoll";
    const string FirstRollPrefix = "story.firstRoll.";
    static string FirstRollKey(MetaAttribute a) => FirstRollPrefix + a;

    // The sheet's abbreviations, as IntroController.rolledAttributes writes them.
    public static bool AttributeFromLabel(string label, out MetaAttribute attribute)
    {
        switch ((label ?? "").Trim().ToUpperInvariant())
        {
            case "STR": attribute = MetaAttribute.Strength; return true;
            case "DEX": attribute = MetaAttribute.Dexterity; return true;
            case "CON": attribute = MetaAttribute.Constitution; return true;
            case "INT": attribute = MetaAttribute.Intelligence; return true;
            default: attribute = MetaAttribute.Strength; return false;
        }
    }

    // The sheet is done: these rolls wait for the next run (rolling again before it replaces them).
    public void KeepFirstRolls(IReadOnlyList<(MetaAttribute attribute, int total)> rolls)
    {
        foreach (MetaAttribute a in System.Enum.GetValues(typeof(MetaAttribute))) SetFlag(FirstRollKey(a), 0);
        foreach (var (attribute, total) in rolls) if (total > 0) SetFlag(FirstRollKey(attribute), total);
        SetFlag(FirstRollFlag, 1);
    }

    // A new run starts (not a continued one): waiting rolls are this run's, a run's rolls are gone.
    public static void FirstRollNewRun()
    {
        if (!HasInstance) return;
        var pm = Instance;
        int state = pm.GetFlag(FirstRollFlag);
        if (state == 1) pm.SetFlag(FirstRollFlag, 2);
        else if (state == 2)
        {
            pm.SetFlag(FirstRollFlag, 0);
            foreach (MetaAttribute a in System.Enum.GetValues(typeof(MetaAttribute))) pm.SetFlag(FirstRollKey(a), 0);
        }
    }

    // The rolled score of this attribute, while it counts: in the run that plays the rolls, or outside a run while they
    // wait for it (the class card at the table).
    public bool FirstRoll(MetaAttribute attribute, bool inRun, out int score)
    {
        score = GetFlag(FirstRollKey(attribute));
        int state = GetFlag(FirstRollFlag);
        return score > 0 && (inRun ? state == 2 : state == 1);
    }

    // The score an attribute starts a run at before the class's points: the roll while it counts, else the sheet's.
    public int StartingScore(MetaAttribute attribute, bool inRun) => FirstRoll(attribute, inRun, out int rolled) ? rolled : AttributeScore(attribute);
}
