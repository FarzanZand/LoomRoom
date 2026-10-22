using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// The campaign: the world map on the table between runs. Using the table brings the player to its edge, looking over
// the map; hovering a place shows its card, clicking a dungeon walks the player's figure there and loads it on the
// table, clicking the figure changes class. Beating a place opens the acts it unlocks (Unlocks), whose fog lifts the
// next time the map is shown. All the campaign's rules and looks are set here; the map itself is a prefab
// (CampaignMap) edited by hand. Progress is plain flags (campaign.*), so saves need no new format.
public class CampaignManager : Singleton<CampaignManager>
{
    [Serializable]
    public class Unlock
    {
        [Tooltip("Beating this place...")] public CampaignEncounter beaten;
        [Range(1, 4), Tooltip("...opens this act (and every act before it).")] public int opensAct = 2;
    }

    [TabGroup("Map"), Tooltip("The map prefab laid on the table. Off: the table works as before (figures, then the dungeon).")]
    public bool useCampaign = true;
    [TabGroup("Map"), Required] public CampaignMap mapPrefab;
    [TabGroup("Map"), Tooltip("Where the map's centre lies on the table (world space). Empty: the table's top, found from its collider.")]
    public Transform mapSpot;
    [TabGroup("Map"), Tooltip("Seconds for the map to grow up out of the table, or fold away.")]
    [Min(0)] public float riseSeconds = .8f;

    [TabGroup("Rules"), Tooltip("Where the figure stands in a new campaign.")] public CampaignEncounter home;
    [TabGroup("Rules"), Tooltip("Every place there is (their order is saved for where the figure stands: append only).")]
    public CampaignEncounter[] encounters = Array.Empty<CampaignEncounter>();
    [TabGroup("Rules"), Tooltip("What opens what.")] public Unlock[] unlocks = Array.Empty<Unlock>();
    [TabGroup("Rules"), Range(1, 4), Tooltip("Acts open from the start.")] public int firstAct = 1;
    [TabGroup("Rules"), Tooltip("Winning a dungeon plays the old wake-up ending and the prototype's thank-you. Off: winning only grows the map.")]
    public bool playEndingOnWin;
    [TabGroup("Rules"), Tooltip("Said when a placeholder place is clicked.")]
    public string placeholderText = "Placeholder encounter";
    [TabGroup("Rules"), Tooltip("Said when the map grows.")]
    public DungeonMaster.Line grewLine = new() { text = "The map grows. New roads, new trouble.", inPerson = true };
    [TabGroup("Rules"), Tooltip("Beating this place wins the campaign (flag campaign.won).")] public CampaignEncounter finalPlace;
    [TabGroup("Rules"), Tooltip("Said back at the map once the final place is beaten.")]
    public DungeonMaster.Line wonLine = new() { text = "That was the last of it. The whole realm, start to end. Well played.", inPerson = true };

    [TabGroup("Events"), Tooltip("Text events, each shown once the first time the map is seen after its act opens.")]
    public CampaignEvent[] events = Array.Empty<CampaignEvent>();
    [TabGroup("Events"), Tooltip("The event screen (HUD canvas).")] public CampaignEventUI eventScreen;

    [TabGroup("Debug"), Tooltip("In the editor and development builds, right-clicking a place on the map offers Complete (it counts as beaten, with everything it opens, without playing it) and Reset campaign.")]
    public bool debugMenu = true;
    bool DebugMenuOn => debugMenu && (Application.isEditor || Debug.isDebugBuild);

    [TabGroup("Cloth"), Tooltip("Acts not yet open lie under a cloth, rolled back when they open. Off: they are simply not shown.")]
    public bool fog = true;
    [TabGroup("Cloth"), ShowIf(nameof(fog)), Tooltip("The cloth's fabric. Each style's material is on the map prefab (Cloth > Cloths); the drape and roll are tuned there too.")]
    public CampaignClothStyle clothStyle = CampaignClothStyle.Plaid;
    [TabGroup("Cloth"), Min(.1f), Tooltip("Seconds for an act's cloth to be rolled back and its land to rise.")] public float revealSeconds = 3.2f;
    [TabGroup("Cloth"), Tooltip("Rustles while the cloth is rolled back (one clip at a time, spaced out).")] public AudioData rollSound;
    [TabGroup("Cloth"), Min(.1f), ShowIf(nameof(rollSound)), Tooltip("Seconds between rustles.")] public float rollSoundEvery = .5f;
    [TabGroup("Cloth"), Tooltip("Debug: every act open (not saved).")] public bool openEverything;

    [TabGroup("View"), Min(0), Tooltip("Where the player stands: this far back from the map's near edge (world units).")]
    public float standBack = 18f;
    [TabGroup("View"), Tooltip("Camera height while looking over the map, in the room player's own units (1 = standing eye height).")]
    public float viewHeight = 2.1f;
    [TabGroup("View"), Tooltip("Field of view change while over the map (negative zooms in).")] public float viewZoom = 0f;
    [TabGroup("View"), Tooltip("Where the view aims, in map units from its centre (x toward the far side, z to the left).")]
    public Vector2 viewAim = new(-6f, 0f);
    [TabGroup("View"), Min(.1f)] public float stepSeconds = 1f;
    [TabGroup("View"), Tooltip("Frame the open land: the player stands in front of it and the view zooms until its places fill it, widening as acts open. Off: the middle of the near side, aimed at View Aim, zoomed by View Zoom.")]
    public bool frameOpenLand = true;
    [TabGroup("View"), ShowIf(nameof(frameOpenLand)), Min(1f), Tooltip("Room round the open places (1 = edge to edge).")] public float frameMargin = 1.3f;
    [TabGroup("View"), ShowIf(nameof(frameOpenLand)), Tooltip("The most the view zooms in (field of view change, negative).")] public float maxZoomIn = -30f;
    [TabGroup("View"), ShowIf(nameof(frameOpenLand)), Min(0f), Tooltip("The most the view widens when the open land doesn't fit (field of view change): with every act open the near places would be cut off.")] public float maxZoomOut = 14f;
    [TabGroup("View"), ShowIf(nameof(frameOpenLand)), Range(0f, .4f), Tooltip("Room kept below the land (a share of the view's height) so the hint line doesn't cover the nearest places.")] public float bottomRoom = .12f;
    [TabGroup("View"), ShowIf(nameof(frameOpenLand)), Tooltip("The Dungeon Master's head is kept in the view too (the view aims between him and the land).")] public bool keepDungeonMasterInView = true;
    [TabGroup("View"), ShowIf(nameof(frameOpenLand)), Range(0f, 1f), Tooltip("How far along the near side the player may walk toward the open land (share of the map's length from its middle).")] public float standSlide = .55f;
    [TabGroup("View"), Tooltip("How far a hovered place rises (map units).")] public float hoverLift = .9f;

