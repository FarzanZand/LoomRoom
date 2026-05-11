using System;
using UnityEngine;

public class HotbarSystem : Singleton<HotbarSystem>
{
    public const int SlotCount = 6;

    private readonly ItemData[][] playerSlots =
    {
        new ItemData[SlotCount], // RoomPlayer
        new ItemData[SlotCount], // TablePlayer
    };

    private readonly int[][] playerCounts =
    {
        new int[SlotCount],
        new int[SlotCount],
    };

    private int PlayerIndex =>
        PlayerManager.Instance?.CurrentPlayer == PlayerManager.ActivePlayer.TablePlayer ? 1 : 0;

    private ItemData[] CurrentSlots  => playerSlots[PlayerIndex];
    private int[]      CurrentCounts => playerCounts[PlayerIndex];

    public event Action OnHotbarChanged;

    public void NotifyChanged() => OnHotbarChanged?.Invoke();

    public int GetCount(int index) =>
        (index >= 0 && index < SlotCount) ? CurrentCounts[index] : 0;

    public bool TryAdd(ItemData item)
    {
        if (item.maxStackSize > 1)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (CurrentSlots[i] == item && CurrentCounts[i] < item.maxStackSize)
                {
                    CurrentCounts[i]++;
                    OnHotbarChanged?.Invoke();
                    return true;
                }
            }
        }

        for (int i = 0; i < SlotCount; i++)
        {
            if (CurrentSlots[i] == null)
            {
                CurrentSlots[i]  = item;
                CurrentCounts[i] = 1;
                OnHotbarChanged?.Invoke();
                return true;
            }
        }
        return false;
    }

    public void Remove(int index)
    {
        if (index < 0 || index >= SlotCount) return;
        CurrentSlots[index]  = null;
        CurrentCounts[index] = 0;
        OnHotbarChanged?.Invoke();
    }

    // Decrement by one; removes the slot when the stack hits zero.
    public void Consume(int index)
    {
        if (index < 0 || index >= SlotCount || CurrentSlots[index] == null) return;
        if (--CurrentCounts[index] <= 0)
        {
            CurrentSlots[index]  = null;
            CurrentCounts[index] = 0;
        }
        OnHotbarChanged?.Invoke();
    }

    public ItemData Get(int index) => (index >= 0 && index < SlotCount) ? CurrentSlots[index] : null;

    public void Set(int index, ItemData item, int count = 1)
    {
        if (index < 0 || index >= SlotCount) return;
        CurrentSlots[index]  = item;
        CurrentCounts[index] = item != null ? count : 0;
        OnHotbarChanged?.Invoke();
    }

    // Shift items between two slots (insert-style reorder rather than swap).
    public void Shift(int from, int to)
    {
        if (from < 0 || from >= SlotCount || to < 0 || to >= SlotCount || from == to) return;
        var   item  = CurrentSlots[from];
        int   count = CurrentCounts[from];
        if (from < to)
        {
            for (int i = from; i < to; i++) { CurrentSlots[i] = CurrentSlots[i + 1]; CurrentCounts[i] = CurrentCounts[i + 1]; }
        }
        else
        {
            for (int i = from; i > to; i--) { CurrentSlots[i] = CurrentSlots[i - 1]; CurrentCounts[i] = CurrentCounts[i - 1]; }
        }
        CurrentSlots[to]  = item;
        CurrentCounts[to] = count;
        OnHotbarChanged?.Invoke();
    }

    public void Swap(int a, int b)
    {
        if (a < 0 || a >= SlotCount || b < 0 || b >= SlotCount || a == b) return;
        (CurrentSlots[a],  CurrentSlots[b])  = (CurrentSlots[b],  CurrentSlots[a]);
        (CurrentCounts[a], CurrentCounts[b]) = (CurrentCounts[b], CurrentCounts[a]);
        OnHotbarChanged?.Invoke();
    }

    public bool IsFull
    {
        get
        {
            for (int i = 0; i < SlotCount; i++)
                if (CurrentSlots[i] == null) return false;
            return true;
        }
    }
}
