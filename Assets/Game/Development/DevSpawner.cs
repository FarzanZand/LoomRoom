#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// F10, Editor and development builds only: the spawn window, in the inventory's look. Tabs list the catalog's items, the
// spell tomes, the room key items (ProgressionManager.RoomKeyItems) and the enemies; a click gives one to the table player
// (items, spells), carries it home (room items) or puts it in front of you (enemies), as many as the count says. Items:
// right-click drops them on the floor instead, and gear takes the rarity picked. While it is open the game is in the Menu
// state. The window is Development/Resources/Dev Spawner.prefab (DevSpawnerBuilder.Build() from an eval; edit it freely).
public class DevSpawner : MonoBehaviour
{
    enum Tab { Items, Spells, RoomItems, Enemies, Rooms }

    [SerializeField] GameObject panel;
    [SerializeField] TMP_InputField search;
    [SerializeField, Tooltip("Items, Spells, Room items, Enemies, Rooms.")] Button[] tabs = new Button[5];
    [SerializeField, Tooltip("The scroll list's content: one cell per entry is cloned from the template into it.")] RectTransform content;
    [SerializeField, Tooltip("One cell (hidden): an Image child named Icon, a RawImage child named Preview and a text child named Name.")] GameObject cellTemplate;
    [SerializeField] TMP_Text status;
    [SerializeField, Tooltip("Under the list: what the entry under the pointer is.")] TMP_Text info;
    [SerializeField] TMP_Text countLabel;
    [SerializeField] Button countDown, countUp;
    [SerializeField, Tooltip("Shown on the Items tab only.")] GameObject rarityRow;
    [SerializeField, Tooltip("Common, Uncommon, Rare, Unique, Legendary.")] Button[] rarityButtons = new Button[5];
    [SerializeField, Tooltip("Shown on the Rooms tab only: one button per room category, cloned from the template.")] RectTransform categoryRow;
    [SerializeField, Tooltip("A category button (hidden).")] Button categoryTemplate;
    [SerializeField, Tooltip("Every enemy prefab, for development builds (the Editor finds them itself). Filled by DevSpawnerBuilder.")]
    GameObject[] enemies = new GameObject[0];
    [SerializeField, Tooltip("Every room template and its category, for development builds (the Editor finds them itself). Filled by DevSpawnerBuilder.")]
    GameObject[] rooms = new GameObject[0];
    [SerializeField] string[] roomCategories = new string[0];

    static readonly ItemRarity[] Rarities = { ItemRarity.Common, ItemRarity.Uncommon, ItemRarity.Rare, ItemRarity.Unique, ItemRarity.Legendary };
    static readonly Color Gold = new(1f, .851f, .4f), Text = new(.902f, .902f, .902f), Dim = new(.62f, .6f, .56f);

    class Entry
    {
        public string name, info;
        public Sprite icon;
        public Object previewOf;   // the Editor's asset preview when there is no icon (enemies)
        public bool marked;        // drawn gold: a room item already carried
        public System.Func<int, string> give;
        public System.Func<int, string> drop;
    }

