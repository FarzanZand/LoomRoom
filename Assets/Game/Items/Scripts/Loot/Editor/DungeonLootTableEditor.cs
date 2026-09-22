using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DungeonLootTable))]
public class DungeonLootTableEditor : Editor
{
    int previewFloor=1,previewSeed=12345;
    DungeonLootSource source=DungeonLootSource.Enemy;
    string report;
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();EditorGUILayout.LabelField("Reward preview",EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Guaranteed entries are added once per reward. Each pool first checks chance, then makes weighted selections. Enemy Drop Chance gates the whole table; carried gear is independent. Zero weight disables a weighted entry. Missing items and ineligible floors are skipped.",MessageType.Info);
        previewFloor=Math.Max(1,EditorGUILayout.IntField("Floor",previewFloor));
        source=(DungeonLootSource)EditorGUILayout.EnumPopup("Source",source);
        previewSeed=EditorGUILayout.IntField("Preview seed",previewSeed);
        if(GUILayout.Button("Simulate 10,000 rewards (no items spawned)")) {
            var table=(DungeonLootTable)target;var rng=new System.Random(previewSeed);
            var quantities=new Dictionary<ItemData,int>();var appearances=new Dictionary<ItemData,int>();int empty=0;
            for(int n=0;n<10000;n++) {
                var drops=table.RollDrops(rng,previewFloor,source);if(drops.Count==0)empty++;
                foreach(var drop in drops){quantities.TryGetValue(drop.item,out var count);quantities[drop.item]=count+drop.quantity;appearances.TryGetValue(drop.item,out var hits);appearances[drop.item]=hits+1;}
            }
            report=$"Empty rewards: {empty/100f:F2}%\n"+string.Join("\n",quantities.OrderByDescending(x=>x.Value).Select(x=>$"{x.Key.itemName}: {appearances[x.Key]/100f:F2}% of rewards; {x.Value/10000f:F3} items/reward"));
        }
        if(!string.IsNullOrEmpty(report))EditorGUILayout.HelpBox(report,MessageType.None);
    }
}
