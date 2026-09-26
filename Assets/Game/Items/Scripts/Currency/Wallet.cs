using System;
using UnityEngine;

// The gold a player carries. Lives on the player like Inventory and Equipment, so each
// player keeps their own purse. A plain int on purpose: save/load reads it directly.
public class Wallet : MonoBehaviour
{
    [SerializeField, Min(0)] int gold;

    public int Gold => gold;
    // (new total, change)
    public event Action<int, int> Changed;

    public void Add(int amount)
    {
        if (amount <= 0) return;
        gold += amount;
        Changed?.Invoke(gold, amount);
    }

    public bool CanAfford(int amount) => amount <= gold;

    public bool TrySpend(int amount)
    {
        if (amount < 0 || amount > gold) return false;
        if (amount == 0) return true;
        gold -= amount;
        Changed?.Invoke(gold, -amount);
        return true;
    }

    public void Clear()
    {
        if (gold == 0) return;
        int old = gold;
        gold = 0;
        Changed?.Invoke(0, -old);
    }
}
