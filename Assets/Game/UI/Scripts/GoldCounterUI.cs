using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The table player's purse on the HUD. The icon comes from CurrencyManager.
public class GoldCounterUI : MonoBehaviour
{
    [SerializeField] Image icon;
    [SerializeField] TMP_Text amount;
    [SerializeField] CanvasGroup group;
    [SerializeField] RectTransform pulseTarget;

    Wallet wallet;
    Tween pulse;

    void Start()
    {
        if (icon != null && CurrencyManager.HasInstance && CurrencyManager.Instance.coinIcon != null)
            icon.sprite = CurrencyManager.Instance.coinIcon;
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        wallet = player != null ? player.Wallet : null;
        if (wallet != null) wallet.Changed += OnChanged;
        SetText();
    }

    void OnDestroy()
    {
        if (wallet != null) wallet.Changed -= OnChanged;
    }

    void OnChanged(int total, int delta)
    {
        SetText();
        if (pulseTarget == null || delta == 0) return;
        pulse?.Kill(true);
        pulse = pulseTarget.DOPunchScale(Vector3.one * .18f, .25f, 6, .5f).SetUpdate(true);
    }

    void SetText()
    {
        if (amount != null) amount.text = wallet != null ? wallet.Gold.ToString() : "0";
    }

    void Update()
    {
        if (group == null) return;
        var state = GameManager.HasInstance ? GameManager.Instance.State : GameState.Explore;
        bool table = PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table;
        bool dungeon = RunManager.HasInstance && RunManager.Instance.Running;
        group.alpha = table && dungeon && (state == GameState.Explore || state == GameState.Inventory) ? 1f : 0f;
    }
}
