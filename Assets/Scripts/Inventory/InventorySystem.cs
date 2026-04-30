using System;
using System.Collections.Generic;
using UnityEngine;

public class InventorySystem : Singleton<InventorySystem>
{
    public const int MaxSlots = 24;

    private readonly List<ItemData> items = new(MaxSlots);
    public IReadOnlyList<ItemData> Items => items;

    public event Action OnInventoryChanged;
    public event Action<ItemData, int> OnItemUsed;

    public bool TryAdd(ItemData item)
    {
        if (items.Count >= MaxSlots) return false;
        items.Add(item);
        OnInventoryChanged?.Invoke();
        return true;
    }

    public void Remove(int index)
    {
        if (index < 0 || index >= items.Count) return;
        items.RemoveAt(index);
        OnInventoryChanged?.Invoke();
    }

    public void UseItem(int index)
    {
        if (index < 0 || index >= items.Count) return;
        OnItemUsed?.Invoke(items[index], index);
    }

    public bool IsFull => items.Count >= MaxSlots;
}
