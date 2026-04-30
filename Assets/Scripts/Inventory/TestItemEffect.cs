using UnityEngine;

public class TestItemEffect : ItemEffectBase
{
    public override void Apply(ItemData item)
    {
        Debug.Log("custom script used");
    }
}
