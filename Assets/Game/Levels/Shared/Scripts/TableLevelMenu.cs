using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class TableLevelMenu : MonoBehaviour
{
    public TableLevelLoader loader;
    public bool IsOpen => root!=null && root.activeSelf;
    GameObject root;
    Font font;
    public void Show(string heading)
    {
        foreach(var inventory in FindObjectsByType<InventoryUI>()) inventory.Close();
        if(root!=null) Destroy(root);
        GameManager.Instance.Push(GameState.Menu);
        font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        root=new GameObject("Table adventures",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
        var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=800;
        var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
        var shade=Panel("Backdrop",root.transform,new Color(.025f,.035f,.05f,.94f));Stretch(shade.rectTransform);
        var panel=Panel("Adventure panel",root.transform,new Color(.075f,.09f,.105f,1));
        var rect=panel.rectTransform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.sizeDelta=new Vector2(760,690);
        Label(panel.transform,"THE TABLE",new Vector2(60,-42),new Vector2(640,30),18,new Color(.8f,.64f,.37f));
        Label(panel.transform,heading,new Vector2(60,-89),new Vector2(640,85),33,Color.white);
        Label(panel.transform,"A familiar town. An unfamiliar path below.",new Vector2(60,-180),new Vector2(640,35),19,new Color(.64f,.71f,.76f));
        int i=0;
        if(loader.catalog!=null && loader.catalog.levels!=null) foreach(var level in loader.catalog.levels)
        {
            if(level==null)continue;
            var captured=level;
            var button=MakeButton(panel.transform,level.displayName+"\n<size=17>"+level.description+"</size>",-245-i*108,() => loader.Load(captured),88);
            if(i++==0 && EventSystem.current!=null) EventSystem.current.SetSelectedGameObject(button.gameObject);
        }
        bool alive=PlayerManager.Instance.Active==null || PlayerManager.Instance.Active.IsAlive;
        if(alive) MakeButton(panel.transform,"Resume / Cancel",-490,Hide,52);
        MakeButton(panel.transform,"Return to room",-552,loader.ReturnToRoom,52);
        Label(panel.transform,"Dungeon1 starts a fresh run • Equipment and healing only",new Vector2(60,-624),new Vector2(640,30),15,new Color(.5f,.57f,.62f));
    }
    public void Hide()
    {
        if(root!=null)root.SetActive(false);
        if(GameManager.HasInstance)GameManager.Instance.Pop(GameState.Menu);
    }
    void OnDestroy() { if(root!=null)Destroy(root); }
    Image Panel(string name,Transform parent,Color color)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);
        var image=go.GetComponent<Image>();image.color=color;return image;
    }
    static void Stretch(RectTransform r){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;}
    Text Label(Transform parent,string text,Vector2 pos,Vector2 size,int points,Color color)
    {
        var go=new GameObject("Label",typeof(RectTransform),typeof(Text));go.transform.SetParent(parent,false);
        var r=go.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=pos;r.sizeDelta=size;
        var label=go.GetComponent<Text>();label.font=font;label.text=text;label.fontSize=points;label.color=color;label.supportRichText=true;label.raycastTarget=false;return label;
    }
    Button MakeButton(Transform parent,string label,float y,UnityEngine.Events.UnityAction action,float height)
    {
        var image=Panel(label,parent,new Color(.14f,.18f,.2f));var r=image.rectTransform;r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(60,y);r.sizeDelta=new Vector2(640,height);
        var button=image.gameObject.AddComponent<Button>();button.targetGraphic=image;button.onClick.AddListener(action);
        var colors=button.colors;colors.highlightedColor=new Color(.85f,.72f,.45f);colors.selectedColor=colors.highlightedColor;button.colors=colors;
        var text=Label(r,label,new Vector2(22,-10),new Vector2(596,height-16),25,new Color(.94f,.91f,.83f));text.alignment=TextAnchor.MiddleLeft;
        return button;
    }
}
