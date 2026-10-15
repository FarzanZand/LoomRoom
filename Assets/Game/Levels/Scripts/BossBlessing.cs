using UnityEngine;

// The boss arena's blessing: from the moment the fight starts, a block (shield up, facing the blow) stops all of a hit's
// damage, spells and the Dungeon Master's beam included (CharacterStats.BlockBlessing). It goes when the boss falls,
// when the player dies (CharacterStats.TakeDamage), when the fight's floor goes and when a run starts.
public static class BossBlessing
{
    // announce off: the caller says it later (Announce), e.g. once the boss stage has gone dark.
    public static void Grant(bool announce = true)
    {
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
        if (player == null || player.Stats == null || !player.IsAlive || player.Stats.BlockBlessing) return;
        player.Stats.BlockBlessing = true;
        if (announce) Announce();
    }

    public static void Announce()
    {
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
        if (player == null || player.Stats == null || !player.Stats.BlockBlessing) return;
        MessageLog.Post("You are blessed: your blocks stop all damage in this fight.", MessageKind.Good);
        AnnouncementUI.Show("Blessed", "Your blocks stop all damage in this fight", 3f);
    }

    public static void Clear()
    {
        foreach (var player in Object.FindObjectsByType<Player>())
            if (player.Stats != null) player.Stats.BlockBlessing = false;
    }
}
