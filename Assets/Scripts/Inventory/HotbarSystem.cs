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

    private int PlayerIndex =>
        PlayerManager.Instance?.CurrentPlayer == PlayerManager.ActivePlayer.TablePlayer ? 1 : 0;

    private ItemData[] CurrentSlots => playerSlots[PlayerIndex];

    public event Action OnHotbarChanged;

    public void NotifyChanged() => OnHotbarChanged?.Invoke();

    public bool TryAdd(ItemData item)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (CurrentSlots[i] == null)
            {
                CurrentSlots[i] = item;
                OnHotbarChanged?.Invoke();
                return true;
            }
        }
        return false;
    }

    public void Remove(int index)
    {
        if (index < 0 || index >= SlotCount) return;
        CurrentSlots[index] = null;
        OnHotbarChanged?.Invoke();
    }

    public ItemData Get(int index) => (index >= 0 && index < SlotCount) ? CurrentSlots[index] : null;

    public void Set(int index, ItemData item)
    {
        if (index < 0 || index >= SlotCount) return;
        CurrentSlots[index] = item;
        OnHotbarChanged?.Invoke();
    }

    public void Swap(int a, int b)
    {
        if (a < 0 || a >= SlotCount || b < 0 || b >= SlotCount || a == b) return;
        (CurrentSlots[a], CurrentSlots[b]) = (CurrentSlots[b], CurrentSlots[a]);
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
