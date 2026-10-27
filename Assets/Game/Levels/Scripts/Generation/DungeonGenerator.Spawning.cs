using UnityEngine;
using UnityEngine.AI;

// Room templates and their sockets, breakables, features, enemies, the guardian and the merchant.
public partial class DungeonGenerator
{
    // A stage with a look of its own breaks only its own things (no cellar barrels in a forest).
    DungeonWeightedPrefab[] Destructibles => OwnLook ? StageBiome.destructibles ?? new DungeonWeightedPrefab[0] : Shared(data.destructibles, b => b.destructibles);
    DungeonFeature[] Features => Shared(data.features, b => b.features);
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
        Authored ? (i<data.AuthoredRoomList.Length && data.AuthoredRoomList[i]?.template!=null ? data.AuthoredRoomList[i].template : i==0 ? data.startingRoom : null)
        : RequestedTemplate(i,Roles[i],profiles[i],shopInPlainRoom ? -1 : MerchantRoom);
    // Generated layouts; also used before the rooms are placed, when i is the requested room.
    GameObject RequestedTemplate(int i,RoomRole role,DungeonRoomProfile profile,int merchantRoom=-1) =>
        i==0 && UsesStartingRoom ? data.startingRoom
        : i==merchantRoom && i!=0 && data.shopRoom!=null ? data.shopRoom
        : role==RoomRole.Exit && Milestone!=null && Milestone.arena!=null ? Milestone.arena
        : profile!=null && profile.roomTemplate!=null ? profile.roomTemplate
        : pooledTemplates!=null && i<pooledTemplates.Length ? pooledTemplates[i] : null;

