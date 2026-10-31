using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The tavern keeper's tabs on the shop screen (ShopUI): Wares (the merchant's rows, as always), Food (TavernDish cards:
// eat once a run, it lasts the run) and Rumours (only while one is left to sell: a cryptic line until paid, then the
// place it tells of, which joins the campaign map). With a plain merchant the tabs are hidden and the wares show.
// Everything is authored in the scene (TavernSetup.Build makes it); this fills it.
public class TavernTabsUI : MonoBehaviour
{
    [System.Serializable]
    public class Tab
    {
        public Button button;
        public TMP_Text label;
        [Tooltip("Shown on the picked tab (the gold frame).")] public GameObject picked;
    }

    [System.Serializable]
    public class Card
    {
        public GameObject root;
        public Image icon;
        public TMP_Text name, info, note, price;
        public GameObject coin;
        public Button button;
        public TMP_Text buttonLabel;
        public HoverText hover;
    }

    [SerializeField] GameObject tabRow;
    [SerializeField] Tab wares, food, rumours;
    [Tooltip("The wares' rows, headers and sold-out lines: hidden on the other tabs.")]
    [SerializeField] GameObject[] waresParts = new GameObject[0];
    [SerializeField] GameObject foodPanel, rumourPanel;
    [SerializeField] Card[] dishCards = new Card[0];
    [SerializeField] Card[] rumourCards = new Card[0];
    [SerializeField] TMP_Text rumourEmpty;
    [SerializeField] Sprite rumourIcon;
    [SerializeField] Color gold = new(1f, .851f, .4f), good = new(.62f, .82f, .49f), dim = new(.72f, .68f, .6f), tooDear = new(.69f, .353f, .306f), text = new(.9f, .9f, .9f);

    enum Page { Wares, Food, Rumours }
    Page page;
    TavernKeeper keeper;
    Player player;
    // Rumours bought while the screen is open: shown told (the place's name) until it closes.
    readonly List<CampaignEncounter> told = new();

    void Awake()
    {
        if (wares.button != null) wares.button.onClick.AddListener(() => Show(Page.Wares));
        if (food.button != null) food.button.onClick.AddListener(() => Show(Page.Food));
        if (rumours.button != null) rumours.button.onClick.AddListener(() => Show(Page.Rumours));
        Hover(food, "Food", "Once a run", "What a dish gives lasts until the run ends.");
        Hover(rumours, "Rumours", "Places you haven't found", "Pay for a rumour to hear where it is. The place joins your campaign map.");
        Hover(wares, "Wares", "Buy and sell", "Supplies and gear for this run. Right click your own things to sell them.");
    }

    static void Hover(Tab t, string title, string sub, string body)
    {
        if (t?.button == null) return;
        var h = t.button.GetComponent<HoverText>() ?? t.button.gameObject.AddComponent<HoverText>();
        h.Set(title, sub, body);
    }

    // Called as the shop screen opens: a tavern keeper gets the tabs, a plain merchant only the wares.
    public void Bind(Merchant merchant, Player who)
    {
        keeper = merchant != null ? merchant.GetComponentInParent<TavernKeeper>() ?? merchant.GetComponentInChildren<TavernKeeper>() : null;
        player = who;
        told.Clear();
        if (player != null && player.Wallet != null) player.Wallet.Changed += OnGold;
        if (tabRow != null) tabRow.SetActive(keeper != null);
        Show(Page.Wares);
    }

    public void Unbind()
    {
        if (player != null && player.Wallet != null) player.Wallet.Changed -= OnGold;
        keeper = null; player = null; told.Clear();
    }

    void OnGold(int total, int delta) => Refresh();

    void Show(Page p)
    {
        if (keeper == null) p = Page.Wares;
        if (p == Page.Rumours && !RumoursShown) p = Page.Wares;
        page = p;
        foreach (var part in waresParts) if (part != null) part.SetActive(p == Page.Wares);
        if (foodPanel != null) foodPanel.SetActive(p == Page.Food);
        if (rumourPanel != null) rumourPanel.SetActive(p == Page.Rumours);
        // The wares' own rows come and go with what is left; ShopUI fills them again.
        if (p == Page.Wares && ShopUI.HasInstance) ShopUI.Instance.RefreshWares();
        if (ShopUI.HasInstance) ShopUI.Instance.SetHelp(p switch
        {
            Page.Food => "Hover over a dish to see what it does.",
            Page.Rumours => "Hover over a rumour to see what it costs.",
            _ => null,
        });
        Refresh();
        // A pad: the first card's button is selected on the tab, so Submit acts at once.
        if (InputManager.PadConnected && p != Page.Wares) SelectFirst(p == Page.Food ? dishCards : rumourCards);
    }

    static void SelectFirst(Card[] cards)
    {
        foreach (var c in cards)
            if (c?.button != null && c.button.gameObject.activeInHierarchy && c.button.IsInteractable()) { c.button.Select(); return; }
    }

    // ShopUI fills the ware rows after any change (gold, the pack): off the Wares tab they go again.
    public void AfterShopRefresh()
    {
        if (keeper != null && page != Page.Wares) foreach (var part in waresParts) if (part != null && part.activeSelf) part.SetActive(false);
    }

    bool RumoursShown => keeper != null && (keeper.HasRumours || told.Count > 0);

    void Refresh()
    {
        if (keeper == null) return;
        if (rumours.button != null) rumours.button.gameObject.SetActive(RumoursShown);
        // How many are waiting, so a new one is noticed.
        if (rumours.label != null) { int left = keeper.Rumours.Count; rumours.label.text = left > 0 ? $"Rumours ({left})" : "Rumours"; }
        Mark(wares, page == Page.Wares); Mark(food, page == Page.Food); Mark(rumours, page == Page.Rumours);
        if (page == Page.Food) FillDishes();
        if (page == Page.Rumours) FillRumours();
    }

    void Mark(Tab t, bool on)
    {
        if (t == null) return;
        if (t.picked != null) t.picked.SetActive(on);
        if (t.label != null) t.label.color = on ? gold : text;
    }

    int Coins => player != null && player.Wallet != null ? player.Wallet.Gold : 0;

    void FillDishes()
    {
        var dishes = keeper.dishes ?? new TavernDish[0];
        for (int i = 0; i < dishCards.Length; i++)
        {
            var c = dishCards[i];
            var dish = i < dishes.Length ? dishes[i] : null;
            if (c?.root == null) continue;
            c.root.SetActive(dish != null);
            if (dish == null) continue;
            bool eaten = dish.Eaten;
            if (c.icon != null) { c.icon.sprite = dish.icon; c.icon.enabled = dish.icon != null; }
            c.name.text = dish.displayName;
            c.info.text = dish.effectText; c.info.color = good;
            c.note.text = eaten ? "Eaten" : dish.flavour;
            c.note.color = eaten ? gold : dim;
            c.price.gameObject.SetActive(!eaten); if (c.coin != null) c.coin.SetActive(!eaten);
            c.price.text = dish.price.ToString(); c.price.color = Coins >= dish.price ? text : tooDear;
            c.button.gameObject.SetActive(!eaten);
            c.buttonLabel.text = "Eat";
            // Too dear: the button stays (a click says what it costs) but reads as off.
            c.buttonLabel.color = Coins >= dish.price ? text : dim;
            c.button.onClick.RemoveAllListeners();
            var d = dish;
            c.button.onClick.AddListener(() => Eat(d));
            if (c.hover != null) c.hover.Set(dish.displayName, eaten ? "Eaten this run" : $"{dish.price} gold · lasts until this run ends", dish.effectText + (string.IsNullOrEmpty(dish.flavour) ? "" : "\n\n" + dish.flavour));
        }
    }

    void Eat(TavernDish dish)
    {
        var result = keeper.Eat(dish, player);
        Say(result switch
        {
            TavernKeeper.Result.Done => $"You eat the {dish.displayName}.",
            TavernKeeper.Result.NoMoney => $"You need {dish.price} gold for that.",
            TavernKeeper.Result.AlreadyEaten => "You have had that already this run.",
            _ => "",
        });
        Refresh();
    }

    void FillRumours()
    {
        var list = new List<(CampaignEncounter place, bool known)>();
        foreach (var e in told) list.Add((e, true));
        foreach (var e in keeper.Rumours) if (!told.Contains(e)) list.Add((e, false));
        for (int i = 0; i < rumourCards.Length; i++)
        {
            var c = rumourCards[i];
            if (c?.root == null) continue;
            c.root.SetActive(i < list.Count);
            if (i >= list.Count) continue;
            var (place, known) = list[i];
            if (c.icon != null) { c.icon.sprite = rumourIcon; c.icon.enabled = rumourIcon != null; }
            if (known)
            {
                c.name.text = $"{place.displayName}  <color=#{ColorUtility.ToHtmlStringRGB(dim)}>act {place.act}</color>";
                c.info.text = string.IsNullOrEmpty(place.rumourTold) ? place.description : place.rumourTold; c.info.color = text;
                c.note.text = "Now on your campaign map"; c.note.color = good;
                c.price.gameObject.SetActive(false); if (c.coin != null) c.coin.SetActive(false);
                c.button.gameObject.SetActive(false);
                if (c.hover != null) c.hover.Set(place.displayName, $"Act {place.act} · on your campaign map", (string.IsNullOrEmpty(place.description) ? "" : place.description + "\n\n") + "It rises onto the map as soon as you are back in the room.");
            }
            else
            {
                c.name.text = "A rumour";
                c.info.text = $"\"{place.rumour}\""; c.info.color = text;
                c.note.text = "Pay to hear the rest."; c.note.color = dim;
                c.price.gameObject.SetActive(true); if (c.coin != null) c.coin.SetActive(true);
                c.price.text = keeper.rumourPrice.ToString(); c.price.color = Coins >= keeper.rumourPrice ? text : tooDear;
                c.button.gameObject.SetActive(true);
                c.buttonLabel.text = "Listen";
                c.buttonLabel.color = Coins >= keeper.rumourPrice ? text : dim;
                c.button.onClick.RemoveAllListeners();
                var p = place;
                c.button.onClick.AddListener(() => Buy(p));
                if (c.hover != null) c.hover.Set("A rumour", $"{keeper.rumourPrice} gold", "About somewhere you haven't found yet. Pay to hear where it is: the place joins your campaign map, and you can go there from the table.");
            }
        }
        if (rumourEmpty != null) rumourEmpty.gameObject.SetActive(keeper.Rumours.Count == 0);
    }

    void Buy(CampaignEncounter place)
    {
        var result = keeper.BuyRumour(place, player);
        if (result == TavernKeeper.Result.Done) { told.Add(place); Say($"{place.displayName} is on your campaign map now."); }
        else if (result == TavernKeeper.Result.NoMoney) Say($"A rumour costs {keeper.rumourPrice} gold.");
        Refresh();
    }

    static void Say(string message) { if (ShopUI.HasInstance && !string.IsNullOrEmpty(message)) ShopUI.Instance.Feedback(message); }
}
