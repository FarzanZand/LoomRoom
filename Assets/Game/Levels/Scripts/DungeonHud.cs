using UnityEngine;
using UnityEngine.UI;

public class DungeonHud : MonoBehaviour
{
    DungeonGenerator dungeon;
    GameObject canvasRoot;
    Texture2D map;
    bool[,] shop, exitRoom;
    bool pocket;
    RawImage mapImage;
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
            shop=new bool[w,h];exitRoom=new bool[w,h];
            if(dungeon.MerchantRoom>=0)foreach(var c in dungeon.Layout.RoomCells(dungeon.MerchantRoom))if(c.x>=0 && c.y>=0 && c.x<w && c.y<h)shop[c.x,c.y]=true;
            // A pocket (the Skeleton Party) has no exit: its last room is where the portal back stands.
            pocket=dungeon.LevelData!=null && dungeon.LevelData.pocket;
            int exit=pocket ? -1 : dungeon.Layout.RoomAt(dungeon.Layout.Exit);
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
            if(explored[x,y] && dungeon.Layout.floor[x,y])color=shop[x,y] ? new Color(.36f,.68f,.34f,.95f) : new Color(.46f,.48f,.43f,.92f); // the shop room shows green
            // The exit room shows red, its stairs a brighter red.
            if(explored[x,y] && dungeon.Layout.floor[x,y] && exitRoom[x,y])color=new Color(.72f,.2f,.17f,.95f);
            if(!pocket && explored[x,y] && new Vector2Int(x,y)==dungeon.Layout.Exit)color=new Color(1f,.35f,.28f);
            if(explored[x,y] && door[x,y] && !walked[x,y])color=new Color(.55f,.85f,1f,1f);
            if(x==px && y==py)color=new Color(1,.8f,.33f);
            map.SetPixel(x,y,color);
        }
        map.Apply(false);
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

    void OnDestroy(){if(map!=null)Destroy(map);}
}
