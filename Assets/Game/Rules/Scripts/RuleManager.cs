using System;
using System.Collections.Generic;
using UnityEngine;

// The Dungeon Master's house rules: which there are (in order, so each has its number), which he has given (pinned on
// the sheet on the bedroom's east door, flags rule.<id> kept by AdventureSave), and his lines for giving and quoting
// them. The sheet (HouseRulesSheet) and its read screen (HouseRulesUI) show the pinned ones; whoever gives a rule (the
// intro gives rule 1) asks for its line here. Under Systems (prefab Rules/Prefabs/RuleManager).
public class RuleManager : Singleton<RuleManager>
{
    [Tooltip("The house rules in order: the first is rule one.")]
    public HouseRule[] rules = Array.Empty<HouseRule>();
    [Min(0), Tooltip("Seconds between two quotes of the same rule (he doesn't repeat himself every time it applies).")]
    public float quoteGap = 45f;
    [Tooltip("The read screen (HUD canvas > House rules).")]
    public HouseRulesUI readScreen;

    public const string FlagPrefix = "rule.";
    // A rule was given or the list changed: the sheet and the read screen redraw.
    public event Action Changed;

    static ProgressionManager PM => ProgressionManager.HasInstance ? ProgressionManager.Instance : null;

    public int Number(HouseRule rule) => Array.IndexOf(rules, rule) + 1;
    public bool Given(HouseRule rule) => rule != null && PM != null && PM.HasFlag(FlagPrefix + rule.id);
    public bool Pinned(HouseRule rule) => rule != null && (rule.pinnedFromStart || Given(rule));

    // The rules on the sheet, in order.
    public List<HouseRule> PinnedRules()
    {
        var list = new List<HouseRule>();
        foreach (var r in rules) if (Pinned(r)) list.Add(r);
        return list;
    }

    // Gives a rule: it is pinned from now on, and this is what he says (null for none). The caller says it, at its own pace.
    public DungeonMaster.Line Give(HouseRule rule)
    {
        if (rule == null) return null;
        if (PM != null && !Given(rule)) { PM.SetFlag(FlagPrefix + rule.id, 1); Changed?.Invoke(); }
        return rule.given != null && !rule.given.IsEmpty ? rule.given : new DungeonMaster.Line { text = $"Rule {Word(Number(rule))}. {rule.text}", inPerson = true, mumble = true };
    }

    readonly Dictionary<HouseRule, float> lastQuote = new();
    // He quotes a rule that just applied ("Rule one."), not twice within Quote Gap.
    public void Quote(HouseRule rule)
    {
        if (rule == null || !Pinned(rule)) return;
        if (lastQuote.TryGetValue(rule, out float at) && Time.unscaledTime - at < quoteGap) return;
        lastQuote[rule] = Time.unscaledTime;
        DungeonMaster.Say(rule.quote != null && !rule.quote.IsEmpty ? rule.quote : new DungeonMaster.Line { text = $"Rule {Word(Number(rule))}.", mumble = true });
    }

    public void ShowRules() { if (readScreen != null) readScreen.Open(); }

    static readonly string[] Words = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty" };
    public static string Word(int n) => n >= 0 && n < Words.Length ? Words[n] : n.ToString();

    void OnEnable() { if (ProgressionManager.HasInstance) ProgressionManager.Instance.FlagChanged += OnFlag; }
    void OnDisable() { if (ProgressionManager.HasInstance) ProgressionManager.Instance.FlagChanged -= OnFlag; }
    void OnFlag(string key, int value) { if (key != null && key.StartsWith(FlagPrefix)) Changed?.Invoke(); }
}
