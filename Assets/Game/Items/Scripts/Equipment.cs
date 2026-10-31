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
    readonly ConsumableUse consuming = new();
    // The hotbar slot each held item was last seen in.
    readonly Dictionary<EquipmentSlot, int> heldAt = new();

    // Eating or drinking: attacks, spells and other items wait until it is done.
    public bool IsUsingItem => consuming.Busy;
    // The item being eaten or drunk while its clip plays, else null (what a teammate's body shows).
    public ItemData UsingItem => consuming.Playing ? consuming.Item : null;
    public float UsingTime => consuming.Length;
    // The item being eaten or drunk already made its sound (as the hand reached the face).
    public bool UseSounded => consuming.Busy && consuming.Sounded;

    // Uses a consumable the way its ItemData asks: Instant at once, Eat or Drink in the right hand with the arm's clip
    // (ConsumableUse); use runs when it takes effect (it takes the item from the inventory and applies it). False when
    // another item is already being used.
    public bool Consume(ItemData item, Action use)
    {
        if (item == null) return false;
        if (IsUsingItem) return false;
        if (item.AnimationOnUse == ItemUseAnimation.Instant || Character is not Player player || !isActiveAndEnabled) { use?.Invoke(); return true; }
        return consuming.Begin(this, player, item, use);
    }

    void Update() => consuming.Tick();

    void LateUpdate()
    {
        if (Character is Player owner)
        {
            // Resolve after inventory transfers finish so swaps cannot leave ghost equipment.
            // Only hands: worn items are held by Equipment itself, not the bag. A held item swapped out of
            // the hotbar for another one of the same hand (another weapon, another shield) hands over to it.
            foreach (var slot in HandSlots)
            {
                var item = Get(slot);
                if (item == null) continue;
                bool known = heldAt.TryGetValue(slot, out int was);
                if (known && owner.Hotbar.ItemAt(was) == item) continue;   // still in its slot (a spare copy elsewhere is not the held one)
                int at = owner.Hotbar.IndexOf(item);
                if (at >= 0) { heldAt[slot] = at; continue; }               // moved along the hotbar
                var replacement = known ? owner.Hotbar.ItemAt(was) : null;
                Unequip(slot);
                if (replacement != null && Fits(replacement, slot) && !IsEquipped(replacement)) { Equip(replacement); heldAt[slot] = was; }
            }
        }
    }

    void OnDisable() => consuming.Cancel();
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
    // The model shown in that slot (the weapon in the hand), or null.
    public GameObject HeldObject(EquipmentSlot slot) => objects.TryGetValue(slot, out var o) ? o : null;
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
        // While eating or drinking the hands are busy (the weapon is only hidden): nothing new is taken in hand.
        if (IsHand(item.equipSlot) && IsUsingItem && !grant) return false;

        var slot = into.HasValue && Fits(item, into.Value) ? into.Value : SlotFor(item);
        int wasAt=-1;   // a worn item taken from the bag: the one it replaces goes back to its place
        if(Character is Player owner && !(grant && !IsHand(slot)))
        {
            if(owner.Bag.IndexOf(item)<0 && owner.Hotbar.IndexOf(item)<0)return false;
            if(!IsHand(slot)){wasAt=owner.Bag.IndexOf(item);if(!owner.Bag.RemoveOne(item)){wasAt=-1;if(!owner.Hotbar.RemoveOne(item))return false;}}
        }
        if(Character is Player p && (slot==EquipmentSlot.RightHand || slot==EquipmentSlot.LeftHand) && p.Hotbar.IndexOf(item)<0) {
            int from=p.Bag.IndexOf(item);int destination=Get(slot)!=null?p.Hotbar.IndexOf(Get(slot)):-1;
            if(destination<0)destination=p.Hotbar.FirstEmpty();
            if(from<0 || destination<0 || !Inventory.Move(p.Bag,from,p.Hotbar,destination)){NotificationUI.Show("Make room in the hotbar to equip a hand item");return false;}
        }
        Unequip(slot, true, wasAt);   // a worn item goes back into the bag
        if(Character is Player held && IsHand(slot))heldAt[slot]=held.Hotbar.IndexOf(item);

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
            NotificationUI.Show("Make room in your bag first");
            return false;
        }
        // A hand item goes from its hotbar slot into the bag; with no room there it stays on the hotbar. One held without
        // a hotbar slot (never expected) is put in the bag or hotbar, or dropped, so it is never lost.
        if (IsHand(slot) && Character is Player p && p.Bag != null && p.Hotbar != null)
        {
            int at = HeldIndex(slot);
            Unequip(slot, false);
            if (at >= 0)
            {
                int spot = p.Bag.Accepts(item) ? p.Bag.FindSpot(item) : -1;
                if (spot >= 0) Inventory.Move(p.Hotbar, at, p.Bag, spot);
                else NotificationUI.Show("No room in your bag: it stays on the hotbar");
            }
            else if (p.Bag.IndexOf(item) < 0 && p.Hotbar.IndexOf(item) < 0 && !p.Bag.TryAdd(item) && !p.Hotbar.TryAdd(item) && InventoryManager.HasInstance)
                InventoryManager.Instance.DropFromPlayer(item, p);
            return true;
        }
        return Unequip(slot) != null;
    }

    // The hotbar slot the item in this hand sits in (-1 when the hand is empty or it isn't a player). Spare copies of
    // the same item elsewhere in the hotbar or bag are not held.
    public int HeldIndex(EquipmentSlot slot)
    {
        var item = Get(slot);
        if (item == null || Character is not Player owner) return -1;
        if (heldAt.TryGetValue(slot, out int at) && owner.Hotbar.ItemAt(at) == item) return at;
        return owner.Hotbar.IndexOf(item);
    }

    // A worn item dragged off the doll into the world: it comes off and lands at the player's feet. If it can't be
    // dropped it goes into the pack instead, so it is never lost.
    public bool UnequipToFloor(EquipmentSlot slot)
    {
        var item = Get(slot);
        if (item == null || IsHand(slot) || Character is not Player owner || !InventoryManager.HasInstance) return false;
        Unequip(slot, false);
        if (InventoryManager.Instance.DropFromPlayer(item, owner) != null) return true;
        if (!owner.Bag.TryAdd(item) && !owner.Hotbar.TryAdd(item)) Equip(item, false, grant: true, into: slot);
        return false;
    }

    // A worn item dragged off the doll onto a bag cell: it goes there, or swaps with the one item there
    // when that one fits this slot. Hand items live in the hotbar and are moved there instead.
    public bool UnequipInto(EquipmentSlot slot, Inventory container, int index)
    {
        var item = Get(slot);
        if (item == null || container == null || IsHand(slot) || !container.Accepts(item)) return false;
        var hit = container.Overlaps(item, index);
        if (hit.Count == 1 && Fits(container.ItemAt(hit[0]), slot)) return Equip(container.ItemAt(hit[0]), into: slot);
        if (hit.Count > 0 || !container.Fits(item, index)) return false;
        Unequip(slot, false);
        container.Set(index, new ItemStack(item));
        return true;
    }

    // A held item dragged from its hotbar slot (or the doll's hand) onto a free spot in the bag: it comes out of the
    // hand and moves there.
    public bool UnequipHandInto(EquipmentSlot slot, Inventory container, int index)
    {
        var item = Get(slot);
        if (item == null || !IsHand(slot) || Character is not Player p || container == null || container == p.Hotbar
            || !container.Accepts(item) || !container.Fits(item, index)) return false;
        int at = HeldIndex(slot);
        if (at < 0) return false;
        Unequip(slot, false);
        return Inventory.Move(p.Hotbar, at, container, index);
    }

    // toBag: a worn item returns to the bag (at returnAt when it fits there), or drops at the player's feet
    // when it is full.
    ItemData Unequip(EquipmentSlot slot, bool toBag, int returnAt = -1)
    {
        EnsureInitialized();
        if (!items.TryGetValue(slot, out var item)) return null;
        if (toBag && !IsHand(slot) && Character is Player owner)
        {
            if (returnAt >= 0 && owner.Bag.Accepts(item) && owner.Bag.Fits(item, returnAt)) owner.Bag.Set(returnAt, new ItemStack(item));
            else if (!owner.Bag.TryAdd(item) && !owner.Hotbar.TryAdd(item) && InventoryManager.HasInstance)
                InventoryManager.Instance.DropFromPlayer(item, owner);
        }

        if (objects.TryGetValue(slot, out var obj) && obj != null) Destroy(obj);
        objects.Remove(slot);
        items.Remove(slot);
        heldAt.Remove(slot);

        if (Character.Stats != null)
            Character.Stats.RemoveAllFromSource(sources[slot]);

        if (slot == EquipmentSlot.RightHand && weaponHitbox != null)
            weaponHitbox.ClearProfile();

        ItemEffectProcessor.Fire(item, EffectTrigger.OnUnequip, EffectContext.For(Character, item));

        Changed?.Invoke();
        return item;
    }

    // Swaps the item in a slot for another that fits it (a shrine honing it): a held item keeps its hotbar slot, a
    // worn one stays worn. Nothing goes to the bag.
    public bool Replace(EquipmentSlot slot, ItemData with)
    {
        if (Get(slot) == null || with == null || !Fits(with, slot)) return false;
        if (IsHand(slot) && Character is Player p)
        {
            int at = HeldIndex(slot);
            if (at < 0) return false;
            p.Hotbar.Set(at, new ItemStack(with));
            Unequip(slot, false);
            return Equip(with, false);
        }
        Unequip(slot, false);
        return Equip(with, false, grant: true, into: slot);
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
