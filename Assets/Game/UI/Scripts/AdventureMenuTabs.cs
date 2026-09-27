using UnityEngine;
using UnityEngine.UI;

// Switches the character menu between Equipment (inventory + gear) and Skills & Spells.
// The open tab's button shows its selected sprite.
public class AdventureMenuTabs : MonoBehaviour
{
    public GameObject progressionPanel;
    public GameObject[] equipmentPanels;
    public Button skillsButton, equipmentButton;
    [Tooltip("Sprite for the open tab; the closed tab keeps its normal sprite.")]
    public Sprite openSprite;
    public GameObject skillsUnderline, equipmentUnderline;
    public float skillsTabVerticalOffset = -140;
    Vector2 skillsPosition, equipmentPosition;

    Sprite closedSprite;

    void Awake()
    {
        skillsPosition = ((RectTransform)skillsButton.transform).anchoredPosition;
        equipmentPosition = ((RectTransform)equipmentButton.transform).anchoredPosition;
        closedSprite = skillsButton.image != null ? skillsButton.image.sprite : null;
        skillsButton.onClick.AddListener(() => Show(true));
        equipmentButton.onClick.AddListener(() => Show(false));
    }

    void OnEnable() => Show(false);

    void Show(bool skills)
    {
        var offset = skills ? Vector2.up * skillsTabVerticalOffset : Vector2.zero;
        ((RectTransform)skillsButton.transform).anchoredPosition = skillsPosition + offset;
        ((RectTransform)equipmentButton.transform).anchoredPosition = equipmentPosition + offset;
        progressionPanel.SetActive(skills);
        if (skillsUnderline != null) skillsUnderline.SetActive(skills);
        if (equipmentUnderline != null) equipmentUnderline.SetActive(!skills);
        foreach (var panel in equipmentPanels) if (panel != null) panel.SetActive(!skills);
        if (openSprite == null) return;
        if (skillsButton.image != null) skillsButton.image.sprite = skills ? openSprite : closedSprite;
        if (equipmentButton.image != null) equipmentButton.image.sprite = skills ? closedSprite : openSprite;
    }
}
