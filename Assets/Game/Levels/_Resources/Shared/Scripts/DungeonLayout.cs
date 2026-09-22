using System;
using System.Collections.Generic;
using UnityEngine;

// Seeded partition layout with explicit room connections and routed corridor sections.
public sealed class DungeonLayout
{
    public readonly bool[,] floor;
    public readonly List<RectInt> rooms = new();
    public readonly int seed;
    public readonly List<Vector2Int> Connections = new();
    public readonly HashSet<Vector2Int> Reserved = new();
    public int[] RoomDistances { get; private set; }
    readonly int[,] corridorOwners;
    int nextCorridor;
    public int[,] RegionIds { get; private set; }
    public int RegionCount { get; private set; }
    public int CorridorCount => RegionCount - rooms.Count;
    public Vector2Int Start => Center(rooms[0]);
    public Vector2Int Exit { get; private set; }
    public static Vector2Int Center(RectInt r) => new(r.x + r.width / 2, r.y + r.height / 2);

    public DungeonLayout(int width, int depth, int count, int seed, float loopPercent=15)
    {
        this.seed = seed;
        floor = new bool[width, depth];
        corridorOwners = new int[width, depth];
        for(int x=0;x<width;x++)for(int y=0;y<depth;y++)corridorOwners[x,y]=-1;
        if(width<14 || depth<14 || count<2)throw new ArgumentException("Dungeon requires dimensions >=14 and at least two rooms.");
        var rng = new System.Random(seed);
        var partitions=new List<RectInt>{new RectInt(1,1,width-2,depth-2)};
        int Capacity(RectInt r)=>(r.width/6)*(r.height/6);
        if(Capacity(partitions[0])<count)throw new ArgumentException("Room count exceeds the grid capacity. Increase width/depth or reduce rooms.");
        while(partitions.Count<count) {
            int capacity=0;foreach(var part in partitions)capacity+=Capacity(part);
            var choices=new List<(int index, bool vertical, int cut, int score)>();
            for(int i=0;i<partitions.Count;i++) {
                var r=partitions[i];
                for(int axis=0;axis<2;axis++) {
                    int length=axis==0?r.width:r.height;
                    for(int cut=6;cut<=length-6;cut++) {
                        var one=axis==0?new RectInt(r.x,r.y,cut,r.height):new RectInt(r.x,r.y,r.width,cut);
                        var two=axis==0?new RectInt(r.x+cut,r.y,r.width-cut,r.height):new RectInt(r.x,r.y+cut,r.width,r.height-cut);
                        if(capacity-Capacity(r)+Capacity(one)+Capacity(two)<count)continue;
                        choices.Add((i,axis==0,cut,r.width*r.height-Math.Abs(length-2*cut)*2+rng.Next(20)));
                    }
                }
            }
            if(choices.Count==0)throw new InvalidOperationException("Unable to partition requested rooms.");
            choices.Sort((a,b)=>b.score.CompareTo(a.score));
            var best=choices[0];var old=partitions[best.index];
            partitions[best.index]=best.vertical?new RectInt(old.x,old.y,best.cut,old.height):new RectInt(old.x,old.y,old.width,best.cut);
            partitions.Add(best.vertical?new RectInt(old.x+best.cut,old.y,old.width-best.cut,old.height):new RectInt(old.x,old.y+best.cut,old.width,old.height-best.cut));
        }
        // Separate each chamber with at least two non-room cells; jitter within each parcel.
        foreach(var parcel in partitions) {
            int w=rng.Next(3,Math.Min(8,parcel.width-1)),h=rng.Next(3,Math.Min(8,parcel.height-1));
            var room=new RectInt(rng.Next(parcel.x+1,parcel.xMax-w),rng.Next(parcel.y+1,parcel.yMax-h),w,h);
            rooms.Add(room);
            foreach(var p in room.allPositionsWithin)floor[p.x,p.y]=true;
        }
        var connected=new HashSet<int>{0};
        while(connected.Count<rooms.Count) {
            int from=-1,to=-1,best=int.MaxValue;
            foreach(int i in connected)for(int j=0;j<rooms.Count;j++)if(!connected.Contains(j)) {
                var d=Center(rooms[i])-Center(rooms[j]);int cost=Math.Abs(d.x)+Math.Abs(d.y);
                if(cost<best){best=cost;from=i;to=j;}
            }
            Route(from,to);connected.Add(to);
        }
        // Short additional links create alternative routes, without connecting every room.
        int loops=Mathf.RoundToInt(rooms.Count*Mathf.Clamp01(loopPercent/100f));
        for(int n=0;n<loops;n++) {
            int from=-1,to=-1,best=int.MaxValue;
            for(int i=0;i<rooms.Count;i++)for(int j=i+1;j<rooms.Count;j++) {
                if(Connections.Exists(c=>(c.x==i&&c.y==j)||(c.x==j&&c.y==i)))continue;
                var d=Center(rooms[i])-Center(rooms[j]);int cost=Math.Abs(d.x)+Math.Abs(d.y)+rng.Next(4);
                if(cost<best){best=cost;from=i;to=j;}
            }
            if(from>=0)Route(from,to);
        }
        // Choose the most distant room by walkable path, not Euclidean distance.
        var distance = new int[width, depth];
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(Start); distance[Start.x, Start.y] = 1;
        var directions = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var dir in directions)
            {
                var n = p + dir;
                if (n.x < 0 || n.y < 0 || n.x >= width || n.y >= depth || !floor[n.x,n.y] || distance[n.x,n.y] != 0) continue;
                distance[n.x,n.y] = distance[p.x,p.y] + 1; queue.Enqueue(n);
            }
        }
        Exit = Start;
        foreach (var r in rooms) { var c = Center(r); if (distance[c.x,c.y] > distance[Exit.x,Exit.y]) Exit = c; }
        RoomDistances=new int[rooms.Count];
        for(int i=0;i<rooms.Count;i++){var c=Center(rooms[i]);RoomDistances[i]=distance[c.x,c.y]-1;}
        BuildRegions();
        // Preserve a clear cross in each chamber and clearance at every doorway.
        foreach(var r in rooms)foreach(var p in r.allPositionsWithin) {
            var c=Center(r);
            if(p.x==c.x || p.y==c.y)Reserved.Add(p);
            foreach(var d in directions){var n=p+d;if(!r.Contains(n)&&floor[n.x,n.y]){Reserved.Add(p);Reserved.Add(p-d);}}
        }
        foreach(var p in rooms[0].allPositionsWithin)Reserved.Add(p);
        foreach(var r in rooms)if(r.Contains(Exit))foreach(var p in r.allPositionsWithin)Reserved.Add(p);
    }

    // Preserve authored corridor legs at junctions instead of merging the entire network.
    void BuildRegions()
    {
        int width = floor.GetLength(0), depth = floor.GetLength(1);
        RegionIds = new int[width, depth];
        for (int x = 0; x < width; x++) for (int y = 0; y < depth; y++) RegionIds[x,y] = -1;
        for (int i = 0; i < rooms.Count; i++)
            foreach (var p in rooms[i].allPositionsWithin) RegionIds[p.x,p.y] = i;
        RegionCount = rooms.Count;
        var queue = new Queue<Vector2Int>();
        var directions = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        for (int x = 0; x < width; x++) for (int y = 0; y < depth; y++)
        {
            if (!floor[x,y] || RegionIds[x,y] >= 0) continue;
            int id = RegionCount++;
            int owner = corridorOwners[x,y];
            RegionIds[x,y] = id; queue.Enqueue(new Vector2Int(x,y));
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                foreach (var d in directions)
                {
                    var n = p + d;
                    if (n.x < 0 || n.y < 0 || n.x >= width || n.y >= depth ||
                        !floor[n.x,n.y] || RegionIds[n.x,n.y] >= 0 || corridorOwners[n.x,n.y]!=owner) continue;
                    RegionIds[n.x,n.y] = id; queue.Enqueue(n);
                }
            }
        }
    }

    public bool[] SelectOpenRegions(float percent, int salt)
    {
        var selected = new bool[RegionCount];
        var order = new List<int>();
        for (int i = 0; i < RegionCount; i++) order.Add(i);
        var rng = new System.Random(unchecked(seed + salt));
        for (int i = order.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        int count = Mathf.RoundToInt(RegionCount * Mathf.Clamp(percent, 0, 100) / 100f);
        for (int i = 0; i < count; i++) selected[order[i]] = true;
        return selected;
    }

    // A passage can bend or branch, but walking through it should never require two gates.
    public List<(Vector2Int roomCell, Vector2Int direction)> SelectDoorways(float percent)
    {
        var result = new List<(Vector2Int, Vector2Int)>();
        var visited = new HashSet<Vector2Int>();
        var dirs = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        int width = floor.GetLength(0), depth = floor.GetLength(1);
        bool Floor(Vector2Int p) => p.x >= 0 && p.y >= 0 && p.x < width && p.y < depth && floor[p.x,p.y];
        bool Passage(Vector2Int p) => Floor(p) && RegionIds[p.x,p.y] >= rooms.Count;
        var rng = new System.Random(unchecked(seed + 7321));
        for (int x = 0; x < width; x++) for (int y = 0; y < depth; y++)
        {
            var start = new Vector2Int(x,y);
            if (!Passage(start) || !visited.Add(start)) continue;
            var queue = new Queue<Vector2Int>(); queue.Enqueue(start);
            var entrances = new List<(Vector2Int, Vector2Int)>();
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                foreach (var dir in dirs)
                {
                    var n = p + dir;
                    if (Passage(n)) { if (visited.Add(n)) queue.Enqueue(n); continue; }
                    if (!Floor(n)) continue;
                    var side = new Vector2Int(-dir.y,dir.x);
                    if (!Floor(p + side) && !Floor(p - side)) entrances.Add((n,-dir));
                }
            }
            // One chance per passage, then choose just one of its room entrances.
            if (entrances.Count > 0 && rng.NextDouble() * 100 < Mathf.Clamp(percent,0,100))
                result.Add(entrances[rng.Next(entrances.Count)]);
        }
        return result;
    }

    public int NearestRoom(Vector2Int cell)
    {
        int nearest = 0, distance = int.MaxValue;
        for (int i = 0; i < rooms.Count; i++)
        {
            var r = rooms[i];
            int dx = cell.x - Mathf.Clamp(cell.x, r.xMin, r.xMax - 1);
            int dy = cell.y - Mathf.Clamp(cell.y, r.yMin, r.yMax - 1);
            int d = dx * dx + dy * dy;
            if (d < distance) { distance = d; nearest = i; }
        }
        return nearest;
    }

    // Only remove the outermost wall in each direction, never a wall facing another room.
    public bool FacesOutside(Vector2Int cell, Vector2Int direction)
    {
        for (var p = cell + direction; p.x >= 0 && p.y >= 0 && p.x < floor.GetLength(0) && p.y < floor.GetLength(1); p += direction)
            if (floor[p.x,p.y]) return false;
        return true;
    }

    void Route(int from,int to)
    {
        var start=Center(rooms[from]);var end=Center(rooms[to]);
        int w=floor.GetLength(0),h=floor.GetLength(1);
        var blocked=new bool[w,h];
        for(int i=0;i<rooms.Count;i++)if(i!=from && i!=to)foreach(var p in rooms[i].allPositionsWithin)blocked[p.x,p.y]=true;
        var distances=new int[w,h];var previous=new Vector2Int[w,h];var done=new bool[w,h];
        for(int x=0;x<w;x++)for(int y=0;y<h;y++)distances[x,y]=int.MaxValue;
        distances[start.x,start.y]=0;
        var pending=new List<Vector2Int>{start};
        var dirs=new[]{Vector2Int.right,Vector2Int.up,Vector2Int.left,Vector2Int.down};
        while(pending.Count>0) {
            int best=0;for(int i=1;i<pending.Count;i++)if(distances[pending[i].x,pending[i].y]<distances[pending[best].x,pending[best].y])best=i;
            var p=pending[best];pending.RemoveAt(best);if(done[p.x,p.y])continue;done[p.x,p.y]=true;
            if(p==end)break;
            foreach(var dir in dirs){var n=p+dir;
                if(n.x<1||n.y<1||n.x>=w-1||n.y>=h-1||blocked[n.x,n.y])continue;
                int cost=distances[p.x,p.y]+(floor[n.x,n.y] && !rooms[from].Contains(n) && !rooms[to].Contains(n)?3:1);
                if(cost>=distances[n.x,n.y])continue;
                distances[n.x,n.y]=cost;previous[n.x,n.y]=p;pending.Add(n);
            }
        }
        if(!done[end.x,end.y])throw new InvalidOperationException("Could not route room connection.");
        var path=new List<Vector2Int>();for(var p=end;p!=start;p=previous[p.x,p.y])path.Add(p);path.Add(start);path.Reverse();
        int corridor=nextCorridor++;Vector2Int lastDirection=Vector2Int.zero;
        for(int i=0;i<path.Count;i++) {
            var dir=i>0?path[i]-path[i-1]:Vector2Int.zero;
            if(i>1&&dir!=lastDirection)corridor=nextCorridor++;
            var p=path[i];if(!floor[p.x,p.y]){floor[p.x,p.y]=true;corridorOwners[p.x,p.y]=corridor;}
            lastDirection=dir;
        }
        Connections.Add(new Vector2Int(from,to));
    }
}
