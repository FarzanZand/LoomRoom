using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// What a character is wearing and holding. Lives on the character, so each player
// keeps their own hands. Spawns the world prefab under the slot's anchor, applies the
// item's stat modifiers, fires OnEquip/OnUnequip effects and feeds the weapon Hitbox.
[RequireComponent(typeof(Character))]
public class Equipment : MonoBehaviour
{
    [Serializable]
    public class Anchor
    {
        public EquipmentSlot slot;
        public Transform     transform;
    }

    [Tooltip("Where each slot's item prefab is parented. Slots without an anchor still equip (stats only).")]
    [ListDrawerSettings(ShowFoldout = true)]
    [SerializeField] List<Anchor> anchors = new();

    [Tooltip("The hitbox that receives the right-hand weapon's hit profile. Leave empty to search children.")]
    [SerializeField] Hitbox weaponHitbox;

    public event Action Changed;

    public Character Character { get; private set; }

    static readonly EquipmentSlot[] AllSlots = (EquipmentSlot[])Enum.GetValues(typeof(EquipmentSlot));
    static readonly EquipmentSlot[] HandSlots = { EquipmentSlot.RightHand, EquipmentSlot.LeftHand };

    readonly Dictionary<EquipmentSlot, ItemData>   items   = new();
    readonly Dictionary<EquipmentSlot, GameObject> objects = new();
    readonly Dictionary<EquipmentSlot, object>     sources = new();
    readonly HeldItemEating eating = new();

    public bool IsUsingItem => eating.IsPlaying;

    public bool PlayEating(ItemData item, EquipmentSlot slot, Action complete)
    {
        if (IsUsingItem || !isActiveAndEnabled || !item.canBeEquipped || Get(slot) != item ||
            Character is not Player player || !objects.TryGetValue(slot, out var held)) return false;
        return eating.Play(player, item, GetAnchor(slot), held, complete);
    }

    // Restore the eating pose before the Animator evaluates the next frame.
    void Update() => eating.RestorePose();

    void LateUpdate()
    {
        if (Character is Player owner)
        {
            // Resolve after inventory transfers finish so swaps cannot leave ghost equipment.
            // Only hands: worn items are held by Equipment itself, not the bag.
            foreach (var slot in HandSlots)
            {
                var item = Get(slot);
                if (item != null && owner.Hotbar.IndexOf(item) < 0) Unequip(slot);
            }
        }
        eating.Tick();
    }

    void OnDisable() => eating.Clear();
    public IEnumerable<ItemData> EquippedItems => items.Values;

    bool initialized;
    void Awake() => EnsureInitialized();

