using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

// Right-click menu for item slots. The overlay, panel and a disabled button template
// are authored in the scene; Show() clones the template per option.
public class ContextMenuUI : Singleton<ContextMenuUI>
{
    [Tooltip("Full-screen transparent blocker. Clicking it closes the menu.")]
    [SerializeField] GameObject overlay;
    [Tooltip("Panel with a VerticalLayoutGroup that holds the buttons.")]
    [SerializeField] RectTransform panel;
    [Tooltip("Disabled Button (with a TMP label child) cloned for each option.")]
    [SerializeField] Button buttonTemplate;

    Canvas rootCanvas;
    readonly List<GameObject> spawned = new();
    Tween entrance;
    public bool IsOpen => overlay != null && overlay.activeInHierarchy;

    protected override void Awake()
    {
        base.Awake();
        rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas != null) rootCanvas = rootCanvas.rootCanvas;
        if (overlay != null)
        {
            var btn = overlay.GetComponent<Button>();
            if (btn != null) btn.onClick.AddListener(Hide);
            overlay.SetActive(false);
        }
        if (buttonTemplate != null) buttonTemplate.gameObject.SetActive(false);
    }

    public void Show(List<(string label, Action callback)> options, Vector2 screenPos)
    {
        if (overlay == null || panel == null || buttonTemplate == null) return;

        foreach (var go in spawned) if (go != null) Destroy(go);
        spawned.Clear();

        foreach (var (label, callback) in options)
        {
            var btn = Instantiate(buttonTemplate, panel);
            btn.gameObject.SetActive(true);
            btn.name = label;
            var text = btn.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text != null) text.text = label;
            var captured = callback;
            btn.onClick.AddListener(() => { captured?.Invoke(); Hide(); });
            spawned.Add(btn.gameObject);
        }

        float pivotX = screenPos.x < Screen.width  * 0.5f ? 0f : 1f;
        float pivotY = screenPos.y < Screen.height * 0.5f ? 0f : 1f;
        panel.pivot = new Vector2(pivotX, pivotY);

        Camera cam = rootCanvas != null && rootCanvas.renderMode == RenderMode.ScreenSpaceCamera ? rootCanvas.worldCamera : null;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rootCanvas.GetComponent<RectTransform>(), screenPos, cam, out var localPos);
        panel.localPosition = localPos;

        overlay.SetActive(true);
        overlay.transform.SetAsLastSibling();
        entrance?.Kill();panel.localScale=Vector3.one*.94f;
        entrance=panel.DOScale(1,.12f).SetEase(Ease.OutCubic).SetUpdate(true);
    }

    public void Hide()
    {
        entrance?.Kill();
        if (overlay != null) overlay.SetActive(false);
    }
}
