using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Explicit, one-time migration. Normal imports and gameplay never rewrite designer assets.
[InitializeOnLoad]
public static class DungeonInventorySetup
{
    const string Root = "Assets/Game/";
    const string Marker = "Assets/Game/UI/Resources/UIFeedback.asset";
    static readonly List<ItemData> gear = new(), foods = new();
    static TMP_FontAsset font;
    static UIFeedbackSettings feedback;
    static DungeonInventorySetup() => EditorApplication.update += Tick;
    static void Tick()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/DungeonInventorySetup.request")) return;
        File.Delete("Temp/DungeonInventorySetup.request");
        try { Build(); File.WriteAllText("Temp/DungeonInventorySetup.report", "PASS: inventory scene, equipment prefab, 12 gear items, four foods, audio, loot and balance wired."); }
        catch (Exception ex) { File.WriteAllText("Temp/DungeonInventorySetup.report", "FAIL: " + ex); Debug.LogException(ex); }
    }
    [MenuItem("Tools/Table Levels/Install inventory and equipment content")]
    public static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<UIFeedbackSettings>(Marker) != null)
            throw new InvalidOperationException("Inventory content is already installed. Edit its authored assets; this migration will not overwrite them.");
        if (EditorSceneManager.GetActiveScene().path != Root + "Scenes/Room.unity")
            throw new InvalidOperationException("Open Room scene before installing inventory content.");
        var mode = EditorSettings.serializationMode;
        EditorSettings.serializationMode = SerializationMode.ForceText;
        try
        {
            foreach (var folder in new[]{"Items/Data/Armor", "Items/Prefabs/Equipment", "Items/Materials", "UI/Audio", "UI/Resources"}) Directory.CreateDirectory(Root + folder);
            AssetDatabase.Refresh();
            font = Load<TMP_FontAsset>("UI/Fonts/PixelOperator.asset");
            feedback = ScriptableObject.CreateInstance<UIFeedbackSettings>();
            feedback.open = Sound("Inventory open", "Inventory/Inv_Open_01.wav", .35f);
            feedback.close = Sound("Inventory close", "Inventory/Inv_Close_01.wav", .3f);
            feedback.hover = Sound("Inventory hover", "Inventory/Inv_Select_Object_01.wav", .1f);
            feedback.equip = Sound("Equip item", "Inventory/Inv_Equip_Weapons_01.wav", .4f);
            feedback.invalid = Sound("Inventory unavailable", "Inventory/Inv_Close_02.wav", .2f);
            var pickup = Sound("Item pickup", "Inventory/Inv_Items_01.wav", .4f);
            var eating = Sound("Eat food", "Items/Itm_Foodeat_01.wav", .5f);
            gear.Clear(); foods.Clear();
            foreach (bool iron in new[]{false,true})
            {
                string tier = iron ? "Iron" : "Bronze", art = iron ? "Iron" : "Copper";
                var metal = Material(tier, iron ? new Color(.46f,.53f,.59f) : new Color(.61f,.32f,.11f), .75f);
                Gear(tier, art, "Helm", EquipmentSlot.Head, iron?2:1, "Helmet1", metal);
                Gear(tier, art, "Armor", EquipmentSlot.Body, iron?3:2, iron?"Chestplate1 1":"Chestplate1", metal);
                Gear(tier, art, "Gloves", EquipmentSlot.Gloves, iron?2:1, "Gloves1", metal);
                Gear(tier, art, "Boots", EquipmentSlot.Boots, iron?2:1, "Boots1", metal);
                Gear(tier, art, "Sword", EquipmentSlot.RightHand, iron?6:5, "Weapon1", metal);
                Gear(tier, art, "Shield", EquipmentSlot.LeftHand, iron?3:2, "Shield1", metal);
            }
            Food("Apple", "Botany/Singles/16_Apple.png", 1, 6, new Color(.7f,.09f,.045f), eating);
            Food("Pear", "Botany/Singles/18_Pear.png", .5f, 16, new Color(.55f,.65f,.12f), eating);
            Food("Roasted mushroom", "Botany/Singles/89_Mushroom_A.png", 1, 12, new Color(.46f,.24f,.1f), eating);
            Food("Cooked meat", "Miscellaneous/Singles/87_Crab_Loot_Meat.png", 1, 20, new Color(.93f,.6f,.43f), eating);
            ConfigureData(pickup);
            ConfigureUI();
            // Save this marker last: a failed setup can be rerun, completed setup preserves edits.
            AssetDatabase.CreateAsset(feedback, Marker);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }
        finally { EditorSettings.serializationMode = mode; }
    }
    static T Load<T>(string path) where T:Object => AssetDatabase.LoadAssetAtPath<T>(Root + path);
    static T SaveNew<T>(T asset, string path) where T:Object
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if(existing!=null){Object.DestroyImmediate(asset);return existing;}
        AssetDatabase.CreateAsset(asset,path);return asset;
    }
    static AudioData Sound(string name,string clip,float volume)
    {
        var data=ScriptableObject.CreateInstance<AudioData>();
        data.clips=new[]{Load<AudioClip>("Audio/Library/_Sound Effects Library/"+clip)};
        if(data.clips[0]==null)throw new Exception("Missing audio: "+clip);
        data.volume=volume;data.pitchVariance=.03f;
        return SaveNew(data,Root+"UI/Audio/"+name+".asset");
    }
    static Material Material(string name,Color color,float metallic=0)
    {
        var m=new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor",color);m.SetFloat("_Metallic",metallic);m.SetFloat("_Smoothness",metallic>.1f?.68f:.2f);
        m.EnableKeyword("_EMISSION");
        return SaveNew(m,Root+"Items/Materials/"+name+".mat");
    }
    static Sprite Icon(string path)
    {
        var sprites=AssetDatabase.LoadAllAssetsAtPath(Root+"Art/PixelItems/"+path).OfType<Sprite>().ToArray();
        if(sprites.Length==0)throw new Exception("Missing pixel sprite: "+path);
        return sprites[0];
    }
    static void Gear(string tier,string art,string type,EquipmentSlot slot,float value,string icon,Material material)
    {
        bool hand=slot==EquipmentSlot.RightHand || slot==EquipmentSlot.LeftHand;
        string folder=hand?(slot==EquipmentSlot.RightHand?"Weapons":"Shields"):"Armor";
        string path=Root+"Items/Data/"+folder+"/"+tier+" "+type+".asset";
        var item=AssetDatabase.LoadAssetAtPath<ItemData>(path);
        if(item!=null){gear.Add(item);return;}
        ItemData template=hand?Load<ItemData>(slot==EquipmentSlot.RightHand?"Items/Data/Weapons/Crypt sword.asset":"Items/Data/Shields/Bronze buckler.asset"):null;
        item=template!=null?Object.Instantiate(template):ScriptableObject.CreateInstance<ItemData>();
        item.name=item.itemName=tier+" "+type;
        item.description=tier+" "+type.ToLowerInvariant()+". "+(hand?"Reliable dungeon equipment.":"Protective plates with a polished metal finish.");
        item.tag=tier+" equipment";item.itemType=hand?(slot==EquipmentSlot.RightHand?ItemType.Weapon:ItemType.Shield):ItemType.Equipment;
        item.canBeEquipped=true;item.equipSlot=slot;item.maxStackSize=1;item.equipOnPickup=false;item.directToHotbar=false;
        item.statModifiers=new[]{new StatModifierEntry{stat=slot==EquipmentSlot.RightHand?StatType.AttackDamage:StatType.Armor,value=value}};
        item.effects=Array.Empty<EffectEntry>();
        item.icon=Icon("Armory/Singles/"+(hand?"Weapon Singles/":"Armor Singles/")+art+"/"+art+"_"+icon+".png");
        item.worldPrefab=Model(item.itemName,slot,material,template?.worldPrefab);
        AssetDatabase.CreateAsset(item,path);gear.Add(item);
    }
    static GameObject Part(Transform parent,string name,PrimitiveType shape,Vector3 position,Vector3 scale,Material material)
    {
        var part=GameObject.CreatePrimitive(shape);part.name=name;part.transform.SetParent(parent,false);
        part.transform.localPosition=position;part.transform.localScale=scale;
        Object.DestroyImmediate(part.GetComponent<Collider>());part.GetComponent<Renderer>().sharedMaterial=material;return part;
    }
    static GameObject Model(string name,EquipmentSlot slot,Material material,GameObject template)
    {
        string path=Root+"Items/Prefabs/Equipment/"+name+".prefab";
        var existing=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(existing!=null)return existing;
        var root=template!=null?(GameObject)PrefabUtility.InstantiatePrefab(template):new GameObject(name);
        root.name=name;
        if(template==null)
        {
            if(slot==EquipmentSlot.Head)
            {
                Part(root.transform,"Crown",PrimitiveType.Cube,new Vector3(0,.22f,0),new Vector3(.42f,.12f,.38f),material);
                Part(root.transform,"Back",PrimitiveType.Cube,new Vector3(0,.06f,.15f),new Vector3(.42f,.28f,.08f),material);
                foreach(float x in new[]{-.18f,.18f})Part(root.transform,"Cheek guard",PrimitiveType.Cube,new Vector3(x,.035f,0),new Vector3(.07f,.31f,.38f),material);
                Part(root.transform,"Nose guard",PrimitiveType.Cube,new Vector3(0,.07f,-.17f),new Vector3(.06f,.23f,.05f),material);
            }
            else if(slot==EquipmentSlot.Body)
            {
                Part(root.transform,"Breastplate",PrimitiveType.Cube,new Vector3(0,.12f,0),new Vector3(.44f,.48f,.22f),material);
                foreach(float x in new[]{-.29f,.29f})Part(root.transform,"Shoulder",PrimitiveType.Cube,new Vector3(x,.3f,0),new Vector3(.18f,.18f,.27f),material);
                Part(root.transform,"Waist",PrimitiveType.Cube,new Vector3(0,-.14f,0),new Vector3(.39f,.09f,.24f),material);
            }
            else foreach(float x in new[]{-.13f,.13f})
            {
                Part(root.transform,slot==EquipmentSlot.Boots?"Boot shaft":"Cuff",PrimitiveType.Cube,new Vector3(x,.1f,0),new Vector3(.18f,.3f,.19f),material);
                Part(root.transform,slot==EquipmentSlot.Boots?"Toe":"Fingers",PrimitiveType.Cube,new Vector3(x,-.055f,-.06f),new Vector3(.19f,.12f,.3f),material);
            }
        }
        else
        {
            // Retain the authored mesh/grip; replace metal surfaces while preserving wood/leather.
            foreach(var renderer in root.GetComponentsInChildren<Renderer>())
            {
                var materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++)if(materials[i]!=null && System.Text.RegularExpressions.Regex.IsMatch(materials[i].name,"metal|iron|steel|blade|bronze|gold",System.Text.RegularExpressions.RegexOptions.IgnoreCase))materials[i]=material;
                renderer.sharedMaterials=materials;
            }
        }
        var shine=root.AddComponent<ItemShine>();shine.surfaces=root.GetComponentsInChildren<Renderer>();
        shine.shineColor=material.GetColor("_BaseColor");
        var saved=PrefabUtility.SaveAsPrefabAsset(root,path);Object.DestroyImmediate(root);return saved;
    }
    static void Food(string name,string icon,float rate,float seconds,Color color,AudioData eating)
    {
        string path=Root+"Items/Data/Consumables/"+name+".asset";
        var item=AssetDatabase.LoadAssetAtPath<ItemData>(path);
        if(item!=null){foods.Add(item);return;}
        item=ScriptableObject.CreateInstance<ItemData>();item.name=item.itemName=name;
        item.description="Eat from your hand to recover health gradually. A new meal replaces the previous meal.";
        item.tag="Food";item.icon=Icon(icon);item.itemType=ItemType.Consumable;item.maxStackSize=5;
        item.canBeEquipped=true;item.equipSlot=EquipmentSlot.RightHand;item.AnimationOnUse=ItemUseAnimation.Eat;item.useAudio=eating;
        item.effects=new[]{new EffectEntry{type=EffectType.FoodRegen,value=rate,duration=seconds}};
        string prefabPath=Root+"Items/Prefabs/Equipment/"+name+".prefab";
        item.worldPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if(item.worldPrefab==null)
        {
            var root=new GameObject(name);var material=Material(name,color);
            if(name.Contains("mushroom"))
            {Part(root.transform,"Stem",PrimitiveType.Cylinder,Vector3.zero,new Vector3(.08f,.08f,.08f),Material("Food stem",new Color(.8f,.72f,.53f)));Part(root.transform,"Cap",PrimitiveType.Sphere,new Vector3(0,.08f,0),new Vector3(.25f,.12f,.25f),material);}
            else
            {Part(root.transform,"Food",PrimitiveType.Sphere,Vector3.zero,name=="Cooked meat"?new Vector3(.24f,.09f,.15f):new Vector3(.18f,.2f,.18f),material);if(name=="Pear")Part(root.transform,"Neck",PrimitiveType.Sphere,new Vector3(0,.1f,0),new Vector3(.1f,.12f,.1f),material);}
            item.worldPrefab=PrefabUtility.SaveAsPrefabAsset(root,prefabPath);Object.DestroyImmediate(root);
        }
        AssetDatabase.CreateAsset(item,path);foods.Add(item);
    }
    static void EditPrefab(string path,Action<GameObject> edit)
    {
        var root=PrefabUtility.LoadPrefabContents(path);
        try{edit(root);PrefabUtility.SaveAsPrefabAsset(root,path);}finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    static void Stat(CharacterData data,StatType stat,float value)
    {data.stats.RemoveAll(s=>s.stat==stat);data.stats.Add(new StatEntry(stat,value));EditorUtility.SetDirty(data);}
    static void ConfigureData(AudioData pickup)
    {
        var player=Load<CharacterData>("Players/Table/TablePlayer.asset");
        Stat(player,StatType.MaxHealth,30);Stat(player,StatType.MaxMana,20);Stat(player,StatType.AttackDamage,5);Stat(player,StatType.Armor,0);Stat(player,StatType.ManaRegen,.5f);
        string[] names={"Crypt Mite","Crypt Soldier","Crypt Warden"};float[] hp={30,40,50},damage={8,14,18},armor={1,2,3},cooldown={1.3f,1.6f,2.1f};
        for(int i=0;i<names.Length;i++)
        {
            var enemy=Load<CharacterData>("Levels/Dungeon1/Enemies/"+names[i]+".asset");
            Stat(enemy,StatType.MaxHealth,hp[i]);Stat(enemy,StatType.AttackDamage,damage[i]);Stat(enemy,StatType.Armor,armor[i]);
            foreach(var attack in enemy.attacks){attack.cooldown=cooldown[i];if(attack.fallbackHitDelay>=0)attack.fallbackHitDelay=i==2?.7f:.45f;attack.hit.damageMultiplier=1;}
            EditorUtility.SetDirty(enemy);
            EditPrefab(Root+"Levels/Dungeon1/Enemies/"+names[i]+".prefab",go=>{
                var stats=go.GetComponent<CharacterStats>();if(stats!=null){var so=new SerializedObject(stats);so.FindProperty("overrides").ClearArray();so.ApplyModifiedPropertiesWithoutUndo();}
                var drop=go.GetComponent<DungeonLootDrop>();if(drop!=null)drop.carriedItem=null;
            });
        }
        EditPrefab(Root+"Combat/Prefabs/CombatManager.prefab",go=>{var cm=go.GetComponent<CombatManager>();cm.blockDamageReduction=.5f;cm.timedBlockWindow=0;});
        foreach(var cm in Object.FindObjectsByType<CombatManager>(FindObjectsInactive.Include)){cm.blockDamageReduction=.5f;cm.timedBlockWindow=0;EditorUtility.SetDirty(cm);PrefabUtility.RecordPrefabInstancePropertyModifications(cm);}
        foreach(var p in Object.FindObjectsByType<Player>(FindObjectsInactive.Include).Where(p=>p.kind==PlayerKind.Table))
        {
            var so=new SerializedObject(p.GetComponent<CharacterStats>());so.FindProperty("overrides").ClearArray();so.ApplyModifiedPropertiesWithoutUndo();
            foreach(var inv in p.GetComponents<Inventory>())if(inv.role==InventoryRole.Bag){so=new SerializedObject(inv);so.FindProperty("allowedTypes").intValue=-1;so.ApplyModifiedPropertiesWithoutUndo();}
        }
        var potion=Load<ItemData>("Items/Data/Consumables/Crimson tonic.asset");
        potion.effects=new[]{new EffectEntry{type=EffectType.Heal,value=12}};potion.maxStackSize=3;EditorUtility.SetDirty(potion);
        var level=Load<TableLevelData>("Levels/Dungeon1/Dungeon1.asset");
        level.chestPrefab=Chest();
        level.balance=SaveNew(ScriptableObject.CreateInstance<DungeonBalance>(),Root+"Levels/Dungeon1/Dungeon balance.asset");
        level.startingEquipment=new[]{gear.First(i=>i.itemName=="Bronze Sword"),gear.First(i=>i.itemName=="Bronze Shield")};
        level.startingItems=level.startingEquipment.Concat(new[]{potion,foods[0]}).ToArray();level.guaranteedHealing=foods[0];
        level.enemyLoot=Loot("Dungeon enemy progression",DungeonLootSource.Enemy,potion);
        level.chestLoot=Loot("Dungeon chest progression",DungeonLootSource.Chest,potion);
        level.barrelLoot=Loot("Dungeon barrel progression",DungeonLootSource.Barrel,potion);
        level.loot=level.chestLoot;EditorUtility.SetDirty(level);
        foreach(var profile in level.roomProfiles)if(profile!=null && profile.rewards!=null){profile.rewards=null;EditorUtility.SetDirty(profile);}
        void InventoryConfig(InventoryManager manager){manager.defaultPickupAudio=pickup;foreach(var item in gear.Concat(foods))if(!manager.itemCatalog.Contains(item))manager.itemCatalog.Add(item);EditorUtility.SetDirty(manager);}
        EditPrefab(Root+"Core/Prefabs/InventoryManager.prefab",go=>InventoryConfig(go.GetComponent<InventoryManager>()));
        foreach(var manager in Object.FindObjectsByType<InventoryManager>(FindObjectsInactive.Include)){InventoryConfig(manager);PrefabUtility.RecordPrefabInstancePropertyModifications(manager);}
        void AudioConfig(AudioManager manager)
        {
            var so=new SerializedObject(manager);var library=so.FindProperty("sfxLibrary");bool exists=false;
            for(int i=0;i<library.arraySize;i++)if(library.GetArrayElementAtIndex(i).FindPropertyRelative("key").stringValue=="genericPickupSound")exists=true;
            if(!exists){int i=library.arraySize++;var entry=library.GetArrayElementAtIndex(i);entry.FindPropertyRelative("key").stringValue="genericPickupSound";entry.FindPropertyRelative("clip").objectReferenceValue=pickup.clips[0];entry.FindPropertyRelative("volume").floatValue=pickup.volume;entry.FindPropertyRelative("pitchVariance").floatValue=.03f;}
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        EditPrefab(Root+"Core/Prefabs/AudioManager.prefab",go=>AudioConfig(go.GetComponent<AudioManager>()));
        foreach(var manager in Object.FindObjectsByType<AudioManager>(FindObjectsInactive.Include)){AudioConfig(manager);PrefabUtility.RecordPrefabInstancePropertyModifications(manager);}
    }
    static DungeonLootTable Loot(string name,DungeonLootSource source,ItemData potion)
    {
        var table=ScriptableObject.CreateInstance<DungeonLootTable>();table.enemyDropChance=.35f;table.maxItemsPerReward=3;
        var equipment=gear.Select(item=>new DungeonLootTable.Entry{item=item,weight=item.itemName.StartsWith("Iron")?1:3,minFloor=item.itemName.StartsWith("Iron")?2:1,weightPerFloor=item.itemName.StartsWith("Iron")?2:0}).ToArray();
        var supplies=foods.Select((item,i)=>new DungeonLootTable.Entry{item=item,weight=i<2?5:2}).Concat(new[]{new DungeonLootTable.Entry{item=potion,weight=1}}).ToArray();
        table.pools=source==DungeonLootSource.Chest?new[]{new DungeonLootTable.Pool{label="Equipment",entries=equipment},new DungeonLootTable.Pool{label="Supplies",entries=supplies,chance=.6f}}:
            source==DungeonLootSource.Enemy?new[]{new DungeonLootTable.Pool{label="Recovered supplies",entries=supplies},new DungeonLootTable.Pool{label="Rare equipment",entries=equipment,chance=.12f}}:
            new[]{new DungeonLootTable.Pool{label="Barrel supplies",entries=supplies,chance=.35f}};
        return SaveNew(table,Root+"Items/LootTables/"+name+".asset");
    }
    static GameObject Chest()
    {
        string path=Root+"Levels/Dungeon1/Prefabs/Props/Supply chest.prefab";
        var existing=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(existing!=null)return existing;
        var root=new GameObject("Supply chest");
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Synty/PolygonFantasyKingdom/Prefabs/Props/Furniture/SM_Prop_Chest_01.prefab");
        var model=(GameObject)PrefabUtility.InstantiatePrefab(source,root.transform);
        var renderers=model.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;
        foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
        float scale=1.25f/bounds.size.x;model.transform.localScale=Vector3.one*scale;
        model.transform.localPosition=new Vector3(-bounds.center.x*scale,-bounds.min.y*scale,-bounds.center.z*scale);
        foreach(var collider in model.GetComponentsInChildren<Collider>())collider.enabled=false;
        var container=root.AddComponent<DungeonContainer>();container.chest=true;
        container.lid=model.GetComponentsInChildren<Transform>().First(t=>t.name=="SM_Prop_Chest_01_Lid");
        var box=root.AddComponent<BoxCollider>();box.center=new Vector3(0,bounds.size.y*scale*.5f,0);box.size=bounds.size*scale;
        root.AddComponent<InteractableTrigger>();
        var saved=PrefabUtility.SaveAsPrefabAsset(root,path);Object.DestroyImmediate(root);return saved;
    }
    static RectTransform Rect(string name,Transform parent,Vector2 size,Vector2 position)
    {
        var go=new GameObject(name,typeof(RectTransform));var rect=(RectTransform)go.transform;rect.SetParent(parent,false);
        rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.sizeDelta=size;rect.anchoredPosition=position;return rect;
    }
    static TextMeshProUGUI Text(string name,Transform parent,string value,Vector2 size,Vector2 position,float fontSize=24)
    {
        var rect=Rect(name,parent,size,position);var text=rect.gameObject.AddComponent<TextMeshProUGUI>();text.font=font;text.fontSize=fontSize;text.text=value;text.color=new Color(.88f,.84f,.71f);text.alignment=TextAlignmentOptions.MidlineLeft;text.raycastTarget=false;return text;
    }
    static void Frame(RectTransform rect)
    {
        var image=rect.GetComponent<Image>()??rect.gameObject.AddComponent<Image>();image.color=new Color(.045f,.055f,.06f,.96f);
        var outline=rect.GetComponent<Outline>()??rect.gameObject.AddComponent<Outline>();outline.effectColor=new Color(.55f,.43f,.23f,.95f);outline.effectDistance=new Vector2(2,-2);
    }
    static void ConfigureUI()
    {
        foreach(string name in new[]{"Inventory slot","Hotbar slot"})EditPrefab(Root+"UI/Prefabs/Items/"+name+".prefab",go=>{if(go.GetComponent<UIInteractionFeedback>()==null)go.AddComponent<UIInteractionFeedback>();});
        string path=Root+"UI/Prefabs/Items/Equipment slot.prefab";
        var slotPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if(slotPrefab==null)
        {
            var rect=Rect("Equipment slot",null,new Vector2(132,100),Vector2.zero);Frame(rect);
            var slot=rect.gameObject.AddComponent<EquipmentSlotUI>();rect.gameObject.AddComponent<UIInteractionFeedback>();
            slot.title=Text("Slot label",rect,"HELM",new Vector2(128,23),new Vector2(0,-34),18);slot.title.alignment=TextAlignmentOptions.Center;
            var imageRect=Rect("Item icon",rect,new Vector2(56,56),new Vector2(0,10));slot.icon=imageRect.gameObject.AddComponent<Image>();slot.icon.preserveAspect=true;slot.icon.raycastTarget=false;
            slotPrefab=PrefabUtility.SaveAsPrefabAsset(rect.gameObject,path);Object.DestroyImmediate(rect.gameObject);
        }
        var ui=Object.FindAnyObjectByType<InventoryUI>(FindObjectsInactive.Include);
        if(ui==null)throw new Exception("Room scene has no InventoryUI");
        var serialized=new SerializedObject(ui);var old=(GameObject)serialized.FindProperty("panel").objectReferenceValue;
        if(ui.inventoryPanel==null)
        {
            var root=Rect("Inventory screen",old.transform.parent,Vector2.zero,Vector2.zero);root.anchorMin=Vector2.zero;root.anchorMax=Vector2.one;root.offsetMin=root.offsetMax=Vector2.zero;
            old.transform.SetParent(root,false);old.SetActive(true);
            var left=(RectTransform)old.transform;left.anchorMin=left.anchorMax=new Vector2(0,.5f);left.pivot=new Vector2(0,.5f);left.anchoredPosition=new Vector2(32,0);
            Frame(left);
            var right=Rect("Equipment and stats",root,new Vector2(480,590),new Vector2(-32,0));right.anchorMin=right.anchorMax=new Vector2(1,.5f);right.pivot=new Vector2(1,.5f);Frame(right);
            Text("Title",right,"CHARACTER",new Vector2(420,40),new Vector2(0,253),32);
            var panel=right.gameObject.AddComponent<EquipmentPanelUI>();
            panel.health=Text("Health",right,"HEALTH",new Vector2(420,30),new Vector2(0,199));
            panel.mana=Text("Mana",right,"MANA",new Vector2(420,30),new Vector2(0,164));
            panel.damage=Text("Damage",right,"DAMAGE",new Vector2(205,30),new Vector2(-107,125));
            panel.armor=Text("Armor",right,"ARMOR",new Vector2(205,30),new Vector2(110,125));
            var slots=new List<EquipmentSlotUI>();int index=0;
            foreach(var equipmentSlot in new[]{EquipmentSlot.Head,EquipmentSlot.Body,EquipmentSlot.Gloves,EquipmentSlot.Boots,EquipmentSlot.RightHand,EquipmentSlot.LeftHand})
            {
                var go=(GameObject)PrefabUtility.InstantiatePrefab(slotPrefab,right);var rect=(RectTransform)go.transform;
                rect.anchoredPosition=new Vector2(-146+(index%3)*146,48-(index/3)*116);
                var slot=go.GetComponent<EquipmentSlotUI>();slot.slot=equipmentSlot;slot.title.text=EquipmentSlotUI.Display(equipmentSlot);go.name=slot.title.text;PrefabUtility.RecordPrefabInstancePropertyModifications(slot);PrefabUtility.RecordPrefabInstancePropertyModifications(slot.title);PrefabUtility.RecordPrefabInstancePropertyModifications(rect);slots.Add(slot);index++;
            }
            panel.slots=slots.ToArray();panel.food=Text("Food regeneration",right,"",new Vector2(420,60),new Vector2(0,-158),21);
            Text("Controls",right,"Drag gear to equip. Click a slot to unequip.\nMainhand and offhand use hotbar slots.\nTAB to close - the dungeon stays live.",new Vector2(420,72),new Vector2(0,-242),18);
            serialized.FindProperty("panel").objectReferenceValue=root.gameObject;serialized.ApplyModifiedPropertiesWithoutUndo();root.gameObject.SetActive(false);
            ui.inventoryPanel=left;ui.statsPanel=right;ui.equipmentPanel=panel;
        }
        ui.feedback=feedback;EditorUtility.SetDirty(ui);
        foreach(var label in Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include))if(label.text=="STAMINA"){label.text="MANA";label.gameObject.name="MANA";EditorUtility.SetDirty(label);}
        foreach(var vitals in Object.FindObjectsByType<PlayerVitalsUI>(FindObjectsInactive.Include))
        {
            var so=new SerializedObject(vitals);var fill=so.FindProperty("staminaFill").objectReferenceValue as Image;
            if(fill!=null){fill.color=new Color(.25f,.5f,.88f);EditorUtility.SetDirty(fill);}
        }
        foreach(var button in Object.FindObjectsByType<Button>(FindObjectsInactive.Include))if(button.GetComponent<UIInteractionFeedback>()==null)button.gameObject.AddComponent<UIInteractionFeedback>();
        var tooltip=Object.FindAnyObjectByType<TooltipUI>(FindObjectsInactive.Include);
        if(tooltip!=null){var so=new SerializedObject(tooltip);var panel=(GameObject)so.FindProperty("panel").objectReferenceValue;if(panel.GetComponent<CanvasGroup>()==null)panel.AddComponent<CanvasGroup>();}
    }
}
