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
    [Header("Way back (the entry in reverse)")]
    [Min(.1f), Tooltip("Flight from the table player's eyes back into the room player's, after a run ends.")] public float exitSeconds = 1.1f;
    [Min(0), Tooltip("How far the view rises out of the floor before it turns toward the room player.")] public float exitRise = 4f;
    [Min(.05f), Tooltip("How long the floor takes to sink into the table once the view is back in the room.")] public float exitSinkSeconds = .45f;
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

    // CampaignManager > Level generator: Cascade. The camera, blueprint lines and the flight in are shared.
    // In order: the footprint, the floor placed from its far end toward the entrance with every lamp out, a pause, the
    // entrance slammed down (shake, dust), the floor's lamps coming on one by one, the entrance's torches last, then the flight in.
    [Header("Cascade: footprint")]
    [Min(0), Tooltip("Seconds the floor's footprint shows as dark tiles before the first piece drops.")] public float ghostSeconds = .45f;
    [Tooltip("The footprint's tiles (their rim; the face is a little lighter), as seen on screen.")] public Color ghostColor = new(.2f, .2f, .23f, .85f);
    [Range(.5f, 1f), Tooltip("Share of a cell a footprint tile covers (the rest is the gap between them).")] public float ghostTileSize = .9f;
    [Header("Cascade: placing the floor")]
    [Min(.1f), Tooltip("Seconds from the first piece dropping at the floor's far end to the last one next to the entrance.")] public float cascadeSeconds = 1.6f;
    [Min(.05f), Tooltip("Seconds a piece takes to drop into place (each varies by Variety).")] public float tileDropSeconds = .34f;
    [Min(0), Tooltip("How high a piece starts, in cells (each varies by Variety).")] public float tileDropHeight = 1.6f;
    [Range(0, .8f), Tooltip("How much each piece's drop height, speed and tip differ from the next one's.")] public float variety = .45f;
    [Range(1, 1.5f), Tooltip("How large a piece starts (it shrinks to its size as it lands).")] public float tilePop = 1.15f;
    [Range(0, .5f), Tooltip("How flat a piece squashes as it lands, springing back after.")] public float tileSquash = .22f;
    [Range(0, 40), Tooltip("Most degrees a piece starts tipped.")] public float tileTilt = 18f;
    [Range(0, .4f), Tooltip("Random lead or lag of each piece, as a share of the whole placing (0: a clean front).")] public float waveJitter = .12f;
    [Range(0, .4f), Tooltip("How uneven the front is: whole patches run ahead or lag, as a share of the whole placing.")] public float waveWarp = .14f;
    [Range(0, .3f), Tooltip("How far behind the floor at the same spot walls and props come, as a share of the whole placing.")] public float wallLag = .04f;
    [Header("Cascade: the entrance")]
    [Min(0), Tooltip("Seconds of stillness after the floor before the entrance comes down.")] public float entrancePause = .45f;
    [Min(0), Tooltip("How high the entrance's pieces start, in cells.")] public float entranceDropHeight = 3f;
    [Min(.05f), Tooltip("Seconds the entrance takes to come down.")] public float entranceDropSeconds = .3f;
    [Range(0, 3), Tooltip("Degrees the view shakes as the entrance lands.")] public float entranceShake = 1.4f;
    [Min(0)] public float entranceShakeSeconds = .5f;
    [Tooltip("Dust blown out from under the entrance as it lands (a soft round particle, alpha blended). Empty: none.")] public Material entranceDustMaterial;
    [Min(0)] public int entranceDustCount = 220;
    [Min(0), Tooltip("A short warm flash over the entrance as it lands, this many times the strongest lamp on the floor (0: none).")] public float entranceFlash = 3f;
    [Header("Cascade: lights")]
    [Min(0), Tooltip("Seconds after the entrance lands before the floor's lamps start coming on.")] public float lightsPause = .3f;
    [Min(0), Tooltip("Seconds over which the floor's lamps come on, one by one in random order.")] public float lightsSeconds = 1.1f;
    [Min(.05f), Tooltip("Seconds each lamp takes to reach its strength.")] public float lightFadeSeconds = .35f;
    [Min(0), Tooltip("Seconds after the floor's lamps before the entrance's torches catch.")] public float entranceLightsPause = .3f;
    [Min(0), Tooltip("Seconds held once everything is lit, before the flight in.")] public float litHold = .35f;
    [Header("Cascade sound")]
    [Tooltip("Played as pieces land, one at a time, picked at random.")] public AudioClip[] tileSounds = new AudioClip[0];
    [Min(.01f), Tooltip("Least seconds between two landing sounds, however many pieces land at once.")] public float clackGap = .045f;
    [Range(0, 1)] public float clackVolume = .3f;
    [Tooltip("Random pitch range of the landing sounds.")] public Vector2 clackPitch = new(.7f, .9f);
    [Tooltip("Played as the footprint shows.")] public AudioClip footprintSound;
    [Range(0, 1)] public float footprintVolume = .35f;
    [Tooltip("Played as the entrance lands.")] public AudioClip entranceSlamSound;
    [Range(0, 1)] public float entranceSlamVolume = .7f;
    [Tooltip("A lamp catching, picked at random (not every lamp gets one).")] public AudioClip[] torchSounds = new AudioClip[0];
    [Range(0, 1)] public float torchVolume = .18f;
    [Min(.05f), Tooltip("Least seconds between two lamp sounds.")] public float torchGap = .18f;
    [Tooltip("Played as the entrance's torches catch.")] public AudioClip entranceTorchSound;
    [Range(0, 1)] public float entranceTorchVolume = .45f;
}
