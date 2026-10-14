using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

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
    [TabGroup(Tabs, "Opening"), Tooltip("The room goes dark for the fight (only the stage and his eyes are lit), as in the intro.")]
    public bool darkRoom = true;
    [TabGroup(Tabs, "Opening"), Min(0)] public float darkenSeconds = 2.5f;
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
    public float beamDamagePerSecond = 12f;
    [TabGroup(Tabs, "Beam"), Min(0), Tooltip("Stamina a second it costs to hold it off with the shield (0: holding block is enough).")]
    public float beamStaminaPerSecond = 0f;
    [TabGroup(Tabs, "Beam"), Range(-1, 1), Tooltip("How squarely the player must face the DM to block it (dot of the view and the direction to him).")]
    public float beamFacing = .2f;
    [TabGroup(Tabs, "Beam"), Tooltip("Start each beam on the next beat of the music.")]
    public bool beamOnBeat = true;
    [TabGroup(Tabs, "Beam"), Min(0), Tooltip("Metres the boss backs off from the player and waits while the beam pours (0: it fights on).")]
    public float beamGiveWay = 4.5f;
    [TabGroup(Tabs, "Beam"), ColorUsage(false, true)] public Color beamColor = new(2f, 1.35f, .55f, 1f);
    [TabGroup(Tabs, "Beam")] public AudioClip beamChargeSound, beamLoopSound, beamBlockedSound;
    [TabGroup(Tabs, "Beam"), Range(0, 1)] public float beamVolume = .9f;

    // ── Volley ────────────────────────────────────────────────────────
    [TabGroup(Tabs, "Volley"), Min(1), Tooltip("Volley events: metres from the player, toward the DM, where his fireballs hang before they fly.")]
    public float volleyDistance = 7f;

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
    [TabGroup(Tabs, "Comments")] public List<DungeonMaster.Line> bossLow = new() { new() { text = "He's nearly done. So are you." } };
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
