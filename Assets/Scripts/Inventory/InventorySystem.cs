using System;
using System.Collections.Generic;
using UnityEngine;

public class InventorySystem : Singleton<InventorySystem>
{
    public const int MaxSlots = 24;

    private readonly List<ItemData>[] playerItems =
    {
        new List<ItemData>(MaxSlots), // RoomPlayer
        new List<ItemData>(MaxSlots), // TablePlayer
    };

    private int PlayerIndex =>
        PlayerManager.Instance?.CurrentPlayer == PlayerManager.ActivePlayer.TablePlayer ? 1 : 0;

    private List<ItemData> CurrentItems => playerItems[PlayerIndex];

    public IReadOnlyList<ItemData> Items => CurrentItems;

    public event Action OnInventoryChanged;
    public event Action<ItemData, int> OnItemUsed;

    public void NotifyChanged() => OnInventoryChanged?.Invoke();

    public bool TryAdd(ItemData item)
    {
        if (CurrentItems.Count >= MaxSlots) return false;
        CurrentItems.Add(item);
        OnInventoryChanged?.Invoke();
        return true;
    }

    public void Remove(int index)
    {
        if (index < 0 || index >= CurrentItems.Count) return;
        CurrentItems.RemoveAt(index);
        OnInventoryChanged?.Invoke();
    }

    public void Insert(int index, ItemData item)
    {
        index = Mathf.Clamp(index, 0, CurrentItems.Count);
        CurrentItems.Insert(index, item);
        OnInventoryChanged?.Invoke();
    }

    public void UseItem(int index)
    {
        if (index < 0 || index >= CurrentItems.Count) return;
        OnItemUsed?.Invoke(CurrentItems[index], index);
    }

    public bool IsFull => CurrentItems.Count >= MaxSlots;
}
