using Unity.Collections;
using Unity.Netcode;

// Where a player is: walking the room (or watching from it after dying), or down on the table.
public enum CoopPlace : byte { Room = 0, Table = 1 }

// One player's state as everyone sees it, written by that player only.
public struct CoopPlayerState : INetworkSerializable, System.IEquatable<CoopPlayerState>
{
    public CoopPlace place;
    public bool alive;          // the table player is alive (false: dead, watching from the room)
    public bool atTable;        // sitting at the table, picking a figure
    public bool ready;          // picked a figure and waits for the run
    public bool inRun;          // part of the run on the table now
    public bool blocking, crouching;
    public byte health;         // 0-255 of the table player's maximum
    public FixedString32Bytes classId;
    public FixedString64Bytes right, left;   // held items (save ids)

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref place); s.SerializeValue(ref alive); s.SerializeValue(ref atTable); s.SerializeValue(ref ready);
        s.SerializeValue(ref inRun); s.SerializeValue(ref blocking); s.SerializeValue(ref crouching); s.SerializeValue(ref health);
        s.SerializeValue(ref classId); s.SerializeValue(ref right); s.SerializeValue(ref left);
    }

    public bool Equals(CoopPlayerState o) =>
        place == o.place && alive == o.alive && atTable == o.atTable && ready == o.ready && inRun == o.inRun &&
        blocking == o.blocking && crouching == o.crouching && health == o.health &&
        classId.Equals(o.classId) && right.Equals(o.right) && left.Equals(o.left);
}
