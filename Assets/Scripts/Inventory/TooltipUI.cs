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
        float pivotX = Input.mousePosition.x < Screen.width  * 0.5f ? 0f : 1f;
        float pivotY = Input.mousePosition.y < Screen.height * 0.5f ? 0f : 1f;
        rect.pivot = new Vector2(pivotX, pivotY);
        float ox = pivotX == 0f ?  16f : -16f;
        float oy = pivotY == 0f ?  16f : -16f;
        rect.position = Input.mousePosition + new Vector3(ox, oy, 0f);
    }
}
