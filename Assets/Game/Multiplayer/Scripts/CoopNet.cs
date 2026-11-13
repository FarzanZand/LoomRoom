using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// Who runs what in a co-op session. The host runs the world: enemies, room events, the stairs, the run's end.
// Every player runs their own player (movement, swings, blocks, inventory) and builds the same floor from the
// same seed, so only what changes travels: enemy snapshots, hits, opened chests, taken loot.
public static class Coop
{
    public static bool Running => MultiplayerManager.HasInstance && MultiplayerManager.Instance.Connected;
    public static bool IsHost => Running && MultiplayerManager.Instance.IsHost;
    // A player who joined someone else's game: enemies are puppets here.
    public static bool IsGuest => Running && !MultiplayerManager.Instance.IsHost;
    public static int LocalSlot => Running ? MultiplayerManager.Instance.LocalSlot : 0;
    // Players in the current run (scales enemies); 1 outside co-op.
    public static int PartySize => Running ? Mathf.Max(1, MultiplayerManager.Instance.PartySize) : 1;
    // Enemies added to each generated pack for the party (MultiplayerManager.extraEnemiesPerPlayer).
    public static int ExtraEnemies => Running && PartySize > 1 ? (PartySize - 1) * MultiplayerManager.Instance.extraEnemiesPerPlayer : 0;
    // The source of the party's extra enemy health.
    public static readonly object PartySource = new();
    // True while a hit that came over the network is applied, so CoopWorld lets it through.
    public static bool ApplyingRemote;

    public const byte NoSlot = 255;

    public static string SaveId(ItemData item) => item != null ? item.saveId : "";
    public static ItemData Item(string saveId)
    {
        if (string.IsNullOrEmpty(saveId) || !InventoryManager.HasInstance) return null;
        return ItemData.FromSaveId(saveId, InventoryManager.Instance.itemCatalog);
    }
}

// A hit sent over the network. Player hits on enemies go guest -> host; the host's result goes to everyone
// (health after the hit, so every screen shows the same bar); enemy hits on a guest go host -> that guest.
public struct NetDamage : INetworkSerializable
{
    public ushort enemy;          // the enemy hit, or the enemy that hit
    public byte slot;             // the player who struck or was struck (Coop.NoSlot: none)
    public float amount;
    public Vector3 hitPoint, direction, swing;
    public float knockback;
    public byte flags;
    public sbyte attack;          // the enemy's attack (its hit profile), -1 unknown
    public float health;          // host -> guests: the enemy's health after the hit
    public float stun;            // guest -> host: seconds the blow stuns the enemy (a shield bash)
    public FixedString128Bytes weapon;
    public bool spared;           // host -> guests: it took no health (the boss shielded during a beam: IMMUNE)
    public bool finisher;         // the combo's last hit (DamageInfo.Finisher: Heavy without being a charged heavy attack)

    const byte Heavy = 1, Critical = 2, Backstab = 4, Magic = 8, Blocked = 16, Parried = 32, FromEffect = 64, DiedFlag = 128;
    public bool Died { get => (flags & DiedFlag) != 0; set => flags = (byte)(value ? flags | DiedFlag : flags & ~DiedFlag); }

    public static NetDamage From(DamageInfo info)
    {
        var d = new NetDamage
        {
            amount = info.Amount, hitPoint = info.HitPoint, direction = info.Direction, swing = info.SwingDirection,
            knockback = info.KnockbackForce, attack = -1, slot = Coop.NoSlot,
            weapon = new FixedString128Bytes(Coop.SaveId(info.Weapon)), spared = info.Spared, finisher = info.Finisher,
        };
        if (info.Heavy) d.flags |= Heavy;
        if (info.Critical) d.flags |= Critical;
        if (info.Backstab) d.flags |= Backstab;
        if (info.Magic) d.flags |= Magic;
        if (info.Blocked) d.flags |= Blocked;
        if (info.Parried) d.flags |= Parried;
        if (info.FromEffect) d.flags |= FromEffect;
        return d;
    }

    public DamageInfo ToInfo(Character source)
    {
        var weaponItem = Coop.Item(weapon.ToString());
        HitProfile profile = null;
        if (weaponItem != null) profile = weaponItem.weapon;
        else if (source != null && attack >= 0 && source.data is EnemyData data && attack < data.attacks.Count) profile = data.attacks[attack].hit;
        return new DamageInfo
        {
            Profile = profile, Amount = amount, Source = source, Weapon = weaponItem,
            HitPoint = hitPoint, Direction = direction, SwingDirection = swing, KnockbackForce = knockback,
            Heavy = (flags & Heavy) != 0, Critical = (flags & Critical) != 0, Backstab = (flags & Backstab) != 0,
            Magic = (flags & Magic) != 0, Blocked = (flags & Blocked) != 0, Parried = (flags & Parried) != 0,
            FromEffect = (flags & FromEffect) != 0, Spared = spared, Finisher = finisher,
        };
    }

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref enemy); s.SerializeValue(ref slot); s.SerializeValue(ref amount);
        s.SerializeValue(ref hitPoint); s.SerializeValue(ref direction); s.SerializeValue(ref swing);
        s.SerializeValue(ref knockback); s.SerializeValue(ref flags); s.SerializeValue(ref attack);
        s.SerializeValue(ref health); s.SerializeValue(ref stun); s.SerializeValue(ref weapon); s.SerializeValue(ref spared); s.SerializeValue(ref finisher);
    }
}

