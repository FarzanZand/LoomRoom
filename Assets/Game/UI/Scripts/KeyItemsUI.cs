using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The room player's key items: a column of icons on the right edge of the screen, shown only in the room. For now
// they are the trophies the DM has handed over and the player hasn't hung on the wall yet (ProgressionManager).
// Authored in the scene (a template slot under this object); this only fills it.
public class KeyItemsUI : MonoBehaviour
{
    [SerializeField, Tooltip("One slot, cloned per key item (it stays hidden).")] RectTransform template;
    [SerializeField, Tooltip("The icon inside the template.")] Image templateIcon;
    readonly List<RectTransform> shown = new();
    float nextCheck;
    int lastSignature = -1;

    void Awake() { if (template != null) template.gameObject.SetActive(false); }
    void OnEnable() { if (ProgressionManager.HasInstance) ProgressionManager.Instance.MetaChanged += Rebuild; }
    void OnDisable() { if (ProgressionManager.HasInstance) ProgressionManager.Instance.MetaChanged -= Rebuild; }

    // Only while the room player walks about: it hides in menus, cutscenes and at the table.
    void Update()
    {
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + .2f;
        bool visible = PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Room
            && GameManager.HasInstance && GameManager.Instance.State == GameState.Explore;
        int signature = visible ? Count() : -2;
        if (signature != lastSignature) Rebuild();
    }

    static int Count() => ProgressionManager.HasInstance ? ProgressionManager.Instance.TrophiesIn(ProgressionManager.TrophyState.Carried).Count : 0;

    void Rebuild()
    {
        foreach (var s in shown) if (s != null) Destroy(s.gameObject);
        shown.Clear();
        bool visible = PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Room
            && GameManager.HasInstance && GameManager.Instance.State == GameState.Explore;
        lastSignature = visible ? Count() : -2;
        if (!visible || template == null || !ProgressionManager.HasInstance) return;
        int i = 0;
        foreach (var (track, milestone) in ProgressionManager.Instance.TrophiesIn(ProgressionManager.TrophyState.Carried))
        {
            var slot = Instantiate(template, template.parent);
            slot.gameObject.SetActive(true);
            slot.name = $"{track.displayName} {milestone.value}";
            slot.anchoredPosition = template.anchoredPosition - new Vector2(0, i++ * (template.rect.height + 10));
            var icon = slot.GetComponentsInChildren<Image>(true);
            foreach (var img in icon)
                if (img.name == templateIcon.name)
                {
                    img.sprite = milestone.trophy != null ? milestone.trophy.icon : track.icon;
                    img.preserveAspect = true;
                    img.enabled = img.sprite != null;
                }
            shown.Add(slot);
        }
    }
}
