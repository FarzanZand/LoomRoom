using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using DG.Tweening;

public class TableLevelMenu : MonoBehaviour
{
    public TableLevelLoader loader;
    public bool IsOpen => root != null && root.gameObject.activeSelf;
    TableAdventureMenuView root;
    Button firstOption;
    Tween fade, scale;
    public void Show(string heading)
    {
        if (IsOpen) { root.heading.text = heading; return; }
        foreach (var inventory in FindObjectsByType<InventoryUI>()) inventory.Close();
        var prefab = WorldManager.Instance.tableAdventureMenu;
        if (prefab == null) { Debug.LogError("Assign the Table Adventure Menu prefab on WorldManager.", this); return; }
        if (root != null) Destroy(root.gameObject);
        root = Instantiate(prefab);
        root.heading.text = heading;
        firstOption = null;
        if (loader.catalog != null && loader.catalog.levels != null)
            foreach (var level in loader.catalog.levels)
            {
                if (level == null) continue;
                var captured = level;
                var option = Instantiate(root.optionPrefab, root.options);
                option.title.text = level.displayName;
                option.description.text = level.description;
                option.button.onClick.AddListener(() => loader.Load(captured));
                if (firstOption == null) firstOption = option.button;
            }
        root.resume.gameObject.SetActive(PlayerManager.Instance.Active == null || PlayerManager.Instance.Active.IsAlive);
        root.resume.button.onClick.AddListener(Hide);
        root.returnToRoom.button.onClick.AddListener(loader.ReturnToRoom);
        EventSystem.current?.SetSelectedGameObject(null);
        GameManager.Instance.Push(GameState.Menu);
        root.group.alpha = 0; root.panel.localScale = Vector3.one * .97f;
        fade = root.group.DOFade(1, .18f).SetUpdate(true);
        scale = root.panel.DOScale(1, .18f).SetEase(Ease.OutCubic).SetUpdate(true);
    }
    void Update()
    {
        if (!IsOpen || EventSystem.current == null || EventSystem.current.currentSelectedGameObject != null || firstOption == null) return;
        var keys = Keyboard.current; var pad = Gamepad.current;
        bool navigate = keys != null && (keys.tabKey.wasPressedThisFrame || keys.downArrowKey.wasPressedThisFrame || keys.upArrowKey.wasPressedThisFrame);
        navigate |= pad != null && (pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame || pad.leftStick.ReadValue().sqrMagnitude > .4f);
        if (navigate) EventSystem.current.SetSelectedGameObject(firstOption.gameObject);
    }
    public void Hide()
    {
        fade?.Kill(); scale?.Kill();
        EventSystem.current?.SetSelectedGameObject(null);
        if (root != null) root.gameObject.SetActive(false);
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Menu);
    }
    void OnDestroy() { fade?.Kill(); scale?.Kill(); if (root != null) Destroy(root.gameObject); }
}
