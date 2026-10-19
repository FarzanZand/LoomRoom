using Unity.Collections;
using Unity.Netcode;

// Where a player is: walking the room, or down on the table (alive, or a ghost after dying).
public enum CoopPlace : byte { Room = 0, Table = 1 }

// One player's state as everyone sees it, written by that player only.
public struct CoopPlayerState : INetworkSerializable, System.IEquatable<CoopPlayerState>
{
    public CoopPlace place;
    public bool alive;          // the table player is alive (false: dead, a ghost on the floor)
    public bool atTable;        // sitting at the table, picking a figure
    public bool ready;          // picked a figure and waits for the run
    public bool inRun;          // part of the run on the table now
    public bool blocking, crouching;
    public bool seated;         // in a chair at the table (the intro)
    public bool charging;       // winding up or holding a swing (the release comes as SwingRpc)
    public bool airborne;       // off the ground (a jump, a fall)
    public bool inLobby;        // joined, waiting on the co-op screen for the host's Begin (not in the room yet)
    public bool ghost;          // dead on the table, a ghost until the next floor
    public bool mustTrain;      // the sheet lesson keeps this player from the table until Blocking is trained
    public bool deflects;       // the raised shield sends arrows and spells back (Blocking's milestone, PlayerCombat.Deflects)
    public byte health;         // 0-255 of the table player's maximum
    public ushort spoils;       // the worth of the spoils carried on the table (pooled at the run's end with shared loot)
    public FixedString32Bytes classId;
    public ushort kills;        // kills in the current run (the party line on the recap)
    public FixedString128Bytes right, left;  // held items (save ids; a rolled item's id carries its rarity and properties)

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref place); s.SerializeValue(ref alive); s.SerializeValue(ref atTable); s.SerializeValue(ref ready);
        s.SerializeValue(ref inRun); s.SerializeValue(ref blocking); s.SerializeValue(ref crouching); s.SerializeValue(ref seated); s.SerializeValue(ref charging); s.SerializeValue(ref airborne); s.SerializeValue(ref inLobby); s.SerializeValue(ref ghost); s.SerializeValue(ref mustTrain); s.SerializeValue(ref deflects); s.SerializeValue(ref health); s.SerializeValue(ref spoils); s.SerializeValue(ref kills);
        s.SerializeValue(ref classId); s.SerializeValue(ref right); s.SerializeValue(ref left);
    }

    public bool Equals(CoopPlayerState o) =>
        place == o.place && alive == o.alive && atTable == o.atTable && ready == o.ready && inRun == o.inRun &&
        blocking == o.blocking && crouching == o.crouching && seated == o.seated && charging == o.charging && airborne == o.airborne && inLobby == o.inLobby && ghost == o.ghost && mustTrain == o.mustTrain && deflects == o.deflects && health == o.health && spoils == o.spoils && kills == o.kills &&
        classId.Equals(o.classId) && right.Equals(o.right) && left.Equals(o.left);
}
