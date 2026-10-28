using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Added to the table at runtime (TableManager), so nothing here is set in the Inspector: the
// catalog comes from Resources/TableLevels, the town is the table's "Woodland Village" child (removed for now) and
// the lighting is the scene's LightingManager.
public class TableLevelLoader : MonoBehaviour
{
    public TableLevelCatalog Catalog { get; private set; }
    GameObject townRoot;
    LightingManager lighting;
    public TableLevelData Current { get; private set; }
    public DungeonGenerator Dungeon { get; private set; }
    public bool Busy { get; private set; }
    // A level starts loading from the room (not a descent); the room is back after a run (under the black).
    public event Action LoadStarted, Returned;
    // The flight back to the room has begun (the floor starts sinking into the table under it): the campaign map rises.
    public event Action Leaving;
    [SerializeField, Min(0), Tooltip("Seconds after arriving in a new biome before the Dungeon Master speaks.")] float entryLineDelay = 5f;
    [SerializeField, Min(.1f), Tooltip("Seconds the last floor takes to sink into the table when the player sits down for the next game.")] float sweepSeconds = .8f;
    DungeonMaster.Line entryLine;
    public int FloorNumber { get; private set; } = 1;
    int generationLevel = 1;
    public int GenerationLevel => generationLevel;
    public bool HasNextFloor => Current!=null && Current.IsDungeon && FloorNumber<Current.FloorCount;
    int runSeed;
    public int RunSeed => runSeed;
    TableLevelMenu menu;
    Player player;
    Vector3 townPosition;
    Quaternion townRotation;
    float defaultRespawn;
    bool initialized, townSaved;
    ItemStack[] townBag, townHotbar;
    readonly List<ItemData> townEquipment = new();
    readonly Dictionary<GameObject,bool> townObjects = new();
    readonly List<GameObject> hiddenTownDrops = new();
    Bounds tableBounds;
    GameObject environment;
    // The room as it was before a table level changed its lighting and music; put back on Return to Room.
    LightingManager.Snapshot? roomLighting;
    AudioClip roomMusic;
    bool roomMusicLoops;
    float roomMusicVolume;

