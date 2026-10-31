using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;

// The script of a boss fight on the stage in front of the Dungeon Master (DungeonMilestone.fight, run by
// BossStageEncounter): the music and its beat, how the DM sets the boss down, his energy beam, events on a
// timeline (seconds, beats or the boss's health) and what he says while it lasts. One asset per fight.
[CreateAssetMenu(menuName = "LoomRoom/Boss Fight")]
public class BossFight : ScriptableObject
{
    const string Tabs = "Tabs";

    // ── Music ─────────────────────────────────────────────────────────
    [TabGroup(Tabs, "Music"), Tooltip("Played from the moment the DM reaches for the boss. Empty keeps the floor's music.")]
    public AudioClip music;
    [TabGroup(Tabs, "Music"), Range(0, 1)] public float musicVolume = .8f;
    [TabGroup(Tabs, "Music"), Min(1), Tooltip("Beats per minute of the music: events timed in beats and beams set to 'On the beat' follow it.")]
    public float bpm = 120f;
    [TabGroup(Tabs, "Music"), Min(0), Tooltip("Seconds from the start of the music to its first beat.")]
    public float firstBeat;
    [TabGroup(Tabs, "Music"), Min(0)] public float musicFade = 1.2f;

    // ── Opening ───────────────────────────────────────────────────────
    [TabGroup(Tabs, "Opening"), Tooltip("Said as the player reaches the middle of the stage, before the DM reaches for the boss.")]
    public DungeonMaster.Line openingLine = new() { text = "You came all this way for him. Very well." };
    [TabGroup(Tabs, "Opening"), Min(0), Tooltip("Seconds between the opening line and the DM reaching out.")]
    public float reachDelay = 1.2f;
    [TabGroup(Tabs, "Opening"), Min(.2f), Tooltip("Seconds the DM takes to bring the boss up from under the table, show it and set it down.")]
    public float placeSeconds = 4.6f;
    // ── Intro ─────────────────────────────────────────────────────────
    // With dialogue or the versus screen on, the fight opens with an intro, in this order: the floor's music fades as
    // the player steps onto the stage; the DM sets the boss down and it stands idle; the spotlight snaps on over it; the
    // dialogue; the versus screen; then the battle begins with the fight's music. Without either, the fight opens as
    // before (the music at once, his opening and fight lines in the feed).
    [TabGroup(Tabs, "Intro"), Min(0), Tooltip("Seconds the floor's music takes to fade out as the player steps onto the stage.")]
    public float enterMusicFade = 2f;
    [TabGroup(Tabs, "Intro"), Min(0), Tooltip("Seconds after the boss is set down before the spotlight comes on.")]
    public float spotlightDelay = 1f;
    [TabGroup(Tabs, "Intro"), AssetsOnly, Tooltip("The spotlight set over the boss (a prefab: lights, a shaft, dust), from the moment it comes on until the battle begins. Empty: none.")]
    public GameObject spotlight;
    [TabGroup(Tabs, "Intro"), Tooltip("Played as the spotlight comes on.")]
    public AudioClip spotlightSound;
    [TabGroup(Tabs, "Intro"), Range(0, 1)] public float spotlightVolume = 1f;
    [TabGroup(Tabs, "Intro"), Min(0), Tooltip("Seconds its lights take to go out as the battle begins.")]
    public float spotlightFadeOut = .8f;
    [TabGroup(Tabs, "Intro"), Min(0), Tooltip("Seconds after the spotlight before the dialogue starts.")]
    public float dialogueDelay = 2f;
    [TabGroup(Tabs, "Intro"), Tooltip("Dialogue boxes (PortraitDialogueUI: each speaker's head and shoulders idling beside a box, typed out) in place of his opening and fight lines in the feed.")]
    public bool introDialogue;
    [TabGroup(Tabs, "Intro"), ShowIf(nameof(introDialogue)), Tooltip("The boss's name on its boxes, the versus screen and the boss bar. Empty: the boss's own name.")]
    public string bossName;
    [TabGroup(Tabs, "Intro"), ShowIf(nameof(introDialogue)), ListDrawerSettings(ShowFoldout = true)]
    public List<PortraitLine> introLines = new();
    public bool HasIntroDialogue => introDialogue && introLines != null && introLines.Count > 0;
    [TabGroup(Tabs, "Intro"), Tooltip("After the dialogue, a you-versus-the-boss screen (VersusScreenUI): the player's band slams in from the left, then the boss's from the right, and VS crashes down between them.")]
    public bool versusScreen;
    [TabGroup(Tabs, "Intro"), ShowIf(nameof(versusScreen)), InlineProperty, HideLabel]
    public VersusSettings versus = new();
    [TabGroup(Tabs, "Intro"), Min(0), Tooltip("Seconds the fight's music takes to come in as the battle begins.")]
    public float fightMusicFade = .25f;

