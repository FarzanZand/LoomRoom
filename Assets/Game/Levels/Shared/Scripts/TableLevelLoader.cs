using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class TableLevelLoader : MonoBehaviour
{
    public TableLevelCatalog catalog;
    public GameObject townRoot;
    public LightingManager lighting;
    public TableLevelData Current { get; private set; }
    public DungeonGenerator Dungeon { get; private set; }
    public bool Busy { get; private set; }
    TableLevelMenu menu;
    Player player;
    Vector3 townPosition;
    Quaternion townRotation;
    float defaultRespawn;
    bool initialized, townSaved;
    ItemStack[] townBag, townHotbar;
    readonly List<ItemData> townEquipment = new();
    readonly Dictionary<GameObject,bool> townObjects = new();
    GameObject environment;

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
        if(townRoot==null) townRoot=transform.Find("Woodland Village")?.gameObject;
        if(lighting==null) lighting=FindAnyObjectByType<LightingManager>();
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
        var tableBounds=GetComponent<BoxCollider>().bounds;
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
    void Update()
    {
        if(Busy || menu==null || menu.IsOpen || !PlayerManager.HasInstance || PlayerManager.Instance.ActiveKind!=PlayerKind.Table) return;
        if(Keyboard.current!=null && Keyboard.current.escapeKey.wasPressedThisFrame && GameManager.Instance.GameplayActive)
            ShowSelection("Choose your next adventure");
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
    IEnumerator LoadRoutine(TableLevelData level)
    {
        Busy=true; menu.Hide();
        GameManager.Instance.Pop(GameState.Dead);
        GameManager.Instance.Push(GameState.Cutscene);
        ScreenManager.Instance.FadeIn(.45f);
        yield return new WaitForSecondsRealtime(.5f);
        bool ready=false;
        try { Prepare(level); ready=true; }
        catch(Exception e) { Debug.LogException(e); }
        yield return null;
        if(ready)
        {
            if(level.kind==TableLevelKind.Dungeon)
            {
                if(!townSaved) SaveTownInventory();
                player.Equipment.UnequipAll();player.Bag.Clear();player.Hotbar.Clear();
                if(level.startingItems!=null) foreach(var item in level.startingItems) InventoryManager.Instance.Pickup(item,player);
                if(level.startingEquipment!=null) foreach(var item in level.startingEquipment) if(item!=null) player.Equipment.Equip(item);
            }
            else RestoreTownInventory();
            player.Stats.Revive();
            if(ProgressionManager.HasInstance) ProgressionManager.Instance.tableEntered=true;
        }
        else
        {
            if(environment!=null) { environment.SetActive(false);Destroy(environment); }
            SetTown(true);PlayerManager.Instance.SwapToPlayerImmediately(PlayerKind.Room);
            Current=null;Dungeon=null;
        }
        ScreenManager.Instance.FadeOut(.6f);
        yield return new WaitForSecondsRealtime(.65f);
        GameManager.Instance.Pop(GameState.Cutscene);Busy=false;
        if(!ready) ShowSelection("Could not load level — select another adventure");
    }
    void Prepare(TableLevelData level)
    {
        if(Current==null || Current.kind==TableLevelKind.Town) SaveTownInventory();
        if(environment!=null) { environment.SetActive(false);Destroy(environment); }
        Dungeon=null;SetTown(level.kind==TableLevelKind.Town);
        Vector3 spawn=townPosition;Quaternion rotation=townRotation;
        if(level.kind==TableLevelKind.Dungeon)
        {
            environment=level.environmentPrefab!=null ? Instantiate(level.environmentPrefab) : new GameObject(level.displayName+" — generated");
            var bounds=GetComponent<BoxCollider>().bounds;
            if(level.width*level.cellSize>bounds.size.x-2 || level.depth*level.cellSize>bounds.size.z-2)
                throw new InvalidOperationException("Dungeon dimensions exceed the table. Reduce dimensions on the level asset.");
            environment.transform.position=new Vector3(bounds.center.x,bounds.max.y+.08f,bounds.center.z);
            Dungeon=environment.GetComponent<DungeonGenerator>() ?? environment.AddComponent<DungeonGenerator>();
            int seed=level.fixedSeed!=0 ? level.fixedSeed : UnityEngine.Random.Range(1,int.MaxValue);
            Dungeon.Build(level,seed);spawn=Dungeon.SpawnPoint;rotation=Quaternion.identity;
        }
        else if(level.environmentPrefab!=null) environment=Instantiate(level.environmentPrefab,transform.position,Quaternion.identity);
        Current=level;
        if(lighting!=null) { if(level.mood!=null) lighting.BlendToMood(level.mood,0);else lighting.RestoreDefault(); }
        if(AudioManager.HasInstance)
        {
            if(level.backgroundMusic!=null) AudioManager.Instance.CrossfadeMusic(level.backgroundMusic,level.loopMusic,level.musicFadeSeconds);
            else AudioManager.Instance.StopMusic(level.musicFadeSeconds);
        }
        PlayerManager.Instance.SwapToPlayerImmediately(PlayerKind.Table);
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
    }
    void OnDied() { if(Current!=null && Current.kind==TableLevelKind.Dungeon) StartCoroutine(DeathMenu()); }
    IEnumerator DeathMenu() { yield return new WaitForSecondsRealtime(.8f);ShowSelection("You fell — choose Dungeon1 to retry"); }
    public void ReturnToRoom()
    {
        if(Busy)return;
        menu.Hide();GameManager.Instance.Pop(GameState.Dead);
        PlayerManager.Instance.SwapToPlayerImmediately(PlayerKind.Room);
        Dungeon?.ShowCeilings(false);
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
        foreach(var item in townEquipment)player.Equipment.Equip(item);
    }
}
