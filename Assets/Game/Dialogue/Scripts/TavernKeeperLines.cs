using UnityEngine;

// What the tavern keeper says when you talk to him (TavernKeeper.Talk, in the dialogue box): his welcomes, the answers
// you pick from and his replies. Sits on the Dialogue Manager with the rest of the game's words; Asset Studio's
// Dialogue view lists and edits them (Tavern).
public class TavernKeeperLines : Singleton<TavernKeeperLines>
{
    [Header("Welcome")]
    [TextArea(1, 3), Tooltip("He says one of these as you walk up to the bar, picked at random.")]
    public string[] welcomeLines =
    {
        "Welcome in! Wipe your boots. Whatever you stepped in out there, it stays outside.",
        "Ah, a customer who's still breathing. My favourite kind. They pay.",
        "Sit anywhere you like. Don't mind the smell, that's the stew. Mostly.",
    };

    [Header("Answers")]
    [DialogueText, Tooltip("The answer that opens the shop.")] public string shopAnswer = "Shop";
    [DialogueText, Tooltip("The answer that buys a rumour. {Price} is its price.")] public string rumourAnswer = "Rumour ({Price} gold)";
    [DialogueText, Tooltip("The answer that walks away.")] public string leaveAnswer = "Never mind";

    [Header("Rumour")]
    [TextArea(1, 3), Tooltip("Said when the rumour is too dear. {Price} is its price, {Gold} what you have.")]
    public string noMoneyLine = "A rumour is {Price} gold, friend. You've got {Gold}. I can count, you know.";
    [TextArea(1, 3), Tooltip("Said when no rumour is left to sell.")]
    public string noRumourLine = "That's all I know for now. Come back when the world's done something interesting.";
    [TextArea(1, 3), Tooltip("Said for a rumour whose place has neither a told line nor a rumour of its own (the place's Rumour Told is said otherwise).")]
    public string rumourFallbackLine = "I heard of a place. That's all I'll say.";
}
