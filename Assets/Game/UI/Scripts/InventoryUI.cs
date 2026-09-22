using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

// The bag panel. Binds its authored slot objects to the active player's Bag and
// toggles through the live Inventory game state, without pausing simulation.
public class InventoryUI : MonoBehaviour
{
    [SerializeField] GameObject panel;
    [SerializeField] List<ItemSlotUI> slots = new();

    Inventory bound;
    Equipment boundEquipment;
    bool isOpen,ownsMenu;
    public bool IsOpen=>isOpen;
    public RectTransform inventoryPanel,statsPanel;
    public EquipmentPanelUI equipmentPanel;
    public UIFeedbackSettings feedback;
    Sequence transition;
    Vector2 inventoryHome,statsHome;
    void Awake(){if(inventoryPanel!=null)inventoryHome=inventoryPanel.anchoredPosition;if(statsPanel!=null)statsHome=statsPanel.anchoredPosition;}
    void Animate(bool opening){
        transition?.Kill();
        if(inventoryPanel==null && statsPanel==null){if(!opening)FinishClose();return;}
        var settings=feedback!=null?feedback:UIFeedbackSettings.Shared;
        float duration=settings!=null?settings.panelDuration:.22f;
        var ease=settings!=null?settings.panelEase:Ease.OutCubic;
        transition=DOTween.Sequence().SetUpdate(true);
        if(inventoryPanel!=null)transition.Join(inventoryPanel.DOAnchorPos(opening?inventoryHome:inventoryHome+Vector2.left*(inventoryPanel.rect.width+80),duration).SetEase(ease));
        if(statsPanel!=null)transition.Join(statsPanel.DOAnchorPos(opening?statsHome:statsHome+Vector2.right*(statsPanel.rect.width+80),duration).SetEase(ease));
        if(!opening)transition.OnComplete(FinishClose);
    }
    void FinishClose(){
        if(ItemDragHandler.HasInstance)ItemDragHandler.Instance.End();
        if(panel!=null)panel.SetActive(false);
        if(ownsMenu){ownsMenu=false;if(GameManager.HasInstance)GameManager.Instance.Pop(GameState.Inventory);}
    }

    void Update(){if(isOpen && PlayerManager.HasInstance && (PlayerManager.Instance.Active==null || !PlayerManager.Instance.Active.IsAlive))Close();}

    void OnEnable()
    {
        if (InputManager.HasInstance) InputManager.Instance.InventoryToggled += Toggle;
        if (InputManager.HasInstance) InputManager.Instance.CancelPressed    += Close;
        if (PlayerManager.HasInstance) PlayerManager.Instance.PlayerSwapped  += OnPlayerSwapped;
        if (PlayerManager.HasInstance && PlayerManager.Instance.Active != null) OnPlayerSwapped(PlayerManager.Instance.Active);
    }

    void OnDisable()
    {
        transition?.Kill();isOpen=false;FinishClose();
        if (InputManager.HasInstance) InputManager.Instance.InventoryToggled -= Toggle;
        if (InputManager.HasInstance) InputManager.Instance.CancelPressed    -= Close;
        if (PlayerManager.HasInstance) PlayerManager.Instance.PlayerSwapped  -= OnPlayerSwapped;
        Bind(null, null);
    }

    void Start()
    {
        // Managers may awaken after this UI's OnEnable. Subscribe once they all exist.
        if (PlayerManager.HasInstance)
        {
            PlayerManager.Instance.PlayerSwapped -= OnPlayerSwapped;
            PlayerManager.Instance.PlayerSwapped += OnPlayerSwapped;
        }
        if (InputManager.HasInstance)
        {
            InputManager.Instance.InventoryToggled -= Toggle;
            InputManager.Instance.InventoryToggled += Toggle;
            InputManager.Instance.CancelPressed -= Close;
            InputManager.Instance.CancelPressed += Close;
        }
        if (panel != null) panel.SetActive(false);
        if (PlayerManager.HasInstance && PlayerManager.Instance.Active != null) OnPlayerSwapped(PlayerManager.Instance.Active);
    }

    void OnPlayerSwapped(Player player) {if(ownsMenu){transition?.Kill();isOpen=false;FinishClose();}Bind(player != null ? player.Bag : null, player != null ? player.Equipment : null);equipmentPanel?.Bind(player);}

    void Bind(Inventory inv, Equipment eq)
    {
        if (bound != null)          bound.Changed          -= Refresh;
        if (boundEquipment != null) boundEquipment.Changed -= Refresh;
        bound = inv;
        boundEquipment = eq;
        if (bound != null)          bound.Changed          += Refresh;
        if (boundEquipment != null) boundEquipment.Changed += Refresh;

        for (int i = 0; i < slots.Count; i++)
            if (slots[i] != null) slots[i].Bind(bound, i);
        Refresh();
    }

    public void Toggle()
    {
        if (isOpen) Close(); else Open();
    }

    void Open()
    {
        if (isOpen) return;
        if(!PlayerManager.HasInstance || PlayerManager.Instance.ActiveKind!=PlayerKind.Table || PlayerManager.Instance.Active==null || !PlayerManager.Instance.Active.IsAlive)return;
        if (GameManager.HasInstance && !GameManager.Instance.GameplayActive && GameManager.Instance.State!=GameState.Inventory) return;
        if(!ownsMenu){
            ownsMenu=true;
            if(inventoryPanel!=null)inventoryPanel.anchoredPosition=inventoryHome+Vector2.left*(inventoryPanel.rect.width+80);
            if(statsPanel!=null)statsPanel.anchoredPosition=statsHome+Vector2.right*(statsPanel.rect.width+80);
        }
        isOpen = true;
        if (panel != null) panel.SetActive(true);
        GameManager.Instance?.Push(GameState.Inventory);
        var style=feedback!=null?feedback:UIFeedbackSettings.Shared;style?.Play(style.openKey);Animate(true);
        Refresh();
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        var style=feedback!=null?feedback:UIFeedbackSettings.Shared;style?.Play(style.closeKey);Animate(false);
        if (TooltipUI.HasInstance) TooltipUI.Instance.Hide();
        if (ContextMenuUI.HasInstance) ContextMenuUI.Instance.Hide();

    }

    void Refresh()
    {
        bool dungeon=PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind==PlayerKind.Table;
        foreach (var s in slots) if (s != null){s.SetDungeonStyle(dungeon,null,null);s.Refresh();}
        equipmentPanel?.Refresh();
    }
}
