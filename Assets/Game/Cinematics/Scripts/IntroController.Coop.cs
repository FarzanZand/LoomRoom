using UnityEngine;

// The intro's co-op side, kept apart from the practice so master's changes there don't meet it: the host runs the
// practice's finale and its killer for everyone (guests get RemoteFinale), anyone of the party in a room counts for it,
// and a fallen teammate cuts the killer's wait short.
public partial class IntroController
{
    // This machine runs the finale and spawns the killer: alone, or as the host.
    bool RunsFinale => !Coop.IsGuest;

    // The finale began here: the host tells the guests.
    void FinaleStarted()
    {
        if (Coop.IsHost && MultiplayerManager.HasInstance) MultiplayerManager.Instance.SendFinale(fromStairs);
    }

    // Co-op: anyone of the party in that room.
    bool PartyInRoom(int roomIndex)
    {
        var dungeon = loader != null ? loader.Dungeon : null;
        if (!Coop.Running || dungeon == null || dungeon.Layout == null) return false;
        foreach (var c in CoopWorld.Party()) if (dungeon.Layout.RoomAt(dungeon.CellOf(c.transform.position)) == roomIndex) return true;
        return false;
    }

    // Co-op guests: the host's practice reached its end.
    public void RemoteFinale(bool stairs)
    {
        if (!running || !PracticeRunning || finale) return;
        fromStairs = stairs;
        StopAllCoroutines();
        StartCoroutine(Finale());
    }

    const float TeammateFallenEnds = 3f;
    static bool TeammateFell()
    {
        if (!Coop.Running || !MultiplayerManager.HasInstance) return false;
        foreach (var p in MultiplayerManager.Instance.Players) if (p != null && !p.IsOwner && p.Current.inRun && !p.Current.alive) return true;
        return false;
    }
}
