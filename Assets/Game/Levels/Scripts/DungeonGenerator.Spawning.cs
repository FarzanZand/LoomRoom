using UnityEngine;
using UnityEngine.AI;

// Room templates and their sockets, breakables, features, enemies, the guardian and the merchant.
public partial class DungeonGenerator
{
    DungeonWeightedPrefab[] Destructibles => Both(data.destructibles, Biome != null ? Biome.destructibles : null);
    DungeonFeature[] Features => Both(data.features, Biome != null ? Biome.features : null);
    static bool IsFloorBreakable(GameObject prefab) { var d=prefab.GetComponent<DungeonDestructible>(); return d==null || d.placement==DestructiblePlacement.Floor; }
    static DungeonRoomRoles Mask(RoomRole role) => (DungeonRoomRoles)(1<<(int)role);

    // A room profile's own enemies replace the biome's spawns for that room.
    GameObject ChooseEnemy(int room, System.Random rng)
    {
        var own=profiles[room]!=null ? profiles[room].enemies : null;
        if(own!=null && own.Length>0) return own[rng.Next(own.Length)];
        return Biome!=null ? Biome.ChooseEnemy(rng,FloorInBiome) : null;
    }

    // The hand-built interior for a room: the starting room, the shop, a milestone arena or the profile's template.
    GameObject TemplatePrefab(int i) =>
        Authored ? (data.authoredRooms!=null && i<data.authoredRooms.Length && data.authoredRooms[i]?.template!=null ? data.authoredRooms[i].template : i==0 ? data.startingRoom : null)
        : RequestedTemplate(i,Roles[i],profiles[i],MerchantRoom);
    // Generated layouts; also used before the rooms are placed, when i is the requested room.
    GameObject RequestedTemplate(int i,RoomRole role,DungeonRoomProfile profile,int merchantRoom=-1) =>
        i==0 && UsesStartingRoom ? data.startingRoom
        : i==merchantRoom && i!=0 && data.shopRoom!=null ? data.shopRoom
        : role==RoomRole.Exit && Milestone!=null && Milestone.arena!=null ? Milestone.arena
        : profile!=null ? profile.roomTemplate : null;
    DungeonRoomTemplate TemplateOf(int i) { var p=TemplatePrefab(i); return p!=null ? p.GetComponent<DungeonRoomTemplate>() : null; }

    void ChooseTemplates()
    {
        templates=new DungeonRoomTemplate[Layout.rooms.Count];
        templateInstances=new GameObject[Layout.rooms.Count];
        for(int i=0;i<templates.Length;i++)
        {
            var prefab=TemplatePrefab(i);
            if(prefab==null)continue;
            templates[i]=prefab.GetComponent<DungeonRoomTemplate>();
            if(templates[i]==null){Debug.LogWarning($"Room template {prefab.name} has no DungeonRoomTemplate component; generating the room normally.",prefab);continue;}
            // Smaller rooms still get the template: pieces outside the room or on walkways are removed.
        }
    }

    // A quiet room for the merchant, or -1 when this floor has none. With a shop room template, rooms
    // that have no hand-built interior of their own are preferred so the shop never replaces a shrine
    // or an arena. Chosen right after the roles, before heights, styles and room shapes, so the shop
    // template's own settings apply; these are the first draws of the extra stream.
    int ChooseMerchantRoom(RoomRole[] roles,System.Func<int,bool> hasTemplate)
    {
        if(data.merchantPrefab==null || extraRandom.NextDouble()>=data.MerchantChance(FloorNumber))return -1;
        var candidates=new System.Collections.Generic.List<int>();
        var plain=new System.Collections.Generic.List<int>();
        for(int i=1;i<roles.Length;i++)
            if(roles[i]==RoomRole.Rest || roles[i]==RoomRole.Storage || roles[i]==RoomRole.Treasure){candidates.Add(i);if(!hasTemplate(i))plain.Add(i);}
        if(data.shopRoom!=null && plain.Count>0)candidates=plain;
        if(candidates.Count==0)candidates.Add(0);
        return candidates[extraRandom.Next(candidates.Count)];
    }

