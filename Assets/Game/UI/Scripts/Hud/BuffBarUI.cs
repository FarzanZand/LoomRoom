using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Effects on the active player, shown as icons above the health bar with the time left: food regen (the food's icon),
// timed stat modifiers (the item's icon, one per source), the tavern's dishes eaten this run (TavernDish: "run" for
// a timer, as they last it) and Deflect recharging (its ability's icon, PlayerCombat.DeflectCooldownLeft). Hovering one says what it is (HoverText).
// The slots are authored in the scene; unused ones are hidden.
public class BuffBarUI : MonoBehaviour
{
    [System.Serializable]
    public class Slot
    {
        public GameObject root;
        public Image icon;
        public TMP_Text timer;
        [Tooltip("Optional: tinted by whether the effect helps or hurts.")] public Image frame;
    }

    [SerializeField] Slot[] slots = new Slot[0];
    [Tooltip("Used for food regen after a load, when the food eaten is no longer known.")]
    [SerializeField] Sprite foodIcon;
    [Tooltip("Used for timed effects that do not come from an item.")]
    [SerializeField] Sprite fallbackIcon;
    [SerializeField] Color buffFrame = new(.5f, .57f, .55f);
    [SerializeField] Color debuffFrame = new(.66f, .28f, .24f);

    struct Shown
    {
        public Sprite icon;
        public float seconds;   // below 0: lasts the run
        public bool harmful;
        public string title, subtitle, body;
    }

    readonly List<Shown> shown = new();
    readonly Dictionary<object, int> bySource = new();
    // Whole seconds each slot's timer shows, so its text is formatted only when that changes.
    int[] slotSeconds;

    void Update()
    {
        Collect(PlayerManager.HasInstance ? PlayerManager.Instance.Active?.Stats : null);
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot == null || slot.root == null) continue;
            bool on = i < shown.Count;
            if (slot.root.activeSelf != on) slot.root.SetActive(on);
            if (!on) continue;
            if (slot.icon != null) { slot.icon.sprite = shown[i].icon; slot.icon.enabled = shown[i].icon != null; }
            if (slot.timer != null)
            {
                if (slotSeconds == null || slotSeconds.Length != slots.Length) slotSeconds = new int[slots.Length];
                int seconds = shown[i].seconds < 0 ? -1 : Mathf.CeilToInt(shown[i].seconds);
                if (slotSeconds[i] != seconds || slot.timer.text.Length == 0) { slotSeconds[i] = seconds; slot.timer.text = seconds < 0 ? "run" : Format(seconds); }
            }
            if (slot.frame != null) slot.frame.color = shown[i].harmful ? debuffFrame : buffFrame;
            var hover = slot.root.GetComponent<HoverText>();
            if (hover == null) hover = slot.root.AddComponent<HoverText>();
            if (hover.title != shown[i].title || hover.body != shown[i].body) hover.Set(shown[i].title, shown[i].subtitle, shown[i].body);
        }
    }

    void Collect(CharacterStats stats)
    {
        shown.Clear();
        bySource.Clear();
        if (stats == null || !stats.IsAlive) return;
        if (stats.FoodRemaining > 0f)
            shown.Add(new Shown { icon = stats.FoodSource != null && stats.FoodSource.icon != null ? stats.FoodSource.icon : foodIcon, seconds = stats.FoodRemaining,
                title = stats.FoodSource != null ? stats.FoodSource.itemName : "Food", subtitle = "Healing over time" });
        // The tavern's dishes: the whole run.
        if (PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table && RunManager.HasInstance && RunManager.Instance.Running)
            foreach (var dish in TavernDish.All)   // not EatenThisRun: its iterator made garbage every frame
                if (dish != null && dish.Eaten) shown.Add(new Shown { icon = dish.icon != null ? dish.icon : foodIcon, seconds = -1, title = dish.displayName, subtitle = "Lasts until this run ends", body = dish.effectText });
        // Deflect recharging after it sent a shot back.
        var combat = PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table ? PlayerManager.Instance.Active.Combat : null;
        if (combat != null && combat.DeflectCooldownLeft > 0f)
        {
            var track = ProgressionManager.HasInstance ? ProgressionManager.Instance.Track(AdventureSkill.Blocking) : null;
            var ability = track != null && track.milestones.Count > 0 && track.milestones[0] != null ? track.milestones[0].ability : null;
            shown.Add(new Shown { icon = ability != null && ability.icon != null ? ability.icon : fallbackIcon, seconds = combat.DeflectCooldownLeft, harmful = true,
                title = ability != null ? ability.displayName : "Deflect", subtitle = "Recharging", body = "Your shield can't send a shot back until this runs out." });
        }
        foreach (var m in stats.Modifiers)
        {
            if (m == null || m.IsPermanent || m.IsExpired) continue;
            object key = m.Source ?? m;
            if (bySource.TryGetValue(key, out int at))
            {
                var s = shown[at];
                s.seconds = Mathf.Max(s.seconds, m.Duration);
                shown[at] = s;
                continue;
            }
            var item = (m.Source as ItemBuffSource)?.Item;
            bySource[key] = shown.Count;
            shown.Add(new Shown { icon = item != null && item.icon != null ? item.icon : fallbackIcon, seconds = m.Duration, harmful = m.Value < 0f,
                title = item != null ? item.itemName : "Effect", subtitle = m.Value < 0f ? "Harmful" : "Helpful", body = StatModifierEntry.Describe(m.Stat, m.Type, m.Value) });
        }
    }

    static string Format(int s) => s >= 60 ? $"{s / 60}:{s % 60:00}" : $"{s}s";
}
