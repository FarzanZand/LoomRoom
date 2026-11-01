using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The merchant as the tavern's keeper (on the Dungeon merchant prefab, next to Merchant). Talking to him opens the
// dialogue box (PortraitDialogueUI) with one of his welcomes and two answers: Shop opens the shop screen (his wares and
// dishes, TavernTabsUI; a dish is eaten once a run and lasts it), Rumour tells of a place off the campaign map
// (CampaignEncounter.rumoured) for its price and puts it on the map. Edit the lines, dishes and price here, the
// rumours on their places.
public class TavernKeeper : MonoBehaviour
{
    [Tooltip("What he serves, in the order the Food tab lists them.")]
    public TavernDish[] dishes = new TavernDish[0];
    [Min(0), Tooltip("Gold for one rumour.")]
    public int rumourPrice = 20;
    [Tooltip("Posted to the feed when a dish is eaten. {Dish} is its name.")]
    public string ateMessage = "You eat the {Dish}.";

    [Header("Talk")]
    [TextArea(1, 3), Tooltip("He says one of these as you walk up to the bar, picked at random.")]
    public string[] welcomes =
    {
        "Welcome in! Wipe your boots. Whatever you stepped in down there, it stays outside.",
        "Ah, a customer who's still breathing. My favourite kind. They pay.",
        "Sit anywhere you like. Don't mind the smell, that's the stew. Mostly.",
    };
    [Tooltip("The answer that opens the shop.")] public string shopLabel = "Shop";
    [Tooltip("The answer that buys a rumour. {Price} is its price.")] public string rumourLabel = "Rumour ({Price} gold)";
    [Tooltip("The answer that walks away.")] public string leaveLabel = "Never mind";
    [TextArea(1, 3), Tooltip("Said when the rumour is too dear. {Price} is its price, {Gold} what you have.")]
    public string noMoneyLine = "A rumour is {Price} gold, friend. You've got {Gold}. I can count, you know.";
    [TextArea(1, 3), Tooltip("Said when no rumour is left to sell.")]
    public string noRumourLine = "That's all I know for now. Come back when the world's done something interesting.";
    [TextArea(1, 3), Tooltip("Said for a rumour whose place has no told line of its own.")]
    public string rumourFallback = "I heard of a place. That's all I'll say.";

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
        // Its New place card says the rest (CampaignManager.BuyRumour).
        CampaignManager.Instance.BuyRumour(place);
        if (AudioManager.HasInstance) AudioManager.Instance.PlayUI(Merchant.ShopTradeKey);
        return Result.Done;
    }

    // ── Talk ──

    Coroutine talk;
    bool pushed;

    // Merchant.Interact asks first: true while he talks (the shop opens from here when Shop is picked).
    public bool Talk(Merchant merchant, Player player)
    {
        if (talk != null) return true;
        var box = PortraitDialogueUI.Current;
        if (box == null || box.Open || welcomes == null || welcomes.Length == 0) return false;
        talk = StartCoroutine(Talking(merchant, player, box));
        return true;
    }

    IEnumerator Talking(Merchant merchant, Player player, PortraitDialogueUI box)
    {
        if (GameManager.HasInstance) { GameManager.Instance.Push(GameState.Dialogue); pushed = true; }
        var body = Body;
        if (player.Look != null && body != null) StartCoroutine(CameraEase.LookAt(player.Look, Head(body), .4f));

        // The rumour is weighed now: the reply to that answer is the rumour itself, the price, or that none is left.
        var place = HasRumours ? Rumours[0] : null;
        int gold = player.Wallet != null ? player.Wallet.Gold : 0;
        bool sells = place != null && gold >= rumourPrice;
        string rumourReply = place == null ? noRumourLine
            : !sells ? noMoneyLine.Replace("{Price}", rumourPrice.ToString()).Replace("{Gold}", gold.ToString())
            : !string.IsNullOrWhiteSpace(place.rumourTold) ? place.rumourTold
            : !string.IsNullOrWhiteSpace(place.rumour) ? place.rumour : rumourFallback;

        string who = merchant != null ? merchant.merchantName : "Tavern keeper";
        var model = body != null ? body.gameObject : gameObject;
        var welcome = new PortraitLine { speaker = PortraitSpeaker.Other, name = who, model = model, text = welcomes[Random.Range(0, welcomes.Length)] };
        welcome.choices.Add(new PortraitChoice { label = shopLabel });
        var rumour = new PortraitChoice { label = rumourLabel.Replace("{Price}", rumourPrice.ToString()) };
        rumour.reply.Add(new PortraitReply { speaker = PortraitSpeaker.Other, name = who, model = model, text = rumourReply });
        welcome.choices.Add(rumour);
        welcome.choices.Add(new PortraitChoice { label = leaveLabel });

        yield return box.Play(new[] { welcome }, null, who);
        int picked = box.Picked;
        EndTalk();
        if (picked == 0 && merchant != null) merchant.OpenShop(player);
        else if (picked == 1 && sells) BuyRumour(place, player);
    }

    void EndTalk()
    {
        talk = null;
        if (pushed && GameManager.HasInstance) GameManager.Instance.Pop(GameState.Dialogue);
        pushed = false;
    }

    // The floor went mid-talk: the box is put away and the state given back (a stopped coroutine runs no finally).
    void OnDisable()
    {
        if (talk == null) return;
        StopCoroutine(talk);
        if (PortraitDialogueUI.Current != null) PortraitDialogueUI.Current.Abort();
        EndTalk();
    }

    Character Body => GetComponentInParent<Character>();

    static Vector3 Head(Character body)
    {
        var animator = body.GetComponentInChildren<Animator>();
        var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        return head != null ? head.position : body.transform.position + Vector3.up * 1.6f;
    }
}
