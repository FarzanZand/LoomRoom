using Sirenix.OdinInspector;
using UnityEngine;

// The shield bash's tuning (Characters/Skills/Blocking/Shield bash), read by ShieldBash through CombatManager.shieldBash.
// Its effects and sounds are in Characters/Skills/Blocking/Shield bash/.
public class ShieldBashAbility : SkillAbility
{
    [Title("Shield bash")]
    [Tooltip("Holding block and pressing attack bashes forward with the shield: a fast dash that stops at the first enemy in the way, hits it and stuns it. Needs a shield in hand. While it cools down, block and attack swings as usual.")]
    public bool enabled = true;
    [Min(0f), Tooltip("Seconds before the bash can be used again.")]
    public float cooldown = 5f;
    [Min(.5f), Tooltip("Dash speed in metres per second.")]
    public float speed = 15f;
    [Min(.5f), Tooltip("Longest dash in metres, when nothing is in the way.")]
    public float distance = 6f;
    [Min(0f), Tooltip("Seconds the dash waits after the press before it moves (the shield coming forward).")]
    public float windup = .06f;
    [Min(0f), Tooltip("Seconds after the hit (or the end of the dash) before you can attack or block again.")]
    public float recovery = .3f;
    [Range(0f, 90f), Tooltip("How far off straight ahead (degrees) an enemy can be and still be hit by the dash.")]
    public float angle = 35f;
    [Min(0f), Tooltip("Damage of the hit before armour: Holy damage, like a spell it ignores the enemy's armour.")]
    public float damage = 6f;
    [Min(0f), Tooltip("Extra damage per point of damage the player's own armour stops (armour x Armor Per Point, so 5 armour counts as 1).")]
    public float damagePerArmor = 1.5f;
    [Min(0f), Tooltip("Seconds the struck enemy stands stunned (its swing is cancelled). Its usual stagger immunity follows.")]
    public float stun = 1f;
    [Min(0f), Tooltip("Push on the struck enemy. Small: it stays standing in front of you for the follow-up.")]
    public float knockback = 1.5f;
    [Min(0f), Tooltip("Stamina the bash costs.")]
    public float staminaCost = 10f;

    [Title("Look and sound")]
    [ColorUsage(false, true), Tooltip("The shield's glow during the dash (HDR: above 1 glows).")]
    public Color glow = new(1.8f, 1.55f, 1.05f);
    [Tooltip("Gold streaks that rush past during the dash (Shield bash/Shield bash streaks).")]
    public GameObject streaks;
    public AudioData swingAudio;
    public AudioData hitAudio;
}
