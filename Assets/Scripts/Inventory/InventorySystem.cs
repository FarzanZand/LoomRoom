using System;
using System.Collections.Generic;
using UnityEngine;

public class InventorySystem : Singleton<InventorySystem>
{
    public const int MaxSlots = 24;

    private readonly ItemData[][] playerItems =
    {
        new ItemData[MaxSlots], // RoomPlayer
        new ItemData[MaxSlots], // TablePlayer
    };

    private readonly int[][] playerCounts =
    {
        new int[MaxSlots],
        new int[MaxSlots],
    };

    private int PlayerIndex =>
        PlayerManager.Instance != null && PlayerManager.Instance.CurrentPlayer == PlayerManager.ActivePlayer.TablePlayer ? 1 : 0;

    private ItemData[] CurrentItems  => playerItems[PlayerIndex];
    private int[]      CurrentCounts => playerCounts[PlayerIndex];

    public IReadOnlyList<ItemData> Items => CurrentItems;

    public event Action OnInventoryChanged;
    public event Action OnInventoryFull;
    public event Action<ItemData, int> OnItemUsed;

    public void NotifyChanged() => OnInventoryChanged?.Invoke();

    public int GetCount(int index) =>
        (index >= 0 && index < MaxSlots) ? CurrentCounts[index] : 0;

    public bool TryAdd(ItemData item)
    {
        if (item.maxStackSize > 1)
        {
            for (int i = 0; i < MaxSlots; i++)
            {
                if (CurrentItems[i] == item && CurrentCounts[i] < item.maxStackSize)
                {
                    CurrentCounts[i]++;
                    OnInventoryChanged?.Invoke();
                    return true;
                }
            }
        }

        for (int i = 0; i < MaxSlots; i++)
        {
            if (CurrentItems[i] == null)
            {
                CurrentItems[i]  = item;
                CurrentCounts[i] = 1;
                OnInventoryChanged?.Invoke();
                return true;
            }
        }

        OnInventoryFull?.Invoke();
        return false;
    }

    // Remove the entire stack (used when dragging).
    public void Remove(int index)
    {
        if (index < 0 || index >= MaxSlots) return;
        CurrentItems[index]  = null;
        CurrentCounts[index] = 0;
        OnInventoryChanged?.Invoke();
    }

    // Decrement by one; removes the slot when the stack hits zero (used when consuming).
    public void Consume(int index)
    {
        if (index < 0 || index >= MaxSlots || CurrentItems[index] == null) return;
        if (--CurrentCounts[index] <= 0)
        {
            CurrentItems[index]  = null;
            CurrentCounts[index] = 0;
        }
        OnInventoryChanged?.Invoke();
    }

    public void Insert(int index, ItemData item, int count = 1)
    {
        index = Mathf.Clamp(index, 0, MaxSlots - 1);
        CurrentItems[index]  = item;
        CurrentCounts[index] = count;
        OnInventoryChanged?.Invoke();
    }

    public void UseItem(int index)
    {
        if (index < 0 || index >= MaxSlots) return;
        OnItemUsed?.Invoke(CurrentItems[index], index);
    }

    public bool IsFull
    {
        get
        {
            for (int i = 0; i < MaxSlots; i++)
                if (CurrentItems[i] == null) return false;
            return true;
        }
    }
}