    readonly List<(GameObject cell, Entry entry)> cells = new();
    readonly List<Button> categoryButtons = new();
    string category = "All";
    Tab tab;
    int count = 1, rarity;
    bool open;
    int openedFrame = -10;
    float nextPreviewCheck;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (!Debug.isDebugBuild || FindAnyObjectByType<DevSpawner>() != null) return;
        var prefab = Resources.Load<GameObject>("Dev Spawner");
        if (prefab == null) { Debug.LogWarning("[DevSpawner] Development/Resources/Dev Spawner.prefab is missing."); return; }
        var go = Instantiate(prefab);
        go.name = "Dev Spawner";
        DontDestroyOnLoad(go);
    }

    void Awake()
    {
        panel.SetActive(false);
        cellTemplate.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.sceneUnloaded += CloseOnUnload;
        for (int i = 0; i < tabs.Length; i++) { int t = i; if (tabs[i] != null) tabs[i].onClick.AddListener(() => { tab = (Tab)t; Fill(); }); }
        for (int i = 0; i < rarityButtons.Length; i++) { int r = i; if (rarityButtons[i] != null) rarityButtons[i].onClick.AddListener(() => { rarity = r; ShowOptions(); }); }
        if (countDown != null) countDown.onClick.AddListener(() => SetCount(count <= 1 ? 1 : count == 5 ? 1 : count == 10 ? 5 : count - 1));
        if (countUp != null) countUp.onClick.AddListener(() => SetCount(count == 1 ? 5 : count == 5 ? 10 : count + 5));
        // F10 must not end up in the search line.
        search.onValidateInput = (text, index, c) => Time.frameCount <= openedFrame + 1 ? '\0' : c;
        search.onValueChanged.AddListener(_ => Fill());
    }

    void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= CloseOnUnload;
        if (open && GameManager.HasInstance) GameManager.Instance.Pop(GameState.Menu);
    }

    void CloseOnUnload(UnityEngine.SceneManagement.Scene scene) { if (open) Close(); }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.f10Key.wasPressedThisFrame) { if (open) Close(); else Open(); }
        else if (open && keyboard.escapeKey.wasPressedThisFrame) Close();
        // Enemy previews come in over a few frames in the Editor.
        if (open && Time.unscaledTime > nextPreviewCheck) { nextPreviewCheck = Time.unscaledTime + .4f; FillPreviews(); }
    }

    void Open()
    {
        open = true;
        openedFrame = Time.frameCount;
        panel.SetActive(true);
        if (GameManager.HasInstance) GameManager.Instance.Push(GameState.Menu);
        // In a run the items are wanted; in the room, its key items.
        bool inRun = PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table;
        if (!inRun && tab != Tab.RoomItems && status.text.Length == 0) tab = Tab.RoomItems;
        Fill();
        search.ActivateInputField();
    }

    void Close()
    {
        open = false;
        search.DeactivateInputField();
        panel.SetActive(false);
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Menu);
    }

    void SetCount(int value) { count = Mathf.Clamp(value, 1, 99); ShowOptions(); }

    void ShowOptions()
    {
        if (countLabel != null) countLabel.text = $"x{count}";
        if (rarityRow != null) rarityRow.SetActive(tab == Tab.Items);
        if (categoryRow != null) categoryRow.gameObject.SetActive(tab == Tab.Rooms);
        foreach (var b in categoryButtons) Mark(b, b.name == category);
        for (int i = 0; i < rarityButtons.Length; i++) Mark(rarityButtons[i], i == rarity);
        for (int i = 0; i < tabs.Length; i++) Mark(tabs[i], i == (int)tab);
    }

    // The picked tab or rarity: gold words on a lighter slot.
    static void Mark(Button button, bool on)
    {
        if (button == null) return;
        var label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.color = on ? Gold : Text;
        if (button.targetGraphic != null) button.targetGraphic.color = on ? new Color(1f, .9f, .62f) : Color.white;
    }

    // ── The list ─────────────────────────────────────────────────────

    void Fill()
    {
        if (tab == Tab.Rooms) MakeCategories();
        ShowOptions();
        foreach (var (cell, _) in cells) if (cell != null) Destroy(cell);
        cells.Clear();
        string filter = search.text.Trim();
        foreach (var entry in Entries())
        {
            if (filter.Length > 0 && entry.name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            var cell = Instantiate(cellTemplate, content);
            cell.name = entry.name;
            cell.SetActive(true);
            var nameText = Child<TMP_Text>(cell, "Name");
            if (nameText != null) { nameText.text = entry.name; nameText.color = entry.marked ? Gold : Text; }
            var icon = Child<Image>(cell, "Icon");
            if (icon != null) { icon.sprite = entry.icon; icon.enabled = entry.icon != null; icon.preserveAspect = true; }
            var preview = Child<RawImage>(cell, "Preview");
            if (preview != null) preview.enabled = false;
            Hook(cell, entry);
            cells.Add((cell, entry));
        }
        FillPreviews();
        if (info != null) info.text = cells.Count == 0 ? "Nothing here." : Hint;
    }

    string Hint => tab switch
    {
        Tab.Items => "Click: into the pack.  Right-click: on the floor in front of you.  Gear takes the rarity picked.",
        Tab.Spells => "Click: the spell's tome into the pack.",
        Tab.RoomItems => "Click: carried home, ready to use in the room. Gold: carried now.",
        Tab.Enemies => "Click: in front of you, scaled to the floor. Only on a floor of a run.",
        _ => "Click: a new run that opens in this room, you standing in it (as loadtemplate).",
    };

    static T Child<T>(GameObject cell, string name) where T : Component
    {
        foreach (var c in cell.GetComponentsInChildren<T>(true)) if (c.gameObject.name == name) return c;
        return null;
    }

    void Hook(GameObject cell, Entry entry)
    {
        var trigger = cell.GetComponent<EventTrigger>() ?? cell.AddComponent<EventTrigger>();
        trigger.triggers.Clear();
        var click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
        click.callback.AddListener(data =>
        {
            var pointer = (PointerEventData)data;
            var act = pointer.button == PointerEventData.InputButton.Right && entry.drop != null ? entry.drop : entry.give;
            if (act == null) return;
            Say(act(count));
            if (tab == Tab.RoomItems) Fill();   // the carried mark
        });
        trigger.triggers.Add(click);
        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => { if (info != null) info.text = string.IsNullOrEmpty(entry.info) ? entry.name : $"{entry.name}: {entry.info}"; });
        trigger.triggers.Add(enter);
        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => { if (info != null) info.text = Hint; });
        trigger.triggers.Add(exit);
    }

    void Say(string line) { if (status != null) status.text = line; }

    void FillPreviews()
    {
#if UNITY_EDITOR
        foreach (var (cell, entry) in cells)
        {
            if (cell == null || entry.icon != null || entry.previewOf == null) continue;
            var preview = Child<RawImage>(cell, "Preview");
            if (preview == null || preview.enabled) continue;
            var texture = UnityEditor.AssetPreview.GetAssetPreview(entry.previewOf);
            if (texture != null) { preview.texture = texture; preview.enabled = true; }
        }
#endif
    }

    IEnumerable<Entry> Entries()
    {
        switch (tab)
        {
            case Tab.Items:
                foreach (var item in DevGive.CatalogItems(false).Where(i => i.spell == null).OrderBy(i => i.itemType).ThenBy(i => i.tier).ThenBy(i => i.itemName))
                {
                    var it = item;
                    yield return new Entry
                    {
                        name = it.itemName, icon = it.icon, info = it.ShortInfo(),
                        give = n => DevGive.Item(it, n, it.IsGear ? Rarities[rarity] : null),
                        drop = n => DevGive.Drop(it, n, it.IsGear ? Rarities[rarity] : null),
                    };
                }
                break;
            case Tab.Spells:
                foreach (var tome in DevGive.CatalogItems(true).OrderBy(DevGive.SpellName))
                {
                    var t = tome;
                    yield return new Entry { name = DevGive.SpellName(t), icon = t.icon, info = t.spell != null ? t.spell.description : null, give = _ => DevGive.Spell(t) };
                }
                break;
            case Tab.RoomItems:
                if (!ProgressionManager.HasInstance) break;
                foreach (var key in ProgressionManager.Instance.RoomKeyItems())
                {
                    var k = key;
                    yield return new Entry { name = k.name, icon = k.icon, marked = k.carried, info = k.carried ? "carried now" : null, give = _ => DevGive.RoomItem(k) };
                }
                break;
            case Tab.Rooms:
                foreach (var (prefab, cat) in Rooms().Where(r => category == "All" || r.category == category).OrderBy(r => r.category).ThenBy(r => r.prefab.name))
                {
                    var p = prefab;
                    var room = p.GetComponent<DungeonRoomTemplate>();
                    yield return new Entry
                    {
                        name = p.name, previewOf = p, info = cat + (room != null && room.roomEvent != DungeonRoomEvent.Rolled ? $", {room.roomEvent}" : ""),
                        give = _ =>
                        {
                            if (!DevConsole.StartRun(p, p.name, out var said)) return said;
                            Close();
                            return said;
                        },
                    };
                }
                break;
            case Tab.Enemies:
                foreach (var prefab in Enemies().OrderBy(p => p.TryGetComponent<Character>(out var c) ? c.DisplayName : p.name))
                {
                    var p = prefab;
                    var character = p.GetComponent<Character>();
                    yield return new Entry { name = character != null ? character.DisplayName : p.name, previewOf = p, info = p.name, give = n => DevGive.Enemy(p, n) };
                }
                break;
        }
    }

    // The room categories: Events (rooms with a scripted event), Scripted, Hand-built (the shared chunks), else the
    // level whose folder holds them.
    IEnumerable<(GameObject prefab, string category)> Rooms()
    {
#if UNITY_EDITOR
        return DevConsole.FindTemplatePrefabs().Select(p => (p, RoomCategory(p)));
#else
        return rooms.Select((p, i) => (p, i < roomCategories.Length ? roomCategories[i] : "Rooms")).Where(r => r.p != null);
#endif
    }

    void MakeCategories()
    {
        if (categoryRow == null || categoryTemplate == null) return;
        categoryTemplate.gameObject.SetActive(false);
        var names = new[] { "All" }.Concat(Rooms().Select(r => r.category).Distinct().OrderBy(c => c)).ToList();
        if (categoryButtons.Select(b => b.name).SequenceEqual(names)) return;
        foreach (var b in categoryButtons) if (b != null) Destroy(b.gameObject);
        categoryButtons.Clear();
        foreach (var n in names)
        {
            var b = Instantiate(categoryTemplate, categoryRow);
            b.name = n;
            b.gameObject.SetActive(true);
            var label = b.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = n;
            string pick = n;
            b.onClick.AddListener(() => { category = pick; Fill(); });
            categoryButtons.Add(b);
        }
        if (!names.Contains(category)) category = "All";
    }

