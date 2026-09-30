using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TableLevelLoader : MonoBehaviour
{
    public TableLevelCatalog catalog;
    public GameObject townRoot;
    public LightingManager lighting;
    public TableLevelData Current { get; private set; }
    public DungeonGenerator Dungeon { get; private set; }
    public bool Busy { get; private set; }
    [SerializeField, Min(0), Tooltip("Seconds after arriving in a new biome before the Dungeon Master speaks.")] float entryLineDelay = 5f;
    DungeonMaster.Line entryLine;
    public int FloorNumber { get; private set; } = 1;
    int generationLevel = 1;
    public int GenerationLevel => generationLevel;
    public bool HasNextFloor => Current!=null && Current.multipleLevels && Current.kind==TableLevelKind.Dungeon && FloorNumber<Mathf.Max(1,Current.levelCount);
    int runSeed;
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
        if(PlayerManager.Instance.ActiveKind==PlayerKind.Table && Current==null) ShowSelection();
    }
    void Initialize()
    {
        if(initialized) return;
        initialized=true;
        if(catalog==null) catalog=Resources.Load<TableLevelCatalog>("TableLevels");
        if(catalog==null) Debug.LogError("TableLevelLoader: no TableLevelCatalog at Resources/TableLevels; the adventure menu will be empty.", this);
        if(townRoot==null) townRoot=transform.Find("Woodland Village")?.gameObject;
        if(townRoot==null) Debug.LogError("TableLevelLoader: no \"Woodland Village\" child under the table; the town cannot be shown or hidden.", this);
        if(lighting==null) lighting=FindAnyObjectByType<LightingManager>();
        if(lighting==null) Debug.LogError("TableLevelLoader: no LightingManager in the scene; level moods will not be applied.", this);
        player=PlayerManager.Instance.GetPlayer(PlayerKind.Table);
        townPosition=player.transform.position;townRotation=player.transform.rotation;defaultRespawn=player.respawnDelay;
        menu=gameObject.AddComponent<TableLevelMenu>();menu.loader=this;
        var intro=GetComponent<TableManager>()?.tableIntroController;
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
        tableBounds=GetComponent<BoxCollider>().bounds;
        tableBounds.Expand(new Vector3(0,70,0));
        foreach(var c in FindObjectsByType<Character>(FindObjectsInactive.Include))
            if(!(c is Player) && tableBounds.Contains(c.transform.position)) townObjects.TryAdd(c.gameObject,c.gameObject.activeSelf);
        foreach(var item in FindObjectsByType<WorldItem>(FindObjectsInactive.Include))
            if(tableBounds.Contains(item.transform.position)) townObjects.TryAdd(item.gameObject,item.gameObject.activeSelf);
        SetTown(false);
    }
    void OnDestroy()
    {
        if(PlayerManager.HasInstance) PlayerManager.Instance.PlayerSwapped-=OnPlayerSwapped;
        if(player!=null) player.Died-=OnDied;
    }
    void OnPlayerSwapped(Player active) => Dungeon?.ShowCeilings(active.kind==PlayerKind.Table);
    public void ShowSelection(string title="Choose your adventure")
    {
        Initialize();
        if(!Busy) menu.Show(title);
    }
    public void Load(TableLevelData level)
    {
        if(Busy || level==null) return;
        Initialize(); StartCoroutine(LoadRoutine(level));
    }
    AdventureSave.Checkpoint pendingResume;
    public void ResumeAdventure()
    {
        Initialize();
        var save = player.GetComponent<AdventureSave>();
        if (Busy || save == null || !save.CanRestore(save.Saved.checkpoint)) { NotificationUI.Show("No compatible saved adventure"); return; }
        var checkpoint = save.Saved.checkpoint;
        var level = Array.Find(catalog.levels, x => x != null && x.name == checkpoint.level);
        if (level == null) return;
        pendingResume = checkpoint;
        StartCoroutine(LoadRoutine(level));
    }
    public void Descend()
    {
        if(Busy || !HasNextFloor || player==null || !player.IsAlive)return;
        StartCoroutine(LoadRoutine(Current,true));
    }
    IEnumerator LoadRoutine(TableLevelData level, bool descending=false)
    {
        Busy=true; menu.Hide();
        // Loading from the room: the table player may never have been active yet.
        PlayerManager.Instance.EnsureInitialized(PlayerKind.Table);
        int previousFloor=FloorNumber, previousSeed=runSeed;
        if(descending) FloorNumber++;
        else { FloorNumber=1;runSeed=level.fixedSeed!=0 ? level.fixedSeed : UnityEngine.Random.Range(1,int.MaxValue); }
        var resume = pendingResume; pendingResume = null;
        if (resume != null) { FloorNumber = resume.floor; runSeed = resume.seed; }
        GameManager.Instance.Pop(GameState.Dead);
        GameManager.Instance.Push(GameState.Cutscene);
        RememberRoom();
        var revealSettings = WorldManager.HasInstance ? WorldManager.Instance.tableLevelReveal : null;
        bool useReveal = !DebugDungeonSession.Active && !descending && level.kind == TableLevelKind.Dungeon && revealSettings != null;
        bool faded=false, completed=false;
        TableLevelReveal reveal=null;
        // C# forbids yield in try/catch, so only Prepare is caught; the finally always releases the cutscene and fade.
        try
        {
            if (useReveal) ScreenManager.Instance.ClearFade();
            else if (!DebugDungeonSession.Active)
            {
                ScreenManager.Instance.FadeIn(.45f); faded=true;
                yield return new WaitForSecondsRealtime(.5f);
            }
            bool ready=false;
            try {
                LootSource.BiomeProgress = floor => { level.BiomeAt(floor, out int at, out int count); return count <= 1 ? 1 : (at - 1f) / (count - 1); };
                LootSource.GenerationLevel = resume != null ? Mathf.Max(1, resume.lootLevel) : descending ? player.GetComponent<AdventurerProgress>().Level : 1;
                generationLevel = LootSource.GenerationLevel.Value;
                Prepare(level, useReveal);
                DebugDungeonSession.ConfigureEnemies(Dungeon);
                ready=true;
            }
            catch(Exception e) { Debug.LogException(e); }
            finally { LootSource.GenerationLevel = null; }
            if (ready && useReveal && AudioManager.HasInstance) AudioManager.Instance.StopMusic(revealSettings.cameraTransitionSeconds);
            if (!useReveal) yield return null;
            if(ready)
            {
                if(level.kind==TableLevelKind.Dungeon && !descending)
                {
                    player.UseLevelLoadout();
                    player.Equipment.UnequipAll();player.Bag.Clear();player.Hotbar.Clear();
                    if(level.startingItems!=null) foreach(var item in level.startingItems)
                        InventoryManager.Instance.Pickup(item,player,preferHotbar: level.startingEquipment!=null && Array.IndexOf(level.startingEquipment,item)>=0,playSound:false,announcePickup:false);
                    if(level.startingEquipment!=null) foreach(var item in level.startingEquipment) if(item!=null) {
                        bool owned=player.Bag.IndexOf(item)>=0 || player.Hotbar.IndexOf(item)>=0;
                        if(!owned)owned=InventoryManager.Instance.Pickup(item,player,preferHotbar:true,playSound:false,announcePickup:false);
                        if(owned)player.Equipment.Equip(item,playSound:false);
                    }
                }
                else if(level.kind==TableLevelKind.Town) RestoreTownInventory();
                if(!descending) player.Stats.Revive();
                if(ProgressionManager.HasInstance) ProgressionManager.Instance.tableEntered=true;
                if(level.kind==TableLevelKind.Dungeon && RunManager.HasInstance)
                {
                    if(!descending) { RunManager.Instance.BeginRun(level,runSeed); if(MessageLog.HasInstance) MessageLog.Instance.Clear(); }
                    RunManager.Instance.ReachFloor(FloorNumber);
                    if (resume != null) player.GetComponent<AdventureSave>()?.Restore(resume);
                    // The biome's line only when the surroundings change; otherwise just the depth.
                    var biome=level.Biome(FloorNumber);
                    bool newBiome=!descending || biome!=level.Biome(FloorNumber-1);
                    string arrival=newBiome && biome!=null && !string.IsNullOrWhiteSpace(biome.entryMessage) ? biome.entryMessage.Trim()
                        : descending ? $"You descend to floor {FloorNumber}." : $"You enter {level.displayName}.";
                    MessageLog.Post(arrival,MessageKind.Lore);
                    if(newBiome && biome!=null) entryLine=biome.PickEntryLine();
                }
                else if(RunManager.HasInstance) RunManager.Instance.Abandon();
            }
            else
            {
                FloorNumber=previousFloor;runSeed=previousSeed;
                if(Current==null)PlayerManager.Instance.SwapToPlayerImmediately(PlayerKind.Room);
            }
            if (ready && useReveal)
            {
                reveal = gameObject.AddComponent<TableLevelReveal>();
                yield return RunGuarded(reveal.Play(Dungeon, revealSettings, () =>
                {
                    if (lighting != null) lighting.BlendToMood(level.DungeonLighting(FloorNumber), reveal.SkipRequested ? .3f : revealSettings.approachSeconds);
                }));
            }
            if (faded)
            {
                ScreenManager.Instance.FadeOut(.6f); faded=false;
                yield return new WaitForSecondsRealtime(.65f);
            }
            if (ready && AudioManager.HasInstance)
            {
                var music = level.MusicFor(FloorNumber, out float musicVolume);
                // A floor with the same track as the one above keeps it playing instead of restarting it.
                if (music == null) AudioManager.Instance.StopMusic(level.musicFadeSeconds);
                else if (music != AudioManager.Instance.CurrentMusic) AudioManager.Instance.CrossfadeMusic(music, level.loopMusic, level.musicFadeSeconds, musicVolume);
            }
            completed=ready;
            if (ready && level.kind == TableLevelKind.Dungeon) player.GetComponent<AdventureSave>()?.CaptureFloor();
        }
        finally
        {
            if (reveal != null) Destroy(reveal);
            if (faded && ScreenManager.HasInstance) ScreenManager.Instance.FadeOut(.6f);
            if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Cutscene);
            Busy=false;
            if(!completed) ShowSelection("Could not load that level");
            // The biome's Dungeon Master line, a moment after the player is actually standing in it.
            else if(entryLine!=null) DungeonMaster.Say(entryLine,null,entryLineDelay);
            entryLine=null;
            if(completed && Dungeon!=null && Dungeon.Merchant!=null) MessageLog.Post("A merchant has set up shop on this floor.", MessageKind.Info);
        }
    }
    // Unity never resumes a coroutine whose nested routine threw, so step nested routines here and log instead.
    static IEnumerator RunGuarded(IEnumerator routine)
    {
        var stack=new Stack<IEnumerator>();
        stack.Push(routine);
        while(stack.Count>0)
        {
            object current=null;
            bool failed=false, advanced=false;
            try { advanced=stack.Peek().MoveNext(); if(advanced) current=stack.Peek().Current; }
            catch(Exception e) { Debug.LogException(e); failed=true; }
            if(failed)
            {
                while(stack.Count>0) (stack.Pop() as IDisposable)?.Dispose();
                yield break;
            }
            if(!advanced) { stack.Pop(); continue; }
            if(current is IEnumerator nested) stack.Push(nested);
            else yield return current;
        }
    }
    void Prepare(TableLevelData level, bool deferPlayerSwitch = false)
    {
        var previousEnvironment=environment;
        bool previousTown=Current!=null && Current.kind==TableLevelKind.Town;
        GameObject candidate=null;
        DungeonGenerator candidateDungeon=null;
        Vector3 spawn=townPosition;Quaternion rotation=townRotation;
        try {
            if(level.kind==TableLevelKind.Dungeon) {
                var bounds=GetComponent<BoxCollider>().bounds;
                if(level.cellSize<=0 || level.width*level.cellSize>bounds.size.x-2 || level.depth*level.cellSize>bounds.size.z-2)
                    throw new InvalidOperationException("Dungeon dimensions exceed the table. Reduce dimensions on the level asset.");
                // Hide and unregister the old surface while keeping it intact for rollback.
                if(previousEnvironment!=null)previousEnvironment.SetActive(false);
                SetTown(false);
                int seed=unchecked(runSeed+(FloorNumber-1)*104729);
                // A layout can fail validation (a room cut off by props). Try the next derived seed
                // rather than failing the floor; the sequence is fixed, so runs stay reproducible.
                for(int attempt=0;;attempt++) {
                    candidate=level.environmentPrefab!=null ? Instantiate(level.environmentPrefab) : new GameObject(level.displayName+" — generated");
                    candidate.transform.position=new Vector3(bounds.center.x,bounds.max.y+.08f,bounds.center.z);
                    candidateDungeon=candidate.GetComponent<DungeonGenerator>();
                    if(candidateDungeon==null)candidateDungeon=candidate.AddComponent<DungeonGenerator>();
                    try { candidateDungeon.Build(level,unchecked(seed+attempt*7907),FloorNumber); break; }
                    catch(InvalidOperationException e) when (attempt<7) {
                        Debug.LogWarning($"Floor {FloorNumber} layout rejected, trying another: {e.Message}");
                        candidate.SetActive(false);DestroyImmediate(candidate);candidate=null;
                    }
                }
                spawn=candidateDungeon.SpawnPoint;rotation=candidateDungeon.SpawnRotation;
            } else if(level.environmentPrefab!=null)candidate=Instantiate(level.environmentPrefab,transform.position,Quaternion.identity);
        } catch {
            if(candidate!=null){candidate.SetActive(false);Destroy(candidate);}
            if(previousEnvironment!=null)previousEnvironment.SetActive(true);
            SetTown(previousTown);throw;
        }
        if(Current==null || Current.kind==TableLevelKind.Town)SaveTownInventory();
        environment=candidate;Dungeon=candidateDungeon;
        if(previousEnvironment!=null){previousEnvironment.SetActive(false);Destroy(previousEnvironment);}
        SetTown(level.kind==TableLevelKind.Town);
        Current=level;
        if(lighting!=null && !deferPlayerSwitch) {
            if(level.kind==TableLevelKind.Dungeon) lighting.BlendToMood(level.DungeonLighting(FloorNumber),0);
            else if(level.mood!=null) lighting.BlendToMood(level.mood,0);
            else lighting.RestoreDefault();
        }
        if (!deferPlayerSwitch) PlayerManager.Instance.SwapToPlayerImmediately(PlayerKind.Table);
        player.StopAllCoroutines();
        player.respawnDelay=level.kind==TableLevelKind.Dungeon ? 0 : defaultRespawn;
        player.Died-=OnDied;player.Died+=OnDied;
        var controller=player.GetComponent<CharacterController>();
        controller.enabled=false;player.transform.SetPositionAndRotation(spawn,rotation);controller.enabled=true;
        player.SetSpawnPoint(spawn,rotation);player.Look?.SetYaw(rotation.eulerAngles.y);player.SynchronizePresentation();
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
        if(Current==null || Current.kind!=TableLevelKind.Dungeon) return;
        if(RunManager.HasInstance && RunManager.Instance.PlayDeath()) return;
        StartCoroutine(DeathMenu());
    }
    // The final stair: the run summary if there is one, otherwise the adventure menu.
    public void CompleteRun()
    {
        if(Busy) return;
        if(RunManager.HasInstance && RunManager.Instance.Running && RunManager.Instance.recap!=null) { RunManager.Instance.EndRunInVictory(); return; }
        ShowSelection("Dungeon complete");
    }
    IEnumerator DeathMenu() { yield return new WaitForSecondsRealtime(.8f);ShowSelection("You died"); }
    public void ReturnToRoom()
    {
        if(Busy)return;
        if (RunManager.HasInstance && RunManager.Instance.Running && !RunManager.Instance.Ended) return;
        StartCoroutine(ReturnRoutine());
    }
    // Fade out, switch to the room player and put the room's lighting and music back under the
    // black, then fade in.
    IEnumerator ReturnRoutine()
    {
        Busy=true;
        menu.Hide();GameManager.Instance.Pop(GameState.Dead);
        if(RunManager.HasInstance) RunManager.Instance.Abandon();
        GameManager.Instance.Push(GameState.Cutscene);
        bool fade=ScreenManager.HasInstance;
        try
        {
            if(fade) { ScreenManager.Instance.FadeIn(.45f); yield return new WaitForSecondsRealtime(.5f); }
            PlayerManager.Instance.SwapToPlayerImmediately(PlayerKind.Room);
            Dungeon?.ShowCeilings(false);
            RestoreRoom(.6f);
            yield return null;
            if(fade) { ScreenManager.Instance.FadeOut(.6f); fade=false; yield return new WaitForSecondsRealtime(.65f); }
        }
        finally
        {
            if(fade && ScreenManager.HasInstance) ScreenManager.Instance.FadeOut(.6f);
            if(GameManager.HasInstance) GameManager.Instance.Pop(GameState.Cutscene);
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
    void RestoreRoom(float musicFade)
    {
        if(!roomLighting.HasValue) return;
        if(lighting!=null) lighting.Restore(roomLighting.Value,0);
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
