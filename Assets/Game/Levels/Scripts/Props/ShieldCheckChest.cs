using UnityEngine;

// A chest that looks at the table player as it opens (the one on the Crypt Lord's stage, before the fight): with no shield
// carried it holds a shield and a potion, with one a potion only, and the Dungeon Master tells them to take the shield into
// the fight. On the chest, next to its DungeonContainer; it replaces the chest's own loot.
[RequireComponent(typeof(DungeonContainer))]
public class ShieldCheckChest : MonoBehaviour
{
    [Tooltip("Given when the player carries no shield (in hand, on the hotbar or in the bag).")]
    public ItemData shield;
    [Tooltip("Always given.")]
    public ItemData potion;
    [Tooltip("Said when the chest gave a shield.")]
    public DungeonMaster.Line gaveShieldLine = new() { text = "A shield and a potion. Put the shield on before you go up. You'll need it." };
    [Tooltip("Said when a shield is carried but not in hand.")]
    public DungeonMaster.Line equipShieldLine = new() { text = "A potion. Put your shield on before you go up. You'll need it." };
    [Tooltip("Said when a shield is already in hand.")]
    public DungeonMaster.Line keepShieldLine = new() { text = "A potion. Keep that shield up for this one." };

    DungeonContainer chest;

    void Awake()
    {
        chest = GetComponent<DungeonContainer>();
        chest.Opened += Fill;
    }

    void OnDestroy() { if (chest != null) chest.Opened -= Fill; }

    // Opened fires before the chest rolls its drops, so these are what comes out.
    void Fill(DungeonContainer opened)
    {
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        bool inHand = player != null && player.Equipment != null && player.Equipment.Get(EquipmentSlot.LeftHand)?.itemType == ItemType.Shield;
        bool carried = inHand || Carries(player != null ? player.Hotbar : null) || Carries(player != null ? player.Bag : null);
        chest.onlyGuaranteed = true;
        chest.guaranteedItems = new System.Collections.Generic.List<ItemData>();
        if (!carried && shield != null) chest.guaranteedItems.Add(shield);
        if (potion != null) chest.guaranteedItems.Add(potion);
        var line = !carried ? gaveShieldLine : inHand ? keepShieldLine : equipShieldLine;
        if (line != null && !line.IsEmpty) DungeonMaster.Say(line);
    }

    static bool Carries(Inventory inventory)
    {
        if (inventory == null) return false;
        for (int i = 0; i < inventory.SlotCount; i++) if (inventory.ItemAt(i)?.itemType == ItemType.Shield) return true;
        return false;
    }
}