    [TabGroup("Figure"), Tooltip("The figure's size on the map, as a share of the class figures' own scale.")]
    public float figureScale = 1f;
    [TabGroup("Figure"), Min(.05f)] public float hopSeconds = .16f;
    [TabGroup("Figure"), Min(.1f), Tooltip("Map units a hop covers.")] public float hopLength = 2.2f;
    [TabGroup("Figure"), Min(0)] public float hopHeight = .9f;

    [TabGroup("UI"), Tooltip("The hover card and the hint line (HUD canvas).")] public CampaignCardUI card;
    [TabGroup("UI"), Tooltip("Choosing a hero when the figure is clicked (HUD canvas). Empty: the class figures come out over the map.")] public CampaignHeroSelectUI heroSelect;
    [TabGroup("UI"), Tooltip("Said the first time the map is seen (flag campaign.seen).")] public DungeonMaster.Line firstLookLine = new() { text = "This is the realm. Start close to home.", inPerson = true };
    string Hint => DebugMenuOn ? hint + "  Right-click a place: debug." : hint;
    [TabGroup("UI")] public string hint = "Click a place to travel there.  Click your figure to change class.  Esc to step back.";

    public const string SeenFlag = "campaign.seen", WonFlag = "campaign.won", EventPrefix = "campaign.event.", GiftPrefix = "campaign.gift.";
    public const string ActFlag = "campaign.act", RevealedFlag = "campaign.revealed", AtFlag = "campaign.at", ClassFlag = "campaign.class", BeatenPrefix = "campaign.beaten.";

    public static bool Active => HasInstance && Instance.useCampaign && Instance.mapPrefab != null;
    public CampaignMap Map { get; private set; }
    public CampaignEncounter Current { get; private set; }

    bool lastRunWon;
    TableLevelLoader loader;
    AdventurerProgress progress;

    // ── Progress ──

    static ProgressionManager PM => ProgressionManager.HasInstance ? ProgressionManager.Instance : null;
    int SavedAct => Mathf.Clamp(Mathf.Max(firstAct, PM != null ? PM.GetFlag(ActFlag) : 0), 1, 4);
    public int OpenAct => openEverything ? 4 : SavedAct;
    // Revealed acts: the save's, or more this session while openEverything (debug) shows them without saving.
    int RevealedAct => Mathf.Max(firstAct, Mathf.Max(revealedNow, PM != null ? PM.GetFlag(RevealedFlag) : 0));
    int revealedNow;
    public bool Beaten(CampaignEncounter e) => e != null && PM != null && PM.HasFlag(BeatenPrefix + e.id);
    public bool IsOpen(CampaignEncounter e) => e != null && e.act <= OpenAct;

    CampaignEncounter At
    {
        get { var all = AllEncounters; int i = PM != null ? PM.GetFlag(AtFlag) - 1 : -1; return i >= 0 && i < all.Length && all[i] != null ? all[i] : home; }
        set { if (PM != null) PM.SetFlag(AtFlag, Array.IndexOf(AllEncounters, value) + 1); }
    }

    [NonSerialized] CampaignEncounter[] allEncounters;   // a run-time cache (kept out of the editor's reload backup)
    // Every place: the listed encounters first (their order keeps saved positions), then any on the map the list lacks
    // (a place a designer added and didn't list), so each one can be stood on, saved and continued.
    public CampaignEncounter[] AllEncounters
    {
        get
        {
            if (allEncounters != null) return allEncounters;
            var list = new List<CampaignEncounter>(encounters ?? Array.Empty<CampaignEncounter>());
            if (Map == null) return list.ToArray();
            foreach (var p in Map.Points)
                if (p.encounter != null && !list.Contains(p.encounter))
                {
                    list.Add(p.encounter);
                    Debug.LogWarning($"[Campaign] {p.encounter.name} is on the map but not in CampaignManager > Map > Encounters (used anyway; list it so its saved place stays put).", p);
                }
            return allEncounters = list.ToArray();
        }
    }

    // Two places with the same encounter (a duplicated place still holding the original's) can't be told apart.
    void CheckPlaces()
    {
        var seen = new Dictionary<string, CampaignPoint>();
        foreach (var p in Map.Points)
        {
            if (p.encounter == null) continue;
            string id = string.IsNullOrEmpty(p.encounter.id) ? p.encounter.name : p.encounter.id;
            if (seen.TryGetValue(id, out var other)) Debug.LogError($"[Campaign] places {other.name} and {p.name} share the encounter id '{id}': give the duplicate its own encounter.", p);
            else seen[id] = p;
        }
    }

