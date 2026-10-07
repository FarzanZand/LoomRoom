using System;
using System.Collections.Generic;
using PixelCrushers.DialogueSystem;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The Dialogue System's UI for conversations: the Dungeon Master's lines go to the DM feed (said in
// person, as at the table) and the player's choices appear as buttons cloned from an authored
// template. The player's chosen line is echoed in the feed. Set as the Dialogue Manager's Dialogue UI.
public class DungeonMasterDialogueUI : MonoBehaviour, IDialogueUI
{
    [SerializeField, Tooltip("Shown while there are choices.")] GameObject responsePanel;
    [SerializeField, Tooltip("One choice. Cloned per response; kept inactive.")] Button responseTemplate;
    [SerializeField, Tooltip("Mumble on the Dungeon Master's lines.")] bool mumble = true;

    public event EventHandler<SelectedResponseEventArgs> SelectedResponseHandler;

    readonly List<Button> buttons = new();

    void Awake()
    {
        if (responseTemplate != null) responseTemplate.gameObject.SetActive(false);
        if (responsePanel != null) responsePanel.SetActive(false);
    }

    public void Open() { }
    public void Close() => HideResponses();

    public void ShowSubtitle(Subtitle subtitle)
    {
        string text = subtitle?.formattedText?.text;
        if (string.IsNullOrWhiteSpace(text)) return;
        if (subtitle.speakerInfo != null && subtitle.speakerInfo.isPlayer)
            MessageLog.Post(text, MessageKind.Info);
        else if (DungeonMaster.HasInstance)
            DungeonMaster.Instance.SayNow(new DungeonMaster.Line { text = text, inPerson = true, mumble = mumble });
    }

    public void HideSubtitle(Subtitle subtitle) { }

    public void ShowResponses(Subtitle subtitle, Response[] responses, float timeout)
    {
        HideResponses();
        if (responsePanel == null || responseTemplate == null || responses == null || responses.Length == 0) return;
        responsePanel.SetActive(true);
        foreach (var response in responses)
        {
            var button = Instantiate(responseTemplate, responseTemplate.transform.parent);
            button.gameObject.SetActive(true);
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = response.formattedText.text;
            button.interactable = response.enabled;
            var captured = response;
            button.onClick.AddListener(() => Choose(captured));
            buttons.Add(button);
        }
        if (EventSystem.current != null && buttons.Count > 0) EventSystem.current.SetSelectedGameObject(buttons[0].gameObject);
    }

    void Choose(Response response)
    {
        HideResponses();
        SelectedResponseHandler?.Invoke(this, new SelectedResponseEventArgs(response));
    }

    public void HideResponses()
    {
        foreach (var b in buttons) if (b != null) Destroy(b.gameObject);
        buttons.Clear();
        if (responsePanel != null) responsePanel.SetActive(false);
    }

    public void ShowQTEIndicator(int index) { }
    public void HideQTEIndicator(int index) { }
    public void ShowAlert(string message, float duration) => NotificationUI.Show(message);
    public void HideAlert() { }
}
