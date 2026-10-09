using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Timed effects on the active player, shown as icons above the health bar with the time left:
// food regen (the food's icon) and timed stat modifiers (the item's icon, one per source).
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
        public float seconds;
        public bool harmful;
    }

    readonly List<Shown> shown = new();
    readonly Dictionary<object, int> bySource = new();

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
            if (slot.timer != null) slot.timer.text = Format(shown[i].seconds);
            if (slot.frame != null) slot.frame.color = shown[i].harmful ? debuffFrame : buffFrame;
        }
    }

    void Collect(CharacterStats stats)
    {
        shown.Clear();
        bySource.Clear();
        if (stats == null || !stats.IsAlive) return;
        if (stats.FoodRemaining > 0f)
            shown.Add(new Shown { icon = stats.FoodSource != null && stats.FoodSource.icon != null ? stats.FoodSource.icon : foodIcon, seconds = stats.FoodRemaining });
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
            shown.Add(new Shown { icon = item != null && item.icon != null ? item.icon : fallbackIcon, seconds = m.Duration, harmful = m.Value < 0f });
        }
    }

    static string Format(float seconds)
    {
        int s = Mathf.CeilToInt(seconds);
        return s >= 60 ? $"{s / 60}:{s % 60:00}" : $"{s}s";
    }
}
