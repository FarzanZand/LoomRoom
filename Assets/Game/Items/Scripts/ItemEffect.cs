using UnityEngine;

// Hook for item behaviour too rich for the inline EffectEntry types. Subclass, add a
// [CreateAssetMenu(menuName = "Items/Effects/<Name>")], create the asset, and drop it
// into an EffectEntry with type = Custom. Assets live under Assets/Game/Items/Effects.
public abstract class ItemEffect : ScriptableObject
{
    public abstract void Apply(EffectEntry entry, EffectContext ctx);

    // One line for the tooltip. Default is the asset name.
    public virtual string Describe(EffectEntry entry) => name;

    // A Relic's unique skill: the tooltip sets it apart, on its own line in the unique skill colour (ItemData.SkillColor).
    public virtual bool UniqueSkill => false;
}
