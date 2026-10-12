using System;
using Sirenix.OdinInspector;
using UnityEngine;

public enum InventoryRole { Bag = 0, Hotbar = 1 }

// A fixed grid of item stacks living on a player. The hotbar is just a second
// Inventory with fewer slots and a type filter, so every move/swap/merge rule is
// written once. Save-friendly: the whole state is slots[] of (item, count).
// With a grid width (the bag) it is a Diablo grid: an item takes ItemData.GridSize cells and its stack is
// stored at its top-left cell (the anchor); the cells it covers stay empty in slots[] but are taken.
public class Inventory : MonoBehaviour
{
    [Tooltip("Bag or Hotbar. UI binds to the matching role on the active player.")]
    public InventoryRole role = InventoryRole.Bag;
    [Tooltip("Number of slots. The UI must have at least this many slot objects.")]
    [SerializeField, Min(1)] int slotCount = 24;
    [Tooltip("Item types this container accepts.")]
    [SerializeField] ItemTypeMask allowedTypes = ItemTypeMask.All;
    [Tooltip("Cells per row for a Diablo grid (items take several cells). 0: plain slots, one item each (the hotbar).")]
    [SerializeField, Min(0)] int gridWidth;

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

    // -- Grid --

    public bool IsGrid => gridWidth > 0;
    public int GridWidth => IsGrid ? gridWidth : SlotCount;
    public int GridHeight => IsGrid ? Mathf.CeilToInt(SlotCount / (float)gridWidth) : 1;
    public Vector2Int SizeOf(ItemData item) => IsGrid && item != null ? item.GridSize : Vector2Int.one;
    public Vector2Int CellOf(int index) => new(index % GridWidth, index / GridWidth);
    int CellIndex(int x, int y) => y * GridWidth + x;

    // The anchor of the item covering this cell, or -1. Plain slots: the cell itself if it holds something.
    public int OwnerAt(int index)
    {
        if (index < 0 || index >= slots.Length) return -1;
        if (!IsGrid) return slots[index] != null && !slots[index].IsEmpty ? index : -1;
        var cell = CellOf(index);
        for (int i = 0; i < slots.Length; i++)
        {
            var s = slots[i];
            if (s == null || s.IsEmpty) continue;
            var at = CellOf(i); var size = SizeOf(s.item);
            if (cell.x >= at.x && cell.x < at.x + size.x && cell.y >= at.y && cell.y < at.y + size.y) return i;
        }
        return -1;
    }

    // Whether the item fits with its top-left at anchor, ignoring the items at the given anchors.
    public bool Fits(ItemData item, int anchor, int ignoreA = -1, int ignoreB = -1)
    {
        if (item == null || anchor < 0 || anchor >= slots.Length) return false;
        if (!IsGrid) return slots[anchor] == null || slots[anchor].IsEmpty || anchor == ignoreA || anchor == ignoreB;
        var at = CellOf(anchor); var size = SizeOf(item);
        if (at.x + size.x > GridWidth || at.y + size.y > GridHeight) return false;
        for (int x = 0; x < size.x; x++)
            for (int y = 0; y < size.y; y++)
            {
                int index = CellIndex(at.x + x, at.y + y);
                if (index >= slots.Length) return false;
                int owner = OwnerAt(index);
                if (owner >= 0 && owner != ignoreA && owner != ignoreB) return false;
            }
        return true;
    }

    // The anchors of the items this footprint would cover (none when it fits).
    public System.Collections.Generic.List<int> Overlaps(ItemData item, int anchor, int ignore = -1)
    {
        var list = new System.Collections.Generic.List<int>();
        if (anchor < 0 || anchor >= slots.Length) return list;
        if (!IsGrid) { if (slots[anchor] != null && !slots[anchor].IsEmpty && anchor != ignore) list.Add(anchor); return list; }
        var at = CellOf(anchor); var size = SizeOf(item);
        for (int x = 0; x < size.x; x++)
            for (int y = 0; y < size.y; y++)
            {
                if (at.x + x >= GridWidth || at.y + y >= GridHeight) continue;
                int owner = OwnerAt(CellIndex(at.x + x, at.y + y));
                if (owner >= 0 && owner != ignore && !list.Contains(owner)) list.Add(owner);
            }
        return list;
    }

