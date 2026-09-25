using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class EquipmentSlotUI : MonoBehaviour,IPointerClickHandler,IPointerEnterHandler,IPointerExitHandler,IDropHandler
{
    public EquipmentSlot slot;
    public Image icon;
    public TMP_Text title;
    Player player;
    public void Bind(Player owner){player=owner;Refresh();}
    public void Refresh(){var item=player?.Equipment?.Get(slot);if(icon!=null){icon.sprite=item!=null?item.icon:null;icon.enabled=item!=null;}if(title!=null)title.text=Display(slot);}
    public static string Display(EquipmentSlot slot)=>slot switch{EquipmentSlot.Head=>"HELM",EquipmentSlot.Body=>"ARMOR",EquipmentSlot.RightHand=>"MAINHAND",EquipmentSlot.LeftHand=>"OFFHAND",_=>slot.ToString().ToUpperInvariant()};
    public void OnPointerClick(PointerEventData e){if(player==null || e.button!=PointerEventData.InputButton.Left)return;player.Equipment.Unequip(slot);if(TooltipUI.HasInstance)TooltipUI.Instance.Hide();}
    public void OnPointerEnter(PointerEventData e){var item=player?.Equipment?.Get(slot);if(item!=null && TooltipUI.HasInstance)TooltipUI.Instance.Show(item);}
    public void OnPointerExit(PointerEventData e){if(TooltipUI.HasInstance)TooltipUI.Instance.Hide();}
    public void OnDrop(PointerEventData e){
        var source=e.pointerDrag!=null?e.pointerDrag.GetComponent<ItemSlotUI>():null;
        if(source?.Item==null || player==null || !source.Item.canBeEquipped || source.Item.equipSlot!=slot){var s=UIFeedbackSettings.Shared;s?.Play(s.invalidKey);return;}
        player.Equipment.Equip(source.Item);
        if(ItemDragHandler.HasInstance)ItemDragHandler.Instance.End();
    }
}