    IEnumerator Start()
    {
        Initialize();
        if (PlayerManager.HasInstance) PlayerManager.Instance.PlayerSwapped += OnPlayerSwapped;
        yield return null;
        if(PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind==PlayerKind.Table && Current==null) ShowSelection();
    }
    void Initialize()
    {
        if(initialized) return;
        initialized=true;
        Catalog=Resources.Load<TableLevelCatalog>("TableLevels");
        if(Catalog==null) Debug.LogError("TableLevelLoader: no TableLevelCatalog at Resources/TableLevels; the adventure menu will be empty.", this);
        var village=transform.Find("Woodland Village");
        townRoot=village!=null ? village.gameObject : null;
        // The village was removed (2026-10-14, build size) along with the Town level; only a catalog with a town needs it.
        if(townRoot==null && Catalog!=null && Array.Exists(Catalog.levels, l => l!=null && l.kind==TableLevelKind.Town)) Debug.LogError("TableLevelLoader: no \"Woodland Village\" child under the table; the town cannot be shown or hidden.", this);
        lighting=LightingManager.Instance!=null ? LightingManager.Instance : FindAnyObjectByType<LightingManager>(); // search only if it has not enabled yet
        if(lighting==null) Debug.LogError("TableLevelLoader: no LightingManager in the scene; level moods will not be applied.", this);
        player=PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        if(player!=null) { townPosition=player.transform.position;townRotation=player.transform.rotation;defaultRespawn=player.respawnDelay; }
        else Debug.LogError("TableLevelLoader: no table player (PlayerManager missing?); levels cannot be loaded.", this);
        menu=gameObject.AddComponent<TableLevelMenu>();menu.loader=this;
        var table=GetComponent<TableManager>();
        var intro=table!=null ? table.tableIntroController : null;
        if(intro!=null)
        {
            intro.enabled=false;
            foreach(var step in intro.steps)
            {
                if(step.activate!=null) foreach(var go in step.activate) if(go!=null) townObjects[go]=true;
                if(step.startMove!=null && step.startMove.targetTransform!=null)
                {
                    step.startMove.transform.SetPositionAndRotation(step.startMove.targetTransform.position,step.startMove.targetTransform.rotation);
                    step.startMove.enabled=false;
                }
            }
        }
        tableBounds=TableBox;
        tableBounds.Expand(new Vector3(0,70,0));
        foreach(var c in FindObjectsByType<Character>(FindObjectsInactive.Include))
            if(!(c is Player) && tableBounds.Contains(c.transform.position)) townObjects.TryAdd(c.gameObject,c.gameObject.activeSelf);
        foreach(var item in FindObjectsByType<WorldItem>(FindObjectsInactive.Include))
            if(tableBounds.Contains(item.transform.position)) townObjects.TryAdd(item.gameObject,item.gameObject.activeSelf);
        SetTown(false);
    }
    // The loading cutscene is held by a flag, so it is popped exactly once, also when the load stops part way (the
    // object disabled or destroyed: Unity stops the coroutine without running its finally).
    bool ownsCutscene;
    void HoldCutscene() { if (ownsCutscene || !GameManager.HasInstance) return; GameManager.Instance.Push(GameState.Cutscene); ownsCutscene = true; }
    void ReleaseCutscene() { if (!ownsCutscene) return; ownsCutscene = false; if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Cutscene); }
    void OnDisable() { if (ownsCutscene) { ReleaseCutscene(); Busy = false; } }
    void OnDestroy()
    {
        if(PlayerManager.HasInstance) PlayerManager.Instance.PlayerSwapped-=OnPlayerSwapped;
        if(player!=null) player.Died-=OnDied;
    }
    // The miniature keeps its ceilings from both sides; skylights and windows are the way to look in.
    void OnPlayerSwapped(Player active) { if(Dungeon!=null) Dungeon.ShowCeilingsFor(active!=null ? active.kind : PlayerKind.Room); }
    public void ShowSelection(string title="Choose your adventure")
    {
        Initialize();
        if(!Busy) menu.Show(title);
    }
    // skipReveal: straight in behind a fade, as the debug sessions do (the dev console's loadtemplate).
    // startFloor: the run's first floor (the toolbar's floor pick); 1 in play.
    public void Load(TableLevelData level, bool skipReveal=false, int startFloor=1)
    {
        if(Busy || level==null) return;
        pendingStartFloor = Mathf.Clamp(startFloor, 1, Mathf.Max(1, level.FloorCount));
        Initialize(); StartCoroutine(LoadRoutine(level,false,skipReveal));
    }
    int pendingStartFloor = 1;
    AdventureSave.Checkpoint pendingResume;
    // The current run was continued from a save, not started fresh.
    public bool Resumed { get; private set; }
    // Whether the saved run can be loaded (its checkpoint fits and its level is still in the catalog).
    public bool CanResumeAdventure()
    {
        Initialize();
        var save = player.GetComponent<AdventureSave>();
        if (Busy || save == null || !save.CanRestore(save.Saved.checkpoint)) return false;
        var checkpoint = save.Saved.checkpoint;
        return Catalog != null && Array.Find(Catalog.levels, x => x != null && x.name == checkpoint.level) != null;
    }

    // Loads the saved run; false when there is none to load (or its level is gone from the catalog).
    public bool ResumeAdventure()
    {
        Initialize();
        var save = player.GetComponent<AdventureSave>();
        if (Busy || save == null || !save.CanRestore(save.Saved.checkpoint)) { NotificationUI.Show("No compatible saved adventure"); return false; }
        var checkpoint = save.Saved.checkpoint;
        var level = Catalog != null ? Array.Find(Catalog.levels, x => x != null && x.name == checkpoint.level) : null;
        if (level == null) { NotificationUI.Show("That saved adventure's table is gone"); return false; }
        pendingResume = checkpoint;
        StartCoroutine(LoadRoutine(level));
        return true;
    }
    public void Descend()
    {
        if(Busy || !HasNextFloor || player==null || !player.IsAlive)return;
        StartCoroutine(LoadRoutine(Current,true));
    }
    IEnumerator LoadRoutine(TableLevelData level, bool descending=false, bool skipReveal=false)
    {
        Busy=true; menu.Hide();
        // Loading from the room: the table player may never have been active yet.
        PlayerManager.Instance.EnsureInitialized(PlayerKind.Table);
        int previousFloor=FloorNumber, previousSeed=runSeed;
        if(!descending) LoadStarted?.Invoke();
        if(descending) FloorNumber++;
        else { FloorNumber=pendingStartFloor;runSeed=level.fixedSeed!=0 ? level.fixedSeed : UnityEngine.Random.Range(1,int.MaxValue); }
        pendingStartFloor=1;
        var resume = pendingResume; pendingResume = null;
        if (!descending) Resumed = resume != null;
        if (resume != null) { FloorNumber = resume.floor; runSeed = resume.seed; }
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Dead);
        HoldCutscene();
        var revealSettings = WorldManager.HasInstance ? WorldManager.Instance.tableLevelReveal : null;
        bool useReveal = !DebugDungeonSession.Active && !skipReveal && !descending && level.IsDungeon && revealSettings != null;
        bool faded=false, completed=false;
        TableLevelReveal reveal=null;
        // C# forbids yield in try/catch, so only Prepare is caught; the finally always releases the cutscene and fade.
        try
        {
            RememberRoom();
            if (useReveal) ScreenManager.Instance.ClearFade();
            else if (!DebugDungeonSession.Active)
            {
                ScreenManager.Instance.FadeToBlack(.45f); faded=true;
                yield return GameManager.WaitCutscene(.5f);
            }
            bool ready=false;
            Exception failure=null;
            IEnumerator preparing=null;
            try {
                LootSource.BiomeProgress = floor => { level.BiomeAt(floor, out int at, out int count); return count <= 1 ? 1 : (at - 1f) / (count - 1); };
                LootSource.FloorGear = floor => level.GearAt(floor);
                LootSource.GenerationLevel = resume != null ? Mathf.Max(1, resume.lootLevel) : descending ? player.GetComponent<AdventurerProgress>().Level : 1;
                generationLevel = LootSource.GenerationLevel.Value;
                // With the reveal (from the room) the floor is built over several frames while the campaign map folds away.
                preparing = PrepareRoutine(level, useReveal, useReveal);
            }
            catch(Exception e) { failure=e; }
            while(failure==null) {
                bool more;
                try { more=preparing.MoveNext(); } catch(Exception e) { failure=e; break; }
                if(!more) break;
                yield return null;
            }
            if(failure==null) {
                try { DebugDungeonSession.ConfigureEnemies(Dungeon); ready=true; }
                catch(Exception e) { failure=e; }
            }
            if(failure!=null) Debug.LogException(failure);
            LootSource.GenerationLevel = null;
            // The campaign map finishes folding away before the floor is shown; until then the floor stays switched off
            // (not drawn, nothing in it started).
            if(ready && useReveal && environment!=null && CampaignManager.MapSinking) {
                environment.SetActive(false);
                while(CampaignManager.MapSinking) yield return null;
                environment.SetActive(true);
            }
            if (ready && useReveal && AudioManager.HasInstance) AudioManager.Instance.StopMusic(revealSettings.cameraTransitionSeconds);
            if (!useReveal) yield return null;
            if(ready)
            {
                if(level.IsDungeon && !descending)
                {
                    player.UseLevelLoadout();
                    player.Equipment.UnequipAll();player.Bag.Clear();player.Hotbar.Clear();
                }
                else if(level.kind==TableLevelKind.Town) RestoreTownInventory();
                if(!descending) player.Stats.Revive();
                if(ProgressionManager.HasInstance) ProgressionManager.Instance.tableEntered=true;
                if(level.IsDungeon && RunManager.HasInstance)
                {
                    if(!descending) { RunManager.Instance.BeginRun(level,runSeed); if(MessageLog.HasInstance) MessageLog.Instance.Clear(); if(resume==null) { TavernDish.ForgetEaten(); DungeonMasterMeddling.NewRun(); player.GetComponent<AdventurerProgress>()?.ApplyStats(); } }
                    // A continued run's checkpoint already holds this floor's experience (it was taken after the floor
                    // was reached): only the depth is set, with no floor experience and no "+XP" meter at level 1.
                    var resumedProgress = resume != null ? player.GetComponent<AdventurerProgress>() : null;
                    var floorRules = resumedProgress != null ? resumedProgress.rules : null;
                    float xpPerFloor = floorRules != null ? floorRules.xpPerFloor : 0f;
                    if (floorRules != null) floorRules.xpPerFloor = 0f;
                    try { RunManager.Instance.ReachFloor(FloorNumber); }
                    finally { if (floorRules != null) floorRules.xpPerFloor = xpPerFloor; }
                    var adventureSave = player.GetComponent<AdventureSave>();
                    if (resume != null && adventureSave != null) adventureSave.Restore(resume);
                    // The biome's line only when the surroundings change; otherwise just the depth.
                    var biome=level.Biome(FloorNumber);
                    bool newBiome=!descending || biome!=level.Biome(FloorNumber-1);
                    string arrival=newBiome && biome!=null && !string.IsNullOrWhiteSpace(biome.entryMessage) ? biome.entryMessage.Trim()
                        : descending ? string.Format(string.IsNullOrWhiteSpace(level.nextFloorMessage) ? "You descend to floor {0}." : level.nextFloorMessage, FloorNumber) : $"You enter {level.displayName}.";
                    MessageLog.Post(arrival,MessageKind.Lore);
                    if(newBiome && biome!=null) entryLine=biome.PickEntryLine();
                }
                else if(RunManager.HasInstance) RunManager.Instance.Abandon();
            }
            else
            {
                FloorNumber=previousFloor;runSeed=previousSeed;
                if(Current==null)PlayerManager.Instance.SwapToPlayerImmediately(PlayerKind.Room);
                // Nothing on the table (no run): the room was never changed, so its snapshot is let go, or the next
                // load would keep this one however the room looks by then.
                if(Dungeon==null && !descending) { roomLighting=null; roomMusic=null; }
            }
            if (ready && useReveal)
            {
                reveal = gameObject.AddComponent<TableLevelReveal>();
                yield return RunGuarded(reveal.Play(Dungeon, revealSettings, () =>
                {
                    if (lighting != null) lighting.BlendToMood(level.BoardOn(FloorNumber) ? level.BoardLighting(lighting, FloorNumber) : level.DungeonLighting(FloorNumber), reveal.SkipRequested ? .3f : revealSettings.approachSeconds);
                }));
            }
            if (faded)
            {
                ScreenManager.Instance.FadeFromBlack(.6f); faded=false;
                yield return GameManager.WaitCutscene(.65f);
            }
            if (ready && AudioManager.HasInstance)
            {
                var music = level.MusicFor(FloorNumber, out float musicVolume);
                // A floor with the same track as the one above keeps it playing instead of restarting it.
                if (music == null) AudioManager.Instance.StopMusic(level.musicFadeSeconds);
                else if (music != AudioManager.Instance.CurrentMusic) AudioManager.Instance.CrossfadeMusic(music, level.loopMusic, level.musicFadeSeconds, musicVolume);
            }
            completed=ready;
            if (ready && Dungeon != null) Dungeon.MergeForPlay();   // the reveal is over: big chunks for play
            var floorSave = ready && level.IsDungeon ? player.GetComponent<AdventureSave>() : null;
            if (floorSave != null) floorSave.CaptureFloor();
        }
        finally
        {
            if (reveal != null) Destroy(reveal);
            if (faded && ScreenManager.HasInstance) ScreenManager.Instance.FadeFromBlack(.6f);
            ReleaseCutscene();
            Busy=false;
            // From the campaign map the old adventure menu is no way back: the player stays standing at the table, and
            // the map rises again on its own (CampaignManager).
            if(!completed && !descending && CampaignManager.Active) FailedFromMap();
            else if(!completed) ShowSelection("Could not load that level");
            // The biome's Dungeon Master line, a moment after the player is actually standing in it.
            else if(entryLine!=null) DungeonMaster.Say(entryLine,null,entryLineDelay);
            entryLine=null;
            if(completed && Dungeon!=null && Dungeon.Merchant!=null) MessageLog.Post("A merchant has set up shop on this floor.", MessageKind.Info);
        }
    }
    void FailedFromMap()
    {
        NotificationUI.Show("Could not load that level");
        if(PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind!=PlayerKind.Room) PlayerManager.Instance.SwapToPlayerImmediately(PlayerKind.Room);
        if(TableManager.HasInstance) TableManager.Instance.StandRoomPlayer();
    }
    // Unity never resumes a coroutine whose nested routine threw, so step nested routines here and log instead.
    static IEnumerator RunGuarded(IEnumerator routine) => GuardedRoutine.Run(routine);
    // The table's whole box: its collider is swapped for a cut one while a floor's pits go into it (TableCut).
    Bounds TableBox=>GetComponent<TableCut>() is { } cut && cut.Bounds.size!=Vector3.zero ? cut.Bounds : GetComponent<BoxCollider>().bounds;
    DungeonGenerator building;
    // How far the floor being built over several frames has got (0 to 1); 1 when none is (the campaign map folds away
    // at this pace).
    public float BuildProgress => building!=null ? building.BuildProgress : 1f;
    // sliced: the floor is built over several frames (DungeonGenerator.BuildSliced), so a floor built from the
    // campaign map never holds the game still while the map folds away.
    IEnumerator PrepareRoutine(TableLevelData level, bool deferPlayerSwitch, bool sliced)
    {
        var previousEnvironment=environment;
        bool previousTown=Current!=null && Current.kind==TableLevelKind.Town;
        GameObject candidate=null;
        DungeonGenerator candidateDungeon=null;
        Vector3 spawn=townPosition;Quaternion rotation=townRotation;
        if(level.IsDungeon) {
            Exception failure=null;
            var bounds=TableBox;
            if(level.cellSize<=0 || level.width*level.cellSize>bounds.size.x-2 || level.depth*level.cellSize>bounds.size.z-2)
                failure=new InvalidOperationException("Dungeon dimensions exceed the table. Reduce dimensions on the level asset.");
            else {
                // Hide and unregister the old surface while keeping it intact for rollback.
                if(previousEnvironment!=null)previousEnvironment.SetActive(false);
                SetTown(false);
            }
            int seed=unchecked(runSeed+(FloorNumber-1)*104729);
            // A layout can fail validation (a room cut off by props). Try the next derived seed
            // rather than failing the floor; the sequence is fixed, so runs stay reproducible.
            for(int attempt=0; failure==null; attempt++) {
                candidate=new GameObject(level.displayName+" (generated)");
                candidate.transform.position=new Vector3(bounds.center.x,bounds.max.y+.08f,bounds.center.z);
                candidateDungeon=candidate.GetComponent<DungeonGenerator>();
                if(candidateDungeon==null)candidateDungeon=candidate.AddComponent<DungeonGenerator>();
                int attemptSeed=unchecked(seed+attempt*7907);
                Exception error=null;
                if(sliced) {
                    var steps=candidateDungeon.BuildSliced(level,attemptSeed,FloorNumber,attempt>=7);
                    building=candidateDungeon;
                    while(true) {
                        bool more;
                        try { more=steps.MoveNext(); } catch(Exception e) { error=e; break; }
                        if(!more) break;
                        yield return null;
                    }
                    building=null;
                }
                else { try { candidateDungeon.Build(level,attemptSeed,FloorNumber,attempt>=7); } catch(Exception e) { error=e; } }
                if(error==null) break;
                if(error is InvalidOperationException && attempt<7) {
                    Debug.LogWarning($"Floor {FloorNumber} layout rejected, trying another: {error.Message}");
                    candidate.SetActive(false);DestroyImmediate(candidate);candidate=null;
                    continue;
                }
                failure=error;
            }
            if(failure!=null) {
                if(candidate!=null){candidate.SetActive(false);Destroy(candidate);}
                if(previousEnvironment!=null)previousEnvironment.SetActive(true);
                SetTown(previousTown);
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
            spawn=candidateDungeon.SpawnPoint;rotation=candidateDungeon.SpawnRotation;
        }
        if(Current==null || Current.kind==TableLevelKind.Town)SaveTownInventory();
        environment=candidate;Dungeon=candidateDungeon;
        if(previousEnvironment!=null){
            previousEnvironment.SetActive(false);Destroy(previousEnvironment);
            // Spells still in the air or about to erupt belong to the old floor: they never reach the new one.
            FireballVolley.ClearAll();SpellProjectile.ClearAll();GroundAreaSpell.ClearAll();
        }
        SetTown(level.kind==TableLevelKind.Town);
        Current=level;
        // Portal pockets are built with the floor from its seed (each portal keeps its own until the floor goes).
        if(level.IsDungeon) PortalArch.PrebuildAll(Dungeon,unchecked(runSeed+(FloorNumber-1)*104729));
        if(lighting!=null && !deferPlayerSwitch) {
            if(level.BoardOn(FloorNumber)) lighting.BlendToMood(level.BoardLighting(lighting,FloorNumber),0);
            else if(level.IsDungeon) lighting.BlendToMood(level.DungeonLighting(FloorNumber),0);
            else if(level.mood!=null) lighting.BlendToMood(level.mood,0);
            else lighting.RestoreDefault();
        }
        if (!deferPlayerSwitch) PlayerManager.Instance.SwapToPlayerImmediately(PlayerKind.Table);
        player.StopAllCoroutines();
        player.respawnDelay=level.IsDungeon ? 0 : defaultRespawn;
        player.Died-=OnDied;player.Died+=OnDied;
        var controller=player.GetComponent<CharacterController>();
        controller.enabled=false;player.transform.SetPositionAndRotation(spawn,rotation);controller.enabled=true;
        player.SetSpawnPoint(spawn,rotation);if(player.Look!=null)player.Look.SetYaw(rotation.eulerAngles.y);player.SynchronizePresentation();
        Physics.SyncTransforms();
    }
    void SetTown(bool active)
    {
        if(townRoot!=null) townRoot.SetActive(active);
        foreach(var pair in townObjects) if(pair.Key!=null) pair.Key.SetActive(active && pair.Value);
        if(active)
        {
            foreach(var go in hiddenTownDrops) if(go!=null) go.SetActive(true);
            hiddenTownDrops.Clear();
            return;
        }
        // Items dropped in town after Initialize are unparented, so townObjects never saw them.
        foreach(var item in FindObjectsByType<WorldItem>())
        {
            var go=item.gameObject;
            if(townObjects.ContainsKey(go) || !tableBounds.Contains(item.transform.position)) continue;
            if(environment!=null && item.transform.IsChildOf(environment.transform)) continue;
            go.SetActive(false);hiddenTownDrops.Add(go);
        }
    }
    void OnDied()
    {
        if(Current==null || !Current.IsDungeon) return;
        if(RunManager.HasInstance && RunManager.Instance.PlayDeath()) return;
        StartCoroutine(DeathMenu());
    }
    // The final stair: the run summary if there is one, otherwise the adventure menu.
    public void CompleteRun()
    {
        if(Busy || (IntroController.HasInstance && IntroController.Instance.StairsReached())) return;
        if(RunManager.HasInstance && RunManager.Instance.Running && RunManager.Instance.recap!=null) { RunManager.Instance.EndRunInVictory(); return; }
        ShowSelection("Dungeon complete");
    }
    IEnumerator DeathMenu() { yield return GameManager.WaitCutscene(.8f);ShowSelection("You died"); }
    // Sinks the last floor into the table, from the player's side outward, over sweepSeconds, then destroys it (never
    // during a run). The loader lets go of it at once, so the next game can be set up while it goes.
    public void SweepTable(Vector3 from)
    {
        if(Busy || environment==null || (RunManager.HasInstance && RunManager.Instance.Running && !RunManager.Instance.Ended)) return;
        var swept=environment;
        environment=null; Dungeon=null;
        StartCoroutine(Sweep(swept, from));
    }
    IEnumerator Sweep(GameObject swept, Vector3 from)
    {
        var settings=WorldManager.HasInstance ? WorldManager.Instance.tableLevelReveal : null;
        float drop=settings!=null ? settings.liftDistance : 2.5f;
        foreach(var ps in swept.GetComponentsInChildren<ParticleSystem>()) ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var pieces=new List<(Transform t, Vector3 at, Vector3 scale, float delay)>();
        float far=.01f;
        foreach(var r in swept.GetComponentsInChildren<Renderer>()) { var d=r.transform.position-from; d.y=0; far=Mathf.Max(far,d.magnitude); }
        var seen=new HashSet<Transform>();
        foreach(var r in swept.GetComponentsInChildren<Renderer>())
        {
            var t=r.transform; if(!seen.Add(t)) continue;
            var d=t.position-from; d.y=0;
            pieces.Add((t,t.position,t.localScale,d.magnitude/far*.5f));
        }
        var lights=new List<(Light l, float intensity)>();
        foreach(var l in swept.GetComponentsInChildren<Light>()) lights.Add((l,l.intensity));
        float piece=sweepSeconds*.5f;
        for(float time=0; time<sweepSeconds && swept!=null; time+=Time.deltaTime)
        {
            float k=Mathf.Clamp01(time/sweepSeconds);
            foreach(var (l,intensity) in lights) if(l!=null) l.intensity=intensity*(1f-k);
            foreach(var (t,at,scale,delay) in pieces)
            {
                if(t==null) continue;
                float u=Mathf.Clamp01((time-delay*sweepSeconds)/piece); u*=u;
                t.position=at+Vector3.down*drop*u;
                t.localScale=scale*(1f-u);
            }
            yield return null;
        }
        if(swept!=null) Destroy(swept);
    }

    // Sweeps the last floor off the table at once before a new game is set up (never during a run).
    public void ClearTable()
    {
        if(Busy || environment==null || (RunManager.HasInstance && RunManager.Instance.Running && !RunManager.Instance.Ended)) return;
        Destroy(environment); environment=null; Dungeon=null;
    }
    public void ReturnToRoom() => ReturnToRoom(null);
    // whileDark runs once the room is back under the black and takes over the fade from black
    // (the run recap wakes the player in bed).
    public void ReturnToRoom(Action whileDark)
    {
        if(Busy)return;
        if (RunManager.HasInstance && RunManager.Instance.Running && !RunManager.Instance.Ended) return;
        StartCoroutine(ReturnRoutine(whileDark));
    }
    // Fade out, switch to the room player and put the room's lighting and music back under the
    // black, then fade in.
    Coroutine sinking;
    bool floorSinking;
    Vector3 sinkTo;
    // Sinks the floor into the table: all of it, under the tabletop, creatures too (their agents would hold them up at
    // the floor's height).
    IEnumerator SinkFloor(GameObject floor, float depth, float seconds)
    {
        try
        {
            if(floor==null) yield break;
            foreach(var agent in floor.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>()) agent.enabled=false;
            var t=floor.transform; var from=t.position; sinkTo=from-Vector3.up*depth;
            for(float s=0; s<seconds && t!=null; s+=GameManager.CutsceneDelta)
            { t.position=from-Vector3.up*depth*Mathf.SmoothStep(0,1,s/seconds); yield return null; }
            if(t!=null) t.position=sinkTo;
        }
        finally { floorSinking=false; }
    }
    IEnumerator ReturnRoutine(Action whileDark)
    {
        Busy=true;
        menu.Hide(); if(GameManager.HasInstance) GameManager.Instance.Pop(GameState.Dead);
        if(RunManager.HasInstance) RunManager.Instance.Abandon();
        HoldCutscene();
        bool fade=ScreenManager.HasInstance;
        // A dungeon's way back is its entry flown in reverse (TableLevelExit), with no black: fast, and the room
        // player is already standing at the table when the view lands. The wake-up and the ending keep the fade.
        var exitSettings = WorldManager.HasInstance ? WorldManager.Instance.tableLevelReveal : null;
        bool fly = whileDark==null && Dungeon!=null && exitSettings!=null && !DebugDungeonSession.Active && Current!=null && Current.IsDungeon;
        // Only from the table's own floor: a portal's pocket lies far off the table (the flight would cross the house).
        if(fly && player!=null) { var box=TableBox; box.Expand(new Vector3(2f,400f,2f)); fly=box.Contains(player.transform.position); }
        try
        {
            if(fly)
            {
                fade=false;
                if (TableManager.HasInstance) TableManager.Instance.StandRoomPlayer();
                var exit = gameObject.AddComponent<TableLevelExit>();
                bool skipped = false;
                var roomSnap = roomLighting;   // RestoreRoom clears it; a skipped flight puts the room's light back at once
                // How high the floor stands above the tabletop (its tallest room, kit pieces, the arena's stands).
                float top = Dungeon.transform.position.y;
                if(environment!=null) foreach(var r in environment.GetComponentsInChildren<Renderer>()) if(r.enabled && !(r is ParticleSystemRenderer)) top=Mathf.Max(top, r.bounds.max.y);
                float standing = top - Dungeon.transform.position.y;
                try
                {
                    yield return RunGuarded(exit.Play(Dungeon, exitSettings, standing, () =>
                    {
                        RestoreRoom(exitSettings.exitSeconds, exitSettings.exitSeconds * .6f);
                        // The boss floor's painting is the room's own again before the room comes into view.
                        if (RoomPainting.BehindDungeonMaster != null) RoomPainting.BehindDungeonMaster.Restore();
                        // The floor sinks into the table as the view leaves it, and the map rises in its place, both
                        // while the view flies back to the room.
                        floorSinking = true; sinking = StartCoroutine(SinkFloor(environment, standing + 1f, exitSettings.exitSinkSeconds));
                        Leaving?.Invoke();
                    }));
                }
                finally { skipped = exit.Skipped; Destroy(exit); }
                if (skipped && roomSnap.HasValue && lighting != null) lighting.Restore(roomSnap.Value, 0f);
                // A skipped flight skips the sink too: the floor is under the table at once. Otherwise what is left of the
                // sink plays out (it usually ends with the flight).
                if(skipped && floorSinking) { if(sinking!=null) StopCoroutine(sinking); floorSinking=false; if(environment!=null) environment.transform.position=sinkTo; }
                while(floorSinking) yield return null;
            }
            else if(fade) { ScreenManager.Instance.FadeToBlack(.45f); yield return GameManager.WaitCutscene(.5f); }
            PlayerManager.Instance.SwapToPlayerImmediately(PlayerKind.Room);
            RestoreRoom(.6f);
            // The run is over: its floor leaves the table under the black (after a death and after the win, so the
            // ending's morning has a clear table). Its holes in the table close with it.
            if(environment!=null) { Destroy(environment); environment=null; Dungeon=null; }
            Returned?.Invoke();
            yield return null;
            if(whileDark!=null) { fade=false; whileDark(); }
            if(fade) { ScreenManager.Instance.FadeFromBlack(.6f); fade=false; yield return GameManager.WaitCutscene(.65f); }
        }
        finally
        {
            if(fade && ScreenManager.HasInstance) ScreenManager.Instance.FadeFromBlack(.6f);
            ReleaseCutscene();
            Busy=false;
        }
    }
    // Once per visit to the table: later loads (descending, retrying) keep the first snapshot.
    void RememberRoom()
    {
        if(roomLighting.HasValue) return;
        if(lighting!=null) roomLighting=lighting.Capture();
        if(AudioManager.HasInstance)
        {
            roomMusic=AudioManager.Instance.CurrentMusic;
            roomMusicLoops=AudioManager.Instance.CurrentMusicLoops;
            roomMusicVolume=AudioManager.Instance.CurrentMusicVolume;
        }
    }
    void RestoreRoom(float musicFade, float lightSeconds=0f)
    {
        if(!roomLighting.HasValue) return;
        if(lighting!=null) lighting.Restore(roomLighting.Value,lightSeconds);
        if(AudioManager.HasInstance)
        {
            if(roomMusic!=null) AudioManager.Instance.CrossfadeMusic(roomMusic,roomMusicLoops,musicFade,roomMusicVolume);
            else AudioManager.Instance.StopMusic(musicFade);
        }
        roomLighting=null;roomMusic=null;
    }
    void SaveTownInventory()
    {
        if(player.Bag==null || player.Hotbar==null) return;
        townBag=Capture(player.Bag);townHotbar=Capture(player.Hotbar);townEquipment.Clear();
        foreach(var item in player.Equipment.EquippedItems) townEquipment.Add(item);
        townSaved=true;
    }
    // What the town snapshot holds, so ItemData.PruneVariants keeps those rolled copies for the way back.
    public void AddTownItemsTo(HashSet<ItemData> items)
    {
        if(!townSaved)return;
        foreach(var s in townBag)if(s?.item!=null)items.Add(s.item);
        foreach(var s in townHotbar)if(s?.item!=null)items.Add(s.item);
        foreach(var item in townEquipment)if(item!=null)items.Add(item);
    }
    static ItemStack[] Capture(Inventory inv)
    {
        var result=new ItemStack[inv.SlotCount];
        for(int i=0;i<result.Length;i++) { var s=inv[i];if(s!=null)result[i]=new ItemStack(s.item,s.count); }
        return result;
    }
    void RestoreTownInventory()
    {
        if(!townSaved)return;
        player.Equipment.UnequipAll();player.Bag.Clear();player.Hotbar.Clear();
        for(int i=0;i<townBag.Length;i++)player.Bag.Set(i,townBag[i]==null ? null:new ItemStack(townBag[i].item,townBag[i].count));
        for(int i=0;i<townHotbar.Length;i++)player.Hotbar.Set(i,townHotbar[i]==null ? null:new ItemStack(townHotbar[i].item,townHotbar[i].count));
        foreach(var item in townEquipment)player.Equipment.Equip(item,playSound:false,grant:true);
    }
}
