using System;

// A slot's contents. A class rather than a struct so it can grow per-instance state
// (durability, charges) later without touching the containers.
[Serializable]
public class ItemStack
{
    public ItemData item;
    public int      count = 1;

    public ItemStack(ItemData item, int count = 1)
    {
        this.item  = item;
        this.count = count;
    }

    public bool IsEmpty => item == null || count <= 0;
    public bool CanStackWith(ItemData other) => item == other && item != null && count < item.maxStackSize;
}
