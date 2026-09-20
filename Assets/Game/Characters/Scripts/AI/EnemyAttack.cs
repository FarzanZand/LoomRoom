using System;
using UnityEngine;

// One attack an enemy can perform. Authored on CharacterData.attacks; EnemyBrain
// picks one whose range fits and whose cooldown is ready, weighted at random.
[Serializable]
public class EnemyAttack
{
    public string name = "Attack";
    [Tooltip("Animator trigger that plays the attack. The state should carry the 'Attack' tag.")]
    public string animatorTrigger = "Attack";
    [Tooltip("Usable when the target is at least this far away.")]
    public float  minRange = 0f;
    [Tooltip("Usable when the target is within this distance.")]
    public float  maxRange = 1.8f;
    public float  cooldown = 1.5f;
    [Tooltip("Target must be within this angle of the enemy's forward before the attack fires.")]
    [Range(0f, 180f)] public float facingAngle = 45f;
    [Tooltip("Relative chance among the attacks that are usable right now.")]
    [Min(0.01f)] public float weight = 1f;
    [Tooltip("Hitbox index enabled by this attack's animation events (EnableHitboxAt).")]
    public int    hitboxIndex = 0;
    [Tooltip("Seconds after the trigger before damage is applied when there is no hitbox / animation event. -1 = rely on hitbox events only.")]
    public float  fallbackHitDelay = 0.4f;
    public HitProfile hit = new HitProfile();
}
