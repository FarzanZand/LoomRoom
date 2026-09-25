using System;
using Sirenix.OdinInspector;
using UnityEngine;

public enum InventoryRole { Bag = 0, Hotbar = 1 }

// A fixed grid of item stacks living on a player. The hotbar is just a second
// Inventory with fewer slots and a type filter, so every move/swap/merge rule is
// written once. Save-friendly: the whole state is slots[] of (item, count).
public class Inventory : MonoBehaviour
{
    [Tooltip("Bag or Hotbar. UI binds to the matching role on the active player.")]
    public InventoryRole role = InventoryRole.Bag;
    [Tooltip("Number of slots. The UI must have at least this many slot objects.")]
    [SerializeField, Min(1)] int slotCount = 24;
    [Tooltip("Item types this container accepts.")]
    [SerializeField] ItemTypeMask allowedTypes = ItemTypeMask.All;

    ItemStack[] storedSlots;
    // The table player's root may still be inactive when a level captures its bag.
    // Initialize on first access as well as Awake, preserving any pre-activation items.
    [ShowInInspector, ReadOnly]
    ItemStack[] slots
    {
        get => storedSlots ??= new ItemStack[Mathf.Max(1, slotCount)];
        set => storedSlots = value;
    }

    public int SlotCount => slots != null ? slots.Length : slotCount;
    public ItemStack this[int index] => index >= 0 && index < slots.Length ? slots[index] : null;

    public event Action Changed;

    void Awake()
    {
        if (slots == null || slots.Length != slotCount)
            slots = new ItemStack[slotCount];
    }

    public bool Accepts(ItemData item) => item != null && allowedTypes.Contains(item.itemType);

    public ItemData ItemAt(int index) => this[index]?.item;
    public int      CountAt(int index) => this[index]?.count ?? 0;

    public int FirstEmpty()
    {
        for (int i = 0; i < slots.Length; i++)
            if (slots[i] == null || slots[i].IsEmpty) return i;
        return -1;
    }

    public bool IsFull => FirstEmpty() < 0;

    public int IndexOf(ItemData item)
    {
        for (int i = 0; i < slots.Length; i++)
            if (slots[i] != null && slots[i].item == item) return i;
        return -1;
    }

    public int TotalCount(ItemData item)
    {
        int n = 0;
        foreach (var s in slots) if (s != null && s.item == item) n += s.count;
        return n;
    }

    // Adds to an existing stack first, then the first empty slot. Returns false if nothing fit.
    public bool TryAdd(ItemData item, int count = 1)
    {
        if (!Accepts(item) || count <= 0) return false;

        // Pickup callers retry in the other container on failure. Refuse atomically
        // so a partially filled hotbar cannot duplicate the same world drop in the bag.
        long capacity = 0;
        foreach (var slot in slots)
            if (slot == null || slot.IsEmpty) capacity += Mathf.Max(1, item.maxStackSize);
            else if (slot.item == item) capacity += Mathf.Max(0, item.maxStackSize - slot.count);
        if (capacity < count) return false;

        int remaining = count;
        if (item.maxStackSize > 1)
        {
            for (int i = 0; i < slots.Length && remaining > 0; i++)
            {
                var s = slots[i];
                if (s == null || s.item != item || s.count >= item.maxStackSize) continue;
                int add = Mathf.Min(remaining, item.maxStackSize - s.count);
                s.count += add;
                remaining -= add;
            }
        }

        while (remaining > 0)
        {
            int empty = FirstEmpty();
            if (empty < 0) break;
            int add = Mathf.Min(remaining, Mathf.Max(1, item.maxStackSize));
            slots[empty] = new ItemStack(item, add);
            remaining -= add;
        }

        bool addedAny = remaining < count;
        if (addedAny) Changed?.Invoke();
        return remaining == 0;
    }

    public ItemStack RemoveAt(int index)
    {
        if (index < 0 || index >= slots.Length) return null;
        var s = slots[index];
        slots[index] = null;
        if (s != null) Changed?.Invoke();
        return s;
    }

    // Decrement by one; clears the slot when the stack hits zero.
    public void Consume(int index)
    {
        var s = this[index];
        if (s == null) return;
        if (--s.count <= 0) slots[index] = null;
        Changed?.Invoke();
    }

    public bool RemoveOne(ItemData item)
    {
        int i = IndexOf(item);
        if (i < 0) return false;
        Consume(i);
        return true;
    }

    public void Set(int index, ItemStack stack)
    {
        if (index < 0 || index >= slots.Length) return;
        slots[index] = stack != null && !stack.IsEmpty ? stack : null;
        Changed?.Invoke();
    }

    public void Clear()
    {
        for (int i = 0; i < slots.Length; i++) slots[i] = null;
        Changed?.Invoke();
    }

    // Move within one container: merges same-item stacks, otherwise swaps.
    public void Move(int from, int to) => Move(this, from, this, to);

    // Move between any two containers. Swaps when the destination is occupied, so a drop
    // onto a filled slot never loses anything. Refuses when either side would end up
    // holding a type it does not accept.
    public static bool Move(Inventory src, int from, Inventory dst, int to)
    {
        if (src == null || dst == null) return false;
        if (from < 0 || from >= src.SlotCount || to < 0 || to >= dst.SlotCount) return false;
        if (src == dst && from == to) return true;
        var a = src[from];
        if (a == null) return false;
        var b = dst[to];

        if (!dst.Accepts(a.item)) return false;
        if (b != null && !src.Accepts(b.item)) return false;

        if (b != null && b.item == a.item && a.item.maxStackSize > 1)
        {
            int add = Mathf.Min(a.count, a.item.maxStackSize - b.count);
            b.count += add;
            a.count -= add;
            if (a.count <= 0) src.slots[from] = null;
        }
        else
        {
            src.slots[from] = b;
            dst.slots[to]   = a;
        }

        src.Changed?.Invoke();
        if (dst != src) dst.Changed?.Invoke();
        return true;
    }
}
