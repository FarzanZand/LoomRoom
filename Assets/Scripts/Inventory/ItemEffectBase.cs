using UnityEngine;

public abstract class ItemEffectBase : ScriptableObject
{
    public abstract void Apply(ItemData item);
}
