using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class DungeonReviewValidation
{
    static int phase;
    static double next;
    static TableLevelData testLevel;
    static float health;
    static int seed, itemCount;
    static readonly List<string> results=new();
    static DungeonReviewValidation()=>EditorApplication.update+=Tick;
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static int Count(Player p)=>Enumerable.Range(0,p.Bag.SlotCount).Sum(i=>p.Bag.CountAt(i))+Enumerable.Range(0,p.Hotbar.SlotCount).Sum(i=>p.Hotbar.CountAt(i));
    [MenuItem("Tools/Table Levels/Validate dungeon system (enters Play mode)")]
    static void Request()=>File.WriteAllText("Temp/DungeonReview.request","run");
    static void Tick(){
        try {
            if(File.Exists("Temp/DungeonReview.request")){
                File.Delete("Temp/DungeonReview.request");
                results.Clear();ValidateLayouts();ValidateLoot();SessionState.SetBool("DungeonReview",true);EditorApplication.isPlaying=true;
            }
            if(!EditorApplication.isPlaying)return;
            if(phase==0 && SessionState.GetBool("DungeonReview",false)){
                SessionState.SetBool("DungeonReview",false);phase=1;next=EditorApplication.timeSinceStartup+3;
            }
            if(phase==0 || EditorApplication.timeSinceStartup<next)return;
            var loader=UnityEngine.Object.FindAnyObjectByType<TableLevelLoader>();
            if(loader==null)return;
            var player=PlayerManager.Instance.GetPlayer(PlayerKind.Table);
            if(phase==1){
                foreach(var cutscene in UnityEngine.Object.FindObjectsByType<CutsceneController>()){cutscene.Stop();cutscene.StopAllCoroutines();}
                GameManager.Instance.PopAll();
                testLevel=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<TableLevelData>("Assets/Game/Levels/Dungeon1/Dungeon1.asset"));
                testLevel.multipleLevels=true;testLevel.levelCount=2;testLevel.fixedSeed=71;
                testLevel.floorSettings=new[]{new DungeonFloorSettings{moodLightning=DungeonMoodLighting.MoonlitStone},new DungeonFloorSettings{moodLightning=DungeonMoodLighting.EmeraldRuins}};
                loader.Load(testLevel);phase=2;next=EditorApplication.timeSinceStartup+8;
            }else if(phase==2 && !loader.Busy){
                Check(loader.Dungeon!=null,"Dungeon failed to load");
                foreach(var brain in loader.Dungeon.GetComponentsInChildren<EnemyBrain>())brain.enabled=false;
                var audio=AudioManager.Instance;
                var uiStyle=UIFeedbackSettings.Shared;
                uiStyle.Play(uiStyle.openKey);
                var uiSound=audio.GetComponentsInChildren<AudioSource>().FirstOrDefault(s=>s.isPlaying && s.outputAudioMixerGroup!=null && s.outputAudioMixerGroup.name=="UI");
                Check(uiSound!=null && uiSound.spatialBlend==0 && uiSound.volume>=.7f,"UI library playback is missing or too quiet");
                audio.TryGetMixerVolume(AudioManager.P_SFX,out var sfxVolume);audio.TryGetMixerVolume(AudioManager.P_UI,out var uiVolume);
                results.Add($"PASS: UI library routed to UI mixer at source gain {uiSound.volume:F2}; mixer SFX={sfxVolume:F2}, UI={uiVolume:F2}.");
                UnityEngine.ScreenCapture.CaptureScreenshot("Temp/Dungeon-system-play.png");
                var chest=loader.Dungeon.GetComponentsInChildren<DungeonContainer>().First(c=>c.chest);
                int WorldCount()=>loader.Dungeon.GetComponentsInChildren<WorldItem>().Sum(w=>w.Count);
                int before=WorldCount();
                int expectedDrops=(chest.guaranteedItem!=null?1:0)+(chest.loot!=null?chest.loot.RollDrops(new System.Random(chest.seed),chest.floorNumber,DungeonLootSource.Chest).Sum(d=>d.quantity):0);
                chest.Interact(player);chest.Interact(player);
                var chestSound=audio.GetComponentsInChildren<AudioSource>().FirstOrDefault(s=>s.isPlaying && s.clip==chest.openAudio);
                Check(chestSound!=null && chestSound.volume>=.9f && chestSound.minDistance>=3.5f,"Chest audio gain/range not applied");
                Check(WorldCount()-before==expectedDrops,"Chest reward quantities or duplicate protection failed");
                results.Add("PASS: opening a chest spawns the rolled quantities exactly once.");
                var doors=loader.Dungeon.GetComponentsInChildren<DungeonDoor>();Check(doors.Length>0,"No doors generated");
                foreach(var door in doors)
                {
                    Check(door.lintel!=null,"Door has no masonry lintel");
                    var bounds=door.lintel.GetComponent<Renderer>().bounds;
                    float scale=door.transform.lossyScale.y;
                    Check(bounds.max.y>=door.transform.position.y+3.2f*scale-.001f,"Door lintel does not reach ceiling");
                    Check(bounds.min.y<=door.transform.position.y+door.openingHeight*scale,"Gap between door and lintel");
                    Check(door.lintel.GetComponent<Collider>().enabled,"Lintel has no solid collider");
                    door.Interact(player);
                }
                results.Add("PASS: every doorway has solid masonry from the gate top to the ceiling.");
                foreach(var agent in loader.Dungeon.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>())Check(agent.isOnNavMesh,"Dungeon agent is off navigation: "+agent.name);
                int renderers=loader.Dungeon.GetComponentsInChildren<Renderer>().Count(r=>r.enabled);
                results.Add($"Generation: {loader.Dungeon.GenerationMilliseconds:F0} ms; {renderers} enabled renderers including enemies, doors and containers.");
                Check(loader.FloorNumber==1&&loader.HasNextFloor,"Floor 1 progression");
                Check(player.Equipment.Get(EquipmentSlot.RightHand)!=null && player.Equipment.Get(EquipmentSlot.LeftHand)!=null,"Starter equipment");
                Check(player.Hotbar.IndexOf(player.Equipment.Get(EquipmentSlot.RightHand))>=0,"Starter weapon hotbar");
                player.Stats.TakeFlatDamage(17);health=player.Stats.CurrentHealth;itemCount=Count(player);seed=loader.Dungeon.Layout.seed;
                phase=3;next=EditorApplication.timeSinceStartup+2;
            }else if(phase==3){
                foreach(var door in loader.Dungeon.GetComponentsInChildren<DungeonDoor>())Check(!door.barrier.enabled && !door.obstacle.enabled,"Door blocks after opening");
                results.Add("PASS: generated doors open and clear collision/navigation; starter equipment on hotbar.");
                var stairs=loader.Dungeon.GetComponentsInChildren<DungeonExit>().First(x=>!x.entrance);stairs.Interact(player);
                phase=4;next=EditorApplication.timeSinceStartup+8;
            }else if(phase==4 && !loader.Busy){
                Check(loader.FloorNumber==2 && !loader.HasNextFloor,"Final floor boundary");
                Check(loader.Dungeon.Layout.seed!=seed,"Fresh floor seed");
                Check(Count(player)==itemCount,"Inventory changed on descent");
                Check(Mathf.Abs(player.Stats.CurrentHealth-health)<.01f,"Health reset on descent");
                var expected=testLevel.floorSettings[1].Lighting(testLevel.mood);
                var lamps=loader.Dungeon.GetComponentsInChildren<Light>().Where(l=>l.name=="Amber chamber light").ToArray();
                Check(lamps.Any(l=>l.color==expected.light),"Per-floor chamber lighting");
                results.Add("PASS: descent creates a different floor, preserves health/items, applies floor lighting, stops at configured count.");
                foreach(var brain in loader.Dungeon.GetComponentsInChildren<EnemyBrain>())brain.enabled=false;
                var bad=UnityEngine.Object.Instantiate(testLevel);bad.width=999;
                loader.Load(bad);phase=5;next=EditorApplication.timeSinceStartup+3;
            }else if(phase==5 && !loader.Busy){
                Check(loader.Current==testLevel && loader.Dungeon!=null && loader.Dungeon.gameObject.activeSelf,"Failed load lost previous floor");
                Check(loader.FloorNumber==2 && Count(player)==itemCount,"Failed load changed progression or inventory");
                results.Add("PASS: invalid generation rolls back to the existing playable floor and preserves progression/inventory.");
                File.WriteAllLines("Temp/DungeonReviewPlayReport.txt",results);
                phase=0;EditorApplication.isPlaying=false;
            }
        }catch(Exception e){File.WriteAllText("Temp/DungeonReviewPlayReport.txt","FAIL: "+e);phase=0;SessionState.SetBool("DungeonReview",false);EditorApplication.isPlaying=false;}
    }
    static void ValidateLoot(){
        var table=ScriptableObject.CreateInstance<DungeonLootTable>();
        var a=ScriptableObject.CreateInstance<ItemData>();var b=ScriptableObject.CreateInstance<ItemData>();
        try {
            table.enemyDropChance=1;
            table.guaranteed=new[]{new DungeonLootTable.Entry{item=a,minQuantity=2,maxQuantity=2,minFloor=2}};
            table.pools=new[]{new DungeonLootTable.Pool{minRolls=5,maxRolls=5,uniqueItems=true,entries=new[]{new DungeonLootTable.Entry{item=b,minQuantity=3,maxQuantity=3},new DungeonLootTable.Entry{item=a,weight=0}}}};
            var result=table.RollDrops(new System.Random(7),2,DungeonLootSource.Chest);
            Check(result.Count==2 && result.Single(d=>d.item==a).quantity==2 && result.Single(d=>d.item==b).quantity==3,"Guaranteed, quantity or unique-pool behavior");
            Check(table.RollDrops(new System.Random(7),1,DungeonLootSource.Chest).All(d=>d.item!=a),"Floor restriction");
            table.enemyDropChance=0;Check(table.RollDrops(new System.Random(7),2,DungeonLootSource.Enemy).Count==0,"Enemy chance gate");
            table.pools[0].sources=DungeonLootSource.Enemy;table.guaranteed=Array.Empty<DungeonLootTable.Entry>();
            Check(table.RollDrops(new System.Random(7),2,DungeonLootSource.Barrel).Count==0,"Source restriction");
            table.guaranteed=new[]{new DungeonLootTable.Entry{item=a,minQuantity=99,maxQuantity=99}};
            Check(table.RollDrops(new System.Random(7),2,DungeonLootSource.Chest).Sum(d=>d.quantity)==12,"Reward safety cap");
            table.guaranteed=Array.Empty<DungeonLootTable.Entry>();
            table.enemyDropChance=1;table.pools[0].uniqueItems=false;table.pools[0].minRolls=1;table.pools[0].maxRolls=1;
            table.pools[0].entries=new[]{new DungeonLootTable.Entry{item=a,weight=1},new DungeonLootTable.Entry{item=b,weight=3}};
            var rng=new System.Random(55);var mirror=new System.Random(55);int hits=0;
            for(int i=0;i<10000;i++){var one=table.RollDrops(rng,1,DungeonLootSource.Enemy);var two=table.RollDrops(mirror,1,DungeonLootSource.Enemy);Check(one[0].item==two[0].item,"Non-deterministic loot");if(one[0].item==b)hits++;}
            Check(hits>7200&&hits<7800,"Weighted distribution outside expected range");
            File.WriteAllText("Temp/DungeonLootValidation.txt",$"PASS: guaranteed drops, quantities, unique pools, source/floor restrictions, zero chance, deterministic rolls. 3:1 weights produced {hits}/10000 heavy selections.");
        }finally{UnityEngine.Object.DestroyImmediate(table);UnityEngine.Object.DestroyImmediate(a);UnityEngine.Object.DestroyImmediate(b);}
    }
    static void ValidateLayouts(){
        int minRooms=int.MaxValue,maxDoors=0;float maxHidden=0;
        for(int seed=1;seed<=100;seed++){
            var map=new DungeonLayout(40,54,30,seed);minRooms=Math.Min(minRooms,map.rooms.Count);Check(map.rooms.Count==30,"Requested room count not met");Check(map.Connections.Count>29,"No route loops");
            var doorways=map.SelectDoorways(100);
            Check(map.SelectDoorways(0).Count==0,"Zero door chance placed doors");
            Check(doorways.SequenceEqual(map.SelectDoorways(100)),"Door placement is not deterministic");
            var claimed=new HashSet<Vector2Int>();
            foreach(var door in doorways) {
                var corridor=door.roomCell+door.direction;
                Check(!claimed.Contains(corridor),"Two doors on the same corridor passage");
                var scan=new Queue<Vector2Int>();scan.Enqueue(corridor);claimed.Add(corridor);
                while(scan.Count>0) {var cell=scan.Dequeue();foreach(var d in new[]{Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right}) {
                    var n=cell+d;
                    if(n.x>=0&&n.y>=0&&n.x<40&&n.y<54&&map.floor[n.x,n.y]&&map.RegionIds[n.x,n.y]>=map.rooms.Count&&claimed.Add(n))scan.Enqueue(n);
                }}
            }
            var selected=map.SelectOpenRegions(5,173);int tiles=0,hidden=0;
            var seen=new HashSet<Vector2Int>{map.Start};var queue=new Queue<Vector2Int>();queue.Enqueue(map.Start);
            while(queue.Count>0){var p=queue.Dequeue();foreach(var d in new[]{Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right}){var n=p+d;if(n.x>=0&&n.y>=0&&n.x<40&&n.y<54&&map.floor[n.x,n.y]&&seen.Add(n))queue.Enqueue(n);}}
            for(int x=0;x<40;x++)for(int y=0;y<54;y++)if(map.floor[x,y]){tiles++;Check(map.RegionIds[x,y]>=0,"Unowned floor tile");if(selected[map.RegionIds[x,y]])hidden++;}
            Check(seen.Count==tiles && seen.Contains(map.Exit),"Disconnected floor");
            Check(map.SelectOpenRegions(0,173).All(v=>!v) && map.SelectOpenRegions(100,173).All(v=>v),"Percentage endpoints");
            Check(selected.Count(v=>v)==Mathf.RoundToInt(map.RegionCount*.05f),"Region quota");
            Check(hidden/(float)tiles<.2f,"5% selection hides excessive area");
            maxHidden=Mathf.Max(maxHidden,100f*hidden/tiles);maxDoors=Math.Max(maxDoors,map.CorridorCount);
        }
        File.WriteAllText("Temp/DungeonReviewLayoutReport.txt",$"PASS: 100 seeds have at most one door per corridor passage, deterministic doors and zero-door setting; connected, 0/5/100% region quotas. Minimum rooms {minRooms}; largest hidden area at 5%: {maxHidden:F1}%. Maximum corridor sections {maxDoors}.");
    }
}
