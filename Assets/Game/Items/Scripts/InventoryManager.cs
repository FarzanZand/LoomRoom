using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// Item-system configuration and the shared operations that need it: picking up,
// dropping, using. The containers themselves live on each player (Inventory,
// Equipment); this only decides the rules.
public class InventoryManager : Singleton<InventoryManager>
{
    public event System.Action<ItemData, Player, int> ItemPickedUp;
    [Header("Pickup")]
    [Tooltip("Honour the item's Equip On Pickup flag.")]
    public bool allowEquipOnPickup = true;
    [Tooltip("Use the shared AudioManager SFX library entry for every pickup, including items with their own audio.")]
    public bool useSharedPickupSound = true;
    public string pickupSoundKey = "genericPickupSound";
    [Tooltip("Played when an item is picked up and the item has no pickup audio of its own.")]
    public AudioData defaultPickupAudio;
    [Tooltip("Pickup visual used when an item has neither a pickup visual nor a world mesh assigned.")]
    public GameObject defaultPickupVisual;

    [Header("Drop")]
    [Tooltip("Prefab with a WorldItem used when items are dropped or spawned from data.")]
    [Required] public GameObject pickupPrefab;
    public float dropDistance = 1.5f;
    public float dropHeight   = 0.5f;

    [Header("Catalog")]
    [Tooltip("Every item the runtime may need to look up by name (dialogue GiveItem, saves).")]
    [ListDrawerSettings(ShowFoldout = true)]
    public List<ItemData> itemCatalog = new();

    public Player    ActivePlayer    => PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
    public Inventory ActiveBag       => ActivePlayer != null ? ActivePlayer.Bag : null;
    public Inventory ActiveHotbar    => ActivePlayer != null ? ActivePlayer.Hotbar : null;
    public Equipment ActiveEquipment => ActivePlayer != null ? ActivePlayer.Equipment : null;

    // ── Pickup ────────────────────────────────────────────────────────

    public bool Pickup(ItemData item, Player player, int count = 1, bool preferHotbar = false, bool playSound = true)
    {
        if (item == null || player == null) return false;

        bool added = false;
        if ((item.directToHotbar || preferHotbar) && player.Hotbar != null && player.Hotbar.Accepts(item))
            added = player.Hotbar.TryAdd(item, count);
        if (!added && player.Bag != null)
            added = player.Bag.TryAdd(item, count);

        if (!added)
        {
            NotificationUI.Show("Inventory full");
            return false;
        }

        if (playSound && AudioManager.HasInstance)
        {
            if (useSharedPickupSound) AudioManager.Instance.PlaySFX2D(pickupSoundKey);
            else
            {
                var audio = item.pickupAudio != null ? item.pickupAudio : defaultPickupAudio;
                if (audio != null) AudioManager.Instance.PlaySFXData2D(audio);
            }
        }

        ItemEffectProcessor.Fire(item, EffectTrigger.OnPickup, EffectContext.For(player, item));

        if (allowEquipOnPickup && item.equipOnPickup && player.Equipment != null &&
            player.Equipment.CanEquip(item) && !player.Equipment.Has(item.equipSlot))
            player.Equipment.Equip(item, playSound);

        ItemPickedUp?.Invoke(item, player, count);
        return true;
    }

    // ── Use ───────────────────────────────────────────────────────────

    // Use a consumable sitting in a container slot.
    public void Use(Inventory container, int index)
    {
        var item = container?.ItemAt(index);
        if (item == null || !item.IsConsumable) return;
        var player = container.GetComponentInParent<Player>();
        if (item.canBeEquipped)
        {
            player?.Equipment?.Equip(item);
            return;
        }
        item.Use(player);
        container.Consume(index);
    }

    // Use the consumable the player is currently holding in a hand.
    public bool UseHeld(Player player, EquipmentSlot slot = EquipmentSlot.RightHand)
    {
        var item = player?.Equipment?.Get(slot);
        if (item == null || !item.IsConsumable) return false;

        if (player.Equipment.IsUsingItem) return true;
        if (item.canBeEquipped && item.AnimationOnUse == ItemUseAnimation.Eat &&
            player.Equipment.PlayEating(item, slot, () => CompleteHeldUse(player, slot, item))) return true;

        CompleteHeldUse(player, slot, item);
        return true;
    }

    void CompleteHeldUse(Player player, EquipmentSlot slot, ItemData item)
    {
        if (player == null || player.Equipment.Get(slot) != item) return;
        // Recheck ownership when an animation completes: the stack may have been moved or dropped.
        bool consumed=player.Hotbar!=null && player.Hotbar.RemoveOne(item);
        if(!consumed)consumed=player.Bag!=null && player.Bag.RemoveOne(item);
        player.Equipment.Unequip(slot);
        if(consumed)item.Use(player);
    }

    // ── Spawn / drop ──────────────────────────────────────────────────

    public WorldItem Spawn(ItemData item, Vector3 position, Quaternion rotation)
    {
        if (item == null || pickupPrefab == null) return null;
        var go = Instantiate(pickupPrefab, position, rotation);
        var wi = go.GetComponent<WorldItem>();
        if (wi == null) { Debug.LogError("[InventoryManager] Pickup prefab has no WorldItem.", pickupPrefab); Destroy(go); return null; }
        wi.Init(item);
        return wi;
    }

    public WorldItem Spawn(ItemData item, Vector3 position) => Spawn(item, position, Quaternion.identity);

    public WorldItem DropFromPlayer(ItemData item, Player player)
    {
        if (player == null) player = ActivePlayer;
        if (player == null) return null;
        Transform t = player.transform;
        Vector3 forward = player.Look != null ? player.Look.YawTransform.forward : t.forward;
        var pos = t.position + forward * dropDistance + Vector3.up * dropHeight;
        var pickup = Spawn(item, pos);
        var level = TableManager.HasInstance ? TableManager.Instance.GetComponent<TableLevelLoader>() : null;
        if (pickup != null && player.kind == PlayerKind.Table && level != null && level.Dungeon != null)
            pickup.transform.SetParent(level.Dungeon.transform, true);
        return pickup;
    }

    // ── Lookup ────────────────────────────────────────────────────────

    public ItemData FindItem(string itemName)
    {
        if (string.IsNullOrEmpty(itemName)) return null;
        foreach (var i in itemCatalog)
            if (i != null && (i.itemName == itemName || i.name == itemName)) return i;
        return null;
    }

#if UNITY_EDITOR
    [Button("Populate Catalog From Project")]
    void PopulateCatalog()
    {
        itemCatalog.Clear();
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:ItemData"))
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/_Archive/")) continue;
            var item = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (item != null) itemCatalog.Add(item);
        }
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
