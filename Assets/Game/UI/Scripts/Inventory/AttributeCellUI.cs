using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One attribute on the character panel (EquipmentPanelUI.attributeCells): its icon, name and score (the character
// sheet's plus this run's points), and a "+" that puts a level's point into it while any are left. Hovering it explains
// what it gives (StatTooltipUI on the same object). Laid out by CharacterPanelLayout.
public class AttributeCellUI : MonoBehaviour
{
    public MetaAttribute attribute;
    public Image icon;
    public TMP_Text label, value;
    public Button plus;

    AdventurerProgress progress;

    void Awake() { if (plus != null) plus.onClick.AddListener(Spend); }

    void Spend() { if (progress != null) progress.SpendPoint(attribute); }

    public void Refresh(AdventurerProgress owner)
    {
        progress = owner;
        var meta = ProgressionManager.HasInstance ? ProgressionManager.Instance : null;
        var track = meta != null ? meta.Track(attribute) : null;
        if (label != null) label.text = (track != null ? track.displayName : attribute.ToString()).ToUpperInvariant();
        if (icon != null && track != null && track.icon != null) icon.sprite = track.icon;
        int score = owner != null ? owner.Score(attribute) : meta != null ? meta.AttributeScore(attribute) : 8;
        bool raised = owner != null && (int)attribute < owner.AttributePoints.Length && owner.AttributePoints[(int)attribute] > 0;
        if (value != null) value.text = raised ? $"<color={ItemData.GoodColor}>{score}</color>" : score.ToString();
        if (plus != null) plus.gameObject.SetActive(owner != null && owner.InRun && owner.UnspentPoints > 0);
    }
}
