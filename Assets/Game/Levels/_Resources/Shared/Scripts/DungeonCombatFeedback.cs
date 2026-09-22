using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Screen-space pixel UI anchored to creatures in the world. No changes to damage rules.
public class DungeonCombatFeedback : MonoBehaviour
{
    sealed class EnemyView
    {
        public Character character;
        public RectTransform root;
        public Image fill, loss;
        public float until, lossUntil, displayed=1;
        public Action<DamageInfo> handler;
    }
    sealed class Popup
    {
        public TMP_Text label;
        public Character target;
        public Vector3 position;
        public float started, until;
        public Color color;
    }
    readonly List<EnemyView> enemies=new();
    readonly List<Popup> popups=new();
    readonly RaycastHit[] sightHits=new RaycastHit[48];
    RectTransform canvas;
    DungeonGenerator dungeon;
    TMP_FontAsset font;
    TMP_Text message, impact;
    float messageUntil, impactUntil;
    Player player;
    HotbarUI hotbar;
    bool styled;
    int popupIndex;
    public int RegisteredEnemies => enemies.Count;
    public int VisibleBars { get { int n=0;foreach(var e in enemies)if(e.root.gameObject.activeSelf)n++;return n; } }
    public int ActiveNumbers { get {int n=0;foreach(var p in popups)if(p.until>Time.time)n++;return n;} }

