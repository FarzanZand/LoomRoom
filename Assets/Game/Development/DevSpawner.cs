#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// F10, Editor and development builds only: the spawn window, laid out like Asset Studio in the inventory's look. Left:
// the kinds (Items, Spells, Room items, Enemies, Rooms, Cheats, Audio) and the groups of the one picked, each with its count. Middle: a
// search and a scrolling grid. Right: the picked entry, big, with what it is and its buttons (Add to pack, Drop on the
// floor, Spawn, Carry home, Play this room), count and rarity where they apply; a button that can't work here says why.
// Click picks, double-click or Enter does the first button, right-click the second. Typing always goes to the search;
// Esc clears it, then closes. While it is open the game is in the Menu state. Development/Resources/Dev Spawner.prefab is
// built by DevSpawnerBuilder.Build() (from an eval; edit the prefab freely after), its lists for builds by RefreshLists().
// Audio (DevSpawner.Audio.cs): every sound effect, sorted by category or by when it was last heard, to play and to set its
// volume, pitch and pitch variance, and save.
public partial class DevSpawner : MonoBehaviour
{
    enum Kind { Items, Spells, RoomItems, Enemies, Rooms, Cheats, Audio }
    static readonly string[] KindNames = { "Items", "Spells", "Room items", "Enemies", "Rooms", "Cheats", "Audio" };

    [Header("Window")]
    [SerializeField] GameObject panel;
    [SerializeField] TMP_InputField search;
    [SerializeField, Tooltip("What is shown and how many, over the grid.")] TMP_Text listTitle;
    [SerializeField, Tooltip("The last thing done (or why not), under the grid.")] TMP_Text status;

    [Header("Left: kinds and groups")]
    [SerializeField, Tooltip("Items, Spells, Room items, Enemies, Rooms, Cheats, Audio; each with children Label and Count.")] Button[] kindButtons = new Button[7];
    [SerializeField, Tooltip("Where the picked kind's groups go: one button each, cloned from the template.")] RectTransform groupList;
    [SerializeField, Tooltip("The groups' heading, hidden when a kind has none.")] GameObject groupTitle;
    [SerializeField, Tooltip("A group button (hidden), children Label and Count.")] Button groupTemplate;

    [Header("Middle: the grid")]
    [SerializeField] ScrollRect scroll;
    [SerializeField, Tooltip("The grid's content: one cell per entry, cloned from the template.")] RectTransform content;
    [SerializeField, Tooltip("One cell (hidden): children Icon (Image), Preview (RawImage), Name (text).")] GameObject cellTemplate;

    [Header("Right: the picked entry")]
    [SerializeField] GameObject detail;
    [SerializeField, Tooltip("Shown while nothing is picked.")] GameObject detailEmpty;
    [SerializeField] Image detailIcon;
    [SerializeField] RawImage detailPreview;
    [SerializeField] TMP_Text detailName, detailKind, detailText, detailNote;
    [SerializeField] GameObject countRow;
    [SerializeField] TMP_Text countLabel;
    [SerializeField] Button countDown, countUp;
    [SerializeField] GameObject rarityRow;
    [SerializeField, Tooltip("Common, Uncommon, Rare, Unique, Legendary.")] Button[] rarityButtons = new Button[5];
    [SerializeField] Button primary, secondary;

    [Header("Lists for development builds (the Editor finds them itself; DevSpawnerBuilder.RefreshLists)")]
    [SerializeField] GameObject[] enemies = new GameObject[0];
    [SerializeField] string[] enemyGroups = new string[0];
    [SerializeField] GameObject[] rooms = new GameObject[0];
    [SerializeField] string[] roomGroups = new string[0];

    static readonly ItemRarity[] Rarities = { ItemRarity.Common, ItemRarity.Uncommon, ItemRarity.Rare, ItemRarity.Unique, ItemRarity.Legendary };
    static readonly Color Gold = new(1f, .851f, .4f), Text = new(.902f, .902f, .902f), Picked = new(1f, .88f, .55f);
    const string All = "All";

    class Entry
    {
        public string key, name, kind, group;
        public System.Func<string> about;      // the detail panel's text
        public Sprite icon;
        public Object previewOf;               // the Editor's picture of it when there is no icon
        public bool marked;                    // gold: a room item carried now
        public bool usesCount, usesRarity;
        public string primaryLabel, secondaryLabel;
        public System.Func<string> primary, secondary;
        public System.Func<string> blocked;    // why the buttons can't work here now (null: they can)
        public Sound sound;                    // an Audio entry: its volume, pitch and variance rows
    }

