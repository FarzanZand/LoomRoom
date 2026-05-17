// Represents a temporary or permanent modification to a stat.
// Pass duration = -1 for permanent. Source is used for batch removal
// (e.g. remove all modifiers from an item when it's unequipped).
public class StatModifier
{
    public StatType     Stat     { get; }
    public float        Value    { get; }
    public ModifierType Type     { get; }
    public object       Source   { get; }
    public float        Duration { get; private set; } // -1 = permanent

    public bool IsPermanent => Duration < 0f;
    public bool IsExpired   => !IsPermanent && Duration <= 0f;

    public StatModifier(StatType stat, float value, ModifierType type,
                        object source = null, float duration = -1f)
    {
        Stat     = stat;
        Value    = value;
        Type     = type;
        Source   = source;
        Duration = duration;
    }

    internal void Tick(float deltaTime) => Duration -= deltaTime;
}
