using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class EquipmentSlotUI : MonoBehaviour,IPointerClickHandler,IPointerEnterHandler,IPointerExitHandler,IDropHandler,IBeginDragHandler,IDragHandler,IEndDragHandler
{
    public EquipmentSlot slot;
    public Image icon;
    public TMP_Text title;
    [Tooltip("Shown while the slot is empty: a dim outline of what goes here.")]
    public Image silhouette;
    Player player;
    public void Bind(Player owner){player=owner;Refresh();}
    [Tooltip("Shown behind the icon in the worn item's rarity colour.")]
    [UnityEngine.Serialization.FormerlySerializedAs("honedFill")] public Image rarityFill;
    public void Refresh(){var item=player?.Equipment?.Get(slot);if(icon!=null){icon.sprite=item!=null?item.icon:null;icon.enabled=item!=null;
        // A long weapon stands upright in a tall slot; everything else fills the slot.
        var area=((RectTransform)transform).rect.size-Vector2.one*12f;
        ItemGridUI.UprightIcon(icon.rectTransform,area,item!=null && item.GridSize.x==1 && area.y>area.x*1.5f ? new Vector2Int(1,item.GridSize.y) : Vector2Int.one);}if(title!=null)title.text=Display(slot);
        if(silhouette!=null)silhouette.enabled=item==null && silhouette.sprite!=null;
        ItemData.ShowSlotFill(rarityFill,item);}
    public static string Display(EquipmentSlot slot)=>ItemData.SlotName(slot).ToUpperInvariant();
    // Click (either button) takes it off: a worn item goes to the bag and needs room there ("Make room" notice),
    // a hand item is put away and stays in its hotbar slot.
    public void OnPointerClick(PointerEventData e){
        if(player==null || e.dragging || e.button==PointerEventData.InputButton.Middle)return;
        var item=player.Equipment.Get(slot);
        if(item==null || !player.Equipment.TryUnequip(slot))return;
        if(Equipment.IsHand(slot)){var s=UIFeedbackSettings.Shared;s?.Play(s.equipKey);}
        if(TooltipUI.HasInstance)TooltipUI.Instance.Hide();
    }
    // The tooltip sits level with the slot but beside the whole doll, not at the mouse, so it covers no doll slot
    // (review 2026-11-12: beside its own slot, the shield's covered the helm and body slots).
    public void OnPointerEnter(PointerEventData e){var item=player?.Equipment?.Get(slot);if(item!=null && TooltipUI.HasInstance)TooltipUI.Instance.ShowBeside(item,(RectTransform)transform,DollArea());}

    static readonly Vector3[] corners=new Vector3[4];
    // The screen area of every doll slot shown with this one (the same player's, on the same canvas).
    Rect DollArea(){
        var canvas=GetComponentInParent<Canvas>();var root=canvas!=null?canvas.rootCanvas.transform:transform.parent;
        float xMin=float.MaxValue,yMin=float.MaxValue,xMax=float.MinValue,yMax=float.MinValue;
        foreach(var other in root.GetComponentsInChildren<EquipmentSlotUI>()){
            if(other!=this && other.player!=player)continue;
            ((RectTransform)other.transform).GetWorldCorners(corners);
            xMin=Mathf.Min(xMin,corners[0].x);yMin=Mathf.Min(yMin,corners[0].y);xMax=Mathf.Max(xMax,corners[2].x);yMax=Mathf.Max(yMax,corners[2].y);}
        return Rect.MinMaxRect(xMin,yMin,xMax,yMax);}
    public void OnPointerExit(PointerEventData e){if(TooltipUI.HasInstance)TooltipUI.Instance.Hide();}

    // Dragged off the doll: a hand item from its hotbar slot (it can be moved along the hotbar, or swapped with
    // another weapon or shield in the bag), a worn item onto a bag cell.
    public void OnBeginDrag(PointerEventData e){
        var item=player?.Equipment?.Get(slot);
        if(item==null || !ItemDragHandler.HasInstance)return;
        var size=((RectTransform)transform).rect.size;
        if(Equipment.IsHand(slot)){int at=player.Equipment.HeldIndex(slot);if(at<0)return;ItemDragHandler.Instance.BeginEquipped(player.Hotbar,at,null,item,size);}
        else ItemDragHandler.Instance.BeginEquipped(null,-1,slot,item,size);
        if(TooltipUI.HasInstance)TooltipUI.Instance.Hide();
    }
    public void OnDrag(PointerEventData e){}
    public void OnEndDrag(PointerEventData e)=>ItemSlotUI.EndDrag(e);

    public void OnDrop(PointerEventData e){
        if(!ItemDragHandler.HasInstance || !ItemDragHandler.Instance.IsDragging)return;
        var drag=ItemDragHandler.Instance;
        var item=drag.Item;
        bool same=drag.WornSlot==slot || Equipment.IsHand(slot) && player!=null && player.Equipment.Get(slot)==item;
        if(!same){
            // Worn items move between the two trinket slots only by way of the bag.
            if(item==null || player==null || drag.WornSlot.HasValue || !player.Equipment.Fits(item,slot)){var s=UIFeedbackSettings.Shared;s?.Play(s.invalidKey);}
            else player.Equipment.Equip(item,into:slot);
        }
        drag.End();
    }
}