    readonly List<Entry> entries = new();
    readonly List<(GameObject cell, Entry entry)> cells = new();
    readonly List<Button> groupButtons = new();
    Kind kind = Kind.Items;
    readonly string[] group = { All, All, All, All, All, All, All };
    Entry picked;
    int count = 1, rarity;
    bool open, openedOnce;
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
        if (groupTemplate != null) groupTemplate.gameObject.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.sceneUnloaded += CloseOnUnload;
        for (int i = 0; i < kindButtons.Length; i++) { var k = (Kind)i; if (kindButtons[i] != null) kindButtons[i].onClick.AddListener(() => SetKind(k)); }
        for (int i = 0; i < rarityButtons.Length; i++) { int r = i; if (rarityButtons[i] != null) rarityButtons[i].onClick.AddListener(() => { rarity = r; ShowDetail(); Refocus(); }); }
        if (countDown != null) countDown.onClick.AddListener(() => { SetCount(count <= 5 ? count - 1 : count == 10 ? 5 : count - 5); Refocus(); });
        if (countUp != null) countUp.onClick.AddListener(() => { SetCount(count < 5 ? count + 1 : count == 5 ? 10 : count + 5); Refocus(); });
        HookSteppers();
        if (primary != null) primary.onClick.AddListener(() => { Do(picked?.primary); Refocus(); });
        if (secondary != null) secondary.onClick.AddListener(() => { Do(picked?.secondary); Refocus(); });
        // F10 must not end up in the search line; typing anywhere goes there, so it never selects all on focus.
        search.onValidateInput = (text, index, c) => Time.frameCount <= openedFrame + 1 ? '\0' : c;
        search.onFocusSelectAll = false;
        search.onValueChanged.AddListener(_ => Fill(keepScroll: false));
        search.onSubmit.AddListener(_ => { Do(picked?.primary); Refocus(); });
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
        if (keyboard.f10Key.wasPressedThisFrame) { if (open) Close(); else Open(); return; }
        if (!open) return;
        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            if (search.text.Length > 0) { search.text = ""; Refocus(); }
            else Close();
            return;
        }
        // Up and down step through the grid a row at a time, left and right one at a time while the search is empty.
        int step = keyboard.downArrowKey.wasPressedThisFrame ? Columns : keyboard.upArrowKey.wasPressedThisFrame ? -Columns : 0;
        if (search.text.Length == 0) step += keyboard.rightArrowKey.wasPressedThisFrame ? 1 : keyboard.leftArrowKey.wasPressedThisFrame ? -1 : 0;
        if (step != 0) Step(step);
        // A click elsewhere takes the focus from the search: give it back, so typing never goes astray.
        if (!search.isFocused && EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null) Refocus();
        // The Editor's pictures come in over a few frames.
        if (Time.unscaledTime > nextPreviewCheck) { nextPreviewCheck = Time.unscaledTime + .3f; FillPreviews(); }
    }

    void Open()
    {
        open = true;
        openedFrame = Time.frameCount;
        panel.SetActive(true);
        if (GameManager.HasInstance) GameManager.Instance.Push(GameState.Menu);
        // The first time: room items in the room, items in a run. After that, where it was left.
        if (!openedOnce) kind = InRun ? Kind.Items : Kind.RoomItems;
        openedOnce = true;
        SetKind(kind);
    }

    void Close()
    {
        open = false;
        search.DeactivateInputField();
        panel.SetActive(false);
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Menu);
    }

    void Refocus()
    {
        if (!open) return;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(search.gameObject);
        search.ActivateInputField();
        search.MoveTextEnd(false);
    }

    static bool InRun => PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table;

    void SetKind(Kind k)
    {
        kind = k;
        entries.Clear();
        entries.AddRange(Build(k));
        search.SetTextWithoutNotify("");
        MakeGroups();
        Fill(keepScroll: false);
        Refocus();
    }

    void SetGroup(string g)
    {
        group[(int)kind] = g;
        MakeGroups();
        Fill(keepScroll: false);
        Refocus();
    }

    void SetCount(int value) { count = Mathf.Clamp(value, 1, 50); ShowDetail(); }

    // ── Left ─────────────────────────────────────────────────────────

    void MakeGroups()
    {
        for (int i = 0; i < kindButtons.Length; i++)
        {
            if (kindButtons[i] == null) continue;
            Mark(kindButtons[i], i == (int)kind);
            SetCountText(kindButtons[i], i == (int)kind ? entries.Count.ToString() : "");
        }
        foreach (var b in groupButtons) if (b != null) Destroy(b.gameObject);
        groupButtons.Clear();
        if (groupList == null || groupTemplate == null) return;
        var groups = entries.Select(e => e.group).Where(g => !string.IsNullOrEmpty(g)).Distinct().ToList();
        if (kind == Kind.Audio) groups.Insert(0, LastPlayed);
        if (groupTitle != null) groupTitle.SetActive(groups.Count >= 2);
        if (groups.Count < 2) { group[(int)kind] = All; return; }
        if (!groups.Contains(group[(int)kind])) group[(int)kind] = All;
        foreach (var g in new[] { All }.Concat(groups))
        {
            var b = Instantiate(groupTemplate, groupList);
            b.name = g;
            b.gameObject.SetActive(true);
            SetLabel(b, g);
            SetCountText(b, (g == All ? entries.Count : g == LastPlayed ? entries.Count(e => e.sound != null && e.sound.LastHeard >= 0f) : entries.Count(e => e.group == g)).ToString());
            Mark(b, g == group[(int)kind]);
            string pick = g;
            b.onClick.AddListener(() => SetGroup(pick));
            groupButtons.Add(b);
        }
    }

    static void SetLabel(Button b, string text) { var t = Find<TMP_Text>(b.gameObject, "Label"); if (t != null) t.text = text; }
    static void SetCountText(Button b, string text) { var t = Find<TMP_Text>(b.gameObject, "Count"); if (t != null) t.text = text; }

    // The picked kind, group or rarity: gold words on a warmer slot.
    static void Mark(Button button, bool on)
    {
        if (button == null) return;
        var label = Find<TMP_Text>(button.gameObject, "Label") ?? button.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.color = on ? Gold : Text;
        if (button.targetGraphic != null) button.targetGraphic.color = on ? Picked : Color.white;
    }

    static T Find<T>(GameObject root, string name) where T : Component
    {
        foreach (var c in root.GetComponentsInChildren<T>(true)) if (c.gameObject.name == name) return c;
        return null;
    }

    // ── Middle ───────────────────────────────────────────────────────

    IEnumerable<Entry> Shown()
    {
        string g = group[(int)kind], filter = search.text.Trim();
        // Audio's Last played: every sound heard since Play began, the latest first.
        var from = g == LastPlayed ? entries.Where(e => e.sound != null && e.sound.LastHeard >= 0f).OrderByDescending(e => e.sound.LastHeard) : entries.Where(e => g == All || e.group == g);
        return from.Where(e => (filter.Length == 0
            || e.name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0
            || (e.kind ?? "").IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0
            || (e.group ?? "").IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0));
    }

    void Fill(bool keepScroll)
    {
        // Out of the grid at once (Destroy waits for the frame's end), so nothing finds or lays out an old cell.
        foreach (var (cell, _) in cells) if (cell != null) { cell.transform.SetParent(null, false); Destroy(cell); }
        cells.Clear();
        foreach (var entry in Shown())
        {
            var cell = Instantiate(cellTemplate, content);
            cell.name = entry.name;
            cell.SetActive(true);
            var nameText = Find<TMP_Text>(cell, "Name");
            if (nameText != null) nameText.text = entry.name;
            var icon = Find<Image>(cell, "Icon");
            if (icon != null) { icon.sprite = entry.icon; icon.enabled = entry.icon != null; icon.preserveAspect = true; }
            var preview = Find<RawImage>(cell, "Preview");
            if (preview != null) preview.enabled = false;
            var handler = cell.GetComponent<DevSpawnerCell>() ?? cell.AddComponent<DevSpawnerCell>();
            var e = entry;
            handler.clicked = (button, clicks) =>
            {
                Pick(e);
                if (button == PointerEventData.InputButton.Right) Do(e.secondary ?? e.primary);
                else if (clicks >= 2) Do(e.primary);
                Refocus();
            };
            cells.Add((cell, entry));
        }
        string g = group[(int)kind], filter = search.text.Trim();
        if (listTitle != null)
            listTitle.text = $"{(g == All ? KindNames[(int)kind] : g)}  <color=#A39B8E>{cells.Count}{(filter.Length > 0 ? $" matching \"{filter}\"" : "")}</color>";
        if (status != null && cells.Count == 0 && filter.Length > 0) status.text = $"Nothing matches \"{filter}\". Esc clears the search.";
        else if (status != null && status.text.StartsWith("Nothing matches")) status.text = "";
        if (picked == null || !cells.Any(c => c.entry == picked)) picked = cells.Count > 0 ? cells[0].entry : null;
        Paint();
        ShowDetail();
        FillPreviews();
        if (!keepScroll && scroll != null) { Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = 1f; }
    }

    // The picked cell stands out.
    void Paint()
    {
        foreach (var (cell, entry) in cells)
        {
            var image = cell.GetComponent<Image>();
            if (image != null) image.color = entry == picked ? Picked : Color.white;
            var nameText = Find<TMP_Text>(cell, "Name");
            if (nameText != null) nameText.color = entry == picked || entry.marked ? Gold : Text;
            var tick = Find<Image>(cell, "Marked");
            if (tick != null) tick.enabled = entry.marked;
        }
    }

    void Pick(Entry e) { picked = e; Paint(); ShowDetail(); }

    int Columns
    {
        get
        {
            var grid = content != null ? content.GetComponent<GridLayoutGroup>() : null;
            if (grid == null) return 1;
            float width = content.rect.width - grid.padding.horizontal + grid.spacing.x;
            return Mathf.Max(1, Mathf.FloorToInt(width / (grid.cellSize.x + grid.spacing.x)));
        }
    }

    void Step(int by)
    {
        if (cells.Count == 0) return;
        int at = Mathf.Max(0, cells.FindIndex(c => c.entry == picked));
        at = Mathf.Clamp(at + by, 0, cells.Count - 1);
        Pick(cells[at].entry);
        // Keep it in view.
        if (scroll != null && content.rect.height > scroll.viewport.rect.height)
        {
            var cell = (RectTransform)cells[at].cell.transform;
            float top = -cell.anchoredPosition.y - cell.rect.height * .5f, bottom = top + cell.rect.height;
            float view = scroll.viewport.rect.height, max = content.rect.height - view, y = content.anchoredPosition.y;
            if (top < y) y = top - 10; else if (bottom > y + view) y = bottom - view + 10;
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, Mathf.Clamp(y, 0, max));
        }
    }

    void FillPreviews()
    {
#if UNITY_EDITOR
        foreach (var (cell, entry) in cells)
        {
            if (cell == null || entry.icon != null || entry.previewOf == null) continue;
            var preview = Find<RawImage>(cell, "Preview");
            if (preview == null || preview.enabled) continue;
            var texture = UnityEditor.AssetPreview.GetAssetPreview(entry.previewOf);
            if (texture != null) { preview.texture = texture; preview.enabled = true; }
        }
        if (picked != null && picked.icon == null && picked.previewOf != null && detailPreview != null && !detailPreview.enabled)
        {
            var texture = UnityEditor.AssetPreview.GetAssetPreview(picked.previewOf);
            if (texture != null) { detailPreview.texture = texture; detailPreview.enabled = true; }
        }
#endif
    }

    // ── Right ────────────────────────────────────────────────────────

    void ShowDetail()
    {
        bool has = picked != null;
        if (detail != null) detail.SetActive(has);
        if (detailEmpty != null) detailEmpty.SetActive(!has);
        if (!has) return;
        detailName.text = picked.name;
        detailKind.text = string.IsNullOrEmpty(picked.group) || picked.group == Plural(picked.kind) ? picked.kind : $"{picked.kind}  ·  {picked.group}";
        detailText.text = picked.about?.Invoke() ?? "";
        if (detailIcon != null) { detailIcon.sprite = picked.icon; detailIcon.enabled = picked.icon != null; detailIcon.preserveAspect = true; }
        if (detailPreview != null) detailPreview.enabled = false;
        FillPreviews();

        string why = picked.blocked?.Invoke();
        detailNote.text = why ?? "";
        detailNote.gameObject.SetActive(!string.IsNullOrEmpty(why));
        if (countRow != null) countRow.SetActive(picked.usesCount);
        if (countLabel != null) countLabel.text = count.ToString();
        if (rarityRow != null) rarityRow.SetActive(picked.usesRarity);
        ShowSteppers(picked.sound);
        for (int i = 0; i < rarityButtons.Length; i++) Mark(rarityButtons[i], i == rarity);
        Button(primary, picked.primaryLabel, picked.primary != null && why == null);
        Button(secondary, picked.secondaryLabel, picked.secondary != null && why == null);
    }

    static void Button(Button b, string label, bool usable)
    {
        if (b == null) return;
        b.gameObject.SetActive(!string.IsNullOrEmpty(label));
        SetLabel(b, label);
        b.interactable = usable;
        var t = Find<TMP_Text>(b.gameObject, "Label");
        if (t != null) t.alpha = usable ? 1f : .4f;
    }

    void Do(System.Func<string> act)
    {
        if (picked == null || act == null) return;
        string why = picked.blocked?.Invoke();
        if (why != null) { Say(why); return; }
        Say(act());
        // A room item's carried mark changes; a run starting closes the window.
        if (kind == Kind.RoomItems) { var key = picked.key; entries.Clear(); entries.AddRange(Build(kind)); picked = entries.FirstOrDefault(e => e.key == key); Fill(keepScroll: true); }
        else { if (kind == Kind.Audio) MakeGroups(); ShowDetail(); }   // a sound played joins Last played
    }

    void Say(string line) { if (status != null) status.text = line; }

    // ── The entries ──────────────────────────────────────────────────

    IEnumerable<Entry> Build(Kind k)
    {
        switch (k)
        {
            case Kind.Items:
                foreach (var item in DevGive.CatalogItems(false).Where(i => i.spell == null).OrderBy(i => ItemGroupOrder(i)).ThenBy(i => i.tier).ThenBy(i => i.itemName))
                {
                    var it = item;
                    bool gear = it.IsGear;
                    yield return new Entry
                    {
                        key = it.name, name = it.itemName, kind = it.TypeLine() + (it.tier > 0 ? $", tier {it.tier}" : ""), group = ItemGroup(it),
                        icon = it.icon, previewOf = it.icon == null ? it.worldPrefab : null,
                        about = () => it.BuildTooltip(true, true),
                        usesCount = true, usesRarity = gear,
                        primaryLabel = "Add to pack", primary = () => DevGive.Item(it, count, gear ? Rarities[rarity] : null),
                        secondaryLabel = "Drop on the floor", secondary = () => DevGive.Drop(it, count, gear ? Rarities[rarity] : null),
                        blocked = () => DevGive.TablePlayer(out var why) == null ? why : !InRun ? "Only while playing the table player: start a run." : null,
                    };
                }
                break;
            case Kind.Spells:
                foreach (var tome in DevGive.CatalogItems(true).OrderBy(DevGive.SpellName))
                {
                    var t = tome;
                    yield return new Entry
                    {
                        key = t.name, name = DevGive.SpellName(t), kind = "Spell", icon = t.icon,
                        about = () => (t.spell != null && !string.IsNullOrWhiteSpace(t.spell.description) ? t.spell.description + "\n\n" : "") + "Its tome goes in the hotbar; hold it in the left hand to cast.",
                        primaryLabel = "Add the tome to the pack", primary = () => DevGive.Spell(t),
                        blocked = () => DevGive.TablePlayer(out var why) == null ? why : null,
                    };
                }
                break;
            case Kind.RoomItems:
                if (!ProgressionManager.HasInstance) break;
                var keys = ProgressionManager.Instance.RoomKeyItems();
                foreach (var key in keys)
                {
                    var r = key;
                    yield return new Entry
                    {
                        key = r.name, name = r.name, kind = r.kind ?? "Room item", group = Plural(r.kind), icon = r.icon, marked = r.carried,
                        about = () => (r.about ?? "") + (r.carried ? "\n\nCarried now: use it in the room." : ""),
                        primaryLabel = r.carried ? "Carry it home again" : "Carry it home", primary = () => DevGive.RoomItem(r),
                        secondaryLabel = $"Carry all {keys.Count} home", secondary = () => { foreach (var x in keys) x.carry?.Invoke(); return $"{keys.Count} room items carried home."; },
                    };
                }
                break;
            case Kind.Enemies:
                foreach (var (prefab, g) in Enemies())
                {
                    var p = prefab;
                    var character = p.GetComponent<Character>();
                    yield return new Entry
                    {
                        key = p.name, name = p.name, kind = "Enemy", group = g, previewOf = p,
                        about = () => EnemyText(character),
                        usesCount = true,
                        primaryLabel = "Spawn in front of you", primary = () => DevGive.Enemy(p, count),
                        blocked = () => !InRun ? "Only on a floor of a run: start one (Rooms) first." : null,
                    };
                }
                break;
            case Kind.Cheats:
                // Shortcuts through a run (the user's F9 request, 2026-10-30).
                yield return new Entry
                {
                    key = "skipFloor", name = "Skip floor", kind = "Cheat",
                    about = () => "Straight down to the next floor, as if the stairs had been taken: the floor's XP, a new floor built, the checkpoint saved. On the last floor there is nowhere further down.",
                    primaryLabel = "Go to the next floor",
                    primary = () => { var l = TableManager.Loader; if (l == null || !l.HasNextFloor) return "No floor further down."; int to = l.FloorNumber + 1; l.Descend(); Close(); return $"Down to floor {to}."; },
                    blocked = () => { var l = TableManager.Loader; return !InRun || l == null ? "Only on a floor of a run." : l.Busy ? "A floor is being built. A moment." : !l.HasNextFloor ? "This is the last floor." : null; },
                };
                yield return new Entry
                {
                    key = "showMap", name = "Show map", kind = "Cheat",
                    about = () => "The whole floor on the minimap at once: every room and passage, the tavern and the stairs.",
                    primaryLabel = "Show the whole map",
                    primary = () => { int n = 0; foreach (var hud in FindObjectsByType<DungeonHud>()) { hud.RevealAll(); n++; } return n == 0 ? "No map here." : "The whole floor is on the map."; },
                    blocked = () => !InRun ? "Only on a floor of a run." : null,
                };
                break;
            case Kind.Audio:
                foreach (var e in AudioEntries()) yield return e;
                break;
            case Kind.Rooms:
                foreach (var (prefab, g) in Rooms())
                {
                    var p = prefab;
                    yield return new Entry
                    {
                        key = p.name, name = p.name, kind = "Room", group = g, previewOf = p,
                        about = () => RoomText(p),
                        primaryLabel = "Play this room",
                        primary = () => { if (!DevConsole.StartRun(p, p.name, out var said)) return said; Close(); return said; },
                        blocked = () => { var l = FindAnyObjectByType<TableLevelLoader>(); return l == null ? "No table here (the Room scene has it)." : l.Busy ? "A floor is being built. A moment." : null; },
                    };
                }
                break;
        }
    }

    static string Plural(string kind) => string.IsNullOrEmpty(kind) ? null : kind.EndsWith("y") ? kind.Substring(0, kind.Length - 1) + "ies" : kind + "s";

    static int ItemGroupOrder(ItemData i) => System.Array.IndexOf(new[] { "Weapons", "Shields", "Armour", "Trinkets", "Food and potions", "Other" }, ItemGroup(i));

    static string ItemGroup(ItemData i)
    {
        if (i.itemType == ItemType.Weapon) return "Weapons";
        if (i.itemType == ItemType.Shield) return "Shields";
        if (i.itemType == ItemType.Consumable) return "Food and potions";
        if (i.itemType == ItemType.Equipment && i.canBeEquipped) return Equipment.IsTrinket(i.equipSlot) ? "Trinkets" : "Armour";
        return "Other";
    }

    static string EnemyText(Character c)
    {
        if (c == null || c.data == null) return "";
        float Stat(StatType s) { foreach (var e in c.data.stats) if (e.stat == s) return e.baseValue; return 0f; }
        var lines = new List<string> { $"Health {Stat(StatType.MaxHealth):0}, damage {Stat(StatType.AttackDamage):0.#}, armour {Stat(StatType.Armor):0} (before the floor's scaling)." };
        if (c.data is EnemyData d)
        {
            var can = new List<string>();
            if (d.archer) can.Add("shoots arrows");
            if (d.caster && d.spells != null && d.spells.Count > 0) can.Add("casts " + string.Join(", ", d.spells.Where(s => s != null && s.spell != null).Select(s => s.spell.displayName).Distinct()));
            if (can.Count > 0) lines.Add("It " + string.Join(" and ", can) + ".");
        }
        lines.Add("Spawned three metres in front of you, facing you, scaled to the floor, with the floor's loot. Not alerted, so it can be backstabbed.");
        return string.Join("\n\n", lines);
    }

    static string RoomText(GameObject prefab)
    {
        var lines = new List<string>();
        if (prefab.TryGetComponent<ScriptedEventRoom>(out var ev) && !string.IsNullOrWhiteSpace(ev.summary)) lines.Add(ev.summary.Trim());
        var room = prefab.GetComponent<DungeonRoomTemplate>();
        if (room != null && room.roomEvent != DungeonRoomEvent.Rolled && room.roomEvent != DungeonRoomEvent.None) lines.Add($"Room event: {room.roomEvent}.");
        var loader = FindAnyObjectByType<TableLevelLoader>();
        var level = loader != null ? DevConsole.LevelFor(prefab, loader, out _) : null;
        lines.Add($"Starts a new run{(level != null ? $" of {level.displayName}" : "")} with you standing in this room; the rest of the floor is built round it.");
        return string.Join("\n\n", lines);
    }

    // ── What is listed ───────────────────────────────────────────────

    IEnumerable<(GameObject prefab, string group)> Enemies()
    {
#if UNITY_EDITOR
        var list = FindEnemyPrefabs().Select(p => (p, EnemyGroup(p)));
#else
        var list = enemies.Select((p, i) => (p, i < enemyGroups.Length ? enemyGroups[i] : "Enemies")).Where(e => e.p != null);
#endif
        return list.OrderBy(e => e.Item2).ThenBy(e => e.p.name);
    }

    IEnumerable<(GameObject prefab, string group)> Rooms()
    {
#if UNITY_EDITOR
        var list = DevConsole.FindTemplatePrefabs().Select(p => (p, RoomGroup(p)));
#else
        var list = rooms.Select((p, i) => (p, i < roomGroups.Length ? roomGroups[i] : "Rooms"));
#endif
        // The intro's own rooms only make sense in the intro.
        return list.Where(r => r.p != null && r.Item2 != null).OrderBy(r => RoomGroupOrder(r.Item2)).ThenBy(r => r.p.name);
    }

    static int RoomGroupOrder(string g) => g switch { "Scripted" => 0, "Bosses" => 1, _ => 2 };

