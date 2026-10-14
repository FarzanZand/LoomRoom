using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The pickup list on the right edge: each item or pile of gold the table player picks up gets a row (icon and
// name, "+12 gold"), newest at the bottom; the same thing picked up again within a moment adds to its row; rows fade
// after a few seconds. While it is on screen the DM feed leaves pickups out (DungeonMaster). The row is authored in
// the scene (template, inactive) and cloned per pickup, like ContextMenuUI's buttons.
public class PickupFeedUI : MonoBehaviour
{
    public static bool Active { get; private set; }

    [SerializeField] RectTransform rows;
    [SerializeField, Tooltip("Inactive row under Rows: an Image named Icon and a TMP text named Label.")] RectTransform template;
    [SerializeField] Sprite goldIcon;
    [SerializeField] Color goldColor = new(1f, .84f, .4f);
    [SerializeField] Color itemColor = new(.92f, .9f, .84f);
    [SerializeField, Min(.5f)] float showSeconds = 3.5f;
    [SerializeField, Min(.05f)] float fadeSeconds = .6f;
    [SerializeField, Min(0f), Tooltip("The same thing picked up again within this many seconds adds to its row.")] float mergeSeconds = 2.5f;
    [SerializeField, Min(1)] int maxRows = 6;
    [SerializeField] Vector2 iconArea = new(38f, 38f);

    class Row { public RectTransform rect; public CanvasGroup group; public TMP_Text label; public Image icon; public object key; public int count; public float shown; }
    readonly List<Row> live = new();
    readonly Stack<Row> spare = new();

    void OnEnable()
    {
        Active = true;
        if (template != null) template.gameObject.SetActive(false);
        if (InventoryManager.HasInstance) InventoryManager.Instance.ItemPickedUp += OnItem;
        if (CurrencyManager.HasInstance) CurrencyManager.Instance.GoldCollected += OnGold;
    }

    void Start()
    {
        // Managers may wake after this UI.
        if (InventoryManager.HasInstance) { InventoryManager.Instance.ItemPickedUp -= OnItem; InventoryManager.Instance.ItemPickedUp += OnItem; }
        if (CurrencyManager.HasInstance) { CurrencyManager.Instance.GoldCollected -= OnGold; CurrencyManager.Instance.GoldCollected += OnGold; }
    }

    void OnDisable()
    {
        Active = false;
        if (InventoryManager.HasInstance) InventoryManager.Instance.ItemPickedUp -= OnItem;
        if (CurrencyManager.HasInstance) CurrencyManager.Instance.GoldCollected -= OnGold;
    }

    void OnItem(ItemData item, Player who, int count)
    {
        if (item == null || who == null || who.kind != PlayerKind.Table) return;
        Show(item, item.icon, item.GridSize, count, n => n > 1 ? $"{item.itemName}  x{n}" : item.itemName, item.HasRarityColour ? Color.Lerp(item.RarityColour, Color.white, .35f) : itemColor);
    }

    void OnGold(Player who, int amount)
    {
        if (who == null || who.kind != PlayerKind.Table) return;
        string name = CurrencyManager.HasInstance ? CurrencyManager.Instance.currencyName : "gold";
        Show(nameof(OnGold), goldIcon, Vector2Int.one, amount, n => $"+{n} {name}", goldColor);
    }

    // cells: the item's bag footprint, so a long weapon lies across its icon box like in the bag.
    void Show(object key, Sprite icon, Vector2Int cells, int count, System.Func<int, string> text, Color color)
    {
        if (rows == null || template == null) return;
        var row = live.Find(r => Equals(r.key, key) && Time.unscaledTime - r.shown < mergeSeconds);
        if (row == null)
        {
            if (live.Count >= maxRows) Recycle(live[0]);
            row = spare.Count > 0 ? spare.Pop() : Make();
            row.key = key; row.count = 0;
            row.rect.SetAsLastSibling();
            row.rect.gameObject.SetActive(true);
            live.Add(row);
        }
        row.count += count;
        row.label.text = text(row.count);
        row.label.color = color;
        if (row.icon != null)
        {
            row.icon.sprite = icon; row.icon.enabled = icon != null;
            ItemGridUI.UprightIcon(row.icon.rectTransform, iconArea, cells);
        }
        row.shown = Time.unscaledTime;
        row.group.alpha = 1f;
    }

    Row Make()
    {
        var rect = Instantiate(template, rows);
        var group = rect.GetComponent<CanvasGroup>(); if (group == null) group = rect.gameObject.AddComponent<CanvasGroup>();
        return new Row { rect = rect, group = group, label = rect.Find("Label")?.GetComponent<TMP_Text>(), icon = rect.Find("Icon")?.GetComponent<Image>() };
    }

    void Recycle(Row row) { row.rect.gameObject.SetActive(false); live.Remove(row); spare.Push(row); }

    void Update()
    {
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var row = live[i];
            float age = Time.unscaledTime - row.shown;
            row.group.alpha = age < showSeconds ? 1f : 1f - (age - showSeconds) / fadeSeconds;
            if (age >= showSeconds + fadeSeconds) Recycle(row);
        }
    }
}
