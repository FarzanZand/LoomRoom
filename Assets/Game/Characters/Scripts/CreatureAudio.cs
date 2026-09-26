using System;
using Sirenix.OdinInspector;
using UnityEngine;

// A creature's voice, footsteps and body impact, stored on its CharacterData. EnemyVoice
// plays it in 3D so the player hears what is coming; CombatManager plays the impact.
[Serializable]
public class CreatureAudio
{
    [Tooltip("Muttering while idle, wandering or patrolling.")]
    public AudioClip[] idle = new AudioClip[0];
    [Tooltip("The bark when it spots the player. Plays before it comes around the corner.")]
    public AudioClip[] alert = new AudioClip[0];
    public AudioClip[] pain = new AudioClip[0];
    public AudioClip[] death = new AudioClip[0];
    public AudioClip[] footsteps = new AudioClip[0];
    [Tooltip("Body hit sound when struck by a weapon using default effects. Empty uses CombatManager's default hit sound.")]
    public AudioClip[] impact = new AudioClip[0];

    [FoldoutGroup("Mix and range"), Range(0f, 1f)] public float voiceVolume = .9f;
    [FoldoutGroup("Mix and range"), Range(0f, 1f)] public float footstepVolume = .55f;
    [FoldoutGroup("Mix and range"), Range(0f, 1f)] public float impactVolume = .9f;
    [FoldoutGroup("Mix and range"), Range(.3f, 2f), Tooltip("Base pitch. Lower for big creatures, higher for small ones.")]
    public float pitch = 1f;
    [FoldoutGroup("Mix and range"), Range(0f, .5f)] public float pitchVariance = .08f;
    [FoldoutGroup("Mix and range"), Min(.1f), Tooltip("Full volume within this distance.")] public float minDistance = 2f;
    [FoldoutGroup("Mix and range"), Min(1f), Tooltip("Silent beyond this distance. Large values let the player hear things around corners.")]
    public float maxDistance = 26f;
    [FoldoutGroup("Timing"), MinMaxSlider(1, 40, true), Tooltip("Seconds between idle mutters.")]
    public Vector2 idleInterval = new Vector2(6f, 14f);
    [FoldoutGroup("Timing"), Min(.05f)] public float walkStepInterval = .55f;
    [FoldoutGroup("Timing"), Min(.05f)] public float runStepInterval = .34f;
    [FoldoutGroup("Timing"), Min(0f)] public float painCooldown = .6f;

    public static AudioClip Pick(AudioClip[] clips) =>
        clips == null || clips.Length == 0 ? null : clips[UnityEngine.Random.Range(0, clips.Length)];

    public void PlayAt(AudioClip[] clips, Vector3 position, float volume)
    {
        var clip = Pick(clips);
        if (clip == null || !AudioManager.HasInstance) return;
        AudioManager.Instance.PlaySFX(clip, position, volume, pitch, pitchVariance, minDistance, maxDistance);
    }
}
