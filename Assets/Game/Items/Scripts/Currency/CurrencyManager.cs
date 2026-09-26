using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.AI;

// Currency rules and presentation: the coin icon, pickup sound, which coin model a pile
// uses, and shop price rules. The gold itself lives on each player's Wallet.
public class CurrencyManager : Singleton<CurrencyManager>
{
    [Serializable]
    public class CoinVisual
    {
        [Tooltip("Piles of at least this many coins use this model. The largest matching threshold wins.")]
        [Min(1)] public int minAmount = 1;
        public GameObject prefab;
        [Min(.01f)] public float scale = 1f;
    }

    [Header("Presentation")]
    public string currencyName = "gold";
    [PreviewField(48)] public Sprite coinIcon;
    public AudioClip pickupClip;
    [Range(0, 1)] public float pickupVolume = .9f;
    [Tooltip("Coin models by pile size, smallest first.")]
    public List<CoinVisual> coinVisuals = new();
    [Tooltip("Walking over a pile collects it. Off requires the interact key.")]
    public bool collectOnTouch = true;
    [Min(.1f)] public float pickupRadius = .6f;

    [Header("Shops")]
    [Range(0, 1), Tooltip("Fraction of an item's value paid when the player sells it.")]
    public float sellMultiplier = .4f;
    [Min(1), Tooltip("Price used for items whose value is zero.")]
    public int fallbackItemValue = 5;

    public event Action<Player, int> GoldCollected;

    public static Wallet WalletOf(Character who) => who != null ? who.GetComponent<Wallet>() : null;

    public int BuyPrice(ItemData item) => item == null ? 0 : Mathf.Max(1, item.value > 0 ? item.value : fallbackItemValue);
    public int SellPrice(ItemData item) => item == null ? 0 : Mathf.Max(1, Mathf.FloorToInt(BuyPrice(item) * sellMultiplier));

    public GameObject VisualFor(int amount, out float scale)
    {
        CoinVisual best = null;
        foreach (var v in coinVisuals)
            if (v != null && v.prefab != null && amount >= v.minAmount && (best == null || v.minAmount >= best.minAmount)) best = v;
        scale = best != null ? best.scale : 1f;
        return best?.prefab;
    }

    // Drop a pile of coins near origin, on navigation when possible.
    public CoinPickup SpawnCoins(int amount, Vector3 origin, Transform parent)
    {
        if (amount <= 0) return null;
        Vector3 position = origin;
        if (NavMesh.SamplePosition(origin, out var hit, 2f, NavMesh.AllAreas))
        {
            position = hit.position;
            float angle = UnityEngine.Random.value * Mathf.PI * 2f;
            var desired = hit.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * .45f;
            if (!NavMesh.Raycast(hit.position, desired, out _, NavMesh.AllAreas)) position = desired;
        }
        var go = new GameObject($"Gold ({amount})");
        go.transform.SetParent(parent, true);
        go.transform.position = position;
        var pickup = go.AddComponent<CoinPickup>();
        pickup.Init(amount);
        return pickup;
    }

    public void Collect(Player player, int amount, Vector3 position)
    {
        var wallet = WalletOf(player);
        if (wallet == null || amount <= 0) return;
        wallet.Add(amount);
        if (AudioManager.HasInstance && pickupClip != null) AudioManager.Instance.PlaySFX2D(pickupClip, pickupVolume, .05f);
        MessageLog.Post($"You pick up {amount} {currencyName}.", MessageKind.Loot);
        GoldCollected?.Invoke(player, amount);
    }
}