    // Where a new item goes: column by column from the top left, so tall things line up (Diablo).
    public int FindSpot(ItemData item, int ignore = -1)
    {
        if (!IsGrid) return FirstEmpty();
        for (int x = 0; x < GridWidth; x++)
            for (int y = 0; y < GridHeight; y++)
            {
                int index = CellIndex(x, y);
                if (index < slots.Length && Fits(item, index, ignore)) return index;
            }
        return -1;
    }

    public ItemData ItemAt(int index) => this[index]?.item;
    public int      CountAt(int index) => this[index]?.count ?? 0;

    // A free slot (a free cell in a grid).
    public int FirstEmpty()
    {
        for (int i = 0; i < slots.Length; i++)
            if ((slots[i] == null || slots[i].IsEmpty) && (!IsGrid || OwnerAt(i) < 0)) return i;
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

    // Whether TryAdd would take all of count: the container accepts the item and has the space.
    public bool HasRoomFor(ItemData item, int count = 1)
    {
        if (!Accepts(item) || count <= 0) return false;
        long capacity = 0;
        foreach (var slot in slots)
            if (slot != null && !slot.IsEmpty && slot.item == item) capacity += Mathf.Max(0, item.maxStackSize - slot.count);
            else if (!IsGrid && (slot == null || slot.IsEmpty)) capacity += Mathf.Max(1, item.maxStackSize);
        if (IsGrid && capacity < count)
        {
            // Place copies on the grid until enough fit, then put it back as it was.
            var saved = (ItemStack[])slots.Clone();
            try
            {
                while (capacity < count)
                {
                    int spot = FindSpot(item);
                    if (spot < 0) break;
                    slots[spot] = new ItemStack(item, 1);
                    capacity += Mathf.Max(1, item.maxStackSize);
                }
            }
            finally { slots = saved; }
        }
        return capacity >= count;
    }

    // Adds to an existing stack first, then the first empty slot. Returns false if nothing fit.
    public bool TryAdd(ItemData item, int count = 1)
    {
        // Pickup callers retry in the other container on failure. Refuse atomically
        // so a partially filled hotbar cannot duplicate the same world drop in the bag.
        if (!HasRoomFor(item, count)) return false;

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
            int empty = FindSpot(item);
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
        if (src.IsGrid || dst.IsGrid) return MoveGrid(src, from, dst, to);
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

    // A move where either side is a grid. The item goes with its top-left at `to`. Landing on one item:
    // a stack of the same thing merges, anything else swaps places with it when it fits where this came from
    // (or anywhere in that container). Landing across two or more items is refused.
    static bool MoveGrid(Inventory src, int from, Inventory dst, int to)
    {
        var a = src[from];
        if (!dst.Accepts(a.item)) return false;
        var hit = dst.Overlaps(a.item, to, src == dst ? from : -1);
        if (hit.Count > 1) return false;
        if (hit.Count == 0)
        {
            if (!dst.Fits(a.item, to, src == dst ? from : -1)) return false;
            src.slots[from] = null;
            dst.slots[to] = a;
        }
        else
        {
            int other = hit[0];
            var b = dst.slots[other];
            if (b.item == a.item && a.item.maxStackSize > 1)
            {
                int add = Mathf.Min(a.count, a.item.maxStackSize - b.count);
                b.count += add; a.count -= add;
                if (a.count <= 0) src.slots[from] = null;
            }
            else
            {
                if (!src.Accepts(b.item)) return false;
                // Lift both, place the dragged one, then put the other back where the dragged one was.
                src.slots[from] = null; dst.slots[other] = null;
                bool placed = dst.Fits(a.item, to);
                if (placed) dst.slots[to] = a;
                int back = placed ? (src.Fits(b.item, from) ? from : src.FindSpot(b.item)) : -1;
                if (!placed || back < 0)
                {
                    if (placed) dst.slots[to] = null;
                    src.slots[from] = a; dst.slots[other] = b;
                    return false;
                }
                src.slots[back] = b;
            }
        }
        src.Changed?.Invoke();
        if (dst != src) dst.Changed?.Invoke();
        return true;
    }
}
