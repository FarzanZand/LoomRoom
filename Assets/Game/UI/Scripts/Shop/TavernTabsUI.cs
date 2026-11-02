using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The tavern keeper's tabs on the shop screen (ShopUI): Wares (the merchant's rows, as always) and Food (TavernDish
// cards: eat once a run, it lasts the run). With a plain merchant the tabs are hidden and the wares show. Rumours are
// no longer sold here: the keeper tells them himself (TavernKeeper.Talk). Everything is authored in the scene
// (TavernSetup.Build makes it); this fills it.
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
    [SerializeField] Tab wares, food;
    [Tooltip("The old Rumours tab and its panel, still in the scene until it is next rebuilt: kept hidden.")]
    [SerializeField] Tab rumours;
    [Tooltip("The wares' rows, headers and sold-out lines: hidden on the other tabs.")]
    [SerializeField] GameObject[] waresParts = new GameObject[0];
    [SerializeField] GameObject foodPanel, rumourPanel;
    [SerializeField] Card[] dishCards = new Card[0];
    [SerializeField] Color gold = new(1f, .851f, .4f), good = new(.62f, .82f, .49f), dim = new(.72f, .68f, .6f), tooDear = new(.69f, .353f, .306f), text = new(.9f, .9f, .9f);

    enum Page { Wares, Food }
    Page page;
    TavernKeeper keeper;
    Player player;

    void Awake()
    {
        if (wares.button != null) wares.button.onClick.AddListener(() => Show(Page.Wares));
        if (food.button != null) food.button.onClick.AddListener(() => Show(Page.Food));
        Hover(food, "Food", "Once a run", "What a dish gives lasts until the run ends.");
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
        if (player != null && player.Wallet != null) player.Wallet.Changed += OnGold;
        if (tabRow != null) tabRow.SetActive(keeper != null);
        if (rumours?.button != null) rumours.button.gameObject.SetActive(false);
        if (rumourPanel != null) rumourPanel.SetActive(false);
        Show(Page.Wares);
    }

    public void Unbind()
    {
        if (player != null && player.Wallet != null) player.Wallet.Changed -= OnGold;
        keeper = null; player = null;
    }

    void OnGold(int total, int delta) => Refresh();

    void Show(Page p)
    {
        if (keeper == null) p = Page.Wares;
        page = p;
        foreach (var part in waresParts) if (part != null) part.SetActive(p == Page.Wares);
        if (foodPanel != null) foodPanel.SetActive(p == Page.Food);
        // The wares' own rows come and go with what is left; ShopUI fills them again.
        if (p == Page.Wares && ShopUI.HasInstance) ShopUI.Instance.RefreshWares();
        if (ShopUI.HasInstance) ShopUI.Instance.SetHelp(p switch
        {
            Page.Food => "Hover over a dish to see what it does.",
            _ => null,
        });
        Refresh();
        // A pad: the first card's button is selected on the tab, so Submit acts at once.
        if (InputManager.PadConnected && p != Page.Wares) SelectFirst(dishCards);
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

    void Refresh()
    {
        if (keeper == null) return;
        Mark(wares, page == Page.Wares); Mark(food, page == Page.Food);
        if (page == Page.Food) FillDishes();
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

    static void Say(string message) { if (ShopUI.HasInstance && !string.IsNullOrEmpty(message)) ShopUI.Instance.Feedback(message); }
}
