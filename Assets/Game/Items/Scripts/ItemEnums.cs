using System;

// All values are serialized as integers. Never renumber, never reuse a retired value.

public enum ItemUseAnimation
{
    Idle = 0,
    Eat = 1,
}

public enum ItemAudioSource
{
    AudioData       = 0,
    AudioClip       = 1,
    AudioManagerKey = 2,
}

public enum ItemType
{
    Generic    = 0,
    Weapon     = 1,
    Shield     = 2,
    Tool       = 3,
    Consumable = 4,
    Key        = 5,
    Equipment  = 6,
}

// Bit mask over ItemType, for container filters ("the hotbar only takes these").
[Flags]
public enum ItemTypeMask
{
    None       = 0,
    Generic    = 1 << 0,
    Weapon     = 1 << 1,
    Shield     = 1 << 2,
    Tool       = 1 << 3,
    Consumable = 1 << 4,
    Key        = 1 << 5,
    Equipment  = 1 << 6,
    All        = ~0,
}

public static class ItemTypeMaskExtensions
{
    public static bool Contains(this ItemTypeMask mask, ItemType type) =>
        (mask & (ItemTypeMask)(1 << (int)type)) != 0;
}

public enum EquipmentSlot
{
    RightHand = 0,
    LeftHand  = 1,
    Head      = 2,
    Body      = 3,
    Trinket1  = 4,
    Trinket2  = 5,
    Gloves    = 6,
    Boots     = 7,
}

public enum EffectType
{
    Heal           = 0,   // restore health by Value
    RestoreMana    = 1,   // restore mana by Value
    TimedStatBuff  = 2,   // add a stat modifier for Duration seconds (Duration <= 0 = permanent; OnEquip ones end on unequip)
    PlayAudio      = 3,   // play an AudioData at the user
    SpawnPrefab    = 4,   // instantiate a prefab at the user or hit point
    SetFlag        = 5,   // set a progression flag
    Damage         = 6,   // deal Value damage to the target (or the user if no target)
    FoodRegen      = 7,   // heal Value per second for Duration seconds, replacing any running food regen
    Custom         = 99,  // run an ItemEffect asset
}

public enum EffectTrigger
{
    OnUse       = 0,   // consumable used from the hotbar or inventory
    OnEquip     = 1,
    OnUnequip   = 2,
    OnHitLanded = 3,   // the wearer's attack connected
    OnHurt      = 4,   // the wearer took damage
    OnPickup    = 5,
}
