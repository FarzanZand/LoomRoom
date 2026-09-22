using System;
using System.Collections.Generic;
using UnityEngine;

[Flags] public enum DungeonLootSource { Enemy=1, Barrel=2, Chest=4, Floor=8, All=15 }

[CreateAssetMenu(menuName = "Table/Loot Table")]
public class DungeonLootTable : ScriptableObject
{
    [Serializable] public class Entry
    {
        public ItemData item;
        [Min(0)] public float weight = 1;
        [Min(1)] public int minQuantity = 1, maxQuantity = 1;
        [Min(1)] public int minFloor = 1;
        [Tooltip("Zero means no upper floor limit.")] [Min(0)] public int maxFloor;
        public DungeonLootSource sources = DungeonLootSource.All;
        [Tooltip("Added to the weight per floor beyond minFloor.")] public float weightPerFloor;
    }
    [Serializable] public class Pool
    {
        public string label = "Supplies";
        public DungeonLootSource sources = DungeonLootSource.All;
        [Range(0,1)] public float chance = 1;
        [Range(0,20)] public int minRolls = 1, maxRolls = 1;
        [Tooltip("Each item can be selected only once within this pool per reward.")] public bool uniqueItems;
        public Entry[] entries = Array.Empty<Entry>();
    }
    public readonly struct Drop
    {
        public readonly ItemData item;
        public readonly int quantity;
        public Drop(ItemData item,int quantity) { this.item=item;this.quantity=quantity; }
    }
    [Tooltip("Legacy single-pick pool. Used only when no reward pools are configured.")]
    public Entry[] entries;
    [Tooltip("Overall chance of a table reward when an enemy dies. Carried items always drop.")]
    [Range(0, 1)] public float enemyDropChance = .7f;
    [Tooltip("Always awarded after the enemy drop-chance gate, subject to floor/source restrictions.")]
    public Entry[] guaranteed = Array.Empty<Entry>();
    public Pool[] pools = Array.Empty<Pool>();
    [Range(1,64), Tooltip("Maximum total items in one reward. Guaranteed entries receive priority; prevents accidental loot floods.")] public int maxItemsPerReward=12;

    public List<Drop> RollDrops(System.Random random, int floor, DungeonLootSource source)
    {
        if(random==null)throw new ArgumentNullException(nameof(random));
        floor=Math.Max(1,floor);
        var result=new List<Drop>();
        if(source==DungeonLootSource.Enemy && random.NextDouble()>=Mathf.Clamp01(enemyDropChance))return result;
        if(guaranteed!=null)foreach(var entry in guaranteed)if(Eligible(entry,floor,source))Add(result,entry,random);
        if(pools==null || pools.Length==0) {
            var selected=Select(entries,random,floor,source,null);
            if(selected!=null)Add(result,selected,random);
        } else foreach(var pool in pools) {
            if(pool==null || (pool.sources&source)==0 || random.NextDouble()>=Mathf.Clamp01(pool.chance))continue;
            int min=Mathf.Clamp(pool.minRolls,0,20),max=Mathf.Clamp(pool.maxRolls,min,20);
            int rolls=random.Next(min,max+1);
            var used=pool.uniqueItems ? new HashSet<ItemData>() : null;
            for(int i=0;i<rolls;i++) {
                var selected=Select(pool.entries,random,floor,source,used);
                if(selected==null)break;
                Add(result,selected,random);used?.Add(selected.item);
            }
        }
        int budget=Mathf.Clamp(maxItemsPerReward,1,64);
        for(int i=0;i<result.Count;i++) {
            int quantity=Math.Min(result[i].quantity,budget);
            if(quantity<=0){result.RemoveRange(i,result.Count-i);break;}
            result[i]=new Drop(result[i].item,quantity);budget-=quantity;
        }
        return result;
    }
    static bool Eligible(Entry e,int floor,DungeonLootSource source)=>e!=null && e.item!=null && floor>=Math.Max(1,e.minFloor) && (e.maxFloor<=0 || floor<=e.maxFloor) && (e.sources&source)!=0;
    static double Weight(Entry e,int floor) {
        double value=e.weight+(double)e.weightPerFloor*(floor-Math.Max(1,e.minFloor));
        return double.IsNaN(value)||double.IsInfinity(value) ? 0 : Math.Max(0,value);
    }
    static Entry Select(Entry[] choices,System.Random random,int floor,DungeonLootSource source,HashSet<ItemData> used) {
        if(choices==null)return null;
        double total=0;
        foreach(var e in choices)if(Eligible(e,floor,source) && (used==null||!used.Contains(e.item)))total+=Weight(e,floor);
        if(total<=0)return null;
        double roll=random.NextDouble()*total;
        foreach(var e in choices)if(Eligible(e,floor,source) && (used==null||!used.Contains(e.item))) {
            roll-=Weight(e,floor);if(roll<0)return e;
        }
        return null;
    }
    static void Add(List<Drop> result,Entry entry,System.Random random) {
        int min=Mathf.Clamp(entry.minQuantity,1,999),max=Mathf.Clamp(entry.maxQuantity,min,999);
        int amount=random.Next(min,max+1);
        for(int i=0;i<result.Count;i++)if(result[i].item==entry.item) {
            result[i]=new Drop(entry.item,result[i].quantity+amount);return;
        }
        result.Add(new Drop(entry.item,amount));
    }
    public ItemData Roll(System.Random random)
    {
        return Select(entries,random,1,DungeonLootSource.Floor,null)?.item;
    }
}