// Something that happened to the floor: a chest opened, loot taken, a door, the stairs. Sent by whoever did it,
// passed on by the host, applied once everywhere else.
public enum CoopEventKind : byte
{
    ContainerOpened = 0,
    DestructibleBroken = 1,
    DoorOpened = 2,
    PickupTaken = 3,
    CoinsTaken = 4,
    ItemDropped = 5,
    AtExit = 6,
    SpikeSprung = 7,
    AmbushSprung = 8,
    AmbushCleared = 9,
    EnemySwing = 10,
    ArrowLoosed = 11,
    EnemyGone = 12,
    RoomDoor = 13,      // a door in the apartment (not a floor's)
    EnemyHealth = 14,   // catching a player up: a wounded enemy's health now (amount = health x 100)
    EnemySpell = 15,    // an enemy cast: amount = its spell (EnemyData.spells), position = the hand or the ground spot, direction = a projectile's
    AmbushOpened = 16,  // an ambush whose room the party left (all dead or outside): the gates lift, the fight goes on
    BossStageOpened = 17, // the stage fight began (position = who stepped up)
    BossBeam = 18,      // the Dungeon Master's beam (target = at whom)
    BossVolley = 19,    // the Dungeon Master's fireball volley (target = at whom, amount = its event in the fight)
    AttackTell = 22,    // an enemy's windup began: its warning glow and sound (id = the enemy)
    PartySpoiled = 21, // a Skeleton Party's dance broke up (position = the party's room spot)
    Ping = 20,          // a player pointed at something (position = where, item = what it is called; CoopPing)
    NpcProvoked = 23,   // a provokable character was struck (id = it on the floor, amount = its hits so far; ProvokableNpc)
    PlayerSpell = 24,   // a player's spell, seen by the others (CoopSpells: item = the spell, id = the enemy aimed at + 1)
    Noise = 25,         // a guest made a sound the host's enemies may hear (amount = its reach x 100)
    EventUse = 27,      // a player used a scripted event room's piece (id = the room on the floor, amount = which use; ICoopEventRoom)
    EventStep = 28,     // the host's event room moved on (id = the room, amount = the step, extra.x = a value, item = a seed)
    Meddle = 26,        // the Dungeon Master meddles (DungeonMasterMeddling): amount 0 adds an enemy at position in room id, 1 takes chest id
    FeatureUsed = 29,   // a blood altar or a shrine answered someone (id = it among the floor's altars and shrines): silent for all
    WareBought = 30,    // shared loot: a ware was bought (id = the merchant on the floor, item = the ware's save id): gone for all
    BossIntroDone = 31, // a guest's boss intro (its lines and versus screen) is over: the host's fight waits for everyone's
    BossFightBegun = 32, // host: the stage fight's battle begins (every intro still showing is put away)
    DevSpawn = 33,      // a guest's spawn window: the host makes the enemy (amount = MultiplayerManager.enemyPrefabs index, item = its name)
    BossBeaten = 34,
    SpellAffliction = 36, // a player's spell stunned, chilled or burned an enemy (id = it, item = the spell, amount = its power x 100): the host does it, the others show it
    DmRemark = 37,      // the Dungeon Master answered a player (amount 0: a spell hit him, item = the spell; 1: a blow on a fighter he holds, position = its puff), id = his line
    ArenaTalk = 35,     // the Arena fighter's say: host, amount 0, it is said to the player in slot id; a guest, amount 1, theirs is over    // host, only to a player catching up: the stage's boss is dead (position = where it fell; its chest and the way on)
}

// The owner's arm animation for a teammate's one body (CoopPlayer.SendArms, CoopAvatar.ApplyArms): each layer's state,
// the animator's float, int and bool parameters in the controller's order, and the look's pitch.
public struct ArmsPose : INetworkSerializeByMemcpy
{
    public Unity.Collections.FixedList64Bytes<int> states;
    public Unity.Collections.FixedList512Bytes<float> values;
    public float pitch;
}

// The host's trophy wall for the guests (MultiplayerManager.SendTrophies): "key=state;key=state" of its meta.trophy flags.
public struct CoopTrophies : INetworkSerializable
{
    public Unity.Collections.FixedString4096Bytes text;
    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter => s.SerializeValue(ref text);
}

public struct CoopEvent : INetworkSerializable
{
    public CoopEventKind kind;
    public byte slot;             // who did it
    public int floorKey;          // the floor it belongs to (CoopWorld.FloorKey)
    public int id;                // the entity (index or loot key)
    public int amount;
    public Vector3 position, direction;
    public FixedString128Bytes item;
    public byte target;           // a player aimed at (Coop.NoSlot: none)
    public Vector3 extra;         // a third point where one is needed (a spell's eye)

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref kind); s.SerializeValue(ref slot); s.SerializeValue(ref floorKey); s.SerializeValue(ref id);
        s.SerializeValue(ref amount); s.SerializeValue(ref position); s.SerializeValue(ref direction); s.SerializeValue(ref item); s.SerializeValue(ref target);
        s.SerializeValue(ref extra);
    }
}