    // ── Room ──────────────────────────────────────────────────────────
    [TabGroup(Tabs, "Room"), Tooltip("The picture in the painting on the wall behind the Dungeon Master (RoomPainting) for this fight. It is changed while the boss floor loads and put back when the floor is left (after a death, on the way back to the room), so it is never seen changing. Empty: the painting's own.")]
    [PreviewField(70)] public Texture2D painting;
    [TabGroup(Tabs, "Room"), FormerlySerializedAs("darkRoom"), Tooltip("The room takes its own look when the fight begins (below), instead of the level's.")]
    public bool changeRoomLighting = true;
    [TabGroup(Tabs, "Room"), ShowIf(nameof(changeRoomLighting)), Tooltip("Normal: as it was. Dim: its lights lowered. Night: dark blue. Dark: black, only the stage, his eyes and the lights below.")]
    public RoomLighting roomLighting = RoomLighting.Dark;
    [TabGroup(Tabs, "Room"), ShowIf(nameof(changeRoomLighting)), Tooltip("The colour the room's light takes for the fight. White leaves it as it is.")]
    public Color roomTint = Color.white;
    [TabGroup(Tabs, "Room"), ShowIf(nameof(changeRoomLighting)), Min(0)] public float darkenSeconds = 2.5f;
    [TabGroup(Tabs, "Room"), Min(0), Tooltip("A light from the stage's side on the Dungeon Master's face and hands during the fight (0: none). He is many times a person's size: values in the hundreds.")]
    public float dungeonMasterLight;
    [TabGroup(Tabs, "Room"), ShowIf("@dungeonMasterLight > 0")] public Color dungeonMasterLightColor = new(1f, .85f, .65f);
    [TabGroup(Tabs, "Room"), Min(0), Tooltip("A light on the painting behind him during the fight, so it shows in the dark (0: none).")]
    public float paintingLight;
    [TabGroup(Tabs, "Room"), ShowIf("@paintingLight > 0")] public Color paintingLightColor = new(1f, .9f, .75f);
    [TabGroup(Tabs, "Opening"), Min(0), Tooltip("Seconds the boss stands still after it is set down, before it attacks.")]
    public float wakeDelay = 1.1f;
    [TabGroup(Tabs, "Opening"), Tooltip("Sound when the boss touches the stage.")]
    public AudioClip landSound;
    [TabGroup(Tabs, "Opening"), Range(0, 1)] public float landVolume = 1f;
    [TabGroup(Tabs, "Opening"), Tooltip("Said when the boss is down and the fight begins.")]
    public DungeonMaster.Line fightLine = new() { text = "Begin." };

