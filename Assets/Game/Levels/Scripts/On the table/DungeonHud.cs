using UnityEngine;
using UnityEngine.UI;

public class DungeonHud : MonoBehaviour
{
    DungeonGenerator dungeon;
    GameObject canvasRoot;
    Texture2D map;
    bool[,] shop, exitRoom, eventRoom;
    bool pocket;
    RawImage mapImage;
    // Enemies are drawn on a finer layer over the map (MarkerScale pixels a cell), so a dot sits where the enemy stands
    // and an elite's dot can have a rim.
    const int MarkerScale = 4;
    Texture2D markers;
    Color32[] markerPixels;
    static readonly Color32 EnemyDot = new Color32(235, 38, 38, 255), EliteRim = new Color32(240, 192, 64, 255), Clear = new Color32(0, 0, 0, 0);
    bool[,] explored, walked, door;
    int lastRoom=-2;
    float next;
    public void Initialize(DungeonGenerator generator)
    {
        dungeon=generator;
        int w=dungeon.Layout.floor.GetLength(0),h=dungeon.Layout.floor.GetLength(1);
        explored=new bool[w,h];walked=new bool[w,h];map=new Texture2D(w,h,TextureFormat.RGBA32,false);map.filterMode=FilterMode.Point;
        canvasRoot=new GameObject("Dungeon HUD",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
        canvasRoot.SetActive(false); // Never render an uninitialized map during level construction.
        canvasRoot.transform.SetParent(transform,false);
        var canvas=canvasRoot.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=15;
        var scaler=canvasRoot.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
        var go=new GameObject("Explored map",typeof(RectTransform),typeof(RawImage));go.transform.SetParent(canvasRoot.transform,false);
        mapImage=go.GetComponent<RawImage>();mapImage.texture=map;mapImage.raycastTarget=false;
        var r=mapImage.rectTransform;r.anchorMin=r.anchorMax=new Vector2(1,1);r.pivot=new Vector2(1,1);r.anchoredPosition=new Vector2(-30,-24);r.sizeDelta=new Vector2(w*4,h*4);
        markers=new Texture2D(w*MarkerScale,h*MarkerScale,TextureFormat.RGBA32,false);markers.filterMode=FilterMode.Point;markerPixels=new Color32[markers.width*markers.height];
        var dots=new GameObject("Enemy dots",typeof(RectTransform),typeof(RawImage));dots.transform.SetParent(go.transform,false);
        var dotImage=dots.GetComponent<RawImage>();dotImage.texture=markers;dotImage.raycastTarget=false;
        var dr=dotImage.rectTransform;dr.anchorMin=Vector2.zero;dr.anchorMax=Vector2.one;dr.offsetMin=dr.offsetMax=Vector2.zero;
        gameObject.AddComponent<DungeonCombatFeedback>().Initialize(generator,canvasRoot.GetComponent<RectTransform>());
    }

    void Update()
    {
        // Runtime layout and exploration arrays do not survive an editor domain reload.
        if(canvasRoot==null || dungeon==null || dungeon.Layout==null || map==null || explored==null)return;
        var players=PlayerManager.HasInstance ? PlayerManager.Instance : null;
        bool show=players!=null && players.ActiveKind==PlayerKind.Table && GameManager.HasInstance && GameManager.Instance.GameplayActive && players.Active!=null;
        var player=show ? players.Active : null;
        int w=map.width,h=map.height,px=0,py=0;
        if(show)
        {
            var origin=dungeon.Cell(Vector2Int.zero);float cell=(dungeon.Cell(Vector2Int.right)-origin).x;
            px=Mathf.RoundToInt((player.transform.position.x-origin.x)/cell);py=Mathf.RoundToInt((player.transform.position.z-origin.z)/cell);
            // Only the map of the floor the player stands in: a pocket (the Skeleton Party) is built beside the floor with
            // a map of its own, and both drew in the same corner, the pocket's over the floor's.
            show=Standing(px,py);
        }
        canvasRoot.SetActive(show);if(!show || Time.time<next)return;next=Time.time+.15f;
        if(shop==null)
        {
            shop=new bool[w,h];exitRoom=new bool[w,h];eventRoom=new bool[w,h];
            if(dungeon.MerchantRoom>=0)foreach(var c in dungeon.Layout.RoomCells(dungeon.MerchantRoom))if(c.x>=0 && c.y>=0 && c.x<w && c.y<h)shop[c.x,c.y]=true;
            // Rooms with an event of their own (the bell, the dice, the duel, Not a witch...) show purple; the tavern and
            // the merchant's shop keep the shop's green.
            foreach(var scripted in dungeon.GetComponentsInChildren<ScriptedEventRoom>(true))
            {
                if(scripted.eventName=="tavern" || scripted.eventName=="shop" || !scripted.Find() || scripted.Dungeon!=dungeon || scripted.Room==dungeon.MerchantRoom)continue;
                foreach(var c in dungeon.Layout.RoomCells(scripted.Room))if(c.x>=0 && c.y>=0 && c.x<w && c.y<h)eventRoom[c.x,c.y]=true;
            }
            // A pocket (the Skeleton Party) has no exit: its last room is where the portal back stands.
            pocket=dungeon.LevelData!=null && dungeon.LevelData.pocket;
            // Where the stairs really stand (a merchant's stall or a boss stage can move them off the layout's exit).
            int exit=pocket ? -1 : dungeon.Layout.RoomAt(dungeon.ExitCell);
            if(exit<0 && !pocket && dungeon.Roles!=null)exit=System.Array.IndexOf(dungeon.Roles,DungeonGenerator.RoomRole.Exit);
            if(exit>=0)foreach(var c in dungeon.Layout.RoomCells(exit))if(c.x>=0 && c.y>=0 && c.x<w && c.y<h)exitRoom[c.x,c.y]=true;
        }
        if(door==null)
        {
            // Doorways: passage cells beside a room's floor. Each shows a light blue dot until the player has walked
            // through it, so the ways not taken yet stand out.
            door=new bool[w,h];
            for(int x=0;x<w;x++)for(int y=0;y<h;y++)
            {
                if(!dungeon.Layout.floor[x,y] || dungeon.Layout.RoomAt(new Vector2Int(x,y))>=0)continue;
                foreach(var d in DungeonLayout.Neighbours)
                {
                    var n=new Vector2Int(x+d.x,y+d.y);
                    if(n.x>=0 && n.y>=0 && n.x<w && n.y<h && dungeon.Layout.floor[n.x,n.y] && dungeon.Layout.RoomAt(n)>=0){door[x,y]=true;break;}
                }
            }
        }
        // Walking into a room shows all of it at once.
        int room=dungeon.Layout.RoomAt(new Vector2Int(px,py));
        // Its doorways too: they lie just outside the room, so the dots show at once (the starting room's door is out of
        // the near reveal's reach from the middle of the room).
        if(room!=lastRoom)
        {
            lastRoom=room;
            if(room>=0)foreach(var c in dungeon.Layout.RoomCells(room))
            {
                if(c.x<0 || c.y<0 || c.x>=w || c.y>=h)continue;
                explored[c.x,c.y]=true;
                foreach(var d in DungeonLayout.Neighbours){int x=c.x+d.x,y=c.y+d.y;if(x>=0 && y>=0 && x<w && y<h && door[x,y])explored[x,y]=true;}
            }
        }
        // A doorway counts as taken once stood in; the other half of a two-cell passage goes with it.
        if(px>=0 && py>=0 && px<w && py<h)
        {
            walked[px,py]=true;
            if(door[px,py])foreach(var d in DungeonLayout.Neighbours){int x=px+d.x,y=py+d.y;if(x>=0 && y>=0 && x<w && y<h && door[x,y])walked[x,y]=true;}
        }
        for(int x=0;x<w;x++)for(int y=0;y<h;y++)
        {
            if(Mathf.Abs(x-px)<=3 && Mathf.Abs(y-py)<=3)explored[x,y]=true;
            Color color=new Color(.025f,.035f,.04f,.85f);
            // The way on: unexplored floor touching explored floor shows as a dim pixel, so a passage running off the
            // edge of what has been seen doesn't look like a dead end.
            if(!explored[x,y] && dungeon.Layout.floor[x,y] && Frontier(x,y,w,h))color=new Color(.27f,.29f,.26f,.9f);
            if(explored[x,y] && dungeon.Layout.floor[x,y])color=shop[x,y] ? new Color(.36f,.68f,.34f,.95f) : eventRoom[x,y] ? new Color(.5f,.3f,.68f,.95f) : new Color(.46f,.48f,.43f,.92f); // the shop room shows green, event rooms purple
            // The exit room shows red, its stairs a brighter red.
            if(explored[x,y] && dungeon.Layout.floor[x,y] && exitRoom[x,y])color=new Color(.72f,.2f,.17f,.95f);
            if(!pocket && explored[x,y] && x==dungeon.ExitCell.x && y==dungeon.ExitCell.y)color=new Color(1f,.35f,.28f);
            if(explored[x,y] && door[x,y] && !walked[x,y])color=new Color(.55f,.85f,1f,1f);
            if(x==px && y==py)color=new Color(1,.8f,.33f);
            map.SetPixel(x,y,color);
        }
        map.Apply(false);
        DrawEnemies(w,h);
    }

    // Every living enemy on explored floor as a red dot where it stands; an elite's has a gold rim.
    void DrawEnemies(int w,int h)
    {
        System.Array.Fill(markerPixels,Clear);
        var origin=dungeon.Cell(Vector2Int.zero);float cell=(dungeon.Cell(Vector2Int.right)-origin).x;
        int mw=markers.width,mh=markers.height;
        foreach(var enemy in EnemyBrain.Active)
        {
            if(enemy==null || enemy.Character==null || !enemy.Character.IsAlive)continue;
            var at=enemy.transform.position;
            float cx=(at.x-origin.x)/cell,cy=(at.z-origin.z)/cell;
            int x=Mathf.RoundToInt(cx),y=Mathf.RoundToInt(cy);
            if(x<0 || y<0 || x>=w || y>=h || !explored[x,y] || !dungeon.Layout.floor[x,y])continue;
            int mx=Mathf.FloorToInt((cx+.5f)*MarkerScale),my=Mathf.FloorToInt((cy+.5f)*MarkerScale);
            bool elite=enemy.TryGetComponent<EliteEnemy>(out _);
            if(elite)Dot(mx,my,2,EliteRim,mw,mh);
            Dot(mx,my,1,EnemyDot,mw,mh);
        }
        markers.SetPixels32(markerPixels);markers.Apply(false);
    }

    void Dot(int cx,int cy,int radius,Color32 color,int mw,int mh)
    {
        for(int x=cx-radius;x<=cx+radius;x++)for(int y=cy-radius;y<=cy+radius;y++)
            if(x>=0 && y>=0 && x<mw && y<mh)markerPixels[y*mw+x]=color;
    }

    // The whole floor on the minimap at once (the spawn window's Show map cheat).
    public void RevealAll()
    {
        if(dungeon==null || dungeon.Layout==null || explored==null)return;
        int w=explored.GetLength(0),h=explored.GetLength(1);
        for(int x=0;x<w;x++)for(int y=0;y<h;y++)if(dungeon.Layout.floor[x,y])explored[x,y]=true;
        next=0f;
    }

    bool Frontier(int x,int y,int w,int h)
    {
        foreach(var d in DungeonLayout.Neighbours)
        {
            int nx=x+d.x,ny=y+d.y;
            if(nx>=0 && ny>=0 && nx<w && ny<h && explored[nx,ny] && dungeon.Layout.floor[nx,ny])return true;
        }
        return false;
    }

    // On (or next to) a floor cell of this layout.
    bool Standing(int px,int py)
    {
        var floor=dungeon.Layout.floor;
        for(int dx=-1;dx<=1;dx++)for(int dy=-1;dy<=1;dy++)
        {
            int x=px+dx,y=py+dy;
            if(x>=0 && y>=0 && x<floor.GetLength(0) && y<floor.GetLength(1) && floor[x,y])return true;
        }
        return false;
    }

    void OnDestroy(){if(map!=null)Destroy(map);if(markers!=null)Destroy(markers);}
}