    public void Initialize(DungeonGenerator generator, RectTransform parent, TMP_FontAsset pixelFont)
    {
        dungeon=generator;canvas=parent;font=pixelFont;
        foreach(var c in generator.GetComponentsInChildren<Character>()) Register(c);
        Character.Spawned+=Register;
        player=PlayerManager.Instance.GetPlayer(PlayerKind.Table);
        player.Damaged+=PlayerDamaged;player.HitLanded+=HitLanded;
        if(InventoryManager.HasInstance) InventoryManager.Instance.ItemPickedUp+=PickedUp;
        hotbar=FindAnyObjectByType<HotbarUI>(FindObjectsInactive.Include);
        // Pickup placement belongs to the authored prefab, not a hard-coded hotbar offset.
        message=Instantiate(dungeon.LevelData.messagePrefab,canvas);
        impact=Instantiate(dungeon.LevelData.messagePrefab,canvas);SetRect(impact.rectTransform,new Vector2(.5f,.5f),new Vector2(0,-46));
        for(int i=0;i<24;i++)
        {
            var label=Instantiate(dungeon.LevelData.damageNumberPrefab,canvas);label.gameObject.SetActive(false);
            popups.Add(new Popup{label=label});
        }
    }
    void Register(Character character)
    {
        if(character==null || character is Player || !character.transform.IsChildOf(dungeon.transform))return;
        foreach(var e in enemies)if(e.character==character)return;
        var bar=Instantiate(dungeon.LevelData.enemyBarPrefab,canvas);
        var root=bar.Rect;bar.enemyName.text=character.DisplayName;
        var view=new EnemyView{character=character,root=root,fill=bar.healthFill,loss=bar.recentDamageFill};
        view.handler=info=>EnemyDamaged(view,info);character.Damaged+=view.handler;
        root.gameObject.SetActive(false);enemies.Add(view);
        if(character.GetComponent<DungeonHitFlash>()==null)character.gameObject.AddComponent<DungeonHitFlash>();
    }
    void EnemyDamaged(EnemyView view,DamageInfo info)
    {
        view.until=Time.time+3.5f;view.lossUntil=Time.time+.35f;
        ShowNumber(view.character,info.Blocked ? "BLOCK":Mathf.CeilToInt(info.Amount).ToString(),
            info.Blocked ? new Color(.53f,.85f,1):info.Heavy ? new Color(1,.78f,.25f):new Color(1,.93f,.79f));
    }
    void ShowNumber(Character target,string text,Color color)
    {
        var p=popups[popupIndex++%popups.Count];p.target=target;p.position=Head(target);p.started=Time.time;p.until=Time.time+.85f;p.color=color;p.label.text=text;
    }
    void PlayerDamaged(DamageInfo info)
    {
        if(info.Blocked) Result(info.Parried ? "PARRY":"BLOCKED",new Color(.5f,.85f,1),.55f);
        else if(info.Amount>0)Result("−"+Mathf.CeilToInt(info.Amount),new Color(1,.35f,.22f),.45f);
    }
    void HitLanded(DamageInfo info)
    {
        if(!info.Blocked && info.Amount>0)Result(info.Heavy ? "HEAVY HIT":"×",info.Heavy ? new Color(1,.8f,.3f):Color.white,.22f);
    }
    void Result(string text,Color color,float duration){if(impact==null)return;impact.text=text;impact.color=color;impactUntil=Time.time+duration;}
    void PickedUp(ItemData item,Player who,int count)
    {
        if(who!=player || message==null || !GameManager.Instance.GameplayActive)return;
        message.text="Picked up "+item.itemName+(count>1 ? " ×"+count:"");messageUntil=Time.time+2.2f;
    }
    void LateUpdate()
    {
        if(canvas==null || dungeon==null)return;
        bool active=PlayerManager.Instance.Active==player;
        if(styled!=active){styled=active;hotbar?.SetDungeonStyle(styled,font,dungeon.LevelData.hotbarFrame);}
        bool show=active && GameManager.Instance.GameplayActive;
        var camera=PlayerManager.Instance.OutputCamera;
        if(camera==null)return;
        foreach(var e in enemies)
        {
            if(e.character==null){e.root.gameObject.SetActive(false);continue;}
            Vector3 head=Head(e.character);
            float distance=Vector3.Distance(camera.transform.position,head);
            // Aim assist against the body, so small creatures don't require pixel-perfect targeting.
            Vector3 body=e.character.transform.position+(head-e.character.transform.position)*.55f;
            bool aimed=e.character.IsAlive && distance<10 && Vector3.Angle(camera.transform.forward,body-camera.transform.position)<7;
            bool visible=show && distance<18 && (aimed || Time.time<e.until) && HasSight(camera,e.character,body);
            e.root.gameObject.SetActive(visible && Project(camera,head+Vector3.up*.14f,e.root));
            float health=e.character.Stats.MaxHealth>0 ? Mathf.Clamp01(e.character.Stats.CurrentHealth/e.character.Stats.MaxHealth):0;
            e.fill.rectTransform.anchorMax=new Vector2(health,1);
            if(Time.time>e.lossUntil)e.displayed=Mathf.MoveTowards(e.displayed,health,Time.deltaTime*1.6f);
            e.displayed=Mathf.Max(e.displayed,health);e.loss.rectTransform.anchorMax=new Vector2(e.displayed,1);
        }
        foreach(var p in popups)
        {
            bool visible=show && Time.time<p.until && p.target!=null && HasSight(camera,p.target,p.position);
            p.label.gameObject.SetActive(visible && Project(camera,p.position+Vector3.up*((Time.time-p.started)*.65f),p.label.rectTransform));
            var color=p.color;color.a=Mathf.Clamp01((p.until-Time.time)/.25f);p.label.color=color;
        }
        if(message!=null){var color=new Color(.94f,.87f,.69f,Mathf.Clamp01((messageUntil-Time.time)/.4f));message.color=color;}
        if(impact!=null)impact.gameObject.SetActive(show && Time.time<impactUntil);
    }
    public bool HasSight(Camera camera,Character target,Vector3 point)
    {
        Vector3 delta=point-camera.transform.position;
        if(Vector3.Dot(camera.transform.forward,delta)<=0)return false;
        int count=Physics.RaycastNonAlloc(camera.transform.position,delta.normalized,sightHits,delta.magnitude,~0,QueryTriggerInteraction.Ignore);
        if(count==sightHits.Length)return false;
        for(int i=0;i<count;i++)
        {
            var t=sightHits[i].transform;
            if(t.IsChildOf(target.transform) || (player!=null && t.IsChildOf(player.ActivationRoot.transform)))continue;
            return false;
        }
        return true;
    }
    bool Project(Camera camera,Vector3 world,RectTransform rect)
    {
        Vector3 screen=camera.WorldToScreenPoint(world);
        if(screen.z<=0 || screen.x<0 || screen.x>Screen.width || screen.y<0 || screen.y>Screen.height)return false;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas,screen,null,out var local);
        rect.anchoredPosition=local;return true;
    }
    static Vector3 Head(Character c)
    {
        var col=c.GetComponent<CapsuleCollider>();
        if(col!=null)return c.transform.TransformPoint(col.center+Vector3.up*col.height*.5f);
        return c.transform.position+Vector3.up*1.9f;
    }
    static void SetRect(RectTransform r,Vector2 anchor,Vector2 position){r.anchorMin=r.anchorMax=anchor;r.anchoredPosition=position;}
    void OnDisable()
    {
        if(styled){hotbar?.SetDungeonStyle(false);styled=false;}
    }
    void OnDestroy()
    {
        Character.Spawned-=Register;
        foreach(var e in enemies)if(e.character!=null)e.character.Damaged-=e.handler;
        if(player!=null){player.Damaged-=PlayerDamaged;player.HitLanded-=HitLanded;}
        if(InventoryManager.HasInstance)InventoryManager.Instance.ItemPickedUp-=PickedUp;
        if(styled)hotbar?.SetDungeonStyle(false);
    }
}
