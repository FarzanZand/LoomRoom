using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One hero on the campaign hero select: the class's portrait and name in a slot. The chosen one is framed in gold and
// moved out of line; a click chooses it (a click on the chosen one takes it). Filled by CampaignHeroSelectUI.
public class CampaignHeroCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public Image frame, portrait;
    public TMP_Text title;
    [Tooltip("What moves out when the card is chosen (the card's contents, so the row's layout stays put).")]
    public RectTransform lift;
    [Tooltip("Which way the chosen card moves out (up in a row, right in a column).")]
    public Vector2 liftDirection = Vector2.up;

    public AdventurerClass Class { get; private set; }
    public Action<CampaignHeroCard> Clicked;
    bool chosen, hovered;
    float shownLift;

    public void Fill(AdventurerClass c)
    {
        Class = c;
        portrait.sprite = c.portrait; portrait.enabled = c.portrait != null;
        title.text = c.displayName;
    }

    public void SetChosen(bool value) => chosen = value;

    void Update()
    {
        // Gold and out of line when chosen, a touch brighter under the mouse, dim otherwise.
        float target = chosen ? 18f : hovered ? 6f : 0f;
        shownLift = Mathf.Lerp(shownLift, target, 1f - Mathf.Exp(-16f * Time.unscaledDeltaTime));
        if (lift != null) lift.anchoredPosition = liftDirection * shownLift;
        if (frame != null) frame.color = chosen ? new Color(.95f, .78f, .4f) : hovered ? new Color(.85f, .85f, .85f) : new Color(.6f, .6f, .6f);
        if (portrait != null) portrait.color = chosen || hovered ? Color.white : new Color(.62f, .62f, .62f);
        if (title != null) title.color = chosen ? new Color(.95f, .8f, .45f) : new Color(.78f, .76f, .72f);
    }

    public void OnPointerEnter(PointerEventData e) { hovered = true; UIFeedbackSettings.Shared?.Hover(); }
    public void OnPointerExit(PointerEventData e) => hovered = false;
    public void OnPointerClick(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) Clicked?.Invoke(this); }
}
