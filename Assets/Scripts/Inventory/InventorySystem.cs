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

    private int PlayerIndex =>
        PlayerManager.Instance?.CurrentPlayer == PlayerManager.ActivePlayer.TablePlayer ? 1 : 0;

    private ItemData[] CurrentItems => playerItems[PlayerIndex];

    public IReadOnlyList<ItemData> Items => CurrentItems;

    public event Action OnInventoryChanged;
    public event Action<ItemData, int> OnItemUsed;

    public void NotifyChanged() => OnInventoryChanged?.Invoke();

    public bool TryAdd(ItemData item)
    {
        for (int i = 0; i < MaxSlots; i++)
        {
            if (CurrentItems[i] == null)
            {
                CurrentItems[i] = item;
                OnInventoryChanged?.Invoke();
                return true;
            }
        }
        return false;
    }

    public void Remove(int index)
    {
        if (index < 0 || index >= MaxSlots) return;
        CurrentItems[index] = null;
        OnInventoryChanged?.Invoke();
    }

    public void Insert(int index, ItemData item)
    {
        index = Mathf.Clamp(index, 0, MaxSlots - 1);
        CurrentItems[index] = item;
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
