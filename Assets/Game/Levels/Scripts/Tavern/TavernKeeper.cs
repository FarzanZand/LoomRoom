using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The merchant as the tavern's keeper (on the Dungeon merchant prefab, next to Merchant). Talking to him opens the
// dialogue box (PortraitDialogueUI) with one of his welcomes and three answers: Shop opens the shop screen (his wares and
// dishes, TavernTabsUI; a dish is eaten once a run and lasts it), Rumour tells of a place off the campaign map
// (CampaignEncounter.rumoured) for its price and puts it on the map, Never mind walks away. His words are a data asset
// (dialogue: Dialogue/Conversations/Tavern keeper, also in Asset Studio's Dialogue view), the dishes and price here, the
// rumours on their places.
public class TavernKeeper : MonoBehaviour
{
    [Tooltip("What he says (an asset in Dialogue/Conversations): his welcomes, the answers and his replies.")]
    public TavernKeeperDialogueData dialogue;
    [Tooltip("What he serves, in the order the Food tab lists them.")]
    public TavernDish[] dishes = new TavernDish[0];
    [Min(0), Tooltip("Gold for one rumour.")]
    public int rumourPrice = 20;
    [Tooltip("Posted to the feed when a dish is eaten. {Dish} is its name.")]
    public string ateMessage = "You eat the {Dish}.";

    public enum Result { Done, NoMoney, AlreadyEaten, Nothing }

    public IReadOnlyList<CampaignEncounter> Rumours =>
        CampaignManager.HasInstance ? CampaignManager.Instance.Rumours() : new List<CampaignEncounter>();
    public bool HasRumours => Rumours.Count > 0;
    // The ! on the Rumour answer while a rumour is left: the yellow of the map's mark over a new place.
    static readonly Color RumourMarkColor = new(.85f, .72f, 0f);
    [Tooltip("The font of the ! after Rumour: the bold one, as the map's mark is (the body font's ! is one pixel wide).")]
    public TMPro.TMP_FontAsset markFont;

    // The ! in the mark's font. TMP finds a <font> by name among the fonts it has been told of, so this one is told first.
    string MarkFontTag()
    {
        if (markFont == null) return "!";
        TMPro.MaterialReferenceManager.AddFontAsset(markFont);
        return "<font=\"" + markFont.name + "\">!</font>";
    }

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
        // Never during the intro's practice run (its board has no tavern, but nothing of the meta counts there).
        if (place == null || player == null || !CampaignManager.HasInstance || !ProgressionManager.MetaActive) return Result.Nothing;
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
        var words = dialogue;
        if (box == null || box.Open || words == null || words.welcomeLines == null || words.welcomeLines.Length == 0) return false;
        talk = StartCoroutine(Talking(merchant, player, box, words));
        return true;
    }

    IEnumerator Talking(Merchant merchant, Player player, PortraitDialogueUI box, TavernKeeperDialogueData words)
    {
        if (GameManager.HasInstance) { GameManager.Instance.Push(GameState.Dialogue); pushed = true; }
        var body = Body;
        if (player.Look != null && body != null) StartCoroutine(CameraEase.LookAt(player.Look, Head(body), .4f));

        // The rumour is weighed now: the reply to that answer is the rumour itself, the price, or that none is left.
        // Co-op: a rumour puts a place on the campaign map, which is the host's; a guest's keeper keeps them to himself.
        var place = HasRumours && !Coop.IsGuest ? Rumours[0] : null;
        int gold = player.Wallet != null ? player.Wallet.Gold : 0;
        bool sells = place != null && gold >= rumourPrice;
        string rumourReply = place == null ? words.noRumourLine
            : !sells ? words.noMoneyLine.Replace("{Price}", rumourPrice.ToString()).Replace("{Gold}", gold.ToString())
            : !string.IsNullOrWhiteSpace(place.rumourTold) ? place.rumourTold
            : !string.IsNullOrWhiteSpace(place.rumour) ? place.rumour : words.rumourFallbackLine;

        string who = merchant != null ? merchant.merchantName : "Tavern keeper";
        var model = body != null ? body.gameObject : gameObject;
        var welcome = new PortraitLine { speaker = PortraitSpeaker.Other, name = who, model = model, text = words.welcomeLines[Random.Range(0, words.welcomeLines.Length)] };
        welcome.choices.Add(new PortraitChoice { label = words.shopAnswer });
        // A yellow ! after it while he has a rumour left to sell, none once they are all told (the user, 2026-11-11; the
        // map's new-place mark's yellow).
        string rumourLabel = words.rumourAnswer.Replace("{Price}", rumourPrice.ToString());
        if (place != null) rumourLabel += " <color=#" + ColorUtility.ToHtmlStringRGB(RumourMarkColor) + ">" + MarkFontTag() + "</color>";
        var rumour = new PortraitChoice { label = rumourLabel };
        rumour.reply.Add(new PortraitReply { speaker = PortraitSpeaker.Other, name = who, model = model, text = rumourReply });
        welcome.choices.Add(rumour);
        welcome.choices.Add(new PortraitChoice { label = words.leaveAnswer, leaves = true });

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
