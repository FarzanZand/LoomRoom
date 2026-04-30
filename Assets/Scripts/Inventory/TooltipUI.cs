using TMPro;
using UnityEngine;

public class TooltipUI : Singleton<TooltipUI>
{
    [SerializeField] private GameObject panel;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI descriptionText;

    private RectTransform rect;

    protected override void Awake()
    {
        base.Awake();
        rect = panel.GetComponent<RectTransform>();
        panel.SetActive(false);
    }

    void Update()
    {
        if (panel.activeSelf)
            FollowMouse();
    }

    public void Show(ItemData item)
    {
        nameText.text        = item.itemName;
        descriptionText.text = item.description;
        panel.SetActive(true);
        FollowMouse();
    }

    public void Hide()
    {
        panel.SetActive(false);
    }

    void FollowMouse()
    {
        rect.position = Input.mousePosition + new Vector3(16f, -16f, 0f);
    }
}
