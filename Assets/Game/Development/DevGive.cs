#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

// What the dev console (additem, addspell, addroomitem) and the spawn window (F10, DevSpawner) give: items into the table
// player's pack or onto the floor, spell tomes, room key items and enemies. Each returns the line to show.
public static class DevGive
{
    // The catalog's items (rolled copies are never in it); spells: only the tomes.
    public static IEnumerable<ItemData> CatalogItems(bool spells) => InventoryManager.HasInstance && InventoryManager.Instance.itemCatalog != null
        ? InventoryManager.Instance.itemCatalog.Where(i => i != null && !string.IsNullOrWhiteSpace(i.itemName) && (!spells || i.spell != null))
        : Enumerable.Empty<ItemData>();

    // A tome's name: its spell's name, else the tome's.
    public static string SpellName(ItemData tome) => tome.spell != null && !string.IsNullOrWhiteSpace(tome.spell.displayName) ? tome.spell.displayName : tome.itemName;

    // The table player once it has played (its bag and hotbar are found when it first wakes; a run empties them as it
    // starts, so items given before would be lost anyway). Null with why in problem.
    public static Player TablePlayer(out string problem)
    {
        problem = null;
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        if (player == null || !InventoryManager.HasInstance) { problem = "No table player."; return null; }
        if (player.Bag == null || player.Hotbar == null) { problem = "Start a run first: the table player's pack is emptied as a run starts."; return null; }
        return player;
    }

    // The item rolled to a rarity for the floor being played (gear only; the same item when it has that rarity).
    public static ItemData AtRarity(ItemData item, ItemRarity rarity)
    {
        if (item == null || rarity == item.rarity || !item.IsGear) return item;
        var loader = Object.FindAnyObjectByType<TableLevelLoader>();
        int floor = loader != null ? loader.FloorNumber : 1;
        var table = ItemPropertyTable.Current;
        return item.Rolled(rarity, table != null ? table.Roll(item, rarity, floor, new System.Random()) : null);
    }

    public static string Item(ItemData item, int count, ItemRarity? rarity)
    {
        var player = TablePlayer(out var problem);
        if (player == null) return problem;
        if (rarity.HasValue && rarity.Value != item.rarity && !item.IsGear) return $"{item.itemName} is not gear: it has no rarity to roll.";
        if (rarity.HasValue) item = AtRarity(item, rarity.Value);
        int given = 0;
        for (int i = 0; i < count; i++)
        {
            if (!InventoryManager.Instance.Pickup(item, player, 1, false, i == 0, i == 0)) break;
            given++;
        }
        return given == 0 ? "No room in the pack." : given < count ? $"{given} of {count} {item.DisplayName} given: the pack is full." : $"{(count > 1 ? count + " x " : "")}{item.DisplayName} given.";
    }

    // On the floor a metre and a half in front of the table player, as loot lies.
    public static string Drop(ItemData item, int count, ItemRarity? rarity)
    {
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        if (player == null || !player.isActiveAndEnabled) return "Only while playing the table player.";
        if (rarity.HasValue) item = AtRarity(item, rarity.Value);
        var loader = Object.FindAnyObjectByType<TableLevelLoader>();
        var parent = loader != null && loader.Dungeon != null ? loader.Dungeon.transform : null;
        var spot = InFront(player, 1.5f);
        for (int i = 0; i < count; i++) DungeonPickup.Spawn(item, spot + Random.insideUnitSphere.WithY(0) * (count > 1 ? .4f : 0f), parent);
        return $"{(count > 1 ? count + " x " : "")}{item.DisplayName} dropped in front of you.";
    }

    public static string Spell(ItemData tome)
    {
        var player = TablePlayer(out var problem);
        if (player == null) return problem;
        if (player.Hotbar.TotalCount(tome) > 0 || player.Bag.TotalCount(tome) > 0) return $"You already carry {tome.DisplayName}.";
        return InventoryManager.Instance.Pickup(tome, player, 1, true, true, true)
            ? $"{tome.DisplayName} given. Put it in your left hand from the hotbar." : "No room in the pack.";
    }

    public static string RoomItem(ProgressionManager.RoomKeyItem key)
    {
        key.carry?.Invoke();
        return $"{key.name} carried home. Use it in the room.";
    }

    // Count enemies in front of the table player on the floor being played, scaled to the floor like its own (with its
    // loot), facing the player; not alerted, so a backstab works.
    public static string Enemy(GameObject prefab, int count)
    {
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        if (player == null || !player.isActiveAndEnabled) return "Only while playing the table player.";
        var loader = Object.FindAnyObjectByType<TableLevelLoader>();
        var dungeon = loader != null ? loader.Dungeon : null;
        int made = 0;
        for (int i = 0; i < count; i++)
        {
            // A row across the view, three metres out.
            Vector3 forward = Forward(player), side = Vector3.Cross(Vector3.up, forward);
            var spot = player.transform.position + forward * 3f + side * ((i - (count - 1) * .5f) * 1.3f);
            var facing = Quaternion.LookRotation(-forward);
            Character enemy = null;
            if (dungeon != null) enemy = dungeon.SpawnAmbusher(prefab, spot, facing);
            else if (NavMesh.SamplePosition(spot, out var hit, 2f, NavMesh.AllAreas)) enemy = Object.Instantiate(prefab, hit.position, facing).GetComponent<Character>();
            if (enemy != null) made++;
        }
        string name = prefab.TryGetComponent<Character>(out var c) ? c.DisplayName : prefab.name;
        return made == 0 ? "No floor in front of you to put it on." : $"{(made > 1 ? made + " x " : "")}{name} in front of you.";
    }

    static Vector3 Forward(Player player)
    {
        Vector3 f = player.Look != null ? player.Look.YawTransform.forward : player.transform.forward;
        f.y = 0f;
        return f.sqrMagnitude > .001f ? f.normalized : Vector3.forward;
    }

    static Vector3 InFront(Player player, float distance)
    {
        var spot = player.transform.position + Forward(player) * distance;
        return NavMesh.SamplePosition(spot, out var hit, 2f, NavMesh.AllAreas) ? hit.position + Vector3.up * .05f : spot;
    }
}
#endif