#if UNITY_EDITOR
    public static string RoomCategory(GameObject prefab)
    {
        var path = UnityEditor.AssetDatabase.GetAssetPath(prefab);
        if (prefab.GetComponent<ScriptedEventRoom>() != null || path.Contains("/Events/")) return "Events";
        if (path.Contains("/Scripted/")) return "Scripted";
        var parts = path.Split('/');
        int i = System.Array.IndexOf(parts, "Levels");
        string folder = i >= 0 && i + 1 < parts.Length ? parts[i + 1] : "Other";
        return folder == "Shared" ? "Hand-built" : folder;
    }
#endif

    IEnumerable<GameObject> Enemies()
    {
        IEnumerable<GameObject> found = enemies;
#if UNITY_EDITOR
        found = FindEnemyPrefabs();
#endif
        return found.Where(p => p != null).Distinct().OrderBy(p => p.name);
    }

#if UNITY_EDITOR
    // Every enemy under Characters/Enemies but the bases and the archive.
    public static GameObject[] FindEnemyPrefabs() => UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Characters/Enemies" })
        .Select(UnityEditor.AssetDatabase.GUIDToAssetPath)
        .Where(path => !path.Contains("/_Archive/") && !path.Contains("/Base/"))
        .Select(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>)
        .Where(p => p != null && p.GetComponent<EnemyBrain>() != null)
        .ToArray();
#endif
}
#endif
