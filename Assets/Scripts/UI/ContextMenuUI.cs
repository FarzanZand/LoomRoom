using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ContextMenuUI : Singleton<ContextMenuUI>
{
    private Canvas          rootCanvas;
    private GameObject      overlay;
    private GameObject      panel;
    private RectTransform   panelRect;
    private TextMeshProUGUI notifText;
    private Coroutine       notifRoutine;

    protected override void Awake()
    {
        base.Awake();
        rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas == null)
            rootCanvas = FindAnyObjectByType<Canvas>();
        BuildUI();
    }

    private void BuildUI()
    {
        // Full-screen transparent blocker — clicking it closes the menu.
        overlay = new GameObject("ContextMenuOverlay");
        overlay.transform.SetParent(rootCanvas.transform, false);
        var ovRt = overlay.AddComponent<RectTransform>();
        ovRt.anchorMin = Vector2.zero;
        ovRt.anchorMax = Vector2.one;
        ovRt.offsetMin = ovRt.offsetMax = Vector2.zero;
        overlay.AddComponent<CanvasRenderer>();
        var ovImg = overlay.AddComponent<Image>();
        ovImg.color = Color.clear;
        ovImg.raycastTarget = true;
        overlay.AddComponent<Button>().onClick.AddListener(Hide);

        // Menu panel.
        panel = new GameObject("ContextMenuPanel");
        panel.transform.SetParent(overlay.transform, false);
        panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = Vector2.zero;
        panelRect.pivot     = new Vector2(0f, 1f);
        panelRect.sizeDelta = new Vector2(320f, 0f);
        panel.AddComponent<CanvasRenderer>();
        var bg = panel.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.08f, 0.96f);
        var vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding             = new RectOffset(4, 4, 4, 4);
        vlg.spacing             = 2;
        vlg.childAlignment      = TextAnchor.UpperCenter;
        vlg.childControlWidth   = true;
        vlg.childControlHeight  = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        var csf = panel.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        overlay.SetActive(false);

        // Notification label — sits directly on the canvas, outside the overlay.
        var notifGo = new GameObject("ContextNotif");
        notifGo.transform.SetParent(rootCanvas.transform, false);
        var nrt = notifGo.AddComponent<RectTransform>();
        nrt.anchorMin        = new Vector2(0.5f, 0.12f);
        nrt.anchorMax        = new Vector2(0.5f, 0.12f);
        nrt.pivot            = new Vector2(0.5f, 0.5f);
        nrt.sizeDelta        = new Vector2(400f, 60f);
        nrt.anchoredPosition = Vector2.zero;
        notifGo.AddComponent<CanvasRenderer>();
        notifText                = notifGo.AddComponent<TextMeshProUGUI>();
        notifText.raycastTarget  = false;
        notifText.fontSize       = 26f;
        notifText.fontStyle      = FontStyles.Bold;
        notifText.alignment      = TextAlignmentOptions.Center;
        notifText.color          = new Color(1f, 0.35f, 0.15f, 0f);
    }

    public void Show(List<(string label, Action callback)> options, Vector2 screenPos)
    {
        foreach (Transform child in panel.transform)
            Destroy(child.gameObject);

        foreach (var (label, callback) in options)
        {
            var btnGo = new GameObject(label);
            btnGo.transform.SetParent(panel.transform, false);
            btnGo.AddComponent<CanvasRenderer>();
            var btnImg = btnGo.AddComponent<Image>();
            btnImg.color = new Color(0.18f, 0.18f, 0.18f, 1f);
            var le = btnGo.AddComponent<LayoutElement>();
            le.preferredHeight = 60f;
            var btn = btnGo.AddComponent<Button>();
            var nav = new Navigation { mode = Navigation.Mode.None };
            btn.navigation = nav;
            var colors = btn.colors;
            colors.normalColor      = new Color(0.18f, 0.18f, 0.18f, 1f);
            colors.highlightedColor = new Color(0.30f, 0.30f, 0.30f, 1f);
            colors.pressedColor     = new Color(0.12f, 0.12f, 0.12f, 1f);
            btn.colors = colors;

            var lblGo = new GameObject("Label");
            lblGo.transform.SetParent(btnGo.transform, false);
            var lrt = lblGo.AddComponent<RectTransform>();
            lrt.anchorMin  = Vector2.zero;
            lrt.anchorMax  = Vector2.one;
            lrt.offsetMin  = new Vector2(6f, 2f);
            lrt.offsetMax  = new Vector2(-6f, -2f);
            lblGo.AddComponent<CanvasRenderer>();
            var tmp = lblGo.AddComponent<TextMeshProUGUI>();
            tmp.raycastTarget = false;
            tmp.text          = label;
            tmp.fontSize  = 26f;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.color     = Color.white;

            var captured = callback;
            btn.onClick.AddListener(() => { captured(); Hide(); });
        }

        // Flip pivot so the menu always opens toward the center of the screen.
        float pivotX = screenPos.x < Screen.width  * 0.5f ? 0f : 1f;
        float pivotY = screenPos.y < Screen.height * 0.5f ? 0f : 1f;
        panelRect.pivot = new Vector2(pivotX, pivotY);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rootCanvas.GetComponent<RectTransform>(),
            screenPos,
            rootCanvas.renderMode == RenderMode.ScreenSpaceCamera ? rootCanvas.worldCamera : null,
            out var localPos);
        panelRect.localPosition = localPos;

        overlay.SetActive(true);
        overlay.transform.SetAsLastSibling();
        MenuManager.Instance?.OpenMenu("contextmenu");
    }

    public void Hide()
    {
        if (overlay != null) overlay.SetActive(false);
        MenuManager.Instance?.CloseMenu("contextmenu");
    }

    public void ShowNotification(string message)
    {
        if (notifText == null) return;
        notifText.text = message;
        if (notifRoutine != null) StopCoroutine(notifRoutine);
        notifRoutine = StartCoroutine(NotifRoutine());
    }

    private IEnumerator NotifRoutine()
    {
        float fadeIn = 0.12f, hold = 1f, fadeOut = 0.35f;
        float t = 0f;
        while (t < fadeIn)  { SetNotifAlpha(t / fadeIn);        t += Time.unscaledDeltaTime; yield return null; }
        SetNotifAlpha(1f);
        yield return new WaitForSecondsRealtime(hold);
        t = 0f;
        while (t < fadeOut) { SetNotifAlpha(1f - t / fadeOut);  t += Time.unscaledDeltaTime; yield return null; }
        SetNotifAlpha(0f);
        notifRoutine = null;
    }

    private void SetNotifAlpha(float a)
    {
        if (notifText == null) return;
        var c = notifText.color; c.a = a; notifText.color = c;
    }

    [SerializeField] private Transform fallbackPlayerTransform;

    // Spawns the item's world prefab in front of the active player.
    public static void DropItemToWorld(ItemData item)
    {
        if (item == null) return;

        Transform dropFrom = null;
        var pm = PlayerManager.Instance;
        if (pm != null)
        {
            var playerGo = pm.CurrentPlayer == PlayerManager.ActivePlayer.TablePlayer
                ? pm.tablePlayer : pm.roomPlayer;
            dropFrom = playerGo?.transform;
        }
        else if (Instance != null)
        {
            dropFrom = Instance.fallbackPlayerTransform;
        }

        if (dropFrom == null) return;
        var pos = dropFrom.position + dropFrom.forward * 1.5f + Vector3.up * 0.5f;
        WorldItemSpawner.Spawn(item, pos);
    }
}