// One enemy as the host sees it, a few times a second: where it is, what it is doing and what its animator plays.
public struct EnemySnapshot : INetworkSerializable
{
    public ushort id;
    public Vector3 position;
    public short yaw;                // degrees * 100
    public float modelHeight;        // a flyer's model above its agent
    public byte state;               // EnemyState
    public bool asleep;
    public byte sees;                // the player it has in sight (Coop.NoSlot: nobody): a guest's stealth look reads it
    public int[] states;             // per animator layer: the state's full path hash
    public byte[] times;             // per layer: normalised time in the state, 0-255 over one loop
    public float[] floats;           // the animator's float parameters, in parameter order
    public uint bools;               // its bool parameters, one bit each

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref id); s.SerializeValue(ref position); s.SerializeValue(ref yaw); s.SerializeValue(ref modelHeight);
        s.SerializeValue(ref state); s.SerializeValue(ref asleep); s.SerializeValue(ref sees); s.SerializeValue(ref bools);
        int layers = states != null ? states.Length : 0;
        s.SerializeValue(ref layers);
        if (s.IsReader) { states = new int[layers]; times = new byte[layers]; }
        for (int i = 0; i < layers; i++) { s.SerializeValue(ref states[i]); s.SerializeValue(ref times[i]); }
        int count = floats != null ? floats.Length : 0;
        s.SerializeValue(ref count);
        if (s.IsReader) floats = new float[count];
        for (int i = 0; i < count; i++) s.SerializeValue(ref floats[i]);
    }
}

// A creature that came into being mid-floor on the host (a crate's rat, an ambush wave), made on every guest too.
public struct EnemySpawn : INetworkSerializable
{
    public int floorKey;
    public ushort id;
    public int prefab;               // index into MultiplayerManager.enemyPrefabs
    public FixedString64Bytes prefabName; // and its name, which wins if the two lists differ (an editor's out of date)
    public Vector3 position;
    public float yaw;
    public int lootSeed;
    public float health, maxHealth;  // as the host made it (the intro's killer is much tougher than its prefab)
    public byte side;                // a summon on the players' side (Raise the Fallen): 1 + its summoner's slot; 0 an enemy
    public sbyte elite;              // the host's elite's EliteTrait, -1 a plain one
    public float eliteFloors;
    public bool minion;              // a boss's minion: killing it gives no experience (EnemyBrain.NoExperience)
    public FixedString64Bytes summonKind; // a summon's spell (Summon.Kind: Raise the Fallen, Summon John): its settings, name and arrival here
    public float scale;              // its size as the host made it (Summon John's is 0.8 of the prefab's)
    public sbyte face;               // the head it wears (KeeperFace: the turned tavern keeper keeps his), -1 none

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref floorKey); s.SerializeValue(ref id); s.SerializeValue(ref prefab); s.SerializeValue(ref prefabName);
        s.SerializeValue(ref position); s.SerializeValue(ref yaw); s.SerializeValue(ref lootSeed);
        s.SerializeValue(ref health); s.SerializeValue(ref maxHealth); s.SerializeValue(ref side);
        s.SerializeValue(ref elite); s.SerializeValue(ref eliteFloors); s.SerializeValue(ref minion);
        s.SerializeValue(ref summonKind); s.SerializeValue(ref scale); s.SerializeValue(ref face);
    }
}

// The run every player loads: the level, its seed and floor, and the loot level the host built it with.
public struct RunStart : INetworkSerializable
{
    public FixedString128Bytes level;
    public int seed;
    public int floor;
    public int generationLevel;
    public int partySize;
    public bool descend;
    // The host's save has cleared the Skeleton Party (SkeletonParty.ClearedFlag): every machine leaves it out of the
    // floor, so the floors match whatever each guest's own save says.
    public bool partyCleared;
    // Which run this is (the host picks one as it starts): a guest who left it and sits down again in the same run gets
    // their adventurer back as they left it (MultiplayerManager.KeepRun).
    public int runId;
    // The host's flags that decide what a floor deals, as "key=value;" pairs: its classes unlocked (class.*: a room dealt
    // only while a class is locked, DungeonRoomTemplate.onlyWhileLocked) and the events this run has had (run.event.*,
    // DungeonGenerator.SeenEarlier). Every machine reads them instead of its own save (MultiplayerManager.HostFlag).
    public FixedString4096Bytes hostFlags;

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref level); s.SerializeValue(ref seed); s.SerializeValue(ref floor);
        s.SerializeValue(ref generationLevel); s.SerializeValue(ref partySize); s.SerializeValue(ref descend);
        s.SerializeValue(ref partyCleared); s.SerializeValue(ref runId); s.SerializeValue(ref hostFlags);
    }
}