    // Walkways inside a room: every doorway approach, and the centre cross or the routes to the centre.
    System.Collections.Generic.HashSet<Vector2Int> Lanes(int room)
    {
        var lanes=new System.Collections.Generic.HashSet<Vector2Int>();
        foreach(var p in Layout.RoomCells(room))if(Layout.Walkways.Contains(p))lanes.Add(p);
        return lanes;
    }

    void PlaceTemplate(int index)
    {
        var template=templates[index];
        // Grown rooms built from a template footprint may be turned; the interior turns with them.
        var instance=Instantiate(template.gameObject,Cell(Layout.RoomCenter(index)),Quaternion.Euler(0,90*Layout.RoomRotation(index),0),transform);
        instance.name=template.gameObject.name;
        templateInstances[index]=instance;
        var lanes=Lanes(index);
        foreach(var piece in instance.GetComponentsInChildren<DungeonTemplatePiece>(true))
        {
            if(piece==null)continue;
            var cell=CellOf(piece.transform.position);
            // Outside a smaller room it would sit in a wall; on a walkway it would block the route.
            if(!Layout.InRoom(index,cell) || (piece.removeIfBlocking && lanes.Contains(cell)))
                DestroyImmediate(piece.gameObject); // navigation is baked this frame
        }
        var rewards=profiles[index]!=null ? profiles[index].rewards : null;
        foreach(var socket in instance.GetComponentsInChildren<DungeonSocket>(true))
        {
            if(!Layout.InRoom(index,CellOf(socket.transform.position)))continue;
            if(socket.chance<1f && extraRandom.NextDouble()>=socket.chance)continue;
            var t=socket.transform;
            switch(socket.type)
            {
                case DungeonSocketType.Chest: MakeContainer(t.position,true,rewards,t.rotation); break;
                case DungeonSocketType.Breakable:
                {
                    var prefab=socket.overridePrefab!=null ? socket.overridePrefab : DungeonWeightedPrefab.Choose(Destructibles,extraRandom);
                    if(prefab!=null)SpawnDestructible(prefab,t.position,t.rotation,rewards,extraRandom.Next());
                    break;
                }
                // A board has no walls to hang them on (its doors get their own torch, BoardDoorTorch).
                case DungeonSocketType.WallLight: if(!Board)SocketLight(socket,index); break;
                case DungeonSocketType.Feature:
                {
                    var prefab=socket.overridePrefab;
                    if(prefab==null){var f=PickFeature(index);prefab=f!=null ? f.prefab : null;}
                    if(prefab!=null)SpawnFeature(prefab,t.position,t.rotation);
                    break;
                }
                default:
                    if(!actorSockets.TryGetValue(index,out var list))actorSockets[index]=list=new();
                    list.Add(socket);
                    break;
            }
        }
        if(!template.replaceGeneratedProps)
        {
            var available=new System.Collections.Generic.List<Vector2Int>();
            foreach(var p in Layout.RoomCells(index))if(!lanes.Contains(p) && !Layout.Reserved.Contains(p))available.Add(p);
            PlaceExtras(index,available,0,rewards);
        }
    }

    // A template's wall light: on the wall its socket faces, or the cell's nearest other wall.
    void SocketLight(DungeonSocket socket,int room)
    {
        if(socket.overridePrefab==null && !DungeonWallLight.Any(WallLights))return;
        var cell=CellOf(socket.transform.position);
        foreach(var d in SocketWallOrder(socket.transform.forward))
        {
            var n=cell+d;
            if(Layout.InBounds(n) && Layout.floor[n.x,n.y])continue;
            SpawnTorch(cell,d,room,TorchParent,extraRandom,socket);
            return;
        }
    }
    // The wall a socket's light looks for first (the one it points at), then the sides, then behind it.
    // Shared with the room template's Prefab Mode preview.
    public static Vector2Int[] SocketWallOrder(Vector3 forward)
    {
        var facing=Mathf.Abs(forward.x)>=Mathf.Abs(forward.z) ? new Vector2Int(forward.x>=0?1:-1,0) : new Vector2Int(0,forward.z>=0?1:-1);
        return new[]{facing,new Vector2Int(-facing.y,facing.x),new Vector2Int(facing.y,-facing.x),-facing};
    }
    // Where a wall light stands in its cell, from the cell centre: on the floor at the wall face.
    public static Vector3 WallLightOffset(Vector2Int wall,float cellSize) => new Vector3(wall.x,0,wall.y)*(cellSize*.5f-WallThickness*.5f);
    Transform torchParent;
    // The root every generated wall light hangs under, or null when there are none.
    public Transform TorchRoot => torchParent;
    Transform TorchParent { get { if(torchParent==null){torchParent=new GameObject("Torches").transform;torchParent.SetParent(transform,false);} return torchParent; } }

