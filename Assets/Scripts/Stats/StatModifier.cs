// Represents a temporary or permanent modification to a stat.
// Source is used to batch-remove all modifiers from one item, buff, or effect.
public class StatModifier
{
    public StatType     Stat   { get; }
    public float        Value  { get; }
    public ModifierType Type   { get; }
    public object       Source { get; }

    public StatModifier(StatType stat, float value, ModifierType type, object source = null)
    {
        Stat   = stat;
        Value  = value;
        Type   = type;
        Source = source;
    }
}
