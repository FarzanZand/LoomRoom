using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DungeonHud : MonoBehaviour
{
    DungeonGenerator dungeon;
    GameObject canvasRoot;
    Texture2D map;
    RawImage mapImage;
    bool[,] explored;
    float next;
    public void Initialize(DungeonGenerator generator)
    {
        dungeon=generator;
        int w=dungeon.Layout.floor.GetLength(0),h=dungeon.Layout.floor.GetLength(1);
        explored=new bool[w,h];map=new Texture2D(w,h,TextureFormat.RGBA32,false);map.filterMode=FilterMode.Point;
        canvasRoot=new GameObject("Dungeon HUD",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
        canvasRoot.SetActive(false); // Never render an uninitialized map during level construction.
        canvasRoot.transform.SetParent(transform,false);
        var canvas=canvasRoot.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=15;
        var scaler=canvasRoot.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
        var go=new GameObject("Explored map",typeof(RectTransform),typeof(RawImage));go.transform.SetParent(canvasRoot.transform,false);
        mapImage=go.GetComponent<RawImage>();mapImage.texture=map;mapImage.raycastTarget=false;
        var r=mapImage.rectTransform;r.anchorMin=r.anchorMax=new Vector2(1,1);r.pivot=new Vector2(1,1);r.anchoredPosition=new Vector2(-30,-54);r.sizeDelta=new Vector2(w*4,h*4);
        var loader=TableManager.HasInstance ? TableManager.Instance.GetComponent<TableLevelLoader>() : null;
        string heading=generator.LevelData.displayName.ToUpperInvariant()+(generator.LevelData.multipleLevels ? " · "+generator.FloorNumber+" / "+Mathf.Max(1,generator.LevelData.levelCount) : "");
        var title=Label(heading,18);var tr=title.rectTransform;tr.anchorMin=tr.anchorMax=new Vector2(1,1);tr.pivot=new Vector2(1,1);tr.anchoredPosition=new Vector2(-30,-24);tr.sizeDelta=new Vector2(340,28);title.alignment=TextAlignmentOptions.MidlineRight;
        gameObject.AddComponent<DungeonCombatFeedback>().Initialize(generator,canvasRoot.GetComponent<RectTransform>(),generator.LevelData.hudFont);
    }
    TMP_Text Label(string value,int size)
    {
        var go=new GameObject(value,typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(canvasRoot.transform,false);
        var text=go.GetComponent<TextMeshProUGUI>();if(dungeon.LevelData.hudFont!=null)text.font=dungeon.LevelData.hudFont;text.text=value;text.fontSize=size+4;text.color=new Color(.9f,.83f,.66f);text.raycastTarget=false;return text;
    }

    void Update()
    {
        // Runtime layout and exploration arrays do not survive an editor domain reload.
        if(canvasRoot==null || dungeon==null || dungeon.Layout==null || map==null || explored==null)return;
        bool show=PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind==PlayerKind.Table && GameManager.Instance.GameplayActive;
        canvasRoot.SetActive(show);if(!show || Time.time<next)return;next=Time.time+.15f;
        var player=PlayerManager.Instance.Active;
        int w=map.width,h=map.height;
        var origin=dungeon.Cell(Vector2Int.zero);float cell=(dungeon.Cell(Vector2Int.right)-origin).x;
        int px=Mathf.RoundToInt((player.transform.position.x-origin.x)/cell),py=Mathf.RoundToInt((player.transform.position.z-origin.z)/cell);
        for(int x=0;x<w;x++)for(int y=0;y<h;y++)
        {
            if(Mathf.Abs(x-px)<=3 && Mathf.Abs(y-py)<=3)explored[x,y]=true;
            Color color=new Color(.025f,.035f,.04f,.85f);
            if(explored[x,y] && dungeon.Layout.floor[x,y])color=new Color(.46f,.48f,.43f,.92f);
            if(explored[x,y] && new Vector2Int(x,y)==dungeon.Layout.Exit)color=new Color(.4f,.85f,.6f);
            if(x==px && y==py)color=new Color(1,.8f,.33f);
            map.SetPixel(x,y,color);
        }
        map.Apply(false);
    }

    void OnDestroy(){if(map!=null)Destroy(map);}
}
