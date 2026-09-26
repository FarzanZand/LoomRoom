using System;
using Sirenix.OdinInspector;
using UnityEngine;

// One attack an enemy can perform. Authored on CharacterData.attacks; EnemyBrain
// picks one whose range fits and whose cooldown is ready, weighted at random.
[Serializable]
public class EnemyAttack
{
    [HorizontalGroup("Top"), LabelWidth(45)]
    public string name = "Attack";
    [HorizontalGroup("Top", Width = 110), LabelWidth(50), Min(0.01f), Tooltip("Relative chance among the attacks that are usable right now.")]
    public float weight = 1f;
    [FoldoutGroup("Timing and reach", Expanded = true), Tooltip("Animator trigger that plays the attack. The state should carry the 'Attack' tag.")]
    public string animatorTrigger = "Attack";
    [FoldoutGroup("Timing and reach"), Tooltip("Usable when the target is at least this far away.")]
    public float  minRange = 0f;
    [FoldoutGroup("Timing and reach"), Tooltip("Usable when the target is within this distance.")]
    public float  maxRange = 1.8f;
    public float EffectiveMaxRange => maxRange * (CombatManager.HasInstance ? Mathf.Max(1, CombatManager.Instance.meleeReachMultiplier) : 1f);
    [FoldoutGroup("Timing and reach")]
    public float  cooldown = 1.5f;
    [FoldoutGroup("Timing and reach"), Tooltip("Target must be within this angle of the enemy's forward before the attack fires.")]
    [Range(0f, 180f)] public float facingAngle = 45f;
    [FoldoutGroup("Timing and reach"), Tooltip("Hitbox index enabled by this attack's animation events (EnableHitboxAt).")]
    public int    hitboxIndex = 0;
    [FoldoutGroup("Timing and reach"), Tooltip("Seconds after the trigger before damage is applied when there is no hitbox / animation event. -1 = rely on hitbox events only.")]
    public float  fallbackHitDelay = 0.4f;
    [FoldoutGroup("On hit"), HideLabel, InlineProperty]
    public HitProfile hit = new HitProfile();
}
