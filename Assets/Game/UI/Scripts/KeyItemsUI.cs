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
    // A loaded save sets its trophy flags without MetaChanged: the signature below catches those on the next check.

    // Only while the room player walks about: it hides in menus, cutscenes and at the table.
    void Update()
    {
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + .2f;
        bool visible = PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Room
            && GameManager.HasInstance && GameManager.Instance.State == GameState.Explore;
        int signature = visible ? Signature() : -2;
        if (signature != lastSignature) Rebuild();
    }

    // Which trophies are carried, not only how many: one swapped for another still rebuilds.
    static int Signature()
    {
        if (!ProgressionManager.HasInstance) return 0;
        int hash = 17;
        foreach (var (track, milestone) in ProgressionManager.Instance.TrophiesIn(ProgressionManager.TrophyState.Carried))
            hash = hash * 31 + ((track?.id ?? "").GetHashCode() ^ milestone.value);
        return hash * 31 + ProgressionManager.Instance.TileState;
    }

    void Rebuild()
    {
        foreach (var s in shown) if (s != null) Destroy(s.gameObject);
        shown.Clear();
        bool visible = PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Room
            && GameManager.HasInstance && GameManager.Instance.State == GameState.Explore;
        lastSignature = visible ? Signature() : -2;
        if (!visible || template == null || !ProgressionManager.HasInstance) return;
        int i = 0;
        foreach (var (track, milestone) in ProgressionManager.Instance.TrophiesIn(ProgressionManager.TrophyState.Carried))
        {
            var slot = Instantiate(template, template.parent);
            slot.gameObject.SetActive(true);
            slot.name = $"{track.displayName} {milestone.value}";
            slot.anchoredPosition = template.anchoredPosition - new Vector2(0, i++ * (template.rect.height + 10));
            // The icon is the template's Icon child (by name); without one set, the slot's own image shows it.
            Image img = null;
            if (templateIcon != null) foreach (var candidate in slot.GetComponentsInChildren<Image>(true)) if (candidate.name == templateIcon.name) { img = candidate; break; }
            if (img == null) img = slot.GetComponent<Image>();
            if (img != null)
            {
                img.sprite = milestone.trophy != null ? milestone.trophy.icon : track.icon;
                img.preserveAspect = true;
                img.enabled = img.sprite != null;
            }
            shown.Add(slot);
        }
        // The puzzle's missing tile, carried home and not yet placed.
        var meta = ProgressionManager.Instance;
        if (meta.missingTile != null && meta.TileState == 1)
        {
            var slot = Instantiate(template, template.parent);
            slot.gameObject.SetActive(true);
            slot.name = meta.missingTile.itemName;
            slot.anchoredPosition = template.anchoredPosition - new Vector2(0, i++ * (template.rect.height + 10));
            Image img = null;
            if (templateIcon != null) foreach (var candidate in slot.GetComponentsInChildren<Image>(true)) if (candidate.name == templateIcon.name) { img = candidate; break; }
            if (img == null) img = slot.GetComponent<Image>();
            if (img != null) { img.sprite = meta.missingTile.icon; img.preserveAspect = true; img.enabled = img.sprite != null; }
            shown.Add(slot);
        }
    }
}
