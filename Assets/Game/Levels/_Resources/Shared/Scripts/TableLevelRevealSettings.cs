using UnityEngine;
using Sirenix.OdinInspector;

[CreateAssetMenu(menuName = "Table/Level Reveal", fileName = "Table level reveal")]
public class TableLevelRevealSettings : ScriptableObject
{
    [Header("Timing (unscaled seconds)")]
    [Min(.1f)] public float blueprintSeconds = .55f;
    [Min(.1f)] public float buildSeconds = 2.1f;
    [Min(.1f)] public float settleSeconds = .4f;
    [Min(.1f)] public float cameraTransitionSeconds = .65f;
    [Min(.1f), Tooltip("Camera flight from the assembled table into first person.")] public float approachSeconds = 1.25f;
    public bool allowSkip = true;
    [Header("Assembly")]
    [Min(0)] public float liftDistance = 2.5f;
    [Range(.1f, 1f)] public float pieceDuration = .35f;
    public AnimationCurve rise = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [Header("Blueprint")]
    public Material lineMaterial;
    [Tooltip("Optional authored dust prefab. Its box emission area is fitted to the dungeon.")]
    public ParticleSystem dustPrefab;
    public Color blueprintColor = new Color(.43f, .56f, .63f, .8f);
    public Color entranceColor = new Color(.65f, .82f, .9f, 1);
    [Min(.01f)] public float lineWidth = .09f;
    [Header("Camera")]
    [Tooltip("Watch construction from the room player's existing position and look direction, without an overhead angle.")]
    public bool useRoomPlayerPOV;
    [HideIf("useRoomPlayerPOV"), Min(1)] public float overheadHeight = 12;
    [HideIf("useRoomPlayerPOV"), Range(45, 90), Tooltip("Overview pitch: 90 is straight down; lower values tilt toward the table.")]
    public float cameraAngle = 80;
    [HideIf("useRoomPlayerPOV"), Range(1, 1.5f)] public float framingMargin = 1.12f;
    [Header("Audio (through AudioManager SFX mixer)")]
    public AudioClip buildSound;
    public AudioClip settleSound;
    [Range(0, 1)] public float volume = .4f;
}
