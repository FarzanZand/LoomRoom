using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

// The character panel's gear bonuses line: everything the worn gear adds beyond health, damage and armor (critical
// chance, kill heal, light radius...), summed, in one short line; hovering it lists them all in the tooltip.
public class GearBonusesUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] TMP_Text text;
    [SerializeField, Tooltip("Shown when the gear adds nothing beyond its main numbers.")] string none = "No gear bonuses";
    [SerializeField, Min(1)] int shownInLine = 2;

    static readonly StatType[] Stats =
    {
        StatType.CriticalChance, StatType.BackstabDamage, StatType.KillHeal, StatType.DamageToSkeletons, StatType.DamageToBeasts,
        StatType.DamageToSlimes, StatType.FireCircleOnHit, StatType.SpellDamage, StatType.ShieldBashDamage, StatType.LightRadius,
        StatType.GoldFind, StatType.SwordsSkill, StatType.MacesSkill, StatType.BlockingSkill,
        StatType.MoveSpeed, StatType.AttackSpeed, StatType.ManaRegen,
    };

    readonly List<string> lines = new();
    // The first number of a bonus line, with its sign and percent ("+20%", "14%", "1").
    static readonly System.Text.RegularExpressions.Regex Number = new(@"([+\-]?\d+(?:\.\d+)?%?)");
    string shown;
    bool hovering;

    void Awake() { if (text == null) text = GetComponent<TMP_Text>(); }

    // The worn gear's bonuses in words, summed from its modifiers (not from the stats, which also hold attributes and
    // buffs); critical chance as the total with the base chance. A weapon's own swing speed counts as its speed, not a bonus.
    public static void Collect(Equipment gear, List<string> into)
    {
        into.Clear();
        if (gear == null) return;
        foreach (var stat in Stats)
        {
            float value = 0f;
            foreach (var item in gear.EquippedItems)
            {
                if (item == null || item.statModifiers == null) continue;
                for (int i = 0; i < item.statModifiers.Length; i++)
                {
                    var m = item.statModifiers[i];
                    if (m == null || m.stat != stat || m.type != ModifierType.Flat) continue;
                    if (stat == StatType.AttackSpeed && item.IsWeapon && !item.IsRolledModifier(i)) continue;
                    value += m.value;
                }
            }
            if (value <= .0001f) continue;
            if (stat == StatType.CriticalChance)
            {
                float total = value + (CombatManager.HasInstance ? CombatManager.Instance.critChance : .08f);
                into.Add($"{total * 100f:0.#}% critical chance");
            }
            else into.Add(StatModifierEntry.Describe(stat, ModifierType.Flat, value));
        }
    }

    public void Refresh(Equipment gear)
    {
        Collect(gear, lines);
        // One bonus a line (the box holds two), the rest counted; hovering lists them all.
        string line = lines.Count == 0 ? none : string.Join("\n", lines.GetRange(0, Mathf.Min(shownInLine, lines.Count))) + (lines.Count > shownInLine ? $"<color={ItemData.DimColor}>   +{lines.Count - shownInLine} more</color>" : "");
        if (line == shown) return;
        shown = line;
        if (text != null) text.text = line;
        if (hovering) ShowTooltip();
    }

    void ShowTooltip()
    {
        if (!TooltipUI.HasInstance) return;
        var body = new System.Text.StringBuilder();
        foreach (var l in lines) body.AppendLine(Number.Replace(l, $"<color={ItemData.GoodColor}>$1</color>", 1));
        TooltipUI.Instance.ShowText("Gear bonuses", "", lines.Count == 0 ? none : body.ToString().TrimEnd());
    }

    public void OnPointerEnter(PointerEventData eventData) { hovering = true; ShowTooltip(); }
    public void OnPointerExit(PointerEventData eventData) { hovering = false; if (TooltipUI.HasInstance) TooltipUI.Instance.Hide(); }
    void OnDisable() { if (hovering && TooltipUI.HasInstance) TooltipUI.Instance.Hide(); hovering = false; }
}
