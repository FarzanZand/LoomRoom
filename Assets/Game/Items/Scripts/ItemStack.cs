using System;

// A slot's contents. A class rather than a struct so it can grow per-instance state
// (durability, charges) later without touching the containers.
[Serializable]
public class ItemStack
{
    public ItemData item;
    public int      count = 1;
    // A scroll's charges spent (ItemData.charges): it goes with its last.
    public int      used;

    public ItemStack(ItemData item, int count = 1)
    {
        this.item  = item;
        this.count = count;
    }

    public bool IsEmpty => item == null || count <= 0;
}
