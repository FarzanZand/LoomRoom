// The one place a class's own action is used from (the Pianist's Mood, 2026-11-25): the Class Action key (InputManager,
// X by default) and a click on the class slot right of the hotbar both call TryUse. It finds the table player's class
// and its action, asks CanUse and says why in the feed when it can't, else uses it.
public static class ClassActionRunner
{
    // The table player's class's action, or null (no class, or a class without one: the slot is hidden).
    public static ClassAction ActionOf(Player player)
    {
        var progress = player != null ? player.GetComponent<AdventurerProgress>() : null;
        return progress != null && progress.selectedClass != null ? progress.selectedClass.action : null;
    }

    public static ClassAction Current =>
        ActionOf(PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null);

    // Uses the action of the table player's class. False when there is none or it can't be used now (then the feed says why).
    public static bool TryUse(Player player = null)
    {
        if (player == null && PlayerManager.HasInstance) player = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
        var action = ActionOf(player);
        if (action == null) return false;
        if (!action.CanUse(player, out var reason))
        {
            if (!string.IsNullOrWhiteSpace(reason)) MessageLog.Post(reason, MessageKind.Warning);
            return false;
        }
        action.Use(player);
        return true;
    }
}
