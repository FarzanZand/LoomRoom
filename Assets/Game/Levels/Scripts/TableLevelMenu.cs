using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
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
        var prefab = WorldManager.HasInstance ? WorldManager.Instance.tableAdventureMenu : null;
        if (prefab == null) { Debug.LogError("Assign the Table Adventure Menu prefab on WorldManager.", this); return; }
        if (root != null) Destroy(root.gameObject);
        root = Instantiate(prefab);
        root.heading.text = heading;
        firstOption = null;
        if (loader.Catalog != null && loader.Catalog.levels != null)
            foreach (var level in loader.Catalog.levels)
            {
                if (level == null) continue;
                var captured = level;
                var option = Instantiate(root.optionPrefab, root.options);
                option.gameObject.SetActive(true);
                option.Bind(level);
                option.button.onClick.AddListener(() => loader.Load(captured));
                if (firstOption == null) firstOption = option.button;
            }
        if (root.optionPrefab.gameObject.scene.IsValid()) root.optionPrefab.gameObject.SetActive(false);

        // A run saved on quit can be picked up where it left off.
        var tablePlayer = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        var save = tablePlayer != null ? tablePlayer.GetComponent<AdventureSave>() : null;
        var checkpoint = save != null && save.HasCheckpoint && save.CanRestore(save.Saved.checkpoint) ? save.Saved.checkpoint : null;
        root.resume.gameObject.SetActive(checkpoint != null);
        if (checkpoint != null)
        {
            var saved = loader.Catalog != null ? System.Array.Find(loader.Catalog.levels, l => l != null && l.name == checkpoint.level) : null;
            var cls = save.GetComponent<AdventurerProgress>().rules.classes;
            var savedClass = System.Array.Find(cls, c => c != null && c.id == checkpoint.classId);
            root.resumeDetail.text = $"{(savedClass != null ? savedClass.displayName : null)}  ·  Level {checkpoint.characterLevel}  ·  {(saved != null ? saved.displayName : checkpoint.level)} floor {checkpoint.floor}";
            root.resume.onClick.AddListener(loader.ResumeAdventure);
        }
        root.returnToRoom.onClick.AddListener(loader.ReturnToRoom);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        if (GameManager.HasInstance) GameManager.Instance.Push(GameState.Menu);
        if (InputManager.HasInstance)
        {
            InputManager.Instance.CancelPressed += OnCancel;
            InputManager.Instance.NavigatePressed += OnNavigate;
        }
        root.group.alpha = 0; root.panel.localScale = Vector3.one * .97f;
        fade = root.group.DOFade(1, .18f).SetUpdate(true);
        scale = root.panel.DOScale(1, .18f).SetEase(Ease.OutCubic).SetUpdate(true);
    }
    // The first navigation press selects an option; the EventSystem handles it from there.
    void OnNavigate()
    {
        if (!IsOpen || EventSystem.current == null || EventSystem.current.currentSelectedGameObject != null || firstOption == null) return;
        EventSystem.current.SetSelectedGameObject(firstOption.gameObject);
    }
    public void Hide()
    {
        if (InputManager.HasInstance)
        {
            InputManager.Instance.CancelPressed -= OnCancel;
            InputManager.Instance.NavigatePressed -= OnNavigate;
        }
        if (!IsOpen) return;
        fade?.Kill(); scale?.Kill();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        if (root != null) root.gameObject.SetActive(false);
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Menu);
    }
    void OnCancel()
    {
        if (IsOpen && GameManager.HasInstance && GameManager.Instance.State == GameState.Menu) Hide();
    }
    void OnDisable() => Hide();
    void OnDestroy() { Hide(); fade?.Kill(); scale?.Kill(); if (root != null) Destroy(root.gameObject); }
}
