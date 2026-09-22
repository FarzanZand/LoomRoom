using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[InitializeOnLoad]
public static class DungeonSystemSetup
{
    static DungeonSystemSetup(){EditorApplication.update+=Tick;}
    static void Tick(){if(EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/DungeonSystemSetup.request"))return;File.Delete("Temp/DungeonSystemSetup.request");Build();}
    [MenuItem("Tools/Table Levels/Create missing dungeon system assets")]
    public static void Build()
    {
        var level=AssetDatabase.LoadAssetAtPath<TableLevelData>("Assets/Game/Levels/Dungeon1/Dungeon1.asset");
        if(level==null)return;
        var mode=EditorSettings.serializationMode;
        try {
            EditorSettings.serializationMode=SerializationMode.ForceText;
            Directory.CreateDirectory("Assets/Game/UI/Prefabs/Dungeon");
            Directory.CreateDirectory("Assets/Game/Levels/Dungeon1/Prefabs/Props");
            AssetDatabase.Refresh();
            if(level.enemyBarPrefab==null)level.enemyBarPrefab=EnemyBar(level.hudFont);
            if(level.damageNumberPrefab==null)level.damageNumberPrefab=TextPrefab("Damage number",level.hudFont,29,new Vector2(160,44));
            if(level.messagePrefab==null)level.messagePrefab=TextPrefab("Dungeon message",level.hudFont,28,new Vector2(850,36));
            if(level.enemyLoot==null)level.enemyLoot=Loot(level,"Enemy rewards",DungeonLootSource.Enemy);
            if(level.chestLoot==null)level.chestLoot=Loot(level,"Chest rewards",DungeonLootSource.Chest);
            if(level.barrelLoot==null)level.barrelLoot=Loot(level,"Barrel rewards",DungeonLootSource.Barrel);
            if(level.roomPropPrefabs==null || level.roomPropPrefabs.Length==0)level.roomPropPrefabs=new[]{Prop(level,"Stone pier",new Vector3(.65f,3,.65f)),Prop(level,"Burial stone",new Vector3(1.15f,.7f,1.15f))};
            if(level.roomProfiles==null || level.roomProfiles.Length==0) {
                Directory.CreateDirectory("Assets/Game/Levels/Dungeon1/Rooms");AssetDatabase.Refresh();
                level.roomProfiles=new[]{Profile("Guard chamber",DungeonGenerator.RoomRole.Combat),Profile("Treasury",DungeonGenerator.RoomRole.Treasure),Profile("Supply store",DungeonGenerator.RoomRole.Storage),Profile("Quiet chamber",DungeonGenerator.RoomRole.Rest)};
            }
            foreach(string name in new[]{"Crypt Soldier","Crypt Warden"}) {
                string path="Assets/Game/Levels/Dungeon1/Enemies/"+name+".prefab";
                var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if(asset!=null && asset.GetComponent<DungeonDoorAccess>()==null) {
                    var content=PrefabUtility.LoadPrefabContents(path);
                    try{content.AddComponent<DungeonDoorAccess>();PrefabUtility.SaveAsPrefabAsset(content,path);}finally{PrefabUtility.UnloadPrefabContents(content);}
                }
            }
            if(level.doorPrefab!=null && level.doorPrefab.GetComponent<DungeonDoor>().openAudio==null) {
                string path=AssetDatabase.GetAssetPath(level.doorPrefab);var content=PrefabUtility.LoadPrefabContents(path);
                try{content.GetComponent<DungeonDoor>().openAudio=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Game/Audio/Library/_Sound Effects Library/SoundFX/doorGateOpen.mp3");PrefabUtility.SaveAsPrefabAsset(content,path);}finally{PrefabUtility.UnloadPrefabContents(content);}
            }
            EditorUtility.SetDirty(level);AssetDatabase.SaveAssets();
            File.WriteAllText("Temp/DungeonSystemSetup.report","PASS: UI prefabs, prop prefabs and source-specific reward tables created; existing assets preserved.");
        }finally{EditorSettings.serializationMode=mode;}
    }
    static DungeonRoomProfile Profile(string name,DungeonGenerator.RoomRole role)
    {
        string path="Assets/Game/Levels/Dungeon1/Rooms/"+name+".asset";
        var old=AssetDatabase.LoadAssetAtPath<DungeonRoomProfile>(path);if(old!=null)return old;
        var profile=ScriptableObject.CreateInstance<DungeonRoomProfile>();profile.role=role;
        profile.minProps=role==DungeonGenerator.RoomRole.Rest?0:1;profile.maxProps=role==DungeonGenerator.RoomRole.Storage?4:2;
        AssetDatabase.CreateAsset(profile,path);return profile;
    }
    static DungeonLootTable Loot(TableLevelData level,string name,DungeonLootSource source)
    {
        string path="Assets/Game/Items/LootTables/"+name+".asset";
        var old=AssetDatabase.LoadAssetAtPath<DungeonLootTable>(path);if(old!=null)return old;
        var table=ScriptableObject.CreateInstance<DungeonLootTable>();table.enemyDropChance=.7f;
        var entries=level.loot.entries.Where(e=>e!=null&&e.item!=null).Select(e=>new DungeonLootTable.Entry{item=e.item,weight=e.weight,minQuantity=1,maxQuantity=1}).ToArray();
        var supplies=entries.Where(e=>e.item.IsConsumable).ToArray();var equipment=entries.Where(e=>!e.item.IsConsumable).ToArray();
        if(source==DungeonLootSource.Chest)table.pools=new[]{new DungeonLootTable.Pool{label="Equipment",entries=equipment,uniqueItems=true},new DungeonLootTable.Pool{label="Supplies",entries=supplies,minRolls=1,maxRolls=2}};
        else table.pools=new[]{new DungeonLootTable.Pool{label=source==DungeonLootSource.Enemy?"Enemy drop":"Barrel supplies",entries=source==DungeonLootSource.Enemy?entries:supplies,chance=source==DungeonLootSource.Enemy?1:.85f}};
        AssetDatabase.CreateAsset(table,path);return table;
    }
    static GameObject Prop(TableLevelData level,string name,Vector3 size)
    {
        string path="Assets/Game/Levels/Dungeon1/Prefabs/Props/"+name+".prefab";
        var old=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(old!=null)return old;
        var root=new GameObject(name);DungeonGenerator.Box(name,Vector3.up*size.y*.5f,size,level.trimMaterial,root.transform);
        var result=PrefabUtility.SaveAsPrefabAsset(root,path);Object.DestroyImmediate(root);return result;
    }
    static TMP_Text TextPrefab(string name,TMP_FontAsset font,float size,Vector2 dimensions)
    {
        string path="Assets/Game/UI/Prefabs/Dungeon/"+name+".prefab";
        var old=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(old!=null)return old.GetComponent<TMP_Text>();
        var label=Label(null,name,font,size);label.rectTransform.sizeDelta=dimensions;
        var saved=PrefabUtility.SaveAsPrefabAsset(label.gameObject,path);Object.DestroyImmediate(label.gameObject);return saved.GetComponent<TMP_Text>();
    }
    static DungeonEnemyBar EnemyBar(TMP_FontAsset font)
    {
        string path="Assets/Game/UI/Prefabs/Dungeon/Enemy health bar.prefab";
        var old=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(old!=null)return old.GetComponent<DungeonEnemyBar>();
        var frame=Image(null,"Enemy health bar",new Color(.62f,.49f,.27f));frame.rectTransform.sizeDelta=new Vector2(180,17);
        var bar=frame.gameObject.AddComponent<DungeonEnemyBar>();
        var bg=Image(frame.transform,"Background",new Color(.035f,.045f,.04f));Stretch(bg.rectTransform,2);
        bar.recentDamageFill=Image(bg.transform,"Recent damage",new Color(1,.77f,.36f));Stretch(bar.recentDamageFill.rectTransform,0);
        bar.healthFill=Image(bg.transform,"Health",new Color(.7f,.22f,.09f));Stretch(bar.healthFill.rectTransform,0);
        bar.enemyName=Label(frame.transform,"Enemy name",font,22);var r=bar.enemyName.rectTransform;r.anchorMin=r.anchorMax=new Vector2(.5f,1);r.anchoredPosition=new Vector2(0,19);r.sizeDelta=new Vector2(260,28);
        var saved=PrefabUtility.SaveAsPrefabAsset(frame.gameObject,path);Object.DestroyImmediate(frame.gameObject);return saved.GetComponent<DungeonEnemyBar>();
    }
    static Image Image(Transform parent,string name,Color color){var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);var image=go.GetComponent<Image>();image.color=color;image.raycastTarget=false;return image;}
    static TMP_Text Label(Transform parent,string name,TMP_FontAsset font,float size){var go=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(parent,false);var label=go.GetComponent<TMP_Text>();label.font=font;label.fontSize=size;label.alignment=TextAlignmentOptions.Center;label.color=new Color(.96f,.89f,.72f);label.raycastTarget=false;var shadow=go.AddComponent<Shadow>();shadow.effectDistance=new Vector2(2,-2);return label;}
    static void Stretch(RectTransform r,float inset){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=Vector2.one*inset;r.offsetMax=-Vector2.one*inset;}
}
