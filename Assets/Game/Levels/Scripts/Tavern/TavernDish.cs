using System.Collections.Generic;
using UnityEngine;

// A dish the tavern keeper serves: eaten once a run, it holds until the run ends (every floor, a continued run too).
// Each one raises every attribute by Attributes and adds its Modifiers. What has been eaten this run is kept as
// ProgressionManager flags (run.dish.<id>), so the save carries it with no format change; a new run forgets them
// (TableLevelLoader). The dishes are read from Levels/Resources/Tavern dishes, so a continued run can apply them before
// any keeper is on the table. Edit them in the Asset Studio (Items, kind Dish) or the inspector.
[CreateAssetMenu(menuName = "Table/Tavern Dish", fileName = "Dish")]
public class TavernDish : ScriptableObject
{
    [Tooltip("Saved in the run (run.dish.<id>): never change it once released.")]
    public string id;
    public string displayName = "Dish";
    public Sprite icon;
    [TextArea(1, 3), Tooltip("What the keeper's screen and the buff's tooltip say it does, in plain words.")]
    public string effectText;
    [TextArea(1, 2), Tooltip("A short line about the dish itself (optional).")]
    public string flavour;
    [Min(0)] public int price = 30;
    [Min(0), Tooltip("Added to Strength, Dexterity, Constitution and Intelligence for the rest of the run.")]
    public int attributes;
    [Tooltip("Stats it adds for the rest of the run (Mana Regen, Percent Add .25 = 25% faster mana).")]
    public List<StatModifierEntry> modifiers = new();

    public const string FlagPrefix = "run.dish.";
    const string Folder = "Tavern dishes";

    static TavernDish[] all;
    public static TavernDish[] All => all ??= Resources.LoadAll<TavernDish>(Folder);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() => all = null;

    static ProgressionManager PM => ProgressionManager.HasInstance ? ProgressionManager.Instance : null;
    // The flag's name, made once (the buff bar asks every frame).
    string flagKey, flagKeyFor;
    string FlagKey { get { if (flagKeyFor != id) { flagKeyFor = id; flagKey = FlagPrefix + id; } return flagKey; } }
    public bool Eaten => PM != null && !string.IsNullOrEmpty(id) && PM.HasFlag(FlagKey);
    public void MarkEaten() { if (PM != null && !string.IsNullOrEmpty(id)) PM.SetFlag(FlagKey, 1); }

    // The dishes eaten this run.
    public static IEnumerable<TavernDish> EatenThisRun()
    {
        foreach (var d in All) if (d != null && d.Eaten) yield return d;
    }

    // Added to every attribute by what has been eaten this run (AdventurerProgress.Score).
    public static int AttributeBonus()
    {
        int sum = 0;
        foreach (var d in EatenThisRun()) sum += d.attributes;
        return sum;
    }

    // A new run starts hungry.
    public static void ForgetEaten()
    {
        if (PM == null) return;
        foreach (var d in All) if (d != null && !string.IsNullOrEmpty(d.id)) PM.SetFlag(FlagPrefix + d.id, 0);
    }
}
