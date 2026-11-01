using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One of the four level-up cards (LevelUpCardsUI, made by LevelUpCardsSetup.Build()): its parts, filled and moved by
// the owner. The root keeps the card's place in the row and takes the pointer; Body is what moves, tips and flips.
public class LevelUpCardUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public RectTransform body;
    public Image face, back, glow, icon, divider, keyFrame;
    public TMP_Text attributeName, score, effects, key;
    public CanvasGroup group, faceParts;

    [HideInInspector] public LevelUpCardsUI owner;
    [HideInInspector] public int index;

    public void OnPointerEnter(PointerEventData e) { if (owner != null) owner.PointerOver(index); }
    public void OnPointerExit(PointerEventData e) { if (owner != null) owner.PointerLeft(index); }
    public void OnPointerClick(PointerEventData e) { if (owner != null) owner.Pick(index); }

    // Face up shows the face and what is on it; face down only the back.
    public void FaceUp(bool up)
    {
        if (back != null) back.enabled = !up;
        if (face != null) face.enabled = up;
        if (faceParts != null) faceParts.alpha = up ? 1f : 0f;
    }
}
