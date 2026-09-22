using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class DungeonInventoryValidation
{
    const string Report="Temp/DungeonInventoryValidation.txt";
    static readonly List<string> log=new();
    static int phase;
    static double next;
    static TableLevelData level;
    static InventoryUI ui;
    static ItemData apple;
    static float health,mana;
    static Vector2 leftHome,rightHome;
    static DungeonContainer chest;
    static int drops;
    static DungeonInventoryValidation()=>EditorApplication.update+=Tick;
    [MenuItem("Tools/Table Levels/Validate inventory and balance (enters Play mode)")]
    static void Request()=>File.WriteAllText("Temp/DungeonInventoryValidation.request","run");
    static void Check(bool condition,string detail){if(!condition)throw new Exception(detail);}
    static ItemData Item(string name)=>AssetDatabase.FindAssets(name+" t:ItemData",new[]{"Assets/Game/Items/Data"}).Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<ItemData>).First(i=>i.itemName==name);
    static int Count(Player p,ItemData item)=>p.Bag.TotalCount(item)+p.Hotbar.TotalCount(item);
    static void Tick()
    {
        try
        {
            if(File.Exists("Temp/DungeonInventoryValidation.request") && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                File.Delete("Temp/DungeonInventoryValidation.request");
                log.Clear();Assets();SessionState.SetBool("DungeonInventoryValidation",true);EditorApplication.isPlaying=true;
            }
            if(!EditorApplication.isPlaying)return;
            if(phase==0 && SessionState.GetBool("DungeonInventoryValidation",false)){SessionState.SetBool("DungeonInventoryValidation",false);phase=1;next=EditorApplication.timeSinceStartup+3;}
            if(phase==0 || EditorApplication.timeSinceStartup<next)return;
            var loader=Object.FindAnyObjectByType<TableLevelLoader>();if(loader==null)return;
            var player=PlayerManager.Instance.GetPlayer(PlayerKind.Table);
            if(phase==1)
            {
                foreach(var c in Object.FindObjectsByType<CutsceneController>()){c.Stop();c.StopAllCoroutines();}
                GameManager.Instance.PopAll();
                level=Object.Instantiate(AssetDatabase.LoadAssetAtPath<TableLevelData>("Assets/Game/Levels/Dungeon1/Dungeon1.asset"));level.fixedSeed=71;level.multipleLevels=true;level.levelCount=3;
                loader.Load(level);phase=2;next=EditorApplication.timeSinceStartup+8;
            }
            else if(phase==2 && !loader.Busy)
            {
                Check(loader.Dungeon!=null,"Dungeon failed to generate");
                foreach(var brain in loader.Dungeon.GetComponentsInChildren<EnemyBrain>())brain.enabled=false;
                Check(player.Stats.MaxHealth==30 && player.Stats.MaxMana==20,"Player baseline not applied");
                Check(player.Stats.GetFinal(StatType.AttackDamage)==10 && player.Stats.GetFinal(StatType.Defense)==2,"Starter sword/shield stats");
                Check(player.Equipment.Get(EquipmentSlot.RightHand)==Item("Bronze Sword") && player.Equipment.Get(EquipmentSlot.LeftHand)==Item("Bronze Shield"),"Starter hands");
                Check(player.Hotbar.IndexOf(Item("Bronze Sword"))>=0 && player.Hotbar.IndexOf(Item("Bronze Shield"))>=0,"Starter hotbar");
                var noMesh=ScriptableObject.CreateInstance<ItemData>();noMesh.itemName="Fallback probe";
                var fallback=InventoryManager.Instance.Spawn(noMesh,player.transform.position);
                Check(fallback.GetComponentsInChildren<Transform>().Any(t=>t.name.StartsWith("SM_Item_Pouch_01")),"Meshless pickup did not use Synty pouch fallback");
                Object.Destroy(fallback.gameObject);
                var routed=Object.Instantiate(Item("Pear"));routed.directToHotbar=true;routed.maxStackSize=1;
                Check(InventoryManager.Instance.Pickup(routed,player) && player.Hotbar.IndexOf(routed)>=0,"directToHotbar route");
                while(!player.Hotbar.IsFull)Check(player.Hotbar.TryAdd(routed),"Fill hotbar");
                Check(InventoryManager.Instance.Pickup(routed,player) && player.Bag.IndexOf(routed)>=0,"Full hotbar did not fall back to bag");
                while(player.Hotbar.RemoveOne(routed)){}player.Bag.RemoveOne(routed);
                Check(InventoryManager.Instance.Pickup(Item("Pear"),player) && player.Hotbar.IndexOf(Item("Pear"))<0,"Default pickup unexpectedly went to hotbar");
                foreach(string tier in new[]{"Bronze","Iron"})
                {
                    foreach(string part in new[]{"Helm","Armor","Gloves","Boots","Sword","Shield"})
                    {var item=Item(tier+" "+part);if(Count(player,item)==0)Check(player.Bag.TryAdd(item),"Bag rejected "+item.itemName);Check(player.Equipment.Equip(item),"Equip rejected "+item.itemName);Check(player.Equipment.Equip(item),"Repeat equip rejected");}
                    Check(player.Stats.GetFinal(StatType.Defense)==(tier=="Bronze"?7:12),"Armor stacked or missing");
                    Check(player.Stats.GetFinal(StatType.AttackDamage)==(tier=="Bronze"?10:11),"Sword bonus stacked");
                }
                Balance(player,level);
                var sword=Item("Iron Sword");int owned=Count(player,sword);
                Check(Inventory.Move(player.Hotbar,player.Hotbar.IndexOf(sword),player.Bag,player.Bag.FirstEmpty()),"Move hand item into bag");
                Check(Count(player,sword)==owned,"Hand transfer duplicated item");
                ui=Object.FindAnyObjectByType<InventoryUI>();
                Check(ui.inventoryPanel!=null && ui.statsPanel!=null && ui.equipmentPanel.slots.Length==6,"Scene panel wiring");
                leftHome=ui.inventoryPanel.anchoredPosition;rightHome=ui.statsPanel.anchoredPosition;
                ui.Toggle();phase=3;next=EditorApplication.timeSinceStartup+1;
            }
            else if(phase==3)
            {
                Check(player.Equipment.Get(EquipmentSlot.RightHand)==null,"Hand stayed equipped outside hotbar");
                Check(ui.IsOpen && GameManager.Instance.SimulationActive && !GameManager.Instance.GameplayActive && Time.timeScale==1,"Live inventory state");
                Check(Vector2.Distance(ui.inventoryPanel.anchoredPosition,leftHome)<.1f && Vector2.Distance(ui.statsPanel.anchoredPosition,rightHome)<.1f,"Panels failed to slide home");
                ui.Close();ui.Toggle();ui.Close();
                apple=Item("Apple");Check(player.Bag.TryAdd(apple),"Food pickup");
                player.Equipment.UnequipAll();player.Stats.Revive();player.Stats.TakeFlatDamage(15);health=player.Stats.CurrentHealth;mana=player.Stats.CurrentMana;
                int count=Count(player,apple);InventoryManager.Instance.Use(player.Bag,player.Bag.IndexOf(apple));
                Check(Count(player,apple)==count && player.Equipment.Get(EquipmentSlot.RightHand)==apple,"Inventory should equip edible before use");
                phase=4;next=EditorApplication.timeSinceStartup+1;
            }
            else if(phase==4)
            {
                Check(!ui.IsOpen && GameManager.Instance.GameplayActive,"Rapid toggle left player locked");
                int count=Count(player,apple);Check(InventoryManager.Instance.UseHeld(player),"Held use rejected");
                Check(player.Equipment.IsUsingItem && Count(player,apple)==count,"Eat animation consumed item too soon");
                Check(player.Equipment.GetAnchor(EquipmentSlot.RightHand).childCount>0,"Hand lost held model");
                phase=5;next=EditorApplication.timeSinceStartup+2;
            }
            else if(phase==5)
            {
                Check(!player.Equipment.IsUsingItem && player.Stats.FoodRemaining>0,"Food failed to finish use");
                Check(player.Stats.CurrentHealth>health && player.Stats.CurrentHealth<health+6,"Food should heal over time");
                Check(player.Stats.CurrentMana==mana,"Physical use spent mana");
                Item("Pear").Use(player);Check(Mathf.Abs(player.Stats.FoodHealingPerSecond-.5f)<.01f && player.Stats.FoodRemaining>15,"Food stacked instead of replacing");
                var instant=Object.Instantiate(apple);instant.canBeEquipped=false;instant.effects=new[]{new EffectEntry{type=EffectType.Heal,value=2}};
                Check(player.Bag.TryAdd(instant),"Instant-use test inventory");health=player.Stats.CurrentHealth;
                InventoryManager.Instance.Use(player.Bag,player.Bag.IndexOf(instant));
                Check(Count(player,instant)==0 && player.Stats.CurrentHealth>health && !player.Equipment.IsUsingItem,"Unequippable consumable did not use instantly");
                chest=loader.Dungeon.GetComponentsInChildren<DungeonContainer>().First(c=>c.chest);
                Check(chest.lid!=null && chest.GetComponentsInChildren<Transform>().Any(t=>t.name=="SM_Prop_Chest_01"),"Synty chest missing");
                chest.Interact(player);drops=loader.Dungeon.GetComponentsInChildren<WorldItem>().Length;chest.Interact(player);
                Check(loader.Dungeon.GetComponentsInChildren<WorldItem>().Length==drops,"Chest rewarded twice");
                phase=6;next=EditorApplication.timeSinceStartup+1;
            }
            else if(phase==6)
            {
                Check(chest!=null && !chest.CanInteract(player),"Opened chest disappeared or can reward again");
                Check(Quaternion.Angle(chest.lid.localRotation,Quaternion.Euler(chest.lidOpenRotation))<1,"Chest lid did not open");
                log.Add("PASS: all equipment slots, repeated equip, hotbar ownership, live sliding panels and rapid toggles.");
                log.Add("PASS: meshless pickups use the Synty pouch; directToHotbar routes to available hotbar space and falls back to bag when full.");
                log.Add("PASS: hand-only eating keeps model until consumed, timed food healing replaces previous food, direct consumables apply instantly; mana unchanged.");
                log.Add("PASS: Synty chest persists with open lid and produces loot once.");
                player.Stats.Revive();Check(player.Stats.FoodRemaining==0,"Food survived run reset");
                loader.Descend();phase=7;next=EditorApplication.timeSinceStartup+6;
            }
            else if(phase==7 && !loader.Busy)
            {
                Check(loader.FloorNumber==2,"Descent failed");
                foreach(var brain in loader.Dungeon.GetComponentsInChildren<EnemyBrain>())
                {
                    brain.enabled=false;var c=brain.Character;
                    Check(Mathf.Abs(c.Stats.MaxHealth-c.data.GetBaseStat(StatType.MaxHealth)*1.12f)<.01f,"Floor health scaling");
                    Check(Mathf.Abs(c.Stats.GetFinal(StatType.AttackDamage)-c.data.GetBaseStat(StatType.AttackDamage)-2)<.01f,"Floor damage scaling");
                }
                log.Add("PASS: new floor retains equipment/inventory and applies +12% health / +2 damage scaling.");
                foreach(var item in new[]{Item("Pear"),Item("Roasted mushroom"),Item("Cooked meat")})if(Count(player,item)==0)player.Bag.TryAdd(item);
                player.Equipment.Equip(Item("Iron Sword"));player.Equipment.Equip(Item("Iron Shield"));
                foreach(string part in new[]{"Helm","Armor","Gloves","Boots"})player.Equipment.Equip(Item("Iron "+part));
                ui.Toggle();phase=8;next=EditorApplication.timeSinceStartup+1;
            }
            else if(phase==8)
            {
                ScreenCapture.CaptureScreenshot("Temp/DungeonInventory.png");
                File.WriteAllLines(Report,log);phase=9;next=EditorApplication.timeSinceStartup+2;
            }
            else if(phase==9){TooltipUI.Instance.Show(Item("Roasted mushroom"));phase=10;next=EditorApplication.timeSinceStartup+1;}
            else if(phase==10){ScreenCapture.CaptureScreenshot("Temp/DungeonFoodTooltip.png");phase=11;next=EditorApplication.timeSinceStartup+1;}
            else if(phase==11){phase=0;EditorApplication.isPlaying=false;}
        }
        catch(Exception ex){log.Add("FAIL: "+ex);File.WriteAllLines(Report,log);phase=0;SessionState.SetBool("DungeonInventoryValidation",false);EditorApplication.isPlaying=false;}
    }
    static void Assets()
    {
        foreach(string tier in new[]{"Bronze","Iron"})foreach(string part in new[]{"Helm","Armor","Gloves","Boots","Sword","Shield"})
        {var i=Item(tier+" "+part);Check(i.icon!=null && i.worldPrefab!=null && i.canBeEquipped && !i.directToHotbar,"Invalid gear: "+i.itemName);}
        foreach(string name in new[]{"Apple","Pear","Roasted mushroom","Cooked meat"})
        {var i=Item(name);Check(i.icon!=null && i.worldPrefab!=null && i.effects.Any(e=>e.type==EffectType.FoodRegen) && i.BuildTooltip().Contains("total"),"Invalid food: "+name);}
        var style=AssetDatabase.LoadAssetAtPath<UIFeedbackSettings>("Assets/Game/UI/Resources/UIFeedback.asset");
        Check(style!=null && style.open?.clips[0]!=null && style.close?.clips[0]!=null && style.equip?.clips[0]!=null,"UI audio wiring");
        var l=AssetDatabase.LoadAssetAtPath<TableLevelData>("Assets/Game/Levels/Dungeon1/Dungeon1.asset");
        var rng=new System.Random(3241);int empty=0;double totalHealing=0;
        for(int i=0;i<10000;i++)
        {
            var drops=l.enemyLoot.RollDrops(rng,1,DungeonLootSource.Enemy);if(drops.Count==0)empty++;
            foreach(var d in drops)
            {
                Check(!d.item.itemName.StartsWith("Iron"),"Iron gear leaked into floor 1");
                if(d.item.effects!=null)foreach(var effect in d.item.effects)totalHealing+=d.quantity*(effect.type==EffectType.Heal?effect.value:effect.type==EffectType.FoodRegen?effect.value*effect.duration:0);
            }
        }
        Check(empty>6100 && empty<6900,"Enemy rewards too common/rare");
        log.Add($"PASS: 12 gear items, 4 food assets, mesh/icon/audio references. 10,000 enemy rolls: {empty/100f:0.0}% empty; mean potential healing {totalHealing/10000:0.00} HP per kill. Iron gated to floor 2+.");
    }
    static void Balance(Player p,TableLevelData data)
    {
        // Exercise real damage resolution against equipped armor, not a separate formula copy.
        foreach(int floor in new[]{1,2,3})foreach(string tier in new[]{"Bronze","Iron"})
        {
            foreach(string part in new[]{"Helm","Armor","Gloves","Boots","Sword","Shield"})Check(p.Equipment.Equip(Item(tier+" "+part)),"Balance equip");
            foreach(string name in new[]{"Crypt Mite","Crypt Soldier","Crypt Warden"})
            {
                var enemy=AssetDatabase.LoadAssetAtPath<CharacterData>("Assets/Game/Levels/Dungeon1/Enemies/"+name+".asset");
                float incoming=enemy.GetBaseStat(StatType.AttackDamage)+data.balance.damagePerFloor*(floor-1);
                float expected=Mathf.Max(0,incoming-p.Stats.GetFinal(StatType.Defense));
                p.Stats.Revive();p.Stats.TakeDamage(DamageInfo.Simple(incoming));float actual=p.Stats.MaxHealth-p.Stats.CurrentHealth;
                Check(Mathf.Abs(actual-expected)<.01f,"Damage-Armor mismatch");
                Check(name=="Crypt Mite" || actual>0,"Armor trivialized soldier/warden");
                log.Add($"Floor {floor}, {tier} set vs {name}: {actual:0.##} damage received, {(actual>0?Mathf.CeilToInt(30/actual).ToString():"immune")} hits to defeat player.");
            }
        }
        p.Stats.Revive();
    }
}
