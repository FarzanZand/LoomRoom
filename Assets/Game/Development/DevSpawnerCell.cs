#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.EventSystems;

// One entry of the spawn window (DevSpawner): its clicks. It takes only clicks, so the mouse wheel and drags reach the list
// behind it and it scrolls (an EventTrigger takes every event and stops them).
public class DevSpawnerCell : MonoBehaviour, IPointerClickHandler
{
    public System.Action<PointerEventData.InputButton, int> clicked;   // the button and the click count (2: a double-click)
    public void OnPointerClick(PointerEventData data) => clicked?.Invoke(data.button, data.clickCount);
}
#endif
