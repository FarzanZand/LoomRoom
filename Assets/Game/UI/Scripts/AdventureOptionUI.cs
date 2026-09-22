using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class AdventureOptionUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Button button;
    public Text title;
    public Text description;
    public void OnPointerEnter(PointerEventData data)
    {
        if (!button.interactable) return;
        EventSystem.current?.SetSelectedGameObject(gameObject);
        UIFeedbackSettings.Shared?.Hover();
    }
    public void OnPointerExit(PointerEventData data)
    {
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject)
            EventSystem.current.SetSelectedGameObject(null);
    }
}
