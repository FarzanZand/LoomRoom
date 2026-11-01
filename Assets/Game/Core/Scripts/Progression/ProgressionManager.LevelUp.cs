using Sirenix.OdinInspector;
using UnityEngine;

// How the attribute point each character level gives in a run is put in (ProgressionManager > Level up Reward).
// Points: it waits on the character panel's Attributes for a click on a +. Cards: once the player is out of a fight,
// four cards are dealt, one per attribute with what a point there gives, and the one picked takes the point
// (LevelUpCardsUI). Either way the point is AdventurerProgress.UnspentPoints until it is put in, so a run saved with one
// left deals the cards again. Serialized by integer; append only.
public enum LevelUpReward { Points = 0, Cards = 1 }

public partial class ProgressionManager
{
    [Header("Level up")]
    [Tooltip("Points: each level's attribute point is put in with the + on the character panel. Cards: four attribute cards are dealt out of a fight and the one picked takes it.")]
    public LevelUpReward levelUpReward = LevelUpReward.Cards;
    [ShowIf(nameof(levelUpReward), LevelUpReward.Cards), Tooltip("The level-up cards (UI/Prefabs/Progression/Level up cards, built by LevelUpCardsSetup.Build()): put on the HUD canvas at the start of play while Level up Reward is Cards.")]
    public LevelUpCardsUI levelUpCards;
    [ShowIf(nameof(levelUpReward), LevelUpReward.Cards), Tooltip("On: the cards come up by themselves once out of a fight, and the game waits. Off: \"Press R to level up\" shows, and R brings the cards up while the game goes on (Esc puts them away again).")]
    public bool autoPopup = true;

    public static bool CardsOnLevelUp => HasInstance && Instance.levelUpReward == LevelUpReward.Cards;
    public static bool CardsAutoPopup => !HasInstance || Instance.autoPopup;

    // The cards on the HUD canvas, from the prefab above, unless the scene already holds a copy.
    void SpawnLevelUpCards()
    {
        if (levelUpReward != LevelUpReward.Cards || levelUpCards == null || LevelUpCardsUI.Find() != null) return;
        var hud = FindAnyObjectByType<HudCanvas>(FindObjectsInactive.Include);
        if (hud == null) { Debug.LogWarning("[Level up] No HUD canvas for the level-up cards."); return; }
        var cards = Instantiate(levelUpCards, hud.transform, false);
        cards.name = levelUpCards.name;
        cards.transform.SetAsLastSibling();
    }
}