    // Breakables and features in the room's free cells, starting after the ones already used.
    // Returns the first cell left unused.
    int PlaceExtras(int index,System.Collections.Generic.List<Vector2Int> available,int cursor,LootSource rewards)
    {
        if(index==MerchantRoom && cursor<available.Count)cursor++; // keep a free cell for the stall
        var breakables=Destructibles;
        if(breakables!=null && breakables.Length>0)
        {
            int min=Mathf.Clamp(data.destructiblesPerRoom.x,0,8),max=Mathf.Clamp(data.destructiblesPerRoom.y,min,8);
            int count=extraRandom.Next(min,max+1);
            var corners=Corners(index);
            for(int i=0;i<count;i++)
            {
                var prefab=DungeonWeightedPrefab.Choose(breakables,extraRandom);
                if(prefab==null)break;
                var d=prefab.GetComponent<DungeonDestructible>();
                if(d!=null && d.placement==DestructiblePlacement.Corner)
                {
                    if(corners.Count==0)continue;
                    int pick=extraRandom.Next(corners.Count);var corner=corners[pick];corners.RemoveAt(pick);
                    SpawnDestructible(prefab,corner.position,corner.rotation,null,extraRandom.Next());
                    continue;
                }
                if(cursor>=available.Count)continue;
                SpawnDestructible(prefab,Cell(available[cursor++]),Quaternion.Euler(0,extraRandom.Next(4)*90,0),rewards,extraRandom.Next());
            }
        }
        if(Roles[index]==RoomRole.Entrance || (Roles[index]==RoomRole.Exit && Milestone!=null))return cursor;
        var feature=PickFeature(index);
        if(feature!=null && feature.placement!=DungeonPropPlacement.Anywhere)
        {
            // Against a wall, in a corner or in the middle, among the cells nothing else has taken.
            var free=available.GetRange(cursor,available.Count-cursor);
            var open=new System.Collections.Generic.HashSet<Vector2Int>(free);
            open.ExceptWith(furnished);
            if(FindSpot(feature.placement,Vector2Int.one,index,free,open,out var spot,out var turn))BackToWall(SpawnFeature(feature.prefab,spot,turn),feature.placement,Vector2Int.one);
        }
        else if(feature!=null && cursor<available.Count)
        {
            var pos=Cell(available[cursor++]);
            SpawnFeature(feature.prefab,pos,FaceCenter(pos,index));
        }
        return cursor;
    }

    DungeonFeature PickFeature(int index)
    {
        var features=Features;
        if(features==null)return null;
        var role=Mask(Roles[index]);
        foreach(var f in features)
        {
            if(f==null || f.prefab==null || (f.rooms&role)==0 || FloorInBiome<f.minFloor)continue;
            featureCounts.TryGetValue(f.prefab,out int used);
            if(f.maxPerFloor>0 && used>=f.maxPerFloor)continue;
            if(extraRandom.NextDouble()>=f.chancePerRoom)continue;
            featureCounts[f.prefab]=used+1;
            return f;
        }
        return null;
    }

