using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

// Reproducible editor checks and a narrow file-command hook for local iteration.
[InitializeOnLoad]
public static class TableLevelValidation
{
    const string Command="Library/TableLevelCommand.txt";
    const string Report="Library/TableLevelReport.txt";
    static double next;
    static int phase;
    static int oldSeed;
    static float combatHealth;
    static int feedbackPhase;
    static Character feedbackEnemy;
    static GameObject sightBlocker;
    static double feedbackNext;
    static readonly List<string> results=new();
    static TableLevelValidation() { EditorApplication.update+=Tick; }
    [MenuItem("Tools/Table Levels/Validate layouts and assets")]
    public static void Validate()
    {
        results.Clear();
        for(int seed=1;seed<=100;seed++)
        {
            var map=new DungeonLayout(32,42,12,seed);
            var visited=new HashSet<Vector2Int>();var queue=new Queue<Vector2Int>();queue.Enqueue(map.Start);visited.Add(map.Start);
            while(queue.Count>0)
            {
                var p=queue.Dequeue();
                foreach(var d in new[]{Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right})
                {
                    var n=p+d;if(n.x<0||n.y<0||n.x>=32||n.y>=42||!map.floor[n.x,n.y]||!visited.Add(n))continue;queue.Enqueue(n);
                }
            }
            Check(map.rooms.Count>=6,"Enough rooms for seed "+seed);
            Check(map.Exit!=map.Start && visited.Contains(map.Exit),"Reachable exit for seed "+seed);
            foreach(var room in map.rooms)Check(visited.Contains(DungeonLayout.Center(room)),"Connected room for seed "+seed);
            var repeat=new DungeonLayout(32,42,12,seed);
            for(int x=0;x<32;x++)for(int y=0;y<42;y++)Check(map.floor[x,y]==repeat.floor[x,y],"Seed determinism");
        }
        var catalog=Resources.Load<TableLevelCatalog>("TableLevels");Check(catalog!=null && catalog.levels.Length==2,"Level catalog");
        var data=catalog.levels.First(l=>l.kind==TableLevelKind.Dungeon);
        Check(data.backgroundMusic!=null && data.backgroundMusic.name=="24. Mines01","Mines01 assigned");
        Check(data.mood!=null && data.loot!=null,"Mood and loot assigned");
        foreach(var enemy in data.enemies)
        {
            Check(enemy!=null && enemy.GetComponent<EnemyBrain>()!=null,"Shared enemy brain");
            Check(enemy.GetComponent<Character>().data.attacks.Count>0,"Enemy attacks");
        }
        results.Add("PASS: 100 seeds, connected rooms/exits, repeatable layouts, music, mood and enemy assets.");Flush();
    }
    static void Check(bool condition,string message) { if(!condition)throw new Exception("Table validation failed: "+message); }
    static void Flush() { File.WriteAllLines(Report,results);Debug.Log(string.Join("\n",results)); }
    static void Tick()
    {
        try
        {
            if(File.Exists(Command))
            {
                string cmd;
                try { cmd=File.ReadAllText(Command).Trim();File.Delete(Command); }
                catch(IOException) { return; }
                if(cmd=="validate")Validate();
                if(cmd=="build")TableLevelAssetBuilder.Build();
                if(cmd=="play") { SessionState.SetBool("TableSmoke",true);EditorApplication.isPlaying=true; }
                if(cmd=="stop") { phase=0;feedbackPhase=0;SessionState.SetBool("TableSmoke",false);EditorApplication.isPlaying=false; }
                if(cmd=="status")Status();
                if(cmd=="capture")Capture();
                if(cmd=="preview")PreviewMite();
                if(cmd=="feedback-assets")TableLevelAssetBuilder.UpdateFeedbackAssets();
                if(cmd=="feedback-test") { feedbackPhase=1;feedbackNext=0; }
                if(cmd=="town" && Application.isPlaying) Loader().Load(Loader().catalog.levels.First(l=>l.kind==TableLevelKind.Town));
                if(cmd=="dungeon" && Application.isPlaying) Loader().Load(Loader().catalog.levels.First(l=>l.kind==TableLevelKind.Dungeon));
            }
            if(!EditorApplication.isPlaying || EditorApplication.isPaused)return;
            if(feedbackPhase>0)CheckFeedback();
            if(phase==0 && SessionState.GetBool("TableSmoke",false)) { SessionState.SetBool("TableSmoke",false);phase=1;next=EditorApplication.timeSinceStartup+3; }
            if(phase==0 || EditorApplication.timeSinceStartup<next)return;
            var loader=Loader();if(loader==null)return;
            var player=PlayerManager.Instance.GetPlayer(PlayerKind.Table);
            if(phase==1)
            {
                results.Clear();
                foreach(var cutscene in UnityEngine.Object.FindObjectsByType<CutsceneController>(FindObjectsInactive.Include)) { cutscene.StopAllCoroutines();cutscene.enabled=false; }
                var wake=UnityEngine.Object.FindAnyObjectByType<WakeUpCutsceneController>();if(wake!=null){wake.StopAllCoroutines();wake.enabled=false;}
                GameManager.Instance.PopAll();
                loader.Load(loader.catalog.levels.First(l=>l.kind==TableLevelKind.Dungeon));phase=2;next=EditorApplication.timeSinceStartup+8;
            }
            else if(phase==2 && !loader.Busy)
            {
                Check(loader.Dungeon!=null,"Dungeon generated");Check(!loader.townRoot.activeSelf,"Town hidden");
                Check(player.IsActive && player.IsAlive,"Player active and alive");
                Check(player.Equipment.Get(EquipmentSlot.RightHand)!=null && player.Equipment.Get(EquipmentSlot.LeftHand)!=null,"Starting weapon and shield");
                var navPath=new NavMeshPath();Check(NavMesh.CalculatePath(loader.Dungeon.SpawnPoint,loader.Dungeon.ExitPoint,NavMesh.AllAreas,navPath) && navPath.status==NavMeshPathStatus.PathComplete,"Runtime navigation to exit");
                var music=AudioManager.Instance.GetComponentsInChildren<AudioSource>().FirstOrDefault(s=>s.isPlaying && s.clip==loader.Current.backgroundMusic);
                Check(music!=null && music.outputAudioMixerGroup!=null && music.outputAudioMixerGroup.name=="Music","Music mixer routing");
                var enemy=loader.Dungeon.GetComponentsInChildren<DungeonLootDrop>().First(d=>d.carriedItem!=null).GetComponent<Character>();int before=loader.Dungeon.GetComponentsInChildren<WorldItem>().Length;
                foreach(var agent in loader.Dungeon.GetComponentsInChildren<NavMeshAgent>())Check(agent.isOnNavMesh,"Enemy has navigation");
                enemy.Stats.TakeFlatDamage(999);Check(!enemy.IsAlive,"Enemy death");Check(loader.Dungeon.GetComponentsInChildren<WorldItem>().Length>before,"Enemy loot drop");
                var potion=loader.Current.guaranteedHealing;player.Stats.TakeFlatDamage(30);float health=player.Stats.CurrentHealth;potion.Use(player);Check(player.Stats.CurrentHealth>health,"Healing item effect");
                var barrel=loader.Dungeon.GetComponentsInChildren<DungeonContainer>().First(c=>!c.chest);
                before=loader.Dungeon.GetComponentsInChildren<WorldItem>().Length;barrel.TakeDamage(DamageInfo.Simple(999,player));
                Check(loader.Dungeon.GetComponentsInChildren<WorldItem>().Length>before,"Breakable drops supplies");
                var chest=loader.Dungeon.GetComponentsInChildren<DungeonContainer>().First(c=>c.chest);
                before=loader.Dungeon.GetComponentsInChildren<WorldItem>().Length;chest.Interact(player);chest.Interact(player);
                Check(loader.Dungeon.GetComponentsInChildren<WorldItem>().Length==before+2,"Chest opens only once");
                var pickup=loader.Dungeon.GetComponentsInChildren<WorldItem>().First(w=>w.Item==potion);
                before=player.Bag.TotalCount(potion)+player.Hotbar.TotalCount(potion);pickup.Interact(player);
                Check(player.Bag.TotalCount(potion)+player.Hotbar.TotalCount(potion)==before+1,"World pickup enters inventory");
                int slot=player.Hotbar.IndexOf(potion);before=player.Hotbar.CountAt(slot);player.Stats.TakeFlatDamage(20);
                InventoryManager.Instance.Use(player.Hotbar,slot);Check(player.Hotbar.CountAt(slot)==before-1,"Using healing consumes one item");
                var testObject=new GameObject("Inventory capacity test");var inv=testObject.AddComponent<Inventory>();
                for(int n=0;n<inv.SlotCount;n++)inv.Set(n,new ItemStack(potion,potion.maxStackSize));
                inv.Set(0,new ItemStack(potion,potion.maxStackSize-1));before=inv.TotalCount(potion);
                Check(!inv.TryAdd(potion,2) && inv.TotalCount(potion)==before,"Full inventory pickup is atomic");UnityEngine.Object.Destroy(testObject);
                results.Add("PASS: dungeon load, spawn, starter equipment, exit navigation, Music mixer, enemy death/drop and healing.");
                oldSeed=loader.Dungeon.Layout.seed;loader.Load(loader.catalog.levels.First(l=>l.kind==TableLevelKind.Town));phase=3;next=EditorApplication.timeSinceStartup+5;
            }
            else if(phase==3 && !loader.Busy)
            {
                Check(loader.townRoot.activeSelf && loader.Dungeon==null,"Town restored");Check(player.IsAlive,"Town player alive");results.Add("PASS: Town loads without BPM intro; dungeon removed.");
                loader.Load(loader.catalog.levels.First(l=>l.kind==TableLevelKind.Dungeon));phase=4;next=EditorApplication.timeSinceStartup+6;
            }
            else if(phase==4 && !loader.Busy)
            {
                Check(loader.Dungeon.Layout.seed!=oldSeed,"Fresh dungeon seed");
                player.Stats.TakeFlatDamage(999);phase=5;next=EditorApplication.timeSinceStartup+2;
            }
            else if(phase==5)
            {
                Check(UnityEngine.Object.FindAnyObjectByType<TableLevelMenu>().IsOpen,"Death retry menu");
                loader.Load(loader.Current);phase=6;next=EditorApplication.timeSinceStartup+6;
            }
            else if(phase==6 && !loader.Busy)
            {
                Check(player.IsAlive && GameManager.Instance.GameplayActive,"Retry restores control and health");results.Add("PASS: new seed, death menu and retry.");
                var mitePrefab=loader.Current.enemies.First(e=>e.GetComponent<CryptMiteAnimation>()!=null);
                var position=player.transform.position+Vector3.forward*1.25f;
                Check(NavMesh.SamplePosition(position,out var hit,2,NavMesh.AllAreas),"Mite test spawn");
                UnityEngine.Object.Instantiate(mitePrefab,hit.position,Quaternion.Euler(0,180,0),loader.Dungeon.transform);
                combatHealth=player.Stats.CurrentHealth;phase=7;next=EditorApplication.timeSinceStartup+5;
            }
            else if(phase==7)
            {
                Check(player.Stats.CurrentHealth<combatHealth,"Mite perceives, approaches and lands attacks through shared AI");
                results.Add("PASS: all enemy navigation, breakables, one-use chests, pickup, consumable depletion, atomic inventory and live Mite combat.");
                player.Stats.Revive();Flush();phase=0;Capture();
            }
        }
        catch(Exception e){results.Add("FAIL: "+e);Flush();phase=0;feedbackPhase=0;SessionState.SetBool("TableSmoke",false);}
    }
    static TableLevelLoader Loader()=>UnityEngine.Object.FindAnyObjectByType<TableLevelLoader>();
    static void CheckFeedback()
    {
        if(EditorApplication.timeSinceStartup<feedbackNext)return;
        var loader=Loader();if(loader==null || loader.Busy || loader.Dungeon==null)return;
        var feedback=loader.Dungeon.GetComponent<DungeonCombatFeedback>();var player=PlayerManager.Instance.Active;
        var camera=PlayerManager.Instance.OutputCamera;
        if(feedbackPhase==1)
        {
            Check(feedback!=null && loader.Current.hudFont!=null && loader.Current.hotbarFrame!=null,"Pixel HUD assets assigned");
            var prefab=loader.Current.enemies.First(e=>e.GetComponent<CryptMiteAnimation>()!=null);
            var forward=camera.transform.forward;forward.y=0;forward.Normalize();
            var point=player.transform.position+forward*2.5f;
            Check(NavMesh.SamplePosition(point,out var hit,2,NavMesh.AllAreas),"Feedback test placement");
            feedbackEnemy=UnityEngine.Object.Instantiate(prefab,hit.position,Quaternion.identity,loader.Dungeon.transform).GetComponent<Character>();
            feedbackEnemy.GetComponent<EnemyBrain>().enabled=false;
            var agent=feedbackEnemy.GetComponent<NavMeshAgent>();agent.isStopped=true;
            feedbackPhase=2;feedbackNext=EditorApplication.timeSinceStartup+.25;
        }
        else if(feedbackPhase==2)
        {
            feedbackEnemy.Stats.TakeDamage(DamageInfo.Simple(5,player));feedbackPhase=3;feedbackNext=EditorApplication.timeSinceStartup+.12;
        }
        else if(feedbackPhase==3)
        {
            Check(feedback.VisibleBars>0,"Overhead health bar appears after damage");Check(feedback.ActiveNumbers>0,"Floating damage number");
            Check(feedback.HasSight(camera,feedbackEnemy,feedbackEnemy.transform.position+Vector3.up*.6f),"Unobstructed creature visibility");
            sightBlocker=GameObject.CreatePrimitive(PrimitiveType.Cube);sightBlocker.name="Temporary health bar occlusion test";
            sightBlocker.transform.position=Vector3.Lerp(camera.transform.position,feedbackEnemy.transform.position+Vector3.up*.6f,.5f);sightBlocker.transform.localScale=new Vector3(2,3,.5f);
            Physics.SyncTransforms();
            Check(!feedback.HasSight(camera,feedbackEnemy,feedbackEnemy.transform.position+Vector3.up*.6f),"Wall hides enemy feedback");
            UnityEngine.Object.Destroy(sightBlocker);feedbackPhase=4;feedbackNext=EditorApplication.timeSinceStartup+.3;
        }
        else if(feedbackPhase==4)
        {
            feedbackEnemy.Stats.TakeDamage(DamageInfo.Simple(2,player));
            ScreenCapture.CaptureScreenshot("Library/Dungeon-feedback.png",2);
            File.WriteAllText("Library/DungeonFeedbackReport.txt","PASS: pixel assets, overhead health bar, floating damage number, unobstructed visibility, wall occlusion.");
            feedbackPhase=0;
        }
    }
    static void Status()
    {
        var l=Loader();var p=PlayerManager.HasInstance ? PlayerManager.Instance.Active:null;
        var agents=UnityEngine.Object.FindObjectsByType<NavMeshAgent>().Select(a=>$"{a.name}: nav={a.isOnNavMesh}, enabled={a.enabled}, pos={a.transform.position}");
        File.WriteAllText("Library/TableLevelStatus.txt",$"playing={Application.isPlaying}, phase={phase}, level={l?.Current?.name}, busy={l?.Busy}, player={p?.name}, position={p?.transform.position}, health={p?.Stats?.CurrentHealth}, state={(GameManager.HasInstance?GameManager.Instance.State.ToString():"none")}\n"+string.Join("\n",agents));
    }
    static void Capture()
    {
        if(!Application.isPlaying)return;
        ScreenCapture.CaptureScreenshot("Library/Dungeon1-play.png",2);
    }
    [MenuItem("Tools/Table Levels/Render Crypt Mite preview")]
    static void PreviewMite()
    {
        var preview=new PreviewRenderUtility();
        try
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Levels/Dungeon1/Enemies/Crypt Mite.prefab");
            var model=UnityEngine.Object.Instantiate(prefab);preview.AddSingleGO(model);
            preview.camera.transform.position=new Vector3(1.9f,1.4f,2.5f);preview.camera.transform.LookAt(new Vector3(0,.5f,0));
            preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=20;preview.camera.fieldOfView=32;
            preview.camera.clearFlags=CameraClearFlags.Color;preview.camera.backgroundColor=new Color(.055f,.075f,.09f);
            preview.lights[0].intensity=1.8f;preview.lights[0].transform.rotation=Quaternion.Euler(35,-30,0);
            preview.lights[1].intensity=1.1f;preview.ambientColor=new Color(.5f,.5f,.5f);
            preview.BeginStaticPreview(new Rect(0,0,768,768));preview.Render(true);
            var image=preview.EndStaticPreview();File.WriteAllBytes("Library/CryptMite-preview.png",image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
        }
        finally { preview.Cleanup(); }
    }
}