    // ── Beam ──────────────────────────────────────────────────────────
    [TabGroup(Tabs, "Beam"), Min(.2f), Tooltip("Seconds the DM pours the beam onto the player. Block (shield up, facing him) the whole time.")]
    public float beamSeconds = 3f;
    [TabGroup(Tabs, "Beam"), Min(0), Tooltip("Seconds of warning: his hand glows and a thin line finds the player before the beam starts.")]
    public float beamWarning = 1.2f;
    [TabGroup(Tabs, "Beam"), Min(0), Tooltip("Damage a second while the beam lands on a player who is not blocking it.")]
    public float beamDamagePerSecond = 22f / 3f;
    [TabGroup(Tabs, "Beam"), Min(0), Tooltip("Stamina a second it costs to hold it off with the shield (0: holding block is enough).")]
    public float beamStaminaPerSecond = 0f;
    [TabGroup(Tabs, "Beam"), Range(-1, 1), Tooltip("How squarely the player must face the DM to block it (dot of the view and the direction to him).")]
    public float beamFacing = .2f;
    [TabGroup(Tabs, "Beam"), Tooltip("Start each beam on the next beat of the music.")]
    public bool beamOnBeat = true;
    [TabGroup(Tabs, "Beam"), Min(0), Tooltip("Metres the boss backs off from the player and waits while the beam pours (0: it fights on).")]
    public float beamGiveWay = 4.5f;
    [TabGroup(Tabs, "Beam"), Tooltip("While the DM charges and pours his beam the boss goes back to where he set it down, faces the table and kneels (HumanoidController Kneel); after the beam it fights on. Takes the place of Beam Give Way.")]
    public bool beamKneel = true;
    [TabGroup(Tabs, "Beam"), ShowIf(nameof(beamKneel)), Min(0), Tooltip("Seconds the boss gets to go back and kneel before the DM starts to charge (he starts sooner if it is already kneeling).")]
    public float beamKneelLead = 2.5f;
    [TabGroup(Tabs, "Beam"), ShowIf(nameof(beamKneel)), Tooltip("Danced instead of kneeling: once back at its spot the boss turns to the player and plays this clip, looped, until the beam is over. Empty: it kneels to the DM.")]
    public AnimationClip beamDance;
    [TabGroup(Tabs, "Beam"), ShowIf("@beamKneel && beamDance != null"), Min(.1f)] public float beamDanceSpeed = 1f;
    [TabGroup(Tabs, "Beam"), ColorUsage(false, true)] public Color beamColor = new(2f, 1.35f, .55f, 1f);
    [TabGroup(Tabs, "Beam")] public AudioClip beamChargeSound, beamLoopSound, beamBlockedSound;
    [TabGroup(Tabs, "Beam"), Range(0, 1)] public float beamVolume = .9f;

    // ── Volley ────────────────────────────────────────────────────────
    [TabGroup(Tabs, "Volley"), Min(1), Tooltip("Volley events: metres from the player, toward the DM, where his fireballs hang before they fly.")]
    public float volleyDistance = 7f;

    // ── Phase two ─────────────────────────────────────────────────────
    [TabGroup(Tabs, "Phase two"), Range(0, 1), Tooltip("When the boss's health falls to this share the fight's second phase begins: the stage's fires turn and the moonlight goes red (the stage's BossStageLights), the DM says the line and the stage shakes. 0: no second phase.")]
    public float phaseTwoHealth = .5f;
    [TabGroup(Tabs, "Phase two")] public DungeonMaster.Line phaseTwoLine = new() { text = "Now he's angry." };
    [TabGroup(Tabs, "Phase two"), Min(0)] public float phaseTwoShake = 1.2f;

    // ── Events ────────────────────────────────────────────────────────
    [TabGroup(Tabs, "Events"), Tooltip("What happens during the fight, in any order. Times count from the moment the boss wakes.")]
    [ListDrawerSettings(ShowFoldout = false, ListElementLabelName = "Label")]
    public List<BossEvent> events = new()
    {
        new BossEvent { when = BossEvent.When.EveryBeats, beats = 24, startBeat = 12, what = BossEvent.What.Beam },
        new BossEvent { when = BossEvent.When.AtBossHealth, health = .5f, what = BossEvent.What.Beam },
    };

