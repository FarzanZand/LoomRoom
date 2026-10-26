using System.Collections.Generic;
using UnityEngine;

// The merchant as the tavern's keeper (on the Dungeon merchant prefab, next to Merchant): besides his wares he serves
// dishes (TavernDish: eaten once a run, they last it) and sells rumours (places off the campaign map that a rumour
// opens, CampaignEncounter.rumoured). Both are tabs of the shop screen (ShopUI, TavernTabsUI); the Rumours tab is only
// there while a rumour is left to sell. Edit the dishes and the price here, the rumours on their places.
public class TavernKeeper : MonoBehaviour
{
    [Tooltip("What he serves, in the order the Food tab lists them.")]
    public TavernDish[] dishes = new TavernDish[0];
    [Min(0), Tooltip("Gold for one rumour.")]
    public int rumourPrice = 20;
    [Tooltip("Posted to the feed when a dish is eaten. {Dish} is its name.")]
    public string ateMessage = "You eat the {Dish}.";
    [Tooltip("Posted when a rumour is bought. {Place} is the place's name.")]
    public string rumourMessage = "{Place} is on your campaign map now.";

    public enum Result { Done, NoMoney, AlreadyEaten, Nothing }

    public IReadOnlyList<CampaignEncounter> Rumours =>
        CampaignManager.HasInstance ? CampaignManager.Instance.Rumours() : new List<CampaignEncounter>();
    public bool HasRumours => Rumours.Count > 0;

    public Result Eat(TavernDish dish, Player player)
    {
        if (dish == null || player == null) return Result.Nothing;
        if (dish.Eaten) return Result.AlreadyEaten;
        if (player.Wallet == null || !player.Wallet.TrySpend(dish.price)) return Result.NoMoney;
        dish.MarkEaten();
        player.GetComponent<AdventurerProgress>()?.ApplyStats();
        MessageLog.Post(ateMessage.Replace("{Dish}", dish.displayName), MessageKind.Info);
        if (AudioManager.HasInstance) AudioManager.Instance.PlayUI(Merchant.ShopTradeKey);
        return Result.Done;
    }

    public Result BuyRumour(CampaignEncounter place, Player player)
    {
        if (place == null || player == null || !CampaignManager.HasInstance) return Result.Nothing;
        if (player.Wallet == null || !player.Wallet.TrySpend(rumourPrice)) return Result.NoMoney;
        CampaignManager.Instance.BuyRumour(place);
        MessageLog.Post(rumourMessage.Replace("{Place}", place.displayName), MessageKind.Info);
        if (AudioManager.HasInstance) AudioManager.Instance.PlayUI(Merchant.ShopTradeKey);
        return Result.Done;
    }
}