    // A level can equip the inactive table player before Unity calls this component's Awake.
    void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;
        Character = GetComponent<Character>();
        if (weaponHitbox == null) weaponHitbox = GetComponentInChildren<Hitbox>(true);
        EffectDispatcher.Ensure(gameObject);
        foreach (var s in AllSlots)
            sources[s] = new object();
    }

    public Transform GetAnchor(EquipmentSlot slot)
    {
        foreach (var a in anchors)
            if (a.slot == slot) return a.transform;
        return null;
    }

    public ItemData Get(EquipmentSlot slot) => items.TryGetValue(slot, out var i) ? i : null;
    public bool     Has(EquipmentSlot slot) => items.ContainsKey(slot);
    public bool     IsEquipped(ItemData item) => item != null && (Get(item.equipSlot) == item || IsTrinket(item.equipSlot) && (Get(EquipmentSlot.Trinket1) == item || Get(EquipmentSlot.Trinket2) == item));
    public static bool IsTrinket(EquipmentSlot slot) => slot == EquipmentSlot.Trinket1 || slot == EquipmentSlot.Trinket2;
    // The slot Equip puts an item in. Trinkets fit either trinket slot: the first free one, else the first.
    public EquipmentSlot SlotFor(ItemData item)
    {
        if (!IsTrinket(item.equipSlot)) return item.equipSlot;
        return !Has(EquipmentSlot.Trinket1) ? EquipmentSlot.Trinket1 : !Has(EquipmentSlot.Trinket2) ? EquipmentSlot.Trinket2 : EquipmentSlot.Trinket1;
    }
    public bool Fits(ItemData item, EquipmentSlot slot) => item != null && item.canBeEquipped && (item.equipSlot == slot || IsTrinket(item.equipSlot) && IsTrinket(slot));

    public bool IsShieldSource(object source)
    {
        foreach (var pair in sources)
            if (Get(pair.Key) is ItemData item && item.itemType == ItemType.Shield &&
                (ReferenceEquals(pair.Value, source) || source is ItemBuffSource buff && buff.Item == item)) return true;
        return false;
    }

    public bool CanEquip(ItemData item) => item != null && item.canBeEquipped;
    // Hand items stay in the hotbar (it is how you switch them); worn items leave the bag while equipped.
    public static bool IsHand(EquipmentSlot slot) => slot == EquipmentSlot.RightHand || slot == EquipmentSlot.LeftHand;

    // grant: equip an item the player doesn't carry (restoring a save), without taking it from the bag.
    // into: a specific slot (a trinket dropped on the second trinket slot).
    public bool Equip(ItemData item, bool playSound = true, bool grant = false, EquipmentSlot? into = null)
    {
        EnsureInitialized();
        if (!CanEquip(item)) return false;
        if (IsHand(item.equipSlot) && IsEquipped(item)) return true;

        var slot = into.HasValue && Fits(item, into.Value) ? into.Value : SlotFor(item);
        if(Character is Player owner && !(grant && !IsHand(slot)))
        {
            if(owner.Bag.IndexOf(item)<0 && owner.Hotbar.IndexOf(item)<0)return false;
            if(!IsHand(slot) && !owner.Bag.RemoveOne(item) && !owner.Hotbar.RemoveOne(item))return false;
        }
        if(Character is Player p && (slot==EquipmentSlot.RightHand || slot==EquipmentSlot.LeftHand) && p.Hotbar.IndexOf(item)<0) {
            int from=p.Bag.IndexOf(item);int destination=Get(slot)!=null?p.Hotbar.IndexOf(Get(slot)):-1;
            if(destination<0)destination=p.Hotbar.FirstEmpty();
            if(from<0 || destination<0 || !Inventory.Move(p.Bag,from,p.Hotbar,destination)){NotificationUI.Show("Make room in the hotbar to equip a hand item");return false;}
        }
        Unequip(slot);   // a worn item goes back into the bag

        var anchor = GetAnchor(slot);
        if (anchor != null && item.worldPrefab != null)
        {
            var obj = Instantiate(item.worldPrefab, anchor);
            obj.transform.SetLocalPositionAndRotation(item.heldPosition, Quaternion.Euler(item.heldRotation));
            obj.transform.localScale *= Mathf.Max(0.01f, item.heldScale);
            foreach (var col in obj.GetComponentsInChildren<Collider>()) col.enabled = false;
            foreach (var rb in obj.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
            objects[slot] = obj;
        }

        items[slot] = item;

        if (Character.Stats != null && item.statModifiers != null)
            foreach (var m in item.statModifiers)
                Character.Stats.AddModifier(new StatModifier(m, sources[slot]));

        if (slot == EquipmentSlot.RightHand && weaponHitbox != null)
        {
            if (item.IsWeapon) weaponHitbox.SetProfile(item.weapon);
            else               weaponHitbox.ClearProfile();
        }

        var equipContext = EffectContext.For(Character, item);
        equipContext.ModifierSource = sources[slot];   // removed with the slot's stat modifiers on unequip
        ItemEffectProcessor.Fire(item, EffectTrigger.OnEquip, equipContext);

        if(playSound && Character is Player){var style=UIFeedbackSettings.Shared;style?.Play(style.equipKey);}
        Changed?.Invoke();
        return true;
    }

    public ItemData Unequip(EquipmentSlot slot) => Unequip(slot, true);

    // A player taking something off by hand (the equipment panel): a worn item needs room in the bag or
    // hotbar, otherwise it stays on and the player is told. Unequip(slot) instead drops it at the player's
    // feet when the pack is full, which Equip relies on when swapping and consuming held items.
    public bool TryUnequip(EquipmentSlot slot)
    {
        var item = Get(slot);
        if (item == null) return false;
        if (!IsHand(slot) && Character is Player owner && !(owner.Bag != null && owner.Bag.HasRoomFor(item)) && !(owner.Hotbar != null && owner.Hotbar.HasRoomFor(item)))
        {
            NotificationUI.Show("Make room in your inventory first");
            return false;
        }
        return Unequip(slot) != null;
    }

    // toBag: a worn item returns to the bag, or drops at the player's feet when it is full.
    ItemData Unequip(EquipmentSlot slot, bool toBag)
    {
        EnsureInitialized();
        if (!items.TryGetValue(slot, out var item)) return null;
        if (toBag && !IsHand(slot) && Character is Player owner && !owner.Bag.TryAdd(item) && !owner.Hotbar.TryAdd(item) && InventoryManager.HasInstance)
            InventoryManager.Instance.DropFromPlayer(item, owner);
        if (item == eating.Item) eating.Clear();

        if (objects.TryGetValue(slot, out var obj) && obj != null) Destroy(obj);
        objects.Remove(slot);
        items.Remove(slot);

        if (Character.Stats != null)
            Character.Stats.RemoveAllFromSource(sources[slot]);

        if (slot == EquipmentSlot.RightHand && weaponHitbox != null)
            weaponHitbox.ClearProfile();

        ItemEffectProcessor.Fire(item, EffectTrigger.OnUnequip, EffectContext.For(Character, item));

        Changed?.Invoke();
        return item;
    }

    public bool Unequip(ItemData item)
    {
        foreach (var pair in items)
            if (pair.Value == item) { Unequip(pair.Key); return true; }
        return false;
    }

    // Clears every slot without returning worn items: callers reset the bag right after.
    public void UnequipAll()
    {
        foreach (var slot in new List<EquipmentSlot>(items.Keys))
            Unequip(slot, false);
    }
}