    // ── Comments ──────────────────────────────────────────────────────
    [TabGroup(Tabs, "Comments"), Min(0), Tooltip("Seconds the DM keeps quiet after a comment. Everything else he would say waits until the fight is over.")]
    public float commentGap = 6f;
    [TabGroup(Tabs, "Comments")] public List<DungeonMaster.Line> beamWarned = new() { new() { text = "Hold this." }, new() { text = "Shield up." } };
    [TabGroup(Tabs, "Comments"), Tooltip("Always said after a beam the player held off the whole time.")]
    public List<DungeonMaster.Line> beamBlocked = new() { new() { text = "Good. You held it." }, new() { text = "Well blocked." }, new() { text = "That's how it's done." } };
    [TabGroup(Tabs, "Comments"), Tooltip("Always said after a beam that got through: tell them to block.")]
    public List<DungeonMaster.Line> beamHit = new() { new() { text = "You were meant to block that." }, new() { text = "Shield up when I do that." }, new() { text = "Block it next time." } };
    [TabGroup(Tabs, "Comments"), Tooltip("Said as he starts a fireball volley.")]
    public List<DungeonMaster.Line> volleyWarned = new() { new() { text = "Catch." }, new() { text = "Mind the fire." } };
    [TabGroup(Tabs, "Comments")] public List<DungeonMaster.Line> bossHalf = new() { new() { text = "Half of him left." } };
    [TabGroup(Tabs, "Comments")] public List<DungeonMaster.Line> bossLow = new() { new() { text = "He's nearly done." } };
    [TabGroup(Tabs, "Comments")] public List<DungeonMaster.Line> playerLow = new() { new() { text = "Careful now." }, new() { text = "You're bleeding a lot." } };
    [TabGroup(Tabs, "Comments")] public List<DungeonMaster.Line> victory = new() { new() { text = "Well played." } };
    [TabGroup(Tabs, "Comments")] public List<DungeonMaster.Line> defeat = new() { new() { text = "Again, then." } };

    public float SecondsPerBeat => 60f / Mathf.Max(1f, bpm);
}

// One event in a BossFight: when it happens and what it does.
[System.Serializable]
public class BossEvent
{
    // Explicit values, append only.
    public enum When { AtSeconds = 0, AtBeat = 1, EveryBeats = 2, AtBossHealth = 3 }
    public enum What { Beam = 0, Say = 1, Music = 2, Spawn = 3, Shake = 4, Volley = 5 }

    [Tooltip("Off: the event is skipped (to try a fight with and without it).")]
    public bool enabled = true;
    public When when;
    [ShowIf("@when == When.AtSeconds"), Min(0), Tooltip("Seconds after the boss wakes.")]
    public float seconds = 10f;
    [ShowIf("@when == When.AtBeat || when == When.EveryBeats"), Min(0), Tooltip("Beat of the music it first happens on (counting from 0 at the first beat).")]
    public float startBeat = 8f;
    [ShowIf("@when == When.EveryBeats"), Min(1), Tooltip("Then again every this many beats.")]
    public float beats = 16f;
    [ShowIf("@when == When.AtBossHealth"), Range(0, 1), Tooltip("When the boss's health first falls to this share.")]
    public float health = .5f;

    public What what;
    [ShowIf("@what == What.Say")] public DungeonMaster.Line line = new();
    [ShowIf("@what == What.Music")] public AudioClip music;
    [ShowIf("@what == What.Music"), Range(0, 1)] public float volume = .8f;
    [ShowIf("@what == What.Spawn"), AssetsOnly, Tooltip("Set down on the stage by the DM, like the boss.")]
    public GameObject enemy;
    [ShowIf("@what == What.Spawn"), Min(1)] public int count = 1;
    [ShowIf("@what == What.Shake"), Min(0)] public float shake = 1f;
    [ShowIf("@what == What.Volley"), AssetsOnly, Tooltip("The spell he throws (Fireball): its projectile, speed, radius and sounds.")]
    public SpellDefinition spell;
    [ShowIf("@what == What.Volley"), Min(0), Tooltip("Damage of each ball.")]
    public float damage = 8f;
    [ShowIf("@what == What.Volley"), InlineProperty, HideLabel]
    public FireballVolley.Settings volley = new() { scale = 3f, rise = 3f, spread = 3.5f, holdSeconds = .8f };

    public string Label => (enabled ? "" : "(off) ") + (when switch
    {
        When.AtSeconds => $"{seconds:0.#} s",
        When.AtBeat => $"beat {startBeat:0.#}",
        When.EveryBeats => $"every {beats:0.#} beats from {startBeat:0.#}",
        _ => $"boss at {health:P0}",
    }) + ": " + what;
}
