using UnityEngine;

// The portrait dialogue box's "go on" arrow (PortraitDialogueUI shows it once a line is typed out): it nudges right and
// back so it reads as waiting for a press.
public class PortraitDialogueArrow : MonoBehaviour
{
    public float distance = 6f, speed = 5f;

    RectTransform rect;
    Vector2 home;

    void Awake() { rect = (RectTransform)transform; home = rect.anchoredPosition; }

    void Update() => rect.anchoredPosition = home + Vector2.right * (Mathf.Round(Mathf.Abs(Mathf.Sin(Time.unscaledTime * speed)) * distance));
}
