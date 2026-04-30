using System;
using UnityEngine;

public class HotbarSystem : Singleton<HotbarSystem>
{
    public const int SlotCount = 6;

    private readonly ItemData[] slots = new ItemData[SlotCount];

    public event Action OnHotbarChanged;

    public bool TryAdd(ItemData item)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (slots[i] == null)
            {
                slots[i] = item;
                OnHotbarChanged?.Invoke();
                return true;
            }
        }
        return false;
    }

    public void Remove(int index)
    {
        if (index < 0 || index >= SlotCount) return;
        slots[index] = null;
        OnHotbarChanged?.Invoke();
    }

    public ItemData Get(int index) => (index >= 0 && index < SlotCount) ? slots[index] : null;

    public bool IsFull
    {
        get
        {
            for (int i = 0; i < SlotCount; i++)
                if (slots[i] == null) return false;
            return true;
        }
    }
}
