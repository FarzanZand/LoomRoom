using System.Collections.Generic;
using UnityEngine;

// The Pianist's passive (the user, 2026-11-25): "every 8 hits deal double damage", and the crescendo sounds and looks
// like a piano. Counted on every blow the table player lands on a character that does damage, melee and spells alike: not a
// blocked one, not one that takes nothing (IMMUNE), not an effect's tick (a burn). The Hits Per Crescendo-th is the
// crescendo: its amount times Damage Multiplier before armour (as a critical's is), shown with "CRESCENDO!" over its
// number (DamageInfo.Crescendo), then the count starts over. Only the crescendo plays Crescendo Burst and Crescendo
// Sound where it landed, on top of the weapon's usual hit sound; every other hit sounds and looks as it does for any
// class (the user, 2026-11-25: "Weapon swings should only do the piano sound on the crescendo hit, also weapon hit audio
// should play like normal", and the notes only on the crescendo). The count is
// per player and starts over with each run. Called from CharacterStats.TakeDamage (Apply) for a run whose class has it.
[CreateAssetMenu(menuName = "LoomRoom/Class Passive/Crescendo")]
public class CrescendoPassive : ClassPassive
{
    [Min(1), Tooltip("Landed hits per crescendo: the last of them is the crescendo.")] public int hitsPerCrescendo = 8;
    [Min(1f), Tooltip("The crescendo's damage, times this, before armour.")] public float damageMultiplier = 2f;
    [Tooltip("At the hit point on the crescendo, and only there (Characters/Classes/Pianist/Effects/Crescendo burst).")]
    public GameObject crescendoBurst;
    [Min(.5f), Tooltip("Seconds a burst is kept before it is removed.")] public float burstSeconds = 3f;
    [Tooltip("Played where the crescendo landed (a run up into a chord), on top of the weapon's hit sound.")] public AudioData crescendoSound;

    // Landed hits since the last crescendo, per player.
    static readonly Dictionary<Player, int> counts = new();
    static bool listening;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { counts.Clear(); listening = false; }

    // Every run starts the count over.
    static void Listen()
    {
        if (listening || !RunManager.HasInstance) return;
        RunManager.Instance.RunStarted += () => counts.Clear();
        listening = true;
    }

    // The hits counted so far (for the HUD and tests).
    public static int Count(Player player) => player != null && counts.TryGetValue(player, out int n) ? n : 0;

    // The Crescendo of the class of the player who struck, if the hit is one it counts.
    public static CrescendoPassive For(in DamageInfo info, Character victim)
    {
        if (info.Source is not Player player || victim == null || victim == player || info.FromEffect || info.Blocked || info.Spared || info.Amount <= 0f) return null;
        if (victim is Player) return null;
        var progress = player.GetComponent<AdventurerProgress>();
        return progress != null && progress.InRun && ProgressionManager.MetaActive && progress.selectedClass != null ? progress.selectedClass.passive as CrescendoPassive : null;
    }

    // Before armour: counts the hit, and makes the Hits Per Crescendo-th the crescendo.
    public void Count(ref DamageInfo info)
    {
        Listen();
        var player = (Player)info.Source;
        counts.TryGetValue(player, out int n);
        n++;
        if (n >= hitsPerCrescendo)
        {
            n = 0;
            info.Amount *= damageMultiplier;
            info.Crescendo = true;
        }
        counts[player] = n;
    }

    // After the hit has landed: on the crescendo, its notes and its piano where it landed; nothing on other hits.
    public void Play(in DamageInfo info, Character victim)
    {
        if (!info.Crescendo) return;
        Vector3 at = info.HitPoint != Vector3.zero ? info.HitPoint : victim.transform.position + Vector3.up;
        if (crescendoBurst != null) Destroy(Instantiate(crescendoBurst, at, Quaternion.identity), burstSeconds);
        if (crescendoSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFXData(crescendoSound, at);
    }
}