    void Start()
    {
        if (!Active) return;
        // The save's flags first (whichever Start runs first reads them in; it only happens once), so the class, the
        // revealed map and the beaten places are known before anything below reads them.
        var saver = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table)?.GetComponent<AdventureSave>() : null;
        saver?.Initialize();
        loader = TableManager.Loader;
        if (loader != null) { loader.LoadStarted += OnLoadStarted; loader.Returned += OnReturned; }
        if (RunManager.HasInstance) { RunManager.Instance.RunEnded += OnRunEnded; RunManager.Instance.RunStarted += GiveGifts; }
        var table = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        progress = table != null ? table.GetComponent<AdventurerProgress>() : null;
        if (progress != null) progress.Changed += RememberClass;
        RestoreClass();
        SpawnMap();
        // The map lies on the table once it has been revealed (after the first morning's dice); before that the table is bare.
        Map.gameObject.SetActive(Unveiled && !IntroController.SheetPending && !(RunManager.HasInstance && RunManager.Instance.Running));
        if (Map.gameObject.activeSelf) { ShowActs(); RefreshMarker(); }
    }

    System.Action cancelHandler;

    // The map view stopped from outside (its table switched off mid-view: a coroutine's finally doesn't run then):
    // nothing it set is left behind.
    void OnDisable() => AbortMapView();

    // Ends a map view whose coroutine was stopped from outside (the table switched off, or an error in it): the cancel
    // handler, the lifted place, the hint and the question go.
    public void AbortMapView()
    {
        changingClass = false;
        if (!InMapView) return;
        InMapView = false;
        if (cancelHandler != null && InputManager.HasInstance) InputManager.Instance.CancelPressed -= cancelHandler;
        cancelHandler = null;
        Unhover();
        if (card != null) { card.Hide(); card.ShowHint(false); }
        CloseAsk();
    }

    void OnDestroy()
    {
        if (loader != null) { loader.LoadStarted -= OnLoadStarted; loader.Returned -= OnReturned; }
        if (RunManager.HasInstance) { RunManager.Instance.RunEnded -= OnRunEnded; RunManager.Instance.RunStarted -= GiveGifts; }
        if (progress != null) progress.Changed -= RememberClass;
    }

    // The class last picked is the figure on the map next time too.
    void RememberClass()
    {
        if (progress == null || progress.InRun || progress.selectedClass == null || progress.rules == null || PM == null) return;
        PM.SetFlag(ClassFlag, ClassKey(progress.selectedClass));
    }

    // A class's key in the save: a hash of its id, kept above 65536 so older saves' indexes (1, 2, 3...) still read.
    static int ClassKey(AdventurerClass c)
    {
        unchecked { uint h = 2166136261; foreach (char ch in string.IsNullOrEmpty(c.id) ? c.name : c.id) h = (h ^ ch) * 16777619; return (int)(h & 0x3fffffff) | 0x10000; }
    }

    void RestoreClass()
    {
        if (progress == null || progress.InRun || progress.rules == null || PM == null) return;
        int v = PM.GetFlag(ClassFlag);
        var classes = progress.rules.classes;
        var c = v >= 0x10000 ? Array.Find(classes, x => x != null && ClassKey(x) == v) : v >= 1 && v <= classes.Length ? classes[v - 1] : null;
        if (c != null && c.Unlocked) progress.SelectClass(c);
    }

    void SpawnMap()
    {
        Vector3 at;
        if (mapSpot != null) at = mapSpot.position;
        else
        {
            var tm = TableManager.Instance;
            var col = tm != null ? tm.GetComponent<Collider>() : null;
            at = col != null ? new Vector3(col.bounds.center.x, col.bounds.max.y, col.bounds.center.z) : tm != null ? tm.transform.position : Vector3.zero;
        }
        Map = Instantiate(mapPrefab, at, Quaternion.identity, TableManager.HasInstance ? TableManager.Instance.transform : null);
        Map.name = "Campaign map";
        foreach (var p in Map.Points) p.EnsurePickCollider();
        CheckPlaces();
        restY = Map.transform.localPosition.y;
    }

    float restY;

    void ShowActs() => Map.ShowActs(Mathf.Min(OpenAct, RevealedAct), fog, clothStyle);

    // Test: shows an act under the given cloth, then uncovers it as a won run would.
    public void TestReveal(CampaignClothStyle style, int act)
    {
        if (Map == null) return;
        clothStyle = style;
        Map.ShowActs(act - 1, true, style);
        var from = Map.PointFor(At);
        StartCoroutine(Map.Reveal(act, revealSeconds, from != null ? from.transform.localPosition : Vector3.zero));
    }

    // ── The map on and off the table ──

    void OnLoadStarted() { if (Map != null && Map.gameObject.activeSelf) StartCoroutine(Sink()); }

    void OnReturned()
    {
        Current = null;
        if (Map == null) return;
        RefreshMarker();
        // Not back from the intro's practice board, nor before the first morning's dice: the map isn't open yet.
        if (MapBelongsOnTableAfterReturn) StartCoroutine(Rise());
        // After a win the player goes straight back to the map, to see what opened.
        if (lastRunWon) StartCoroutine(BackToMap());
        lastRunWon = false;
    }

    // After a win: what opened is shown at once (the fog lifts over the table where the player stands), then the player
    // is taken to the map, unless something holds the table (the sheet lesson).
    IEnumerator BackToMap()
    {
        yield return new WaitForSecondsRealtime(.3f);
        for (float until = Time.unscaledTime + 8f; Time.unscaledTime < until; )
        {
            bool loading = loader != null && loader.Busy;
            bool exploring = GameManager.HasInstance && GameManager.Instance.State == GameState.Explore;
            if (!rising && !loading && exploring) break;
            yield return null;
        }
        yield return RevealPending();
        SayWonLine();
        bool held = ProgressionManager.HasInstance && ProgressionManager.Instance.LessonHoldsRuns;
        // Only while the player is free and still at the table (not in the inventory, the sheet, a menu, or walked off):
        // otherwise the map just stays revealed for the next time they use the table.
        bool free = GameManager.HasInstance && GameManager.Instance.State == GameState.Explore;
        var room = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Room) : null;
        bool near = room != null && TableManager.HasInstance && Vector3.Distance(room.transform.position, TableManager.Instance.transform.position) < nearTable;
        if (!held && free && near && !InMapView && TableManager.HasInstance) TableManager.Instance.Play();
    }

    bool revealing;
    [TabGroup("View"), Min(0), Tooltip("After a win the map view opens by itself only while the player stands within this distance of the table (world units).")]
    public float nearTable = 140f;

    // Lifts the fog of every act opened since the map was last seen.
    IEnumerator RevealPending()
    {
        if (revealing || Map == null || RevealedAct >= OpenAct) yield break;
        revealing = true;
        try
        {
            for (int act = RevealedAct + 1; act <= OpenAct; act++)
            {
                var from = Map.PointFor(At);
                if (fog) StartCoroutine(Rustle(revealSeconds * .85f));
                yield return Map.Reveal(act, revealSeconds, from != null ? from.transform.localPosition : Vector3.zero);
            }
            revealedNow = OpenAct;
            if (PM != null && !openEverything) PM.SetFlag(RevealedFlag, OpenAct);
        }
        finally { revealing = false; }
        if (grewLine != null && !grewLine.IsEmpty) DungeonMaster.Say(grewLine);
    }

    IEnumerator Rustle(float seconds)
    {
        if (rollSound == null || !AudioManager.HasInstance) yield break;
        for (float t = 0; t < seconds; t += rollSoundEvery)
        {
            AudioManager.Instance.PlaySFXData2D(rollSound);
            yield return new WaitForSeconds(rollSoundEvery);
        }
    }

    public void HideNow() { if (Map != null) Map.gameObject.SetActive(false); }

    // The map grows up out of the tabletop like a pop-up book (its height from flat to full, a little past and back), so
    // nothing passes through the table.
    public IEnumerator Rise()
    {
        if (Map == null) yield break;
        bool wasShown = Map.gameObject.activeSelf;
        Map.gameObject.SetActive(true);
        ShowActs();
        if (wasShown) yield break;
        rising = true;
        var t = Map.transform;
        t.localPosition = new Vector3(t.localPosition.x, restY, t.localPosition.z);
        MapLights(false);
        for (float s = 0; s < riseSeconds; s += Time.deltaTime)
        {
            float k = s / riseSeconds;
            // Ease out with a small overshoot.
            float c = 1.6f, e = 1f + (c + 1f) * Mathf.Pow(k - 1f, 3) + c * Mathf.Pow(k - 1f, 2);
            t.localScale = new Vector3(1f, Mathf.Max(.01f, e), 1f);
            yield return null;
        }
        t.localScale = Vector3.one;
        MapLights(true);
        rising = false;
    }

    // The first reveal (after the first morning's dice): a cloth falls onto the bare table, the land rises under it
    // into hills and folds, then the cloth over act 1 is rolled back from the player's side and its places pop up as the
    // roll passes them (CampaignMap.DropReveal, simulated in Blender: Tools/campaign_cloth_drop.py). The other acts
    // stay under the cloth.
    IEnumerator BigReveal()
    {
        if (Map == null) yield break;
        rising = true;
        Map.gameObject.SetActive(true);
        var t = Map.transform;
        t.localPosition = new Vector3(t.localPosition.x, restY, t.localPosition.z);
        MapLights(false);
        if (fog) StartCoroutine(Rustle(1.2f));
        var from = new Vector3(-Map.halfX, 0f, Map.PointFor(home) != null ? Map.PointFor(home).transform.localPosition.z : 0f);
        try { yield return Map.DropReveal(clothStyle, fog, from, revealSeconds, () => { MapLights(true); if (fog) StartCoroutine(Rustle(revealSeconds * .85f)); }); }
        finally { rising = false; MapLights(true); }
        revealedNow = Mathf.Max(revealedNow, 1);
        if (PM != null) PM.SetFlag(RevealedFlag, Mathf.Max(1, PM.GetFlag(RevealedFlag)));
    }

    // A reveal or fold that was cut short (an error mid-way) leaves the map squashed, dark or marked as rising: put it
    // right before the map view starts, so the table is never locked by it.
    void RepairMap()
    {
        if (Map == null || rising || revealing) return;
        Map.transform.localScale = Vector3.one;
        var stray = Map.transform.Find("Cloth drop");
        if (stray != null) Destroy(stray.gameObject);
        if (mapLights.Count > 0) MapLights(true);
    }

    // The places' lights don't squash with the map, so they are off while it grows or folds (lying on the flattened
    // land they would blow it out).
    readonly List<Light> mapLights = new();
    void MapLights(bool on)
    {
        // Lights already switched off by an earlier call stay on the list (a fold and a rise can overlap).
        if (!on) { foreach (var l in Map.GetComponentsInChildren<Light>()) if (l.enabled) { mapLights.Add(l); l.enabled = false; } }
        else { foreach (var l in mapLights) if (l != null) l.enabled = true; mapLights.Clear(); }
    }

    IEnumerator Sink()
    {
        var t = Map.transform;
        MapLights(false);
        float seconds = riseSeconds * .6f;
        for (float s = 0; s < seconds; s += Time.deltaTime)
        {
            t.localScale = new Vector3(1f, Mathf.Max(.01f, 1f - Mathf.SmoothStep(0, 1, s / seconds)), 1f);
            yield return null;
        }
        Map.gameObject.SetActive(false);
        t.localScale = Vector3.one;
        MapLights(true);
    }

    // ── Runs: which place is being played, and what a win opens ──

    void OnRunEnded(bool victory)
    {
        if (!victory || Current == null || IntroController.PracticeRunning) return;
        if (RunManager.HasInstance && RunManager.Instance.Level != Current.level) return;   // a run not started from the map
        lastRunWon = true;
        MarkBeaten(Current);
    }

    bool wonLinePending;

    // A place counts as beaten: the acts it unlocks open (their cloth is rolled back the next time the map is seen), and
    // the final place wins the campaign.
    void MarkBeaten(CampaignEncounter e)
    {
        if (PM == null || e == null) return;
        PM.SetFlag(BeatenPrefix + e.id, 1);
        int act = SavedAct;
        foreach (var u in unlocks) if (u != null && u.beaten == e) act = Mathf.Max(act, u.opensAct);
        if (act > SavedAct) PM.SetFlag(ActFlag, act);
        if (e == finalPlace && !PM.HasFlag(WonFlag)) { PM.SetFlag(WonFlag, 1); wonLinePending = true; }
    }

    void SayWonLine()
    {
        if (!wonLinePending) return;
        wonLinePending = false;
        if (wonLine != null && !wonLine.IsEmpty) DungeonMaster.Say(wonLine);
    }

    // ── Text events ──

    // Every event whose act is now shown and that hasn't been seen yet, one after another.
    IEnumerator RunEvents()
    {
        CampaignEvent current = null;
        if (eventScreen == null || PM == null) yield break;
        int shown = Mathf.Min(OpenAct, RevealedAct);
        foreach (var e in events)
        {
            if (e == null || e.afterAct > shown || PM.HasFlag(EventPrefix + e.id)) continue;
            current = e;
            if (card != null) { card.Hide(); card.ShowHint(false); }
            Unhover();
            yield return eventScreen.Play(e, () => PM.Spoils, Choose);
            // An event with no choices is marked seen once it has been played through.
            PM.SetFlag(EventPrefix + e.id, 1);
            if (card != null) card.ShowHint(true, Hint);
        }

        void Choose(CampaignEvent.Choice c)
        {
            // Seen from the moment a choice is made (before its rewards): quitting after it can't pay out twice.
            PM.SetFlag(EventPrefix + current.id, 1);
            if (c.spoilsCost > 0) PM.AddSpoils(-c.spoilsCost);
            if (c.spoilsGain > 0) PM.AddSpoils(c.spoilsGain);
            foreach (var item in c.nextRunItems)
                if (item != null && !string.IsNullOrEmpty(item.saveId)) PM.SetFlag(GiftPrefix + item.saveId, PM.GetFlag(GiftPrefix + item.saveId) + 1);
            if (c.line != null && !c.line.IsEmpty) DungeonMaster.Say(c.line);
        }
    }


    // Items an event promised for the next run go into the table player's bag as it starts (not a resumed run's: its
    // bag is restored from the save).
    void GiveGifts()
    {
        if (loader != null && loader.Resumed) return;
        if (PM == null || !InventoryManager.HasInstance || IntroController.PracticeRunning) return;
        var given = new List<string>();
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        if (player == null) return;
        foreach (var item in InventoryManager.Instance.itemCatalog)
        {
            if (item == null || string.IsNullOrEmpty(item.saveId)) continue;
            int count = PM.GetFlag(GiftPrefix + item.saveId);
            if (count <= 0) continue;
            PM.SetFlag(GiftPrefix + item.saveId, 0);
            for (int i = 0; i < count; i++) InventoryManager.Instance.Pickup(item, player, preferHotbar: true, playSound: false, announcePickup: false);
            given.Add(count > 1 ? $"{item.itemName} x{count}" : item.itemName);
        }
        // Posted a frame later: the run's log is cleared right after it starts.
        if (given.Count > 0) StartCoroutine(PostLater("In your pack, as promised: " + string.Join(", ", given) + "."));
    }

    static IEnumerator PostLater(string line)
    {
        yield return null;
        MessageLog.Post(line, MessageKind.Info);
    }

    // Debug: every campaign flag cleared, the cloth back over every closed act and the figure home.
    void ResetCampaign()
    {
        if (PM == null) return;
        foreach (var e in AllEncounters) if (e != null) PM.SetFlag(BeatenPrefix + e.id, 0);
        foreach (var f in new[] { ActFlag, RevealedFlag, AtFlag, WonFlag }) PM.SetFlag(f, 0);
        foreach (var ev in events) if (ev != null) PM.SetFlag(EventPrefix + ev.id, 0);
        if (InventoryManager.HasInstance)
            foreach (var item in InventoryManager.Instance.itemCatalog)
                if (item != null && !string.IsNullOrEmpty(item.saveId) && PM.GetFlag(GiftPrefix + item.saveId) != 0) PM.SetFlag(GiftPrefix + item.saveId, 0);
        RefreshMarker();
        wonLinePending = false;
        ShowActs();
        PlaceFigure(At, true);
    }

    // A run saved on quit: its place, class and where it stopped.
    bool SavedRun(out CampaignEncounter place, out AdventurerClass cls, out string detail)
    {
        place = null; cls = null; detail = null;
        var table = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        var save = table != null ? table.GetComponent<AdventureSave>() : null;
        if (save == null || !save.HasCheckpoint || !save.CanRestore(save.Saved.checkpoint)) return false;
        var c = save.Saved.checkpoint;
        foreach (var e in AllEncounters) if (e != null && e.level != null && e.level.name == c.level) { place = e; break; }
        var rules = table.GetComponent<AdventurerProgress>()?.rules;
        cls = rules != null ? Array.Find(rules.classes, x => x != null && x.id == c.classId) : null;
        detail = $"{(cls != null ? cls.displayName : "?")}, level {c.characterLevel}, floor {c.floor}";
        return place != null;
    }

    // ── At the table ──

    GameObject figure;
    Collider figureCollider;
    CampaignPoint hovered;
    bool overFigure;
    readonly RaycastHit[] hits = new RaycastHit[32];

    // The map view, from using the table to a run starting (started true) or the player stepping back (false).
    public IEnumerator Choose(Player player, Action<bool> done)
    {
        if (!Active || Map == null || player == null) { done?.Invoke(false); yield break; }
        RepairMap();
        // The very first time (right after the first morning's dice) the map is revealed in full view: the player steps
        // up to the bare table and the realm unfolds out of it.
        // A reveal still running (a win's, as the player came straight back to the table) finishes first.
        while (revealing) yield return null;
        bool firstReveal = !Map.gameObject.activeSelf && PM != null && !PM.HasFlag(SeenFlag);
        if (firstReveal) yield return StepToMap(player);
        if (!Map.gameObject.activeSelf) yield return StartCoroutine(firstReveal ? BigReveal() : Rise());
        else ShowActs();
        PlaceFigure(At, firstReveal);
        if (!firstReveal) yield return StepToMap(player);
        // The first time the realm is seen, the Dungeon Master says where to start.
        if (PM != null && !PM.HasFlag(SeenFlag))
        {
            PM.SetFlag(SeenFlag, 1);
            if (firstLookLine != null && !firstLookLine.IsEmpty) DungeonMaster.Say(firstLookLine);
        }

        // Acts opened since the map was last seen: their cloth is rolled back now, and the view widens to take them in.
        while (revealing) yield return null;
        int shownBefore = Mathf.Min(OpenAct, RevealedAct);
        // On this manager, not the table's sitting coroutine: a table switched off mid-reveal can't leave it half done.
        yield return StartCoroutine(RevealPending());
        if (frameOpenLand && Mathf.Min(OpenAct, RevealedAct) != shownBefore) yield return Frame(player, .9f);
        yield return RunEvents();

        SavedRun(out var savedPlace, out var savedClass, out var savedDetail);
        UpdateMarker(savedPlace);
        SayWonLine();
        if (card != null) card.ShowHint(true, Hint);
        InMapView = true;
        bool cancelled = false, started = false;
        CampaignEncounter completing = null; bool resetting = false;
        bool MenuOpen() => ContextMenuUI.HasInstance && ContextMenuUI.Instance.IsOpen;
        cancelHandler = OnCancel;
        void OnCancel()
        {
            if (eventScreen != null && eventScreen.Open) return;
            if (MenuOpen()) { ContextMenuUI.Instance.Hide(); return; }
            if (!asking && !changingClass && !(heroSelect != null && heroSelect.Open)) cancelled = true;
        }
        if (InputManager.HasInstance) InputManager.Instance.CancelPressed += OnCancel;
        try
        {
            while (!cancelled && !started)
            {
                Hover(savedPlace, savedDetail);
                if (testClick != null) { hovered = testClick == "figure" ? null : TestPoint(testClick); overFigure = testClick == "figure"; }
                bool clicked = testClick != null || InputManager.HasInstance && InputManager.Instance.PointerClicked && !OverUI();
                testClick = null;
                if (testCancel) { testCancel = false; cancelled = true; }
                if (testComplete != null) { completing = TestPoint(testComplete)?.encounter; testComplete = null; }
                // Debug: right-click a place to complete it without playing it.
                if (DebugMenuOn && !asking && !MenuOpen() && hovered != null && hovered.encounter != null && IsOpen(hovered.encounter)
                    && InputManager.HasInstance && InputManager.Instance.PointerRightClicked && ContextMenuUI.HasInstance)
                {
                    var e = hovered.encounter;
                    var options = new List<(string, Action)>();
                    if (e.kind != CampaignEncounterKind.Home) options.Add((Beaten(e) ? "Complete again (debug)" : "Complete (debug)", () => completing = e));
                    options.Add(("Reset campaign (debug)", () => resetting = true));
                    ContextMenuUI.Instance.Show(options, InputManager.Instance.PointerPosition);
                }
                if (resetting)
                {
                    resetting = false;
                    ResetCampaign();
                    SavedRun(out savedPlace, out savedClass, out savedDetail);
                    yield return Frame(player, .6f);
                }
                if (completing != null)
                {
                    var e = completing; completing = null;
                    if (card != null) card.Hide();
                    Unhover();
                    yield return Walk(e);
                    MarkBeaten(e);
                    int shown = Mathf.Min(OpenAct, RevealedAct);
                    yield return StartCoroutine(RevealPending());
                    if (frameOpenLand && Mathf.Min(OpenAct, RevealedAct) != shown) yield return Frame(player, .9f);
                    SayWonLine();
                    yield return RunEvents();
                    continue;
                }
                if (clicked && !asking && !MenuOpen())
                {
                    if (overFigure)
                    {
                        if (card != null) { card.Hide(); card.ShowHint(false); }
                        yield return ChangeClass(player);
                        if (card != null) card.ShowHint(true, Hint);
                        SavedRun(out savedPlace, out savedClass, out savedDetail);
                    }
                    else if (hovered != null && hovered.encounter != null)
                    {
                        var e = hovered.encounter;
                        if (!IsOpen(e)) { }
                        else if (e.kind == CampaignEncounterKind.Placeholder) NotificationUI.Show(placeholderText);
                        else if (e.kind == CampaignEncounterKind.Home) { if (At != e) yield return Walk(e); }
                        else if (e.level != null)
                        {
                            // The saved run's place continues it, as whatever class the run was played with (when it
                            // still can be: checked before anything moves).
                            bool resume = savedPlace == e && savedClass != null;
                            if (resume && !loader.CanResumeAdventure()) { NotificationUI.Show("That saved run can't be continued"); continue; }
                            if (resume && progress != null && progress.selectedClass != savedClass) { progress.SelectClass(savedClass); RememberClass(); PlaceFigure(At); }
                            // A new run over a saved one asks first: the saved run would be gone.
                            if (!resume && savedPlace != null)
                            {
                                bool yes = false;
                                yield return Ask($"Start a new run in {e.displayName}? Your saved run ({savedDetail}) in {savedPlace.displayName} will be lost.", r => yes = r);
                                if (!yes) continue;
                            }
                            if (e.pickedLine != null && !e.pickedLine.IsEmpty) DungeonMaster.Say(e.pickedLine);
                            if (card != null) { card.Hide(); card.ShowHint(false); }
                            Unhover();
                            yield return Walk(e);
                            Current = e;
                            yield return CameraEase.Zoom(player.CameraRig, 0f, .35f, smooth: false);
                            player.Look.HeightOverride = null;
                            if (resume) loader.ResumeAdventure(); else loader.Load(e.level);
                            started = true;
                        }
                    }
                }
                yield return null;
            }
        }
        finally
        {
            InMapView = false;
            if (InputManager.HasInstance) InputManager.Instance.CancelPressed -= OnCancel;
            Unhover();
            if (card != null) { card.Hide(); card.ShowHint(false); }
            if (MenuOpen()) ContextMenuUI.Instance.Hide();
            CloseAsk();
        }
        done?.Invoke(started);
    }

    // The resumed run's place, for the win: set when a saved run is continued from elsewhere (the old menu).
    public void NoteCurrent(TableLevelData level)
    {
        foreach (var e in AllEncounters) if (e != null && e.level == level) { Current = e; return; }
    }

    static bool OverUI() => UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

    void Hover(CampaignEncounter savedPlace, string savedDetail)
    {
        CampaignPoint under = null; overFigure = false;
        var cam = PlayerManager.HasInstance ? PlayerManager.Instance.OutputCamera : null;
        if (cam != null && InputManager.HasInstance && !OverUI() && !asking)
        {
            var ray = cam.ScreenPointToRay(InputManager.Instance.PointerPosition);
            int n = Physics.RaycastNonAlloc(ray, hits, 600f, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (hits[i].collider == figureCollider && hits[i].distance < best) { best = hits[i].distance; under = null; overFigure = true; continue; }
                var p = hits[i].collider.GetComponent<CampaignPoint>();
                if (p != null && p.isActiveAndEnabled && p.encounter != null && IsOpen(p.encounter) && hits[i].distance < best) { best = hits[i].distance; under = p; overFigure = false; }
            }
        }
        if (under != hovered)
        {
            if (hovered != null) hovered.Lift = 0f;
            hovered = under;
            if (hovered != null) { hovered.Lift = hoverLift; if (UIFeedbackSettings.Shared != null) UIFeedbackSettings.Shared.Hover(); }
        }
        if (card == null) return;
        if (overFigure && progress != null && progress.selectedClass != null)
            card.Show(progress.selectedClass.displayName, "Your character", "Click to change class.", null, figure.transform.position + Vector3.up * 3f, cam);
        else if (hovered != null)
        {
            var e = hovered.encounter;
            string kindLine = e.KindName + (e.kind == CampaignEncounterKind.Level && e.level != null ? $"  ·  {e.level.FloorCount} floor{(e.level.FloorCount == 1 ? "" : "s")}" : "") + $"  ·  Act {e.act}";
            string statusLine = savedPlace == e ? $"<color=#E8C46A>Your run waits here: {savedDetail}. Click to continue.</color>"
                : Beaten(e) ? "<color=#8FD18B>Beaten</color>"
                : e.kind == CampaignEncounterKind.Placeholder ? "<color=#A9A49A>Not made yet</color>" : null;
            card.Show(e.displayName, kindLine, e.description, statusLine, hovered.transform.position + Vector3.up * 2f, cam);
        }
        else card.Hide();
    }

    void Unhover() { if (hovered != null) hovered.Lift = 0f; hovered = null; overFigure = false; }

    // ── The figure ──

    // pop: the figure springs up in place (a new class chosen).
    void PlaceFigure(CampaignEncounter at, bool pop = false)
    {
        if (figure != null) Destroy(figure);
        var cls = progress != null ? progress.selectedClass : null;
        var point = Map.PointFor(at) ?? Map.PointFor(home);
        if (cls == null || cls.miniature == null || point == null) return;
        var figures = ClassFiguresOnTable;
        figure = new GameObject("Campaign figure");
        figure.transform.SetParent(Map.transform, false);
        figure.transform.localPosition = FigureSpot(point);
        var model = Instantiate(cls.miniature, figure.transform);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.Euler(0, -90, 0);
        model.transform.localScale = Vector3.one * (figures != null ? figures.figureScale : 3f) * figureScale;
        foreach (var a in model.GetComponentsInChildren<Animator>()) a.enabled = false;
        if (figures != null && figures.figureMaterial != null)
            foreach (var r in model.GetComponentsInChildren<Renderer>()) { var m = r.sharedMaterials; for (int i = 0; i < m.Length; i++) m[i] = figures.figureMaterial; r.sharedMaterials = m; }
        var bounds = new Bounds(figure.transform.position, Vector3.zero);
        foreach (var r in model.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(disc.GetComponent<Collider>());
        disc.transform.SetParent(figure.transform, false);
        disc.transform.localScale = new Vector3(2.4f, .08f, 2.4f);
        disc.transform.localPosition = Vector3.up * .08f;
        if (CampaignBaseMaterials.Top != null) disc.GetComponent<Renderer>().sharedMaterial = CampaignBaseMaterials.Top;
        var box = figure.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = figure.transform.InverseTransformPoint(bounds.center);
        box.size = Vector3.Scale(figure.transform.InverseTransformVector(bounds.size), new Vector3(1.6f, 1.2f, 1.6f));
        box.size = new Vector3(Mathf.Abs(box.size.x), Mathf.Abs(box.size.y), Mathf.Abs(box.size.z));
        figureCollider = box;
        if (pop) StartCoroutine(Pop(figure.transform));
    }

    // The figure stands at the edge of a place's base, on the side toward home, so it never hides the place.
    Vector3 FigureSpot(CampaignPoint point)
    {
        var p = point.transform.localPosition;
        var toward = -p; toward.y = 0; toward = toward.sqrMagnitude > .01f ? toward.normalized : Vector3.right;
        var spot = p + toward * (point.radius + 1.2f);
        return Map.OnGround(spot, .05f);
    }

    static ClassFigures ClassFiguresOnTable => TableManager.HasInstance ? TableManager.Instance.GetComponentInChildren<ClassFigures>(true) : null;

    // The figure hops along the roads to a place.
    IEnumerator Walk(CampaignEncounter to)
    {
        var fromPoint = Map.PointFor(At); var toPoint = Map.PointFor(to);
        if (figure == null || toPoint == null) { At = to; yield break; }
        if (fromPoint == toPoint) { At = to; yield break; }   // already there
        var route = Map.Route(fromPoint, toPoint, OpenAct);
        if (route.Count == 0) route.Add(toPoint.transform.localPosition);
        route[route.Count - 1] = FigureSpot(toPoint);
        var stops = new List<Vector3>();
        var last = figure.transform.localPosition;
        foreach (var p in route)
        {
            float d = Vector3.Distance(new Vector3(last.x, 0, last.z), new Vector3(p.x, 0, p.z));
            if (d < hopLength && p != route[route.Count - 1]) continue;
            int steps = Mathf.Max(1, Mathf.RoundToInt(d / hopLength));
            for (int i = 1; i <= steps; i++) stops.Add(Map.OnGround(Vector3.Lerp(last, p, i / (float)steps), .05f));
            last = p;
        }
        foreach (var stop in stops)
        {
            var start = figure.transform.localPosition;
            var dir = stop - start; dir.y = 0;
            if (dir.sqrMagnitude > .01f) figure.transform.localRotation = Quaternion.Slerp(figure.transform.localRotation, Quaternion.LookRotation(dir) * Quaternion.Euler(0, 90, 0), .6f);
            for (float t = 0; t < hopSeconds; t += Time.deltaTime)
            {
                float k = t / hopSeconds;
                figure.transform.localPosition = Vector3.Lerp(start, stop, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * hopHeight;
                yield return null;
            }
            figure.transform.localPosition = stop;
            if (UIFeedbackSettings.Shared != null && stops.IndexOf(stop) % 2 == 0) UIFeedbackSettings.Shared.Hover();
        }
        At = to;
    }

    // ── Changing class: the class figures come out over the map ──

    IEnumerator ChangeClass(Player player)
    {
        // The hero select: cards along the bottom, the figure on the map changing as they are chosen.
        if (heroSelect != null && progress != null && progress.rules != null)
        {
            var classes = Array.FindAll(progress.rules.classes, c => c != null && c.Unlocked);
            void Follow(AdventurerClass c) => PlaceFigure(At, true);
            heroSelect.Shown += Follow;
            AdventurerClass chosen = null;
            try { yield return heroSelect.Choose(progress, classes, c => chosen = c); }
            finally { heroSelect.Shown -= Follow; }
            if (chosen != null) RememberClass();
            PlaceFigure(At, true);
            yield break;
        }
        var figures = ClassFiguresOnTable;
        if (figures == null) yield break;
        // The figures stand up on the map, in front of the player.
        var holder = figures.transform;
        var oldPos = holder.position;
        var near = Map.transform.TransformPoint(new Vector3(-Map.halfX * .55f, 0, 0));
        var centre = figures.Centre;
        holder.position += new Vector3(near.x - centre.x, Map.transform.position.y + 9f - centre.y, near.z - centre.z);
        AdventurerClass picked = null;
        figures.SilentTake = true;
        changingClass = true;
        try { yield return figures.Choose(c => picked = c); }
        finally { figures.SilentTake = false; changingClass = false; }
        holder.position = oldPos;
        if (picked != null) { progress?.SelectClass(picked); RememberClass(); }
        PlaceFigure(At);
    }

    static IEnumerator Pop(Transform t)
    {
        for (float k = 0; k < 1f && t != null; k += Time.unscaledDeltaTime / .28f)
        {
            float s = k < .7f ? Mathf.Lerp(.3f, 1.12f, k / .7f) : Mathf.Lerp(1.12f, 1f, (k - .7f) / .3f);
            t.localScale = Vector3.one * s;
            yield return null;
        }
        if (t != null) t.localScale = Vector3.one;
    }

    // ── Asking before a saved run is lost (the class figures' question panel) ──

    bool asking; bool? answer;
    bool changingClass;

    IEnumerator Ask(string question, Action<bool> result)
    {
        var figures = ClassFiguresOnTable;
        if (figures == null || figures.confirmPanel == null) { result(true); yield break; }
        asking = true; answer = null;
        if (figures.confirmText != null) figures.confirmText.text = question;
        figures.confirmPanel.SetActive(true);
        void Yes() => answer = true; void No() => answer = false;
        figures.confirmYes.onClick.AddListener(Yes); figures.confirmNo.onClick.AddListener(No);
        void Esc() => answer = false;
        if (InputManager.HasInstance) InputManager.Instance.CancelPressed += Esc;
        figures.confirmNo?.Select();
        while (answer == null) yield return null;
        if (InputManager.HasInstance) InputManager.Instance.CancelPressed -= Esc;
        figures.confirmYes.onClick.RemoveListener(Yes); figures.confirmNo.onClick.RemoveListener(No);
        CloseAsk();
        yield return null;   // the click that answered is not also a click on the map
        result(answer == true);
    }

    void CloseAsk()
    {
        asking = false;
        var figures = ClassFiguresOnTable;
        if (figures != null && figures.confirmPanel != null && figures.confirmPanel.activeSelf) figures.confirmPanel.SetActive(false);
    }

    // ── Saved-run marker over its place ──

    GameObject marker;

    // The saved-run flag over its place, kept true whenever the map is on the table (a run that ended or was saved).
    void RefreshMarker()
    {
        if (Map == null) return;
        SavedRun(out var place, out _, out _);
        UpdateMarker(place);
    }

    void UpdateMarker(CampaignEncounter place)
    {
        if (marker != null) Destroy(marker);
        var point = place != null ? Map.PointFor(place) : null;
        if (point == null) return;
        var flag = Resources.Load<GameObject>("Campaign run marker");
        if (flag == null) return;
        marker = Instantiate(flag, point.transform);
        marker.transform.localPosition = new Vector3(0, point.radius * 1.4f, 0);
    }

    float nextTableCheck;
    bool rising;

    // Once the campaign is open (after the first morning's sheet), the map always lies on the table when nothing else is
    // on it: no run being played, no level being laid out, the intro's practice board gone.
    // The map has had its first reveal (after the first morning's dice; on an older save, the first use of the table).
    // Until then the table is bare: the intro's night, the wake-up, the sheet and dice.
    bool Unveiled => PM != null && PM.HasFlag(SeenFlag);
    bool MapBelongsOnTableAfterReturn => Unveiled && !IntroController.SheetPending && !IntroController.PracticeRunning;

    bool MapBelongsOnTable => Unveiled && !IntroController.SheetPending
        && !IntroController.PracticeRunning && !(RunManager.HasInstance && RunManager.Instance.Running)
        && !(loader != null && loader.Busy);

    void Update()
    {
        if (Active && Map != null && !rising && Time.unscaledTime >= nextTableCheck)
        {
            nextTableCheck = Time.unscaledTime + .5f;
            if (!Map.gameObject.activeSelf && MapBelongsOnTable) StartCoroutine(Rise());
        }
        if (marker != null)
        {
            marker.transform.localPosition = new Vector3(0, 3.4f + Mathf.Sin(Time.time * 2.4f) * .35f, 0);
            marker.transform.localRotation = Quaternion.Euler(0, Time.time * 40f, 0);
        }
    }

    // ── Standing over the map ──

    IEnumerator StepToMap(Player player)
    {
        if (player.Look == null) yield break;
        player.Look.HeightOverride = viewHeight;
        yield return Frame(player, stepSeconds);
    }

    // Walks the player along the near side to face the open land and turns the view onto it, then zooms until its
    // places fill the view (or, with frameOpenLand off, the fixed spot, aim and zoom).
    IEnumerator Frame(Player player, float seconds)
    {
        var m = Map.transform;
        var (centre, half) = OpenLand();
        var aimLocal = frameOpenLand ? new Vector3(centre.x, 0f, centre.y) : new Vector3(viewAim.x, 0f, viewAim.y);
        aimLocal.y = Map.HeightAt(aimLocal.x, aimLocal.z);
        var aim = m.TransformPoint(aimLocal);
        float slide = frameOpenLand ? Mathf.Clamp(centre.y, -Map.halfZ * standSlide, Map.halfZ * standSlide) : 0f;
        var standLocal = new Vector3(-Map.halfX, 0f, slide);
        var stand = m.TransformPoint(standLocal);
        var to = new Vector3(stand.x - standBack, player.transform.position.y, stand.z);
        var from = player.transform.position;
        float fromYaw = player.Look.YawTransform.eulerAngles.y, fromPitch = player.Look.Pitch;
        float fromZoom = player.CameraRig != null ? player.CameraRig.FovOffset : 0f;
        float midZoom = frameOpenLand ? Mathf.Min(fromZoom, 0f) : viewZoom;
        for (float t = 0; t < seconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / seconds);
            player.Warp(Vector3.Lerp(from, to, k));
            CameraEase.YawPitchTo(player.Look, aim, out float yaw, out float pitch);
            player.Look.SetYaw(Mathf.LerpAngle(fromYaw, yaw, k));
            player.Look.SetPitch(Mathf.Lerp(fromPitch, pitch, k));
            if (player.CameraRig != null) player.CameraRig.FovOffset = Mathf.Lerp(fromZoom, midZoom, k);
            yield return null;
        }
        player.Warp(to);
        yield return CameraEase.LookAt(player.Look, aim, .2f);
        if (!frameOpenLand || player.CameraRig == null) yield break;
        // Turn and zoom until the open places (and their margin), and the Dungeon Master's head, just fit: the view aims
        // at the middle of what it must show, from where the eye now stands.
        var cam = PlayerManager.HasInstance ? PlayerManager.Instance.OutputCamera : null;
        if (cam == null) yield break;
        var eye = player.Look.PitchTransform.position;
        float baseYaw = player.Look.YawTransform.eulerAngles.y;
        float minYaw = float.MaxValue, maxYaw = float.MinValue, minPitch = float.MaxValue, maxPitch = float.MinValue;
        void Take(Vector3 world)
        {
            CameraEase.YawPitchTo(eye, world, out float y, out float p, baseYaw);
            float dy = Mathf.DeltaAngle(baseYaw, y);
            minYaw = Mathf.Min(minYaw, dy); maxYaw = Mathf.Max(maxYaw, dy);
            minPitch = Mathf.Min(minPitch, p); maxPitch = Mathf.Max(maxPitch, p);
        }
        for (int cx = -1; cx <= 1; cx += 2)
            for (int cz = -1; cz <= 1; cz += 2)
            {
                var corner = new Vector3(centre.x + cx * half.x * frameMargin, 0f, centre.y + cz * half.y * frameMargin);
                corner.y = Map.HeightAt(corner.x, corner.z);
                Take(m.TransformPoint(corner));
            }
        var head = keepDungeonMasterInView ? DungeonMasterHead() : null;
        if (head != null) Take(head.position + Vector3.up * 1.5f);
        // Room under the land for the hint line at the foot of the screen.
        minPitch -= (maxPitch - minPitch) * bottomRoom;
        float yawTo = baseYaw + (minYaw + maxYaw) * .5f, pitchTo = (minPitch + maxPitch) * .5f;
        float midPitch = pitchTo * Mathf.Deg2Rad;
        float hSpread = (maxYaw - minYaw) * Mathf.Cos(midPitch) * Mathf.Deg2Rad;
        float vFov = Mathf.Max((maxPitch - minPitch) * 1.04f, 2f * Mathf.Atan(Mathf.Tan(hSpread * .52f) / Mathf.Max(.5f, cam.aspect)) * Mathf.Rad2Deg);
        float baseFov = cam.fieldOfView - player.CameraRig.FovOffset;
        float zoom = Mathf.Clamp(vFov - baseFov, maxZoomIn, maxZoomOut);
        float startZoom = player.CameraRig.FovOffset, startPitch = player.Look.Pitch;
        for (float t = 0; t < .5f; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / .5f);
            player.Look.SetYaw(Mathf.LerpAngle(baseYaw, yawTo, k));
            player.Look.SetPitch(Mathf.Lerp(startPitch, pitchTo, k));
            player.CameraRig.FovOffset = Mathf.Lerp(startZoom, zoom, k);
            yield return null;
        }
        player.Look.SetYaw(yawTo); player.Look.SetPitch(pitchTo);
        player.CameraRig.FovOffset = zoom;
    }

    Transform dmHead;
    Transform DungeonMasterHead()
    {
        if (dmHead != null) return dmHead;
        var seat = FindAnyObjectByType<DungeonMasterSeat>();
        if (seat != null && seat.animator != null && seat.animator.isHuman) dmHead = seat.animator.GetBoneTransform(HumanBodyBones.Head);
        return dmHead;
    }

    // The middle and half size (map units, x across and z along) of the open places, each with its base round it.
    (Vector2 centre, Vector2 half) OpenLand()
    {
        int shown = Mathf.Min(OpenAct, RevealedAct);
        var min = new Vector2(float.MaxValue, float.MaxValue); var max = -min;
        foreach (var p in Map.Points)
        {
            if (p.encounter == null || p.encounter.act > shown) continue;
            var l = p.transform.localPosition; float r = p.radius + 1f;
            min = Vector2.Min(min, new Vector2(l.x - r, l.z - r)); max = Vector2.Max(max, new Vector2(l.x + r, l.z + r));
        }
        if (min.x > max.x) return (Vector2.zero, new Vector2(Map.halfX, Map.halfZ));
        return ((min + max) * .5f, (max - min) * .5f);
    }

    // For tests (evals): click a place by id (or "figure"), or step back.
    string testClick, testComplete; bool testCancel;
    public void TestComplete(string id) => testComplete = id;
    public bool InMapView { get; private set; }
    public void TestClick(string id) => testClick = id;
    public void TestCancel() => testCancel = true;
    public void TestOpenAct(int act) { if (PM != null) PM.SetFlag(ActFlag, act); }
    public CampaignPoint TestPoint(string id) { foreach (var p in Map.Points) if (p.encounter != null && p.encounter.id == id) return p; return null; }
}