    // Hand-built rooms from the biome's pool (DungeonBiome.roomTemplates), one roll per room on its own stream so the
    // rest of the floor keeps its seed. Indexed like the requested rooms while the layout grows, then like the placed ones.
    GameObject[] pooledTemplates;
    GameObject[] PoolTemplates(RoomRole[] roles, DungeonRoomProfile[] chosen, int seed)
    {
        var pool = new GameObject[roles.Length];
        var choices = Biome != null ? Biome.roomTemplates : null;
        if (Authored || choices == null || choices.Length == 0) return pool;
        var rng = new System.Random(unchecked(seed + 130363));
        data.BiomeAt(FloorNumber, out int floorInBiome, out _);
        var used = new System.Collections.Generic.Dictionary<GameObject, int>();
        // Rooms built around a feature this floor switches off (a chasm, a shrine, candlelight) are left out.
        var features = RoomFeatures;
        var allowed = new System.Collections.Generic.Dictionary<GameObject, bool>();
        bool Allowed(GameObject t)
        {
            if (!allowed.TryGetValue(t, out bool ok)) { var tpl = t.GetComponent<DungeonRoomTemplate>(); allowed[t] = ok = tpl == null || features.AllowsAll(tpl.Requires()); }
            return ok;
        }
        // No room template comes up twice on one floor (the Not a witch room, the event rooms): a profile's own
        // template counts as used too. maxPerFloor can only lower that (0 or 1 both mean once).
        for (int i = 0; i < roles.Length && i < chosen.Length; i++)
            if (chosen[i] != null && chosen[i].roomTemplate != null) used[chosen[i].roomTemplate] = 1;
        for (int i = 0; i < roles.Length; i++)
        {
            double roll = rng.NextDouble();
            if ((i == 0 && UsesStartingRoom) || (chosen[i] != null && chosen[i].roomTemplate != null) || roll >= Biome.templateChance) continue;
            var role = Mask(roles[i]);
            var pick = WeightedPick.Choose(choices, c => c != null && c.template != null && (c.rooms & role) != 0 && floorInBiome >= c.fromFloor && Allowed(c.template)
                && !used.ContainsKey(c.template)
                ? c.weight / (1f + 2f * (used.TryGetValue(c.template, out var n) ? n : 0)) : 0, rng);
            if (pick == null) continue;
            pool[i] = pick.template;
            used[pick.template] = (used.TryGetValue(pick.template, out var k) ? k : 0) + 1;
        }
        return pool;
    }
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
        // A painted layout can name the merchant's room itself (AuthoredRoom.merchant): always there, no roll.
        if(Authored && data.merchantPrefab!=null)
        {
            var authoredRooms=data.AuthoredRoomList;
            for(int i=0;i<authoredRooms.Length && i<roles.Length;i++)if(authoredRooms[i]!=null && authoredRooms[i].merchant)return i;
        }
        if(data.merchantPrefab==null || extraRandom.NextDouble()>=data.MerchantChance(FloorNumber))return -1;
        var candidates=new System.Collections.Generic.List<int>();
        var plain=new System.Collections.Generic.List<int>();
        for(int i=1;i<roles.Length;i++)
            if(roles[i]==RoomRole.Rest || roles[i]==RoomRole.Storage || roles[i]==RoomRole.Treasure){candidates.Add(i);if(!hasTemplate(i))plain.Add(i);}
        // No quiet room: a combat room but the exit (his room gets no enemies), never the entrance, where he would
        // stand beside the spawn. Only a generated one: a room with no hand-built interior of its own, unless the shop's
        // template replaces it. An authored map's combat rooms bring their own enemies, so it has no merchant then.
        if(candidates.Count==0 && !Authored)
            for(int i=1;i<roles.Length;i++)
                if(roles[i]==RoomRole.Combat && (data.shopRoom!=null || !hasTemplate(i))){candidates.Add(i);if(!hasTemplate(i))plain.Add(i);}
        if(data.shopRoom!=null && plain.Count>0)candidates=plain;
        // Drawn even when there is no room for him, so the stream stays in step.
        int pick=extraRandom.Next(Mathf.Max(1,candidates.Count));
        return candidates.Count>0 ? candidates[pick] : -1;
    }

    // Walkways inside a room: every doorway approach, and the centre cross or the routes to the centre.
    System.Collections.Generic.HashSet<Vector2Int> Lanes(int room)
    {
        var lanes=new System.Collections.Generic.HashSet<Vector2Int>();
        foreach(var p in Layout.RoomCells(room))if(Layout.Walkways.Contains(p))lanes.Add(p);
        // Every actual opening too, and the cell inside it: nothing is ever set down in a way in or out (a turned
        // template's openings need not be where the walkways were drawn).
        lanes.UnionWith(Openings(room));
        return lanes;
    }

    // The room's cells at a way in or out, and the cell just inside each.
    System.Collections.Generic.HashSet<Vector2Int> Openings(int room)
    {
        var cells=new System.Collections.Generic.HashSet<Vector2Int>();
        foreach(var p in Layout.RoomCells(room))
            foreach(var d in DungeonLayout.Neighbours)
            {
                if(!Layout.IsOpening(room,p,d))continue;
                cells.Add(p);
                if(Layout.InRoom(room,p-d))cells.Add(p-d);
            }
        return cells;
    }

    // A turned or smaller room can put a doorway where the template had a wall: any prop the template set down that
    // stands in a way in or out is taken out, so it can never cut the room off (a barrel stack in the shop did on
    // about one floor in sixteen). Only the prop itself goes, never the group it sits in (a template's "Dressing"):
    // the unit is the hit object's ancestor just under the template or under a plain grouping node. Kit pieces,
    // sockets, characters and floor cuts are left alone; a portal, shrine, chest or merchant is moved a cell into
    // the room instead of deleted.
    static readonly Collider[] openingHits=new Collider[32];
    void ClearOpenings(GameObject instance,int room)
    {
        Physics.SyncTransforms();
        var gone=new System.Collections.Generic.HashSet<GameObject>();
        var moved=new System.Collections.Generic.Dictionary<Transform,Vector2Int>();
        var centre=Layout.RoomCenter(room);
        foreach(var cell in Layout.RoomCells(room))
        {
            bool doorway=false;
            foreach(var d in DungeonLayout.Neighbours)if(Layout.IsOpening(room,cell,d))doorway=true;
            if(!doorway)continue;
            int n=Physics.OverlapBoxNonAlloc(Cell(cell)+Vector3.up*1f,new Vector3(.5f,.6f,.5f),openingHits,Quaternion.identity,~0,QueryTriggerInteraction.Ignore);
            for(int i=0;i<n;i++)
            {
                var t=openingHits[i].transform;
                if(!t.IsChildOf(instance.transform) || t==instance.transform)continue;
                while(t.parent!=instance.transform && !IsGroup(t.parent))t=t.parent;
                if(Kept(t))continue;
                if(Moves(t)){if(!moved.ContainsKey(t))moved[t]=cell;continue;}
                gone.Add(t.gameObject);
            }
        }
        foreach(var go in gone)DestroyImmediate(go); // navigation is baked this frame
        foreach(var pair in moved)
        {
            var inward=centre-pair.Value;
            var step=Mathf.Abs(inward.x)>=Mathf.Abs(inward.y) ? new Vector2Int((int)Mathf.Sign(inward.x),0) : new Vector2Int(0,(int)Mathf.Sign(inward.y));
            if(inward==Vector2Int.zero || !Layout.InRoom(room,pair.Value+step))continue;
            pair.Key.position+=new Vector3(step.x,0,step.y)*data.cellSize;
        }
    }
    // Enemies dragged straight into a template become Enemy sockets that keep that enemy: it counts toward the room's
    // pack like a socket, gets floor scaling and loot, joins its pack, marks a wave point in an ambush room, and is
    // switched off until the NavMesh is built (a body standing there would punch a hole in it).
    void TakePlacedEnemies(GameObject instance,int room)
    {
        // Enemy Spawners are Enemy sockets with one field: their enemy, or the room's or biome's pick.
        foreach(var spawner in instance.GetComponentsInChildren<EnemySpawner>(true))
        {
            var socket=spawner.gameObject.AddComponent<DungeonSocket>();
            socket.type=DungeonSocketType.Enemy;
            socket.overridePrefab=spawner.enemy;
        }
        foreach(var brain in instance.GetComponentsInChildren<EnemyBrain>(true))
        {
            var enemy=brain.gameObject;
            // Outside a smaller room it would stand in a wall.
            if(!Layout.InRoom(room,CellOf(enemy.transform.position))){DestroyImmediate(enemy);continue;}
            var socket=new GameObject("Placed "+enemy.name).AddComponent<DungeonSocket>();
            socket.transform.SetParent(instance.transform,false);
            socket.transform.SetPositionAndRotation(enemy.transform.position,enemy.transform.rotation);
            socket.type=DungeonSocketType.Enemy;
            socket.placedEnemy=enemy;
            enemy.SetActive(false);
            enemy.transform.SetParent(transform,true);
        }
    }

    // An empty node holding several props: a template's dressing group, never a prop of its own.
    static bool IsGroup(Transform t) => t.childCount>1 && t.GetComponents<Component>().Length==1;
    static bool Has<T>(Transform t) where T : Component => t.GetComponentInChildren<T>(true)!=null || t.GetComponentInParent<T>(true)!=null;
    static bool Kept(Transform t) => Has<DungeonKitBlock>(t) || Has<DungeonKitPiece>(t) || Has<DungeonSocket>(t) || Has<Character>(t)
        || Has<DungeonFloorCut>(t) || Has<DungeonSpikeTrap>(t) || Has<DungeonStage>(t) || Has<DungeonAmbush>(t);
    static bool Moves(Transform t) => Has<PortalArch>(t) || Has<DungeonShrine>(t) || Has<DungeonContainer>(t) || Has<Merchant>(t);

    // A taller room (Height Tiles over 1): what hangs from a one-tile ceiling (a hanging chain) is lifted by the extra
    // height so it still hangs from the ceiling, and a moonlit room's patch of night sky stays above the ceiling.
    void HangFromCeiling(GameObject instance,DungeonRoomTemplate template,int index)
    {
        if(template==null)return;
        // Under an open sky (a board, or wild ground with no roof) nothing hangs and no patch of sky is needed.
        if(Board || WildSettings is { roofed: false })
        {
            foreach(var t in instance.GetComponentsInChildren<Transform>(true))
                if(t!=null && (t.name.StartsWith("Hanging chain") || t.name=="Night sky"))DestroyImmediate(t.gameObject);
            return;
        }
        float tile=Mathf.Max(.01f,data.architectureTileSize), extra=(Mathf.Max(1,template.heightTiles)-1)*tile, ceiling=instance.transform.position.y+Mathf.Max(1,template.heightTiles)*tile;
        Transform sky=null;
        foreach(var t in instance.GetComponentsInChildren<Transform>(true))
        {
            if(extra>0f && t.name.StartsWith("Hanging chain"))t.position+=Vector3.up*extra;
            else if(t.name=="Night sky")
            {
                sky=t;
                if(t.position.y<ceiling+1f)
                {
                    // The moon above it goes up with it.
                    float lift=ceiling+1.6f-t.position.y;
                    t.position+=Vector3.up*lift;
                    var moon=t.parent!=null ? t.parent.Find("Moon") : null;
                    if(moon!=null)moon.position+=Vector3.up*lift;
                }
            }
        }
        if(sky!=null)CloseSkyGap(instance,index,sky,ceiling);
    }

    // The patch of night sky hangs a little above the ceiling's skylight: the room's walls go on up to it in its own
    // upper stone (kit blocks, built with the room's other blocks; no collider), and the sky is cut to the room's own
    // outline, so nothing shows through between the walls and the stars and no sky hangs over the rooms round it.
    void CloseSkyGap(GameObject instance,int index,Transform sky,float ceiling)
    {
        if(index<0 || index>=Layout.rooms.Count)return;
        float height=sky.position.y-ceiling;
        if(height<=.05f)return;
        var r=Layout.rooms[index];
        Vector3 a=Cell(new Vector2Int(r.xMin,r.yMin)), b=Cell(new Vector2Int(r.xMax-1,r.yMax-1));
        float half=data.cellSize*.5f, thick=.4f;
        float x0=a.x-half, x1=b.x+half, z0=a.z-half, z1=b.z+half, cx=(x0+x1)*.5f, cz=(z0+z1)*.5f, w=x1-x0, d=z1-z0;
        var holder=new GameObject("Sky walls").transform;
        holder.SetParent(instance.transform,true);
        void Band(Vector3 at,Vector3 size)
        {
            var go=new GameObject("Sky wall");
            go.transform.SetParent(holder,true);
            go.transform.position=at; go.transform.rotation=Quaternion.identity;
            var block=go.AddComponent<DungeonKitBlock>();
            block.size=size; block.standsOnPosition=true; block.solid=false;
            block.sides=DungeonKitBlock.Surface.UpperWall; block.top=DungeonKitBlock.Surface.UpperWall;
        }
        Band(new Vector3(cx,ceiling,z0),new Vector3(w+thick,height,thick));
        Band(new Vector3(cx,ceiling,z1),new Vector3(w+thick,height,thick));
        Band(new Vector3(x0,ceiling,cz),new Vector3(thick,height,d+thick));
        Band(new Vector3(x1,ceiling,cz),new Vector3(thick,height,d+thick));
        // The sky over the room alone, lying on the bands.
        sky.position=new Vector3(cx,sky.position.y,cz);
        sky.rotation=Quaternion.Euler(-90f,0f,0f);
        var parentScale=sky.parent!=null ? sky.parent.lossyScale : Vector3.one;
        sky.localScale=new Vector3((w+thick)/Mathf.Max(.001f,parentScale.x),(d+thick)/Mathf.Max(.001f,parentScale.z),1f);
    }

    void PlaceTemplate(int index)
    {
        var template=templates[index];
        // Grown rooms built from a template footprint may be turned; the interior turns with them.
        // Made under a switched-off holder so an enemy dragged into the template is switched off before it wakes: awake
        // before the NavMesh is baked, its agent finds no floor ("not close enough to the NavMesh"). It is put down after
        // the bake (TakePlacedEnemies, PutDownPlacedEnemy).
        var holder=new GameObject("Placing room");
        holder.SetActive(false);
        holder.transform.SetParent(transform,false);
        var instance=Instantiate(template.gameObject,Cell(Layout.RoomCenter(index)),Quaternion.Euler(0,90*Layout.RoomRotation(index),0),holder.transform);
        foreach(var brain in instance.GetComponentsInChildren<EnemyBrain>(true))brain.gameObject.SetActive(false);
        instance.transform.SetParent(transform,true);
        DestroyImmediate(holder);
        instance.name=template.gameObject.name;
        templateInstances[index]=instance;
        // A boss stage: the platform and stair go in, and the template is lifted onto it.
        if(index==StageRoom)BuildStage(instance,index);
        HangFromCeiling(instance,template,index);
        var lanes=Lanes(index);
        foreach(var piece in instance.GetComponentsInChildren<DungeonTemplatePiece>(true))
        {
            if(piece==null)continue;
            var cell=CellOf(piece.transform.position);
            // Outside a smaller room it would sit in a wall; on a walkway it would block the route.
            if(!Layout.InRoom(index,cell) || (piece.removeIfBlocking && lanes.Contains(cell)))
                DestroyImmediate(piece.gameObject); // navigation is baked this frame
        }
        ClearOpenings(instance,index);
        // Pits and sunken courts take their floor out first.
        BuildCuts(instance,index);
        // The floor's damage and prices on the room's own traps, shrines and pits (DungeonRoomFeatures).
        var features=RoomFeatures;
        foreach(var plate in instance.GetComponentsInChildren<DungeonSpikeTrap>(true))plate.playerDamage=features.spikeDamage;
        foreach(var shrine in instance.GetComponentsInChildren<DungeonShrine>(true))shrine.bloodPrice=features.shrinePrice;
        foreach(var cut in instance.GetComponentsInChildren<DungeonFloorCut>(true)){cut.fallDamage=features.pitFallDamage;cut.keepPlayerOut=!features.playerFallsInPits;}
        // Kit blocks (pillars, platforms, steps, niches) become boxes of this room's own stone, before the NavMesh bake.
        var centre=Layout.RoomCenter(index);
        foreach(var piece in instance.GetComponentsInChildren<DungeonKitPiece>(true))
            if(piece!=null)piece.Choose(data.kitModels,HeightAt(centre.x,centre.y));
        foreach(var block in instance.GetComponentsInChildren<DungeonKitBlock>(true))
        {
            // A board has no ceiling: what hangs from one (the tavern's beams, a pillar's capital) isn't there.
            if(block!=null && block.fromCeiling && (Board || WildSettings is { roofed: false })){DestroyImmediate(block.gameObject);continue;}
            // On a boss stage the floor is the stage's top.
            if(block!=null)block.Build(RegionStyles!=null && index<RegionStyles.Length ? RegionStyles[index] : null,data,HeightAt(centre.x,centre.y),Cell(centre).y+(index==StageRoom && Stage!=null ? Stage.height : 0f));
        }
        var rewards=profiles[index]!=null ? profiles[index].rewards : null;
        StockPlacedLoot(instance,rewards);
        // A chest set down by hand with its own trap stays trapped (while the floor allows traps); others may be trapped.
        foreach(var chest in instance.GetComponentsInChildren<DungeonContainer>(true))
        {
            var own=chest.GetComponent<DungeonChestTrap>();
            if(own!=null && own.traps.Count>0)
            {
                if(!RoomFeatures.trappedTreasure){foreach(var plate in own.traps)if(plate!=null)DestroyImmediate(plate.gameObject);own.traps.Clear();}
                continue;
            }
            MaybeTrap(chest);
        }
        TakePlacedEnemies(instance,index);
        var openings=Openings(index);
        foreach(var socket in instance.GetComponentsInChildren<DungeonSocket>(true))
        {
            if(!Layout.InRoom(index,CellOf(socket.transform.position)))continue;
            // Things that stand in the way never go in an opening (the room may have been turned).
            if((socket.type==DungeonSocketType.Breakable || socket.type==DungeonSocketType.Feature || socket.type==DungeonSocketType.Chest) && openings.Contains(CellOf(socket.transform.position)))continue;
            if(socket.chance<1f && extraRandom.NextDouble()>=socket.chance)continue;
            var t=socket.transform;
            switch(socket.type)
            {
                case DungeonSocketType.Chest: MaybeTrap(MakeContainer(t.position,true,rewards,t.rotation)); break;
                case DungeonSocketType.Breakable:
                {
                    var prefab=socket.overridePrefab!=null ? socket.overridePrefab : DungeonWeightedPrefab.Choose(Destructibles,extraRandom);
                    // A board has no walls or ceiling for corner pieces (cobwebs) to hang from.
                    if(prefab!=null && (!Board || IsFloorBreakable(prefab)))SpawnDestructible(prefab,t.position,t.rotation,rewards,extraRandom.Next());
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
            // Cells the room's own pieces stand on (kit blocks, sockets) are taken: extras go elsewhere.
            var taken=new System.Collections.Generic.HashSet<Vector2Int>();
            foreach(var block in instance.GetComponentsInChildren<DungeonKitBlock>(true))
            {
                if(block==null || !block.solid || !block.TryGetComponent<BoxCollider>(out var box))continue;
                var b=box.bounds;
                for(float x=b.min.x+.05f;x<=b.max.x;x+=data.cellSize*.5f)for(float z=b.min.z+.05f;z<=b.max.z;z+=data.cellSize*.5f)taken.Add(CellOf(new Vector3(x,0,z)));
                taken.Add(CellOf(new Vector3(b.max.x-.05f,0,b.max.z-.05f)));
            }
            foreach(var socket in instance.GetComponentsInChildren<DungeonSocket>(true))if(socket!=null)taken.Add(CellOf(socket.transform.position));
            // A spike plate's cell too (a fountain was set down over the treasure vault's grates).
            foreach(var plate in instance.GetComponentsInChildren<DungeonSpikeTrap>(true))if(plate!=null)taken.Add(CellOf(plate.transform.position));
            taken.UnionWith(cutCells);
            // A walkway between two holes (a ravine's bridge) and the cells at its ends stay clear: one tree on it cuts
            // the room in two.
            foreach(var p in Layout.RoomCells(index))
            {
                if(cutCells.Contains(p))continue;
                bool between=cutCells.Contains(p+Vector2Int.left) && cutCells.Contains(p+Vector2Int.right)
                    || cutCells.Contains(p+Vector2Int.up) && cutCells.Contains(p+Vector2Int.down);
                if(!between)continue;
                taken.Add(p);
                foreach(var d in DungeonLayout.Neighbours)taken.Add(p+d);
            }
            var available=new System.Collections.Generic.List<Vector2Int>();
            foreach(var p in Layout.RoomCells(index))if(!lanes.Contains(p) && !Layout.Reserved.Contains(p) && !taken.Contains(p))available.Add(p);
            int left=PlaceExtras(index,available,0,rewards);
            // Furnished like a generated room as well (the level's and biome's prop rules), around the room's own pieces.
            var (rules,_)=Furnishing(profiles[index]);
            if(template.addFurnishing && rules!=null && left<available.Count)PlaceRules(index,available.GetRange(left,available.Count-left),rules);
        }
    }

    // Chests and breakables placed by hand in a template have no loot of their own: they get the room's or the floor's,
    // and a seed from where they stand (no random draws, so the floor's other seeds stay as they were). One that has
    // its own loot set keeps it.
    void StockPlacedLoot(GameObject instance,LootSource rewards)
    {
        foreach(var chest in instance.GetComponentsInChildren<DungeonContainer>(true))
        {
            if(chest.loot==null)chest.loot=First(rewards,FloorLoot);
            chest.seed=PlacedSeed(chest.transform.position);chest.floorNumber=FloorNumber;
            if(chest.openAudio==null)chest.openAudio=chest.chest ? data.chestOpenAudio : data.containerBreakAudio;
        }
        foreach(var breakable in instance.GetComponentsInChildren<DungeonDestructible>(true))
        {
            if(breakable.loot==null)breakable.loot=First(rewards,FloorLoot);
            breakable.seed=PlacedSeed(breakable.transform.position);breakable.floorNumber=FloorNumber;
        }
    }
    // The floor's seed is in it too: a fixed map (a portal's pocket, an authored level) puts its chests in the same
    // places every run, and their loot (the Skeleton Party's Legendary) was the same every run.
    public int PlacedSeed(Vector3 p)=>unchecked(Mathf.RoundToInt(p.x*100f)*73856093 ^ Mathf.RoundToInt(p.z*100f)*19349663 ^ FloorNumber*83492791 ^ Layout.rooms.Count*2654435 ^ Layout.seed*1103515245);

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
        cursor=Mathf.Min(cursor,available.Count);   // a room full of props has no cells left
        if(index==MerchantRoom && cursor<available.Count)cursor++; // keep a free cell for the stall
        var breakables=Destructibles;
        if(breakables!=null && breakables.Length>0)
        {
            int min=Mathf.Clamp(data.destructiblesPerRoom.x,0,8),max=Mathf.Clamp(data.destructiblesPerRoom.y,min,8);
            int count=extraRandom.Next(min,max+1);
            // A board has no walls or ceiling for corner pieces (cobwebs) to hang from.
            var corners=Board ? new System.Collections.Generic.List<Pose>() : Corners(index);
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
        if(d==null){SeedGroup(go,seed,rewardOverride);return go.GetComponentInChildren<DungeonDestructible>();}
        d.loot=First(rewardOverride,FloorLoot);
        d.seed=seed;d.floorNumber=FloorNumber;
        return d;
    }

    // A group (barrels stacked, a crate with sacks) holds several breakables: each gets the floor's loot and its own
    // seed made from the group's, so no extra random draws are taken and the floor's other seeds stay as they were.
    void SeedGroup(GameObject group,int seed,LootSource rewardOverride=null)
    {
        int i=0;
        foreach(var part in group.GetComponentsInChildren<DungeonDestructible>(true))
        {
            part.loot=First(rewardOverride,FloorLoot);
            part.seed=unchecked(seed*31+(++i)*7919);part.floorNumber=FloorNumber;
        }
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

    // Close to the nearest wall, facing it (DungeonSocket.faceWall).
    // A guard set facing the wall (DungeonSocket.faceWall), its back to the way in: the player comes in behind it. The
    // way in is the room's opening nearest the starting room; it turns to the room's real wall away from that (pillars,
    // arches and props don't count), steps up to it and faces it.
    void FaceWall(Character enemy,float gap=.75f)
    {
        var at=enemy.transform.position;
        int room=Layout.RoomAt(CellOf(at));
        var away=enemy.transform.forward;
        if(room>=0)
        {
            var start=Cell(Layout.RoomCenter(0));
            Vector3 door=Vector3.zero; float nearest=float.MaxValue;
            foreach(var cell in Openings(room)){var c=Cell(cell);float d=(c-start).sqrMagnitude;if(d<nearest){nearest=d;door=c;}}
            if(nearest<float.MaxValue){away=at-door;away.y=0f;}
        }
        if(away.sqrMagnitude<.01f)away=enemy.transform.forward;
        away.Normalize();
        var chest=at+Vector3.up*1.2f;
        Vector3 best=Vector3.zero; float bestScore=float.MaxValue, bestDistance=0f;
        for(int k=-2;k<=2;k++)
        {
            var dir=Quaternion.Euler(0,k*30f,0)*away;
            float distance=WallDistance(chest,dir,9f);
            if(distance<0f)continue;
            // The straight-away wall first; a turned one only when it is much nearer.
            float score=distance+Mathf.Abs(k)*1.5f;
            if(score<bestScore){bestScore=score;best=dir;bestDistance=distance;}
        }
        if(best==Vector3.zero)return;
        var spot=at+best*Mathf.Max(0f,bestDistance-gap);
        if(NavMesh.SamplePosition(spot,out var hit,.6f,NavMesh.AllAreas) && RoomForBody(hit.position))
        {
            var agent=enemy.GetComponent<NavMeshAgent>();
            if(agent!=null && agent.enabled && agent.isOnNavMesh)agent.Warp(hit.position); else enemy.transform.position=hit.position;
        }
        enemy.transform.rotation=Quaternion.LookRotation(best);
        if(enemy.TryGetComponent<Perception>(out var perception))perception.NoCloseAwareness=true;
    }

    // How far along dir the room's own wall is, skipping characters and furnishing (kit pieces, template pieces,
    // breakables); -1 if none within range.
    static readonly RaycastHit[] wallHits=new RaycastHit[16];
    float WallDistance(Vector3 from,Vector3 dir,float range)
    {
        int n=Physics.RaycastNonAlloc(from,dir,wallHits,range,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
        float nearest=-1f;
        for(int i=0;i<n;i++)
        {
            var c=wallHits[i].collider;
            if(c.GetComponentInParent<Character>()!=null || c.GetComponentInParent<DungeonKitPiece>()!=null || c.GetComponentInParent<DungeonKitBlock>()!=null
               || c.GetComponentInParent<DungeonTemplatePiece>()!=null || c.GetComponentInParent<DungeonDestructible>()!=null)continue;
            if(nearest<0f || wallHits[i].distance<nearest)nearest=wallHits[i].distance;
        }
        return nearest;
    }

    Character SpawnEnemyAt(GameObject prefab,Vector3 pos,Quaternion rotation,LootSource rewards,bool boss=false,bool anywhere=false)
    {
        if(prefab==null)return null;
        if(!NavMesh.SamplePosition(pos,out var hit,2,NavMesh.AllAreas))
        {
            if(!anywhere)return null;
            hit=new NavMeshHit{position=pos};
        }
        // A socket's spot inside a prop or stair: the nearest clear floor around it instead.
        if(!boss && !anywhere && !RoomForBody(hit.position))
        {
            bool found=false;
            for(int k=0;k<12 && !found;k++)
            {
                var around=hit.position+Quaternion.Euler(0,k*30f,0)*Vector3.forward*(.6f+.25f*(k/4));
                if(NavMesh.SamplePosition(around,out var near,.6f,NavMesh.AllAreas) && RoomForBody(near.position)){hit=near;found=true;}
            }
            if(!found)return null;
        }
        return SetUpEnemy(Instantiate(prefab,hit.position,rotation,transform),extraRandom,rewards,boss);
    }

    // Floor scaling for every enemy the generator places. With a loot stream, also the room's rewards
    // (or the floor's loot) and a seed on its loot drop.
    // A creature that comes out of something mid-floor (a crate's rat): floor scaling, loot and the NavMesh, like any
    // generated enemy. Uses its own random so the level's draw order never shifts.
    int ambushersSpawned;
    public Character SpawnAmbusher(GameObject prefab,Vector3 pos,Quaternion rotation)
    {
        if(!NavMesh.SamplePosition(pos,out var hit,2f,NavMesh.AllAreas))return null;
        var go=Instantiate(prefab,hit.position,rotation,transform);
        // Mid-floor the NavMesh may be re-carving (an ambush's gates coming down): put the agent on it if it missed.
        var agent=go.GetComponent<NavMeshAgent>();
        if(agent!=null && agent.enabled && !agent.isOnNavMesh)agent.Warp(hit.position);
        // Each one rolls its own loot: from the floor's seed, its place and how many came before (the old float-to-int
        // hash overflowed at the table's position, so every ambusher rolled the same).
        return SetUpEnemy(go,new System.Random(Scramble(unchecked(PlacedSeed(pos)^seed0*31^++ambushersSpawned*486187739))));
    }

    // A hand-placed enemy (TakePlacedEnemies) on the floor where its author put it, switched on. Its loot seed comes from
    // its place, so the level's draw order is the same as without it.
    Character PutDownPlacedEnemy(DungeonSocket socket,LootSource rewards)
    {
        var go=socket.placedEnemy; var t=socket.transform;
        var pos=NavMesh.SamplePosition(t.position,out var hit,2f,NavMesh.AllAreas) ? hit.position : t.position;
        go.transform.SetPositionAndRotation(pos,t.rotation);
        go.SetActive(true);
        var agent=go.GetComponent<NavMeshAgent>();
        if(agent!=null && agent.enabled && !agent.isOnNavMesh)agent.Warp(pos);
        return SetUpEnemy(go,new System.Random(Scramble(unchecked(PlacedSeed(t.position)^seed0*31))),rewards);
    }

    Character SetUpEnemy(GameObject enemy,System.Random lootRandom,LootSource rewards=null,bool boss=false)
    {
        var character=enemy.GetComponent<Character>();
        if(character!=null && data.balance!=null)data.balance.Apply(character,FloorNumber,boss);
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
                        // An ambush room starts empty: its sockets are where the waves come from.
                        if(EventOf(room)==RoomEvent.Ambush)
                        {
                            if(!ambushPoints.TryGetValue(room,out var points))ambushPoints[room]=points=new();
                            points.Add(t.position);
                            if(socket.placedEnemy!=null)DestroyImmediate(socket.placedEnemy);
                            break;
                        }
                        if(socket.placedEnemy!=null)
                        {
                            var hand=PutDownPlacedEnemy(socket,profiles[room]!=null ? profiles[room].rewards : null);
                            if(hand!=null && socket.faceWall)FaceWall(hand,socket.wallGap);
                            JoinPack(room,hand);
                            break;
                        }
                        var prefab=socket.overridePrefab!=null ? socket.overridePrefab : socket.archer ? ChooseArcher(room,extraRandom) : ChooseEnemy(room,extraRandom);
                        var placed=SpawnEnemyAt(prefab,t.position,t.rotation,profiles[room]!=null ? profiles[room].rewards : null);
                        // A socket's own enemy (a set piece such as the court's witch) is put down even where the floor
                        // check finds no room for a body: the template's author placed it there on purpose.
                        if(placed==null && socket.overridePrefab!=null)
                        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                            Debug.LogWarning($"[Sockets] {prefab.name} found no clear floor at {t.position} (room {room}); placed there anyway.",this);
#endif
                            placed=SpawnEnemyAt(prefab,t.position,t.rotation,profiles[room]!=null ? profiles[room].rewards : null,false,true);
                        }
                        if(placed!=null && socket.faceWall)FaceWall(placed,socket.wallGap);
                        JoinPack(room,placed);
                        break;
                    }
                    case DungeonSocketType.Boss:
                        if(Milestone!=null && boss==null && room==exitIndex && StageRoom<0)boss=SpawnBoss(t.position,t.rotation);
                        break;
                    case DungeonSocketType.Merchant:
                        if(room==MerchantRoom)SpawnMerchant(t.position,t.rotation);
                        break;
                }
            }
        }
        // A stage fight: the Dungeon Master sets the boss down when the player reaches the middle.
        if(StageRoom>=0 && Milestone!=null)gameObject.AddComponent<BossStageEncounter>().Initialize(this,Milestone,exitStair);
        else if(Milestone!=null && boss==null && exitIndex>=0)
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

    // Characters (a player still standing where the last floor's stairs were, another peer) never block: the stall
    // must land on the same cell on every machine.
    bool Blocked(Vector3 pos)
    {
        int n=Physics.OverlapBoxNonAlloc(pos+Vector3.up*.6f,new Vector3(.45f,.5f,.45f),blockHits,Quaternion.identity,~0,QueryTriggerInteraction.Ignore);
        for(int i=0;i<n;i++) if(blockHits[i]!=null && blockHits[i].GetComponentInParent<Character>()==null) return true;
        return false;
    }
    static readonly Collider[] blockHits=new Collider[32];
    Merchant FindAnyMerchant()=>GetComponentInChildren<Merchant>(true);
    public Merchant Merchant=>FindAnyMerchant();

    Character SpawnBoss(Vector3 pos,Quaternion rotation)
    {
        // With a boss chest the reward goes in the chest (SpawnBossChest); the body drops like any enemy's.
        bool chest=Milestone.rewardChest;
        var boss=SpawnEnemyAt(Milestone.boss,pos,rotation,chest ? null : Milestone.bossLoot,true);
        if(boss==null){Debug.LogWarning($"Milestone boss {Milestone.boss.name} could not be placed on navigation.",this);return null;}
        var drop=boss.GetComponent<DungeonLootDrop>();
        if(drop!=null && !chest){if(Milestone.bossLoot!=null){drop.table=Milestone.bossLoot;drop.overrideLevelTable=true;}drop.bonusGold+=Milestone.bonusGold;}
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