    // Room corners at ceiling height, pulled into the corner so hanging cobwebs meet both walls
    // and the ceiling. Corner prefabs hang down from their pivot.
    static readonly (int,int)[] CornerSigns={(-1,-1),(1,-1),(-1,1),(1,1)};
    System.Collections.Generic.List<Pose> Corners(int index)
    {
        var list=new System.Collections.Generic.List<Pose>();
        float inset=data.cellSize*.5f-.03f; // walls are flush with the cell edge
        var room=Layout.rooms[index];
        var candidates=new System.Collections.Generic.List<(Vector2Int,int,int)>();
        if(Layout.IsRectangular(index))
            candidates.AddRange(new[]{(new Vector2Int(room.xMin,room.yMin),-1,-1),(new Vector2Int(room.xMax-1,room.yMin),1,-1),(new Vector2Int(room.xMin,room.yMax-1),-1,1),(new Vector2Int(room.xMax-1,room.yMax-1),1,1)});
        else
            // Shaped rooms: every inside corner, including those around pillars.
            foreach(var p in Layout.RoomCells(index))
                foreach(var (sx,sz) in CornerSigns)
                    if(!Open(p+new Vector2Int(sx,0)) && !Open(p+new Vector2Int(0,sz)))candidates.Add((p,sx,sz));
        foreach(var (cell,sx,sz) in candidates)
        {
            if(Layout.Reserved.Contains(cell))continue;
            var pos=Cell(cell)+new Vector3(sx*inset,HeightAt(cell.x,cell.y)-.02f,sz*inset);
            list.Add(new Pose(pos,Quaternion.LookRotation(new Vector3(-sx,0,-sz))));
        }
        return list;
    }

    DungeonDestructible SpawnDestructible(GameObject prefab,Vector3 pos,Quaternion rotation,LootSource rewardOverride,int seed)
    {
        var go=Instantiate(prefab,pos,rotation,transform);
        var d=go.GetComponent<DungeonDestructible>();
        if(d==null)return null;
        d.loot=First(rewardOverride,FloorLoot);
        d.seed=seed;d.floorNumber=FloorNumber;
        return d;
    }

    GameObject SpawnFeature(GameObject prefab,Vector3 pos,Quaternion rotation)
    {
        var go=Instantiate(prefab,pos,rotation,transform);
        int seed=extraRandom.Next();
        var chestLoot=FloorLoot;
        foreach(var grave in go.GetComponentsInChildren<DungeonGrave>()){grave.seed=seed;grave.floorNumber=FloorNumber;grave.fallbackLoot=chestLoot;}
        foreach(var shelf in go.GetComponentsInChildren<DungeonBookshelf>()){shelf.seed=seed;shelf.floorNumber=FloorNumber;shelf.fallbackLoot=chestLoot;}
        foreach(var altar in go.GetComponentsInChildren<DungeonAltar>())altar.floorNumber=FloorNumber;
        return go;
    }

    Character SpawnEnemyAt(GameObject prefab,Vector3 pos,Quaternion rotation,LootSource rewards)
    {
        if(prefab==null || !NavMesh.SamplePosition(pos,out var hit,2,NavMesh.AllAreas))return null;
        return SetUpEnemy(Instantiate(prefab,hit.position,rotation,transform),extraRandom,rewards);
    }

    // Floor scaling for every enemy the generator places. With a loot stream, also the room's rewards
    // (or the floor's loot) and a seed on its loot drop.
    Character SetUpEnemy(GameObject enemy,System.Random lootRandom,LootSource rewards=null)
    {
        var character=enemy.GetComponent<Character>();
        if(character!=null && data.balance!=null)data.balance.Apply(character,FloorNumber);
        var drop=lootRandom!=null ? enemy.GetComponent<DungeonLootDrop>() : null;
        if(drop!=null){if(!drop.overrideLevelTable)drop.table=First(rewards,FloorLoot);drop.seed=lootRandom.Next();drop.floorNumber=FloorNumber;}
        return character;
    }

