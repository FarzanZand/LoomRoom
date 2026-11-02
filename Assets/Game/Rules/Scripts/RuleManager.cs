using System;
using System.Collections.Generic;
using UnityEngine;

// The Dungeon Master's house rules: which there are, which he has given (pinned on the sheet on the bedroom's east
// door, flags rule.<id> kept by AdventureSave) and his lines for giving and quoting them. A rule's number is the order
// it was given in (the user, 2026-11-02): the rules pinned from the start come first, then each given one after the
// last (its flag holds that order; saves from before all hold 1 and keep the list's order), so the sheet, the read
// screen and his quotes ("Rule two.") count the way the player met them. {number} in a given or quote line is that
// number as a word ({Number} capitalised). The sheet (HouseRulesSheet) and its read screen (HouseRulesUI) show the pinned ones; whoever gives a rule (the
// intro gives rule 1) asks for its line here. Under Systems (prefab Rules/Prefabs/RuleManager).
public class RuleManager : Singleton<RuleManager>
{
    [Tooltip("Every house rule. Scripts name a rule by its place here (Rule(2) is the second listed); its number on the sheet is the order it was given in.")]
    public HouseRule[] rules = Array.Empty<HouseRule>();
    [Min(0), Tooltip("Seconds between two quotes of the same rule (he doesn't repeat himself every time it applies).")]
    public float quoteGap = 45f;
    [Tooltip("The read screen (HUD canvas > House rules).")]
    public HouseRulesUI readScreen;

    public const string FlagPrefix = "rule.";
    // A rule was given or the list changed: the sheet and the read screen redraw.
    public event Action Changed;

    static ProgressionManager PM => ProgressionManager.HasInstance ? ProgressionManager.Instance : null;

    // Its number on the sheet: its place among the pinned rules, or the next number for one not given yet.
    public int Number(HouseRule rule)
    {
        var pinned = PinnedRules();
        int at = pinned.IndexOf(rule);
        return at >= 0 ? at + 1 : pinned.Count + 1;
    }
    public bool Given(HouseRule rule) => rule != null && PM != null && PM.HasFlag(FlagPrefix + rule.id);
    public bool Pinned(HouseRule rule) => rule != null && (rule.pinnedFromStart || Given(rule));

    // When it was pinned: 0 from the start, else the order it was given in.
    int Order(HouseRule rule) => rule.pinnedFromStart ? 0 : PM != null ? PM.GetFlag(FlagPrefix + rule.id) : 0;

    // The rules on the sheet, in the order they were given (ties, as in older saves, in the list's order).
    public List<HouseRule> PinnedRules()
    {
        var list = new List<HouseRule>();
        foreach (var r in rules) if (Pinned(r)) list.Add(r);
        var listed = new Dictionary<HouseRule, int>();
        for (int i = 0; i < list.Count; i++) listed[list[i]] = i;
        list.Sort((a, b) => { int c = Order(a).CompareTo(Order(b)); return c != 0 ? c : listed[a].CompareTo(listed[b]); });
        return list;
    }

    // Gives a rule: it is pinned from now on, after every rule given before it, and this is what he says (null for none).
    // The caller says it, at its own pace.
    public DungeonMaster.Line Give(HouseRule rule)
    {
        if (rule == null) return null;
        if (PM != null && !Given(rule))
        {
            int last = 0;
            foreach (var r in rules) if (r != null && r != rule && Given(r)) last = Mathf.Max(last, Order(r));
            PM.SetFlag(FlagPrefix + rule.id, last + 1);
            Changed?.Invoke();
        }
        return rule.given != null && !rule.given.IsEmpty ? Numbered(rule.given, rule) : new DungeonMaster.Line { text = $"*Rule {Word(Number(rule))}.* {rule.text}", inPerson = true, mumble = true };
    }

    // The line with {number} / {Number} filled in.
    DungeonMaster.Line Numbered(DungeonMaster.Line line, HouseRule rule)
    {
        if (line.text == null || !line.text.Contains("umber}")) return line;
        string word = Word(Number(rule));
        string cap = word.Length > 0 ? char.ToUpper(word[0]) + word.Substring(1) : word;
        return line.With(line.text.Replace("{number}", word).Replace("{Number}", cap));
    }

    readonly Dictionary<HouseRule, float> lastQuote = new();
    // He quotes a rule that just applied ("Rule one."), not twice within Quote Gap.
    public void Quote(HouseRule rule)
    {
        if (rule == null || !Pinned(rule)) return;
        if (lastQuote.TryGetValue(rule, out float at) && Time.unscaledTime - at < quoteGap) return;
        lastQuote[rule] = Time.unscaledTime;
        DungeonMaster.Say(rule.quote != null && !rule.quote.IsEmpty ? Numbered(rule.quote, rule) : new DungeonMaster.Line { text = $"*Rule {Word(Number(rule))}.*", mumble = true });
    }

    // A rule by its place in Rules (1 = the first listed), null past the list. Not its number on the sheet.
    public HouseRule Rule(int number) => rules != null && number >= 1 && number <= rules.Length ? rules[number - 1] : null;

    // Quotes the rule listed n-th where it applies (only once pinned, paced by Quote Gap). Safe without a manager.
    public static void QuoteRule(int number) { if (HasInstance) Instance.Quote(Instance.Rule(number)); }

    public void ShowRules() { if (readScreen != null) readScreen.Open(); }

    static readonly string[] Words = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty" };
    public static string Word(int n) => n >= 0 && n < Words.Length ? Words[n] : n.ToString();

    // Also from Start: on the scene's first frame this OnEnable can run before the ProgressionManager's Awake, and the
    // save's rule flags (read after) would then never redraw the sheet.
    bool listening;
    void OnEnable() => Listen();
    void Start() { Listen(); Changed?.Invoke(); }
    void Listen()
    {
        if (listening || !ProgressionManager.HasInstance) return;
        ProgressionManager.Instance.FlagChanged += OnFlag;
        listening = true;
    }
    void OnDisable()
    {
        if (listening && ProgressionManager.HasInstance) ProgressionManager.Instance.FlagChanged -= OnFlag;
        listening = false;
    }
    void OnFlag(string key, int value) { if (key != null && key.StartsWith(FlagPrefix)) Changed?.Invoke(); }
}
