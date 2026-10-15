using UnityEngine;

// Co-op pings: on the table, the Ping key (middle mouse, F, the pad's d-pad up) marks what the player looks at for
// everyone: an enemy, an item, gold, a chest, a door, or just the spot. Each player's pings are in their own colour
// (a short beam and a label drawn over walls, CoopPingMarker), a line goes in the message feed ("Sam: Crypt Rat") and a
// soft sound plays. Sent as a CoopEvent (Ping), relayed by the host.
public static class CoopPing
{
    public const float Reach = 40f, Seconds = 4.5f;
    static readonly Color[] Colours = { new(1f, .8f, .3f), new(.35f, .85f, 1f), new(.5f, 1f, .45f), new(1f, .5f, .85f) };
    public static readonly string[] SoundKeys = { "ping", "notification", "uiClick" };
    static float next;

    public static Color ColourOf(int slot) => Colours[Mathf.Abs(slot) % Colours.Length];

    // The local player's Ping key (InputManager.PingPressed).
    public static void PingFromLocal()
    {
        if (!Coop.Running || !PlayerManager.HasInstance || PlayerManager.Instance.ActiveKind != PlayerKind.Table) return;
        if (GameManager.HasInstance && GameManager.Instance.State != GameState.Explore) return;
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + .4f;
        var player = PlayerManager.Instance.Active;
        var cam = PlayerManager.Instance.OutputCamera;
        if (player == null || cam == null) return;
        if (!Aim(new Ray(cam.transform.position, cam.transform.forward), player.transform.root, out var at, out var label)) return;
        Show(Coop.LocalSlot, at, label);
        CoopWorld.Send(new CoopEvent { kind = CoopEventKind.Ping, position = at, item = new Unity.Collections.FixedString128Bytes(label) });
    }

    // What the ray meets first: something worth naming, or the first solid surface.
    static bool Aim(Ray ray, Transform self, out Vector3 at, out string label)
    {
        at = default; label = null;
        var hits = Physics.RaycastAll(ray, Reach, ~0, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            var c = hit.collider;
            if (c == null || c.transform.IsChildOf(self)) continue;
            string named = Name(c);
            if (named == null && c.isTrigger) continue;
            at = hit.point;
            label = named ?? "Here";
            return true;
        }
        return false;
    }

    static string Name(Collider c)
    {
        var brain = c.GetComponentInParent<EnemyBrain>();
        if (brain != null && brain.Character != null && brain.Character.IsAlive) return brain.Character.DisplayName;
        var item = c.GetComponentInParent<WorldItem>();
        if (item != null && item.Item != null) return item.Item.itemName;
        if (c.GetComponentInParent<CoinPickup>() != null) return "Gold";
        var chest = c.GetComponentInParent<DungeonContainer>();
        if (chest != null && chest.chest) return string.IsNullOrWhiteSpace(chest.chestName) ? "Chest" : char.ToUpper(chest.chestName[0]) + chest.chestName.Substring(1);
        if (c.GetComponentInParent<DungeonDoor>() != null) return "Door";
        var mate = c.GetComponentInParent<CoopPlayer>();
        if (mate != null) return mate.DisplayName;
        return null;
    }

    // Everyone: the marker, the feed line and the sound.
    public static void Show(int slot, Vector3 at, string label)
    {
        var who = MultiplayerManager.HasInstance ? MultiplayerManager.Instance.PlayerInSlot(slot) : null;
        string name = who != null ? who.DisplayName : $"Player {slot + 1}";
        CoopPingMarker.Spawn(at, label, ColourOf(slot), Seconds);
        MessageLog.Post($"{name}: {label}", MessageKind.Info);
        if (AudioManager.HasInstance)
            foreach (var key in SoundKeys) if (AudioManager.Instance.HasUI(key)) { AudioManager.Instance.PlayUI(key); break; }
    }
}
