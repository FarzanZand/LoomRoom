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
    [Tooltip("Draws grid items across their cells (the bag grid).")]
    public ItemGridUI itemGrid;
    public UIFeedbackSettings feedback;
    Sequence transition;
    Vector2 inventoryHome,statsHome;
    void Awake(){if(inventoryPanel!=null)inventoryHome=inventoryPanel.anchoredPosition;if(statsPanel!=null)statsHome=statsPanel.anchoredPosition;}
    // Off screen toward the side the panel is anchored to.
    static Vector2 Away(RectTransform panel)=>(panel.anchorMin.x>.5f?Vector2.right:Vector2.left)*(panel.rect.width+80);
    void Animate(bool opening){
        transition?.Kill();
        if(inventoryPanel==null && statsPanel==null){if(!opening)FinishClose();return;}
        var settings=feedback!=null?feedback:UIFeedbackSettings.Shared;
        float duration=settings!=null?settings.panelDuration:.22f;
        var ease=settings!=null?settings.panelEase:Ease.OutCubic;
        transition=DOTween.Sequence().SetUpdate(true);
        if(inventoryPanel!=null)transition.Join(inventoryPanel.DOAnchorPos(opening?inventoryHome:inventoryHome+Away(inventoryPanel),duration).SetEase(ease));
        if(statsPanel!=null)transition.Join(statsPanel.DOAnchorPos(opening?statsHome:statsHome+Away(statsPanel),duration).SetEase(ease));
        if(!opening)transition.OnComplete(HidePanel);
    }
    // The Inventory state is given back as soon as the panel starts closing, so a killed close tween
    // (scene change, DOTween.KillAll) can never leave it pushed; the tween only hides the panel.
    void ReleaseMenu(){
        if(ItemDragHandler.HasInstance)ItemDragHandler.Instance.End();
        if(ownsMenu){ownsMenu=false;if(GameManager.HasInstance)GameManager.Instance.Pop(GameState.Inventory);}
    }
    void HidePanel(){if(panel!=null)panel.SetActive(false);}
    void FinishClose(){ReleaseMenu();HidePanel();}

    void Update(){
        if(isOpen && PlayerManager.HasInstance && (PlayerManager.Instance.Active==null || !PlayerManager.Instance.Active.IsAlive))Close();
        // Nothing selected and a pad's d-pad or an arrow key pressed: select a cell.
        if(isOpen && InputManager.MenuInputStarted && InputManager.NothingSelectedIn(panel!=null?panel.transform:null) && !ContextMenuOpen)SelectStart();
    }

    // With a pad a bag cell is selected as the bag opens (the first holding an item, else the first), so Submit and the
    // d-pad have something to act on; the mouse works as before.
    void SelectStart(){
        ItemSlotUI pick=null;
        foreach(var s in slots)if(s!=null && s.gameObject.activeInHierarchy && s.Item!=null){pick=s;break;}
        if(pick==null)foreach(var s in slots)if(s!=null && s.gameObject.activeInHierarchy){pick=s;break;}
        if(pick==null)return;
        if(pick.TryGetComponent<UnityEngine.UI.Selectable>(out var selectable))selectable.Select();
        else if(UnityEngine.EventSystems.EventSystem.current!=null)UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(pick.gameObject);
    }
    static bool ContextMenuOpen=>ContextMenuUI.HasInstance && ContextMenuUI.Instance.IsOpen;

    // Closing with a cell selected: the selection is let go while the cell can still hear it (its tint and tooltip go),
    // so the next menu starts clean.
    void ReleaseSelection(){
        var events=UnityEngine.EventSystems.EventSystem.current;
        var selected=events!=null?events.currentSelectedGameObject:null;
        if(selected!=null && panel!=null && selected.transform.IsChildOf(panel.transform) && !events.alreadySelecting)events.SetSelectedGameObject(null);
    }

    void OnEnable()
    {
        if (InputManager.HasInstance) InputManager.Instance.InventoryToggled += Toggle;
        if (InputManager.HasInstance) InputManager.Instance.CancelPressed    += OnCancel;
        if (PlayerManager.HasInstance) PlayerManager.Instance.PlayerSwapped  += OnPlayerSwapped;
        if (PlayerManager.HasInstance && PlayerManager.Instance.Active != null) OnPlayerSwapped(PlayerManager.Instance.Active);
    }

    void OnDisable()
    {
        transition?.Kill();isOpen=false;FinishClose();
        if (InputManager.HasInstance) InputManager.Instance.InventoryToggled -= Toggle;
        if (InputManager.HasInstance) InputManager.Instance.CancelPressed    -= OnCancel;
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
            InputManager.Instance.CancelPressed -= OnCancel;
            InputManager.Instance.CancelPressed += OnCancel;
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
        itemGrid?.Bind(bound);
        LinkCells();
        Refresh();
    }

    // Keys and a pad move cell to cell and stop at the bag's edges: left to themselves they walked off the bottom row onto
    // other HUD buttons (a hidden card's Pick) and the bag lost the selection.
    void LinkCells()
    {
        int w = bound != null ? bound.GridWidth : 0;
        if (w <= 0) return;
        UnityEngine.UI.Selectable Cell(int i) => i >= 0 && i < slots.Count && slots[i] != null && slots[i].TryGetComponent<UnityEngine.UI.Selectable>(out var c) ? c : null;
        for (int i = 0; i < slots.Count; i++)
        {
            var cell = Cell(i);
            if (cell == null) continue;
            var nav = cell.navigation;
            nav.mode = UnityEngine.UI.Navigation.Mode.Explicit;
            nav.selectOnUp = Cell(i - w);
            nav.selectOnDown = Cell(i + w);
            nav.selectOnLeft = i % w > 0 ? Cell(i - 1) : null;
            nav.selectOnRight = i % w < w - 1 ? Cell(i + 1) : null;
            cell.navigation = nav;
        }
    }

    public void Toggle()
    {
        if (!isOpen && CoopGhost.Active) return;   // a co-op ghost can't reach its bag
        if (isOpen) Close(); else Open();
    }

    void Open()
    {
        if (isOpen) return;
        if(!PlayerManager.HasInstance || PlayerManager.Instance.ActiveKind!=PlayerKind.Table || PlayerManager.Instance.Active==null || !PlayerManager.Instance.Active.IsAlive)return;
        if (GameManager.HasInstance && !GameManager.Instance.GameplayActive && GameManager.Instance.State!=GameState.Inventory) return;
        if(!ownsMenu){
            ownsMenu=true;
            if(inventoryPanel!=null)inventoryPanel.anchoredPosition=inventoryHome+Away(inventoryPanel);
            if(statsPanel!=null)statsPanel.anchoredPosition=statsHome+Away(statsPanel);
            if(GameManager.HasInstance)GameManager.Instance.Push(GameState.Inventory);
        }
        isOpen = true;
        if (panel != null) panel.SetActive(true);
        if (ProgressionManager.HasInstance) ProgressionManager.Instance.InventoryOpened();
        var style=feedback!=null?feedback:UIFeedbackSettings.Shared;style?.Play(style.openKey);Animate(true);
        Refresh();
        if(InputManager.PadConnected)SelectStart();
    }

    void OnCancel()
    {
        if (GameManager.HasInstance && GameManager.Instance.State == GameState.Inventory) Close();
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        ReleaseMenu();
        var style=feedback!=null?feedback:UIFeedbackSettings.Shared;style?.Play(style.closeKey);Animate(false);
        if (ContextMenuUI.HasInstance) ContextMenuUI.Instance.Hide();
        ReleaseSelection();
        if (TooltipUI.HasInstance) TooltipUI.Instance.Hide();
    }

    void Refresh()
    {
        bool dungeon=PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind==PlayerKind.Table;
        foreach (var s in slots) if (s != null){s.SetDungeonStyle(dungeon,null,null);s.Refresh();}
        itemGrid?.Refresh();
        equipmentPanel?.Refresh();
    }
}
