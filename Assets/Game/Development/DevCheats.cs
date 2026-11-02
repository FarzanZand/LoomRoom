#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

// Switches for the cheats in the spawn window's Cheats (F6 opens that list on its own), Editor and development builds only.
public static class DevCheats
{
    // Every hit the table player lands does 999 damage (CharacterStats.TakeDamage).
    public static bool MightyBlows;
    public const float MightyDamage = 999f;

    // Statics outlive Play mode (domain reload is off): a cheat must not carry over to the next session.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => MightyBlows = false;
}
#endif