#if UNITY_EDITOR
    // Every enemy under Characters/Enemies but the bases and the archive.
    public static GameObject[] FindEnemyPrefabs() => UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Characters/Enemies" })
        .Select(UnityEditor.AssetDatabase.GUIDToAssetPath)
        .Where(path => !path.Contains("/_Archive/") && !path.Contains("/Base/"))
        .Select(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>)
        .Where(p => p != null && p.GetComponent<EnemyBrain>() != null)
        .ToArray();

    // The folder under Characters/Enemies (Crypt, Town, Events, Arena...).
    public static string EnemyGroup(GameObject prefab)
    {
        var parts = UnityEditor.AssetDatabase.GetAssetPath(prefab).Split('/');
        int i = System.Array.IndexOf(parts, "Enemies");
        return i >= 0 && i + 2 < parts.Length ? parts[i + 1] : "Enemies";
    }

    // Bosses (a stage, the arena's pit, any biome guardian's arena), Scripted (rooms with an event of their own: a
    // ScriptedEventRoom, or anything in a Scripted folder), Hand-built (the shared rooms), or null for the intro's own rooms.
    public static string RoomGroup(GameObject prefab)
    {
        var path = UnityEditor.AssetDatabase.GetAssetPath(prefab);
        if (path.Contains("/IntroDungeon/")) return null;
        if (prefab.GetComponentInChildren<DungeonStage>(true) != null || prefab.GetComponent<ArenaFight>() != null || GuardianArenas().Contains(prefab)) return "Bosses";
        if (prefab.GetComponent<ScriptedEventRoom>() != null || path.Contains("/Scripted/")) return "Scripted";
        return "Hand-built";
    }

    static HashSet<GameObject> guardianArenas;
    static HashSet<GameObject> GuardianArenas()
    {
        if (guardianArenas != null) return guardianArenas;
        guardianArenas = new HashSet<GameObject>();
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:DungeonBiome", new[] { "Assets/Game/Levels" }))
            foreach (var b in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(UnityEditor.AssetDatabase.GUIDToAssetPath(guid)).OfType<DungeonBiome>())
                if (b != null && b.guardian != null && b.guardian.arena != null) guardianArenas.Add(b.guardian.arena);
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:TableLevelData", new[] { "Assets/Game/Levels" }))
            foreach (var b in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(UnityEditor.AssetDatabase.GUIDToAssetPath(guid)).OfType<DungeonBiome>())
                if (b != null && b.guardian != null && b.guardian.arena != null) guardianArenas.Add(b.guardian.arena);
        return guardianArenas;
    }
#endif
}
#endif
