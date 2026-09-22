using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

// Creates authored assets once. All resulting data/prefabs can then be edited normally.
[InitializeOnLoad]
public static class TableLevelAssetBuilder
{
    const string Root="Assets/Game/Levels/";
    const string Dungeon=Root+"Dungeon1/";
    static TableLevelAssetBuilder() { EditorApplication.delayCall+=EnsureAssets; }
    static void EnsureAssets()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        // Folder moves must never silently regenerate and overwrite designer-authored content.
        if(AssetDatabase.FindAssets("t:TableLevelCatalog").Length==0) Build();
    }
    [MenuItem("Tools/Table Levels/Create missing level assets")]
    public static void Build()
    {
        if(AssetDatabase.FindAssets("t:TableLevelCatalog").Length>0) {
            Debug.Log("Table level assets already exist. Edit their data/prefabs directly; setup will not overwrite them.");
            return;
        }
        foreach(var folder in new[]{"Resources","Town/Lighting","Dungeon1/Lighting","Dungeon1/Materials","Dungeon1/Enemies","Dungeon1/Prefabs"}) Directory.CreateDirectory(Root+folder);
        AssetDatabase.Refresh();
        var floor=Material("Flagstone",new Color(.36f,.39f,.42f),new Color(.24f,.27f,.31f),true);
        var wall=Material("Crypt stone",new Color(.44f,.48f,.47f),new Color(.29f,.34f,.35f),true);
        var trim=Material("Cut sandstone",new Color(.57f,.53f,.42f),new Color(.42f,.39f,.31f),false);
        var wood=Material("Oak",new Color(.38f,.22f,.12f),new Color(.24f,.13f,.08f),false);
        var metal=Material("Aged brass",new Color(.41f,.36f,.21f),new Color(.2f,.24f,.23f),false);
        var ceiling=Material("Vault",new Color(.37f,.4f,.42f),new Color(.26f,.29f,.32f),true);
        var bone=Material("Mite bone pixels",new Color(.8f,.74f,.53f),new Color(.48f,.43f,.32f),false);
        var dark=Material("Mite carapace pixels",new Color(.2f,.29f,.3f),new Color(.09f,.15f,.18f),false);
        var eye=Material("Mite amber eyes",new Color(1,.53f,.11f),new Color(.85f,.27f,.07f),false);
        eye.EnableKeyword("_EMISSION");eye.SetColor("_EmissionColor",new Color(.9f,.3f,.04f));EditorUtility.SetDirty(eye);
        var knife=Item("Knife","Notched knife",3,1.35f);
        var sword=Item("Sword","Crypt sword",6,1);
        var mace=Item("Mace","Grave mace",10,.75f);
        var shield=Item("Shield","Bronze buckler",0,1);
        var potion=Item("healPotion","Crimson tonic",0,1);
        potion.description="Restores 35 health. Use from the hotbar or inventory.";
        potion.effects=new[]{new EffectEntry { trigger=EffectTrigger.OnUse,type=EffectType.Heal,value=35,chance=100 }};
        potion.maxStackSize=5;potion.canBeEquipped=false;EditorUtility.SetDirty(potion);
        var food=Item("Food","Trail ration",0,1);
        food.description="Restores 18 health.";food.effects=new[]{new EffectEntry { trigger=EffectTrigger.OnUse,type=EffectType.Heal,value=18,chance=100 }};
        food.maxStackSize=5;food.canBeEquipped=false;EditorUtility.SetDirty(food);
        Directory.CreateDirectory("Assets/Game/Items/LootTables");
        var loot=Asset<DungeonLootTable>("Assets/Game/Items/LootTables/Crypt supplies.asset");
        loot.entries=new[]{Entry(knife,2),Entry(sword,2),Entry(mace,1),Entry(shield,2),Entry(potion,5),Entry(food,4)};
        EditorUtility.SetDirty(loot);
        var soldier=Enemy("Crypt Soldier",38,7,2.3f,1.2f,"SM_Chr_Skeleton_LightArmor_01",sword,loot);
        var warden=Enemy("Crypt Warden",65,12,1.6f,1.8f,"SM_Chr_Skeleton_HeavyArmor_01",mace,loot);
        EnsureAnimationRelay(soldier);EnsureAnimationRelay(warden);
        var mite=Mite(bone,dark,eye,loot);
        var mood=Asset<SceneMood>(Dungeon+"Lighting/Amber crypt.asset");
        mood.skyTint=new Color(.4f,.5f,.62f);mood.skyExposure=.8f;mood.lightColor=new Color(.78f,.85f,1);mood.lightIntensity=.65f;
        mood.ambientSky=new Color(.85f,.88f,.94f);mood.ambientHorizon=new Color(.74f,.76f,.79f);mood.ambientGround=new Color(.5f,.52f,.56f);mood.fogColor=new Color(.28f,.34f,.42f);EditorUtility.SetDirty(mood);
        var townMood=Asset<SceneMood>(Root+"Town/Lighting/Town daylight.asset");
        townMood.skyTint=new Color(.7f,.8f,.93f);townMood.skyExposure=1;townMood.lightColor=new Color(1,.9f,.73f);townMood.lightIntensity=1.15f;
        townMood.ambientSky=new Color(.6f,.69f,.8f);townMood.ambientHorizon=new Color(.48f,.49f,.47f);townMood.ambientGround=new Color(.25f,.27f,.24f);EditorUtility.SetDirty(townMood);
        var town=Asset<TableLevelData>(Root+"Town/Town.asset");town.displayName="Town";town.description="Return to the woodland village";town.kind=TableLevelKind.Town;town.mood=townMood;EditorUtility.SetDirty(town);
        var dungeon=Asset<TableLevelData>(Dungeon+"Dungeon1.asset");dungeon.displayName="Dungeon1";dungeon.description="The Amber Crypt • a new layout every run";dungeon.kind=TableLevelKind.Dungeon;
        dungeon.mood=mood;dungeon.backgroundMusic=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Game/Audio/Library/Music/24. Mines01.mp3");
        dungeon.containerBreakAudio=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Game/Audio/Library/_Sound Effects Library/Crafting/Crt_Chopping_Wood_03.wav");
        dungeon.chestOpenAudio=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Game/Audio/Library/_Sound Effects Library/SoundFX/ChestOpen.wav");
        dungeon.floorMaterial=floor;dungeon.wallMaterial=wall;dungeon.trimMaterial=trim;dungeon.ceilingMaterial=ceiling;dungeon.woodMaterial=wood;dungeon.metalMaterial=metal;
        dungeon.enemies=new[]{soldier,mite,mite,warden};dungeon.loot=loot;dungeon.guaranteedHealing=potion;
        dungeon.startingItems=new[]{knife,shield,potion,potion};dungeon.startingEquipment=new[]{knife,shield};EditorUtility.SetDirty(dungeon);
        ConfigureFeedback(dungeon);
        var catalog=Asset<TableLevelCatalog>(Root+"Resources/TableLevels.asset");catalog.levels=new[]{town,dungeon};EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();Debug.Log("[Table Levels] Town and Dungeon1 assets created. Mines01 uses AudioManager's Music mixer group.");
    }
    static DungeonLootTable.Entry Entry(ItemData item,float weight)=>new(){item=item,weight=weight};
    [MenuItem("Tools/Table Levels/Update dungeon feedback assets")]
    public static void UpdateFeedbackAssets()
    {
        var data=AssetDatabase.LoadAssetAtPath<TableLevelData>(Dungeon+"Dungeon1.asset");
        if(data!=null){ConfigureFeedback(data);AssetDatabase.SaveAssets();}
    }
    static void ConfigureFeedback(TableLevelData level)
    {
        level.hudFont=AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/Game/UI/Fonts/PixelOperator.asset");
        string path=Dungeon+"Materials/Hotbar frame.png";
        if(!File.Exists(path))
        {
            var texture=new Texture2D(16,16,TextureFormat.RGBA32,false);
            for(int y=0;y<16;y++)for(int x=0;x<16;x++)
            {
                int edge=Mathf.Min(x,y,15-x,15-y);
                Color color=edge==0 ? new Color(.055f,.065f,.055f):edge==1 ? Color.white:edge==2 ? new Color(.55f,.55f,.55f):Color.clear;
                if((x<2||x>13)&&(y<2||y>13))color=Color.clear;
                texture.SetPixel(x,y,color);
            }
            texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);AssetDatabase.ImportAsset(path);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.spriteBorder=new Vector4(4,4,4,4);importer.filterMode=FilterMode.Point;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
        }
        var frameImporter=(TextureImporter)AssetImporter.GetAtPath(path);
        if(frameImporter.spriteImportMode!=SpriteImportMode.Single){frameImporter.spriteImportMode=SpriteImportMode.Single;frameImporter.SaveAndReimport();}
        level.hotbarFrame=AssetDatabase.LoadAssetAtPath<Sprite>(path);EditorUtility.SetDirty(level);
        foreach(string enemy in new[]{"Crypt Soldier","Crypt Warden","Crypt Mite"})
        {
            var definition=AssetDatabase.LoadAssetAtPath<CharacterData>(Dungeon+"Enemies/"+enemy+".asset");
            if(definition==null)continue;
            string clip=enemy=="Crypt Mite" ? "Blood_Impact_01":"Breaking_Bones_01";
            var audio=Asset<AudioData>(Dungeon+"Enemies/"+enemy+" impact.asset");
            audio.clips=new[]{AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Game/Audio/Library/_Sound Effects Library/Blood Guts Bones/"+clip+".wav")};audio.volume=.45f;audio.pitchVariance=.12f;audio.minDistance=2;audio.maxDistance=18;
            definition.bodyImpactAudio=audio;EditorUtility.SetDirty(audio);EditorUtility.SetDirty(definition);
        }
    }
    static void EnsureAnimationRelay(GameObject prefab)
    {
        string path=AssetDatabase.GetAssetPath(prefab);
        var contents=PrefabUtility.LoadPrefabContents(path);
        var animator=contents.GetComponentInChildren<Animator>(true);
        if(animator!=null && animator.GetComponent<WeaponAnimationRelay>()==null)
        {
            animator.gameObject.AddComponent<WeaponAnimationRelay>();PrefabUtility.SaveAsPrefabAsset(contents,path);
        }
        PrefabUtility.UnloadPrefabContents(contents);
    }
    static T Asset<T>(string path) where T:ScriptableObject
    {
        var asset=AssetDatabase.LoadAssetAtPath<T>(path);if(asset!=null)return asset;
        asset=ScriptableObject.CreateInstance<T>();AssetDatabase.CreateAsset(asset,path);return asset;
    }
    static ItemData Item(string original,string name,float attack,float speed)
    {
        string category = original == "Shield" ? "Shields" : original == "Food" || original == "healPotion" ? "Consumables" : "Weapons";
        string folder = "Assets/Game/Items/Data/" + category + "/";
        Directory.CreateDirectory(folder);
        string path=folder+name+".asset";
        var item=AssetDatabase.LoadAssetAtPath<ItemData>(path);if(item!=null)return item;
        item=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ItemData>(folder+original+".asset"));
        item.name=name;item.itemName=name;item.description=item.IsWeapon ? "Recovered from the crypt. Equip in your weapon hand." : "Supplies for the journey below.";
        if(item.IsWeapon) item.statModifiers=new[]{new StatModifierEntry{stat=StatType.AttackDamage,value=attack},new StatModifierEntry{stat=StatType.AttackSpeed,value=speed-1,type=ModifierType.PercentAdd}};
        if(item.itemType==ItemType.Shield) { item.equipSlot=EquipmentSlot.LeftHand;item.statModifiers=new[]{new StatModifierEntry{stat=StatType.Armor,value=1}}; }
        AssetDatabase.CreateAsset(item,path);return item;
    }
    static Material Material(string name,Color a,Color b,bool bricks)
    {
        string path=Dungeon+"Materials/"+name+".mat";
        var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(mat!=null)return mat;
        var tex=new Texture2D(32,32);tex.filterMode=FilterMode.Point;tex.wrapMode=TextureWrapMode.Repeat;
        var rng=new System.Random(name.Length*117);
        for(int y=0;y<32;y++)for(int x=0;x<32;x++)
        {
            bool seam=bricks && (y%8==0 || (x+((y/8)%2)*8)%16==0);
            tex.SetPixel(x,y,seam ? b*.55f : Color.Lerp(a,b,(float)rng.NextDouble()*.65f));
        }
        tex.Apply();string texturePath=Dungeon+"Materials/"+name+".png";File.WriteAllBytes(texturePath,tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(texturePath);
        var importer=(TextureImporter)AssetImporter.GetAtPath(texturePath);importer.filterMode=FilterMode.Point;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=false;importer.SaveAndReimport();
        mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));mat.name=name;mat.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));mat.SetFloat("_Smoothness",.05f);
        AssetDatabase.CreateAsset(mat,path);return mat;
    }
    static CharacterData Definition(string name,float hp,float damage,float speed,float cooldown)
    {
        var profile=Asset<EnemyBehaviourProfile>(Dungeon+"Enemies/"+name+" behaviour.asset");
        profile.settings=new EnemyBehaviourSettings { defaultState=EnemyState.Wander,detectionRadius=12,closeDetectionRadius=2.5f,fieldOfView=140,useChaseSpeed=true,chaseSpeed=speed,useWanderSpeed=true,wanderSpeed=.7f,wanderRadius=3,wanderZoneRadius=4,circleTarget=speed>2,circleSpeedFraction=.22f };
        EditorUtility.SetDirty(profile);
        var data=Asset<CharacterData>(Dungeon+"Enemies/"+name+".asset");data.characterName=name;data.faction=Faction.Enemy;data.behaviour=profile;
        data.stats=new List<StatEntry>{new(StatType.MaxHealth,hp),new(StatType.AttackDamage,damage),new(StatType.Armor,0),new(StatType.MoveSpeed,1),new(StatType.AttackSpeed,1)};
        data.attacks=new List<EnemyAttack>{new(){name="Bite / strike",maxRange=1.65f,cooldown=cooldown,fallbackHitDelay=.42f,animatorTrigger="Attack",facingAngle=45}};data.deathDisableDelay=8;EditorUtility.SetDirty(data);return data;
    }
    static GameObject EnemyRoot(string name,CharacterData data,float height)
    {
        var go=new GameObject(name);go.SetActive(false);
        var c=go.AddComponent<Character>();c.data=data;go.AddComponent<CharacterStats>();
        var col=go.AddComponent<CapsuleCollider>();col.height=height;col.radius=.35f;col.center=Vector3.up*height*.5f;
        var agent=go.AddComponent<NavMeshAgent>();agent.height=height;agent.radius=.35f;agent.speed=2;agent.baseOffset=0;
        go.AddComponent<EnemyMotor>();go.AddComponent<Perception>();go.AddComponent<EnemyBrain>();go.AddComponent<DungeonLootDrop>();return go;
    }
    static GameObject Enemy(string name,float hp,float damage,float speed,float cooldown,string model,ItemData carried,DungeonLootTable loot)
    {
        string path=Dungeon+"Enemies/"+name+".prefab";var existing=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(existing!=null)return existing;
        var go=EnemyRoot(name,Definition(name,hp,damage,speed,cooldown),1.8f);
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Synty/PolygonDarkFantasy/Prefabs/Characters/"+model+".prefab");
        var visual=(GameObject)PrefabUtility.InstantiatePrefab(prefab);visual.transform.SetParent(go.transform,false);
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Characters/Enemies/HumanoidEnemy.prefab");
        var sourceAnimator=source.GetComponentInChildren<Animator>(true);
        var animator=visual.GetComponentInChildren<Animator>(true);
        if(animator==null)animator=visual.AddComponent<Animator>();
        animator.runtimeAnimatorController=sourceAnimator.runtimeAnimatorController;animator.applyRootMotion=false;
        foreach(var col in visual.GetComponentsInChildren<Collider>())UnityEngine.Object.DestroyImmediate(col);
        var drop=go.GetComponent<DungeonLootDrop>();drop.table=loot;drop.carriedItem=carried;
        var loadout=go.AddComponent<EnemyWeaponLoadout>();
        loadout.weapon=carried;
        loadout.grip=Resources.Load<EnemyWeaponGrip>("Synty humanoid weapon grip");
        loadout.RefreshWeapon();
        go.SetActive(true);var saved=PrefabUtility.SaveAsPrefabAsset(go,path);UnityEngine.Object.DestroyImmediate(go);return saved;
    }
    static GameObject Mite(Material bone,Material dark,Material eye,DungeonLootTable loot)
    {
        string path=Dungeon+"Enemies/Crypt Mite.prefab";var existing=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(existing!=null)return existing;
        var data=Definition("Crypt Mite",20,5,3.2f,1.3f);data.behaviour.settings.eyeHeight=.65f;EditorUtility.SetDirty(data.behaviour);
        var go=EnemyRoot("Crypt Mite",data,1.05f);go.GetComponent<DungeonLootDrop>().table=loot;
        var anim=go.AddComponent<CryptMiteAnimation>();
        anim.shell=new GameObject("Shell pivot").transform;anim.shell.SetParent(go.transform,false);anim.shell.localPosition=Vector3.up*.6f;
        Part("Carapace",anim.shell,new Vector3(0,0,0),new Vector3(.85f,.42f,.8f),dark);
        Part("Bone crown",anim.shell,new Vector3(0,.2f,-.05f),new Vector3(.72f,.2f,.6f),bone);
        Part("Chipped ridge",anim.shell,new Vector3(-.12f,.33f,-.12f),new Vector3(.19f,.12f,.4f),bone);
        Part("Brow",anim.shell,new Vector3(0,.05f,.45f),new Vector3(.78f,.22f,.3f),bone);
        foreach(float x in new[]{-.24f,.24f})
        {
            Part("Eye socket",anim.shell,new Vector3(x,.04f,.61f),new Vector3(.23f,.17f,.08f),dark);
            Part("Amber eye",anim.shell,new Vector3(x,.04f,.66f),new Vector3(.11f,.09f,.04f),eye);
        }
        anim.jaw=new GameObject("Jaw hinge").transform;anim.jaw.SetParent(anim.shell,false);anim.jaw.localPosition=new Vector3(0,-.14f,.34f);
        Part("Jaw",anim.jaw,new Vector3(0,-.04f,.18f),new Vector3(.64f,.17f,.48f),bone);
        foreach(float x in new[]{-.23f,.23f}) Part("Fang",anim.jaw,new Vector3(x,.09f,.34f),new Vector3(.09f,.2f,.09f),bone);
        anim.legs=new Transform[6];
        for(int i=0;i<6;i++)
        {
            float side=i%2==0 ? -1:1;var leg=new GameObject("Leg "+i).transform;leg.SetParent(go.transform,false);leg.localPosition=new Vector3(side*.35f,.35f,(i/2-1)*.3f);anim.legs[i]=leg;
            Part("Upper",leg,new Vector3(side*.17f,0,0),new Vector3(.4f,.13f,.14f),dark);
            Part("Claw",leg,new Vector3(side*.33f,-.13f,.06f),new Vector3(.13f,.27f,.2f),bone);
        }
        go.SetActive(true);var saved=PrefabUtility.SaveAsPrefabAsset(go,path);UnityEngine.Object.DestroyImmediate(go);return saved;
    }
    static void Part(string name,Transform parent,Vector3 pos,Vector3 size,Material mat)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=pos;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=mat;UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
    }
}
