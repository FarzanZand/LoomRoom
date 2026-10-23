// Serialized by integer — never renumber, append only.
public enum Faction
{
    Neutral = 0,
    Player  = 1,
    Enemy   = 2,
}

// One place that answers "can A hurt B". Combat, perception and item effects all ask
// here instead of comparing enums, so allies or infighting later are a one-line change.
public static class FactionRules
{
    public static bool IsHostile(Faction a, Faction b)
    {
        if (a == Faction.Neutral || b == Faction.Neutral) return false;
        return a != b;
    }
}
