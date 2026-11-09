using System;
using Sirenix.OdinInspector;
using UnityEngine;

// A text event on the campaign map: a picture, a title, a few lines, and choices. Each choice can cost banked spoils,
// give spoils, put items in the table player's bag at the start of the next run, and say something; its outcome text
// replaces the event's text, then the player carries on. Shown once (flag campaign.event.<id>) the first time the map is
// seen after its act opens (CampaignManager > Events).
[CreateAssetMenu(fileName = "Campaign event", menuName = "LoomRoom/Campaign Event")]
public class CampaignEvent : ScriptableObject
{
    [Serializable]
    public class Choice
    {
        [Tooltip("The button's words.")] public string label;
        [Min(0), Tooltip("Banked spoils it costs (the button is greyed out when the bank is short).")] public int spoilsCost;
        [Min(0), Tooltip("Banked spoils it gives.")] public int spoilsGain;
        [Tooltip("Put in the table player's bag when the next run starts.")] public ItemData[] nextRunItems = Array.Empty<ItemData>();
        [TextArea(3, 8), Tooltip("Shown in place of the event's text once chosen.")] public string outcome;
        [Tooltip("Said by the Dungeon Master once chosen (optional).")] public DungeonMaster.Line line = new();
    }

    [Tooltip("Saved in a flag: never change it once released.")] public string id;
    public string title;
    [PreviewField(120), Tooltip("The picture that fills the screen behind the text.")] public Sprite art;
    [TextArea(4, 12)] public string text;
    [Min(1), Tooltip("Shown the first time the map is seen after this act opens.")] public int afterAct = 2;
    public Choice[] choices = Array.Empty<Choice>();
    [Tooltip("The button that closes the event after a choice.")] public string continueLabel = "Continue";
}