    // After navigation exists: enemies, the boss and the merchant at their sockets.
    void SpawnSocketActors(DungeonExit exitStair)
    {
        Character boss=null;
        int exitIndex=System.Array.IndexOf(Roles,RoomRole.Exit);
        foreach(var pair in actorSockets)
        {
            int room=pair.Key;
            foreach(var socket in pair.Value)
            {
                var t=socket.transform;
                switch(socket.type)
                {
                    case DungeonSocketType.Enemy:
                    {
                        var prefab=socket.overridePrefab!=null ? socket.overridePrefab : ChooseEnemy(room,extraRandom);
                        SpawnEnemyAt(prefab,t.position,t.rotation,profiles[room]!=null ? profiles[room].rewards : null);
                        break;
                    }
                    case DungeonSocketType.Boss:
                        if(Milestone!=null && boss==null && room==exitIndex)boss=SpawnBoss(t.position,t.rotation);
                        break;
                    case DungeonSocketType.Merchant:
                        if(room==MerchantRoom)SpawnMerchant(t.position,t.rotation);
                        break;
                }
            }
        }
        if(Milestone!=null && boss==null && exitIndex>=0)
        {
            var c=Cell(Layout.RoomCenter(exitIndex));
            var toExit=ExitPoint-c;toExit.y=0;
            boss=SpawnBoss(c+(toExit.sqrMagnitude>.01f ? -toExit.normalized*data.cellSize : Vector3.zero),Quaternion.LookRotation(toExit.sqrMagnitude>.01f ? -toExit : Vector3.forward));
        }
        if(boss!=null)gameObject.AddComponent<DungeonBossEncounter>().Initialize(this,Milestone,boss,exitIndex,exitStair);
        if(MerchantRoom>=0 && FindAnyMerchant()==null)
        {
            var lanes=Lanes(MerchantRoom);
            var middle=Cell(Layout.RoomCenter(MerchantRoom));
            foreach(var p in Layout.RoomCells(MerchantRoom))
            {
                if(lanes.Contains(p) || Layout.Reserved.Contains(p) || Blocked(Cell(p)))continue;
                var toCenter=middle-Cell(p);
                SpawnMerchant(Cell(p),Quaternion.LookRotation(toCenter.sqrMagnitude>.01f ? toCenter : Vector3.forward));
                break;
            }
            if(FindAnyMerchant()==null)SpawnMerchant(middle+Vector3.right*data.cellSize*.5f,Quaternion.identity);
        }
    }

    bool Blocked(Vector3 pos)=>Physics.CheckBox(pos+Vector3.up*.6f,new Vector3(.45f,.5f,.45f),Quaternion.identity,~0,QueryTriggerInteraction.Ignore);
    Merchant FindAnyMerchant()=>GetComponentInChildren<Merchant>(true);
    public Merchant Merchant=>FindAnyMerchant();

    Character SpawnBoss(Vector3 pos,Quaternion rotation)
    {
        var boss=SpawnEnemyAt(Milestone.boss,pos,rotation,Milestone.bossLoot);
        if(boss==null){Debug.LogWarning($"Milestone boss {Milestone.boss.name} could not be placed on navigation.",this);return null;}
        var drop=boss.GetComponent<DungeonLootDrop>();
        if(drop!=null){if(Milestone.bossLoot!=null){drop.table=Milestone.bossLoot;drop.overrideLevelTable=true;}drop.bonusGold+=Milestone.bonusGold;}
        if(boss.Stats!=null && (Milestone.healthMultiplier!=1 || Milestone.damageMultiplier!=1))
        {
            boss.Stats.AddModifier(new StatModifier(StatType.MaxHealth,Milestone.healthMultiplier-1,ModifierType.PercentMultiply,Milestone));
            boss.Stats.AddModifier(new StatModifier(StatType.AttackDamage,Milestone.damageMultiplier-1,ModifierType.PercentMultiply,Milestone));
            boss.Stats.Heal(boss.Stats.MaxHealth);
        }
        return boss;
    }

    void SpawnMerchant(Vector3 pos,Quaternion rotation)
    {
        if(data.merchantPrefab==null || FindAnyMerchant()!=null)return;
        if(NavMesh.SamplePosition(pos,out var hit,1.5f,NavMesh.AllAreas))pos=hit.position;
        var go=Instantiate(data.merchantPrefab,pos,rotation,transform);
        var merchant=go.GetComponentInChildren<Merchant>(true);
        if(merchant==null)return;
        if(merchant.stockTable==null)merchant.stockTable=data.merchantStock!=null ? data.merchantStock : FloorLoot;
        merchant.seed=extraRandom.Next();merchant.floorNumber=FloorNumber;
    }
}
