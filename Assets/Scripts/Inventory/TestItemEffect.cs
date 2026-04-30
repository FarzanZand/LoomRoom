using UnityEngine;

[CreateAssetMenu(fileName = "TestItemEffect", menuName = "Inventory/Effects/Test")]
public class TestItemEffect : ItemEffectBase
{
    public override void Apply(ItemData item)
    {
        Debug.Log("custom script used");
    }
}
