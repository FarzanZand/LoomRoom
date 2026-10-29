using System.Text;
using Sirenix.OdinInspector;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// One asset per item. Sections show up only when the item type needs them.
[HideMonoScript]
[CreateAssetMenu(fileName = "NewItem", menuName = "Items/Item")]
public partial class ItemData : ScriptableObject
{
    public string itemName;
    [Tooltip("Stable save identifier; do not change after shipping.")] public string saveId;
    public WeaponCategory weaponCategory;
    [Tooltip("Makes this item a spell tome: held in the left hand, it casts this spell.")] public SpellDefinition spell;
    [Tooltip("Gear drops Common and is rolled Uncommon, Rare or Unique by the loot (Legendary is only given) (ItemPropertyTable); set Relic on a hand-made item. " +
        "Spell tomes are Rare. Each rarity has its colour on its slot, loot beam and name (UIManager).")]
    public ItemRarity rarity;

    // Gear that takes a rarity: weapons, shields, worn armour and trinkets (not spell tomes).
    public bool IsGear => spell == null && (itemType == ItemType.Weapon || itemType == ItemType.Shield || itemType == ItemType.Equipment && canBeEquipped);
    // A name, beam and pickup line in colour: any rarity above Common.
    public bool HasRarityColour => rarity != ItemRarity.Common;
    public Color RarityColour => UIManager.RarityColor(rarity);
    public string RarityTextColour => "#" + ColorUtility.ToHtmlStringRGB(Color.Lerp(RarityColour, Color.white, .45f));
    // The faint floor behind the item in a bag, hotbar, equipment or shop slot: only items with a rarity above Plain (a
    // Common item's slot looks like an empty one). The floor sprite (UI/Sprites/Inventory/Rarity floor, 9-sliced) carries
    // the shape: a stepped band at the edges, nearly clear behind the icon; this is its colour.
    public bool HasSlotFill => HasRarityColour;
    public Color SlotFillColour { get { var c = RarityColour; c.a = 1f; return c; } }

    // A slot's rarity fill and its optional border image (an authored child named "Rarity border"), for any slot UI.
    public static void ShowSlotFill(UnityEngine.UI.Image fill, ItemData item, bool show = true)
    {
        bool on = show && item != null && item.HasSlotFill;
        if (fill != null) { fill.enabled = on; if (on) fill.color = item.SlotFillColour; }
        // The old 1 px border is retired: the floor's edge band does its job.
        var border = fill != null ? fill.transform.Find("Rarity border")?.GetComponent<UnityEngine.UI.Image>() : null;
        if (border != null) border.enabled = false;
    }
    // The name with a rolled rarity in front, for text without colour (the message log): "Unique Iron Sword".
    public string DisplayName => IsGear && (rarity == ItemRarity.Uncommon || rarity == ItemRarity.Rare || rarity == ItemRarity.Unique || rarity == ItemRarity.Legendary) ? $"{RarityName} {itemName}" : itemName;
    public string RarityName => rarity switch { ItemRarity.Uncommon => "Uncommon", ItemRarity.Rare => "Rare", ItemRarity.Unique => "Unique", ItemRarity.Legendary => "Legendary", ItemRarity.Relic => "Relic", ItemRarity.Spoils => "Spoils", _ => "Common" };
    [TextArea] public string description;
    [HorizontalGroup("IconRow")]
    [Tooltip("Bag icon, sized to the item's footprint. Generated from the item's model (Icon Source) unless custom.")]
    public Sprite icon;
    [Tooltip("How the icon is generated from the item's model; Custom keeps an icon you assign yourself.")]
    public ItemIconSource iconSource = new();
    [Tooltip("Free-form label for grouping in the Item Database window.")]
    public string tag;

    [HorizontalGroup("PrefabRow"), HideLabel]
    [Tooltip("Mesh prefab shown in the world and in the hand.")]
    public GameObject worldPrefab;
    [Tooltip("Optional pickup-only visual. Empty uses World Prefab. Hand/equipment visuals still use World Prefab.")]
    public GameObject pickupVisualPrefab;

    public ItemType itemType = ItemType.Generic;

    [Min(1)] public int maxStackSize = 1;

    [Tooltip("Cells it takes in the bag grid, width x height. 0 x 0 picks by kind: swords and maces 1x3, " +
        "knives 1x2, shields 2x2, body armour 2x3, helm, gloves, boots and leggings 2x2, the rest 1x1.")]
    public Vector2Int gridSize;
    public Vector2Int GridSize
    {
        get
        {
            if (gridSize.x > 0 && gridSize.y > 0) return gridSize;
            switch (itemType)
            {
                case ItemType.Weapon: return itemName != null && itemName.IndexOf("Knife", System.StringComparison.OrdinalIgnoreCase) >= 0 ? new Vector2Int(1, 2) : new Vector2Int(1, 3);
                case ItemType.Shield: return new Vector2Int(2, 2);
                case ItemType.Spell: return new Vector2Int(2, 2);   // a spell tome is a big book
                case ItemType.Tool: return new Vector2Int(1, 2);
                case ItemType.Equipment:
                    if (!canBeEquipped) return Vector2Int.one;
                    return equipSlot switch
                    {
                        EquipmentSlot.Body => new Vector2Int(2, 3),
                        EquipmentSlot.Head or EquipmentSlot.Gloves or EquipmentSlot.Boots or EquipmentSlot.Legs => new Vector2Int(2, 2),
                        _ => Vector2Int.one,
                    };
                default: return Vector2Int.one;
            }
        }
    }

    [Tooltip("Price in gold at a merchant. Selling pays CurrencyManager's sell fraction of this. Zero uses the manager's fallback price.")]
    [Min(0)] public int value;

    [Tooltip("Random loot tier: 1 Bronze/Leather, 2 Iron, 3 Steel, 4 Crystal. 0 = never dropped at random (quest items, keys, uniques placed by hand). Biome loot profiles pick gear by tier.")]
    [Range(0, 4)] public int tier;
    [Tooltip("How often this item is picked among others of its tier and kind.")]
    [Min(0)] public float lootWeight = 1;

    [Tooltip("AudioData preserves the shared pickup setting. Clip or key explicitly overrides the shared pickup sound for this item.")]
    public ItemAudioSource pickupAudioSource;
    [ShowIf("PickupUsesData"), Tooltip("Sound when picked up. Empty = InventoryManager default. Shared pickup sound may override this.")]
    public AudioData pickupAudio;
    [ShowIf("PickupUsesClip")] public AudioClip pickupClip;
    [ShowIf("PickupUsesClip"), Range(0,1)] public float pickupClipVolume = 1;
    [ShowIf("PickupUsesKey"), Tooltip("Key in AudioManager's SFX Library. Uses its library volume and pitch settings.")]
    public string pickupAudioKey;

    [Tooltip("On pickup, try the hotbar first, then inventory if it has no room. Off sends the item straight to inventory.")]
    public bool directToHotbar = false;

    // ── Equipment ─────────────────────────────────────────────────────

    [BoxGroup("Equipment")]
    public bool canBeEquipped = false;

    [BoxGroup("Equipment"), ShowIf("canBeEquipped")]
    public EquipmentSlot equipSlot = EquipmentSlot.RightHand;

    [BoxGroup("Equipment"), ShowIf("ShowsInHand"), Min(0.01f)]
    [Tooltip("Visual scale while held (and while eaten or drunk). Does not change the world pickup or combat reach.")]
    public float heldScale = 1f;

    [BoxGroup("Equipment"), ShowIf("ShowsInHand")]
    [Tooltip("Grip position relative to the equipment socket.")]
    public Vector3 heldPosition;

    [BoxGroup("Equipment"), ShowIf("ShowsInHand")]
    [Tooltip("Grip rotation relative to the equipment socket, in degrees.")]
    public Vector3 heldRotation;

    [BoxGroup("Equipment"), ShowIf("canBeEquipped")]
    [Tooltip("Equip straight into the slot when picked up, if the slot is free.")]
    public bool equipOnPickup = false;

    [BoxGroup("Equipment"), ShowIf("canBeEquipped")]
    [Tooltip("Stat changes while equipped. A weapon's damage, a shield's defense, a ring's speed.")]
    [ListDrawerSettings(ShowFoldout = false)]
    public StatModifierEntry[] statModifiers;

    // ── Weapon ────────────────────────────────────────────────────────

    [BoxGroup("Weapon"), ShowIf("IsWeapon"), HideLabel]
    public HitProfile weapon = new HitProfile();

    [BoxGroup("Weapon"), ShowIf("IsWeapon"), Min(0)]
    [Tooltip("Each hit rolls this much above or below the weapon's damage (Iron Sword 7 with 2 hits for 5-9).")]
    public int damageSpread;

    // ── Effects ───────────────────────────────────────────────────────

    [BoxGroup("Effects")]
    [Tooltip("What the item does. Consumables use OnUse; gear uses OnEquip/OnHitLanded/OnHurt.")]
    [ListDrawerSettings(ShowFoldout = false)]
    public EffectEntry[] effects;

    [BoxGroup("Effects"), ShowIf("IsConsumable")]
    public ItemAudioSource useAudioSource;
    [BoxGroup("Effects"), ShowIf("UseUsesData")]
    [Tooltip("Sound when consumed.")]
    public AudioData useAudio;
    [BoxGroup("Effects"), ShowIf("UseUsesClip")] public AudioClip useClip;
    [BoxGroup("Effects"), ShowIf("UseUsesClip"), Range(0,1)] public float useClipVolume = 1;
    [BoxGroup("Effects"), ShowIf("UseUsesKey"), Tooltip("Key in AudioManager's SFX Library. Uses its library volume and pitch settings.")]
    public string useAudioKey;

    [BoxGroup("Consumable"), ShowIf("IsConsumable")]
    [Tooltip("Line added to the message log when used, e.g. \"The potion tastes bitter.\" Empty posts \"You use <item>.\"")]
    public string useMessage;

    [BoxGroup("Consumable"), ShowIf("IsConsumable")]
    [Tooltip("Instant: used at once. Eat or Drink: taken in the right hand in place of the held weapon, the arm eats or drinks (Characters/Animations/FirstPersonPlayer/Consume), it takes effect near the end and the weapon comes back. Attacks and spells wait until it is done.")]
    public ItemUseAnimation AnimationOnUse = ItemUseAnimation.Instant;

    [BoxGroup("Consumable"), ShowIf("UsesAnimation"), Min(.2f)]
    [Tooltip("Seconds the eating or drinking takes (the clip is played to fit).")]
    public float eatDuration = 1.3f;

    // ── Variants ──────────────────────────────────────────────────────
    // A rarity and its rolled properties live on runtime copies of the asset (ItemData.Variants.cs).
    [System.NonSerialized] public ItemData baseItem;
    public ItemData BaseItem => baseItem != null ? baseItem : this;

    // ── Queries ───────────────────────────────────────────────────────

    public bool IsWeapon     => itemType == ItemType.Weapon;
    public bool IsConsumable => itemType == ItemType.Consumable;
    bool UsesAnimation => IsConsumable && AnimationOnUse != ItemUseAnimation.Instant;
    bool ShowsInHand => canBeEquipped || UsesAnimation;
    bool PickupUsesData => pickupAudioSource == ItemAudioSource.AudioData;
    bool PickupUsesClip => pickupAudioSource == ItemAudioSource.AudioClip;
    bool PickupUsesKey => pickupAudioSource == ItemAudioSource.AudioManagerKey;
    bool UseUsesData => IsConsumable && useAudioSource == ItemAudioSource.AudioData;
    bool UseUsesClip => IsConsumable && useAudioSource == ItemAudioSource.AudioClip;
    bool UseUsesKey => IsConsumable && useAudioSource == ItemAudioSource.AudioManagerKey;

    // The pickup sound. The item's own clip or key always wins; otherwise sharedKey (InventoryManager's shared
    // pickup sound, null when off) or the item's AudioData, else fallback (InventoryManager's default).
    public void PlayPickupSound(string sharedKey, AudioData fallback)
    {
        if (!AudioManager.HasInstance) return;
        if (pickupAudioSource != ItemAudioSource.AudioData && PlayItemAudio(pickupAudioSource, null, pickupClip, pickupClipVolume, pickupAudioKey)) return;
        if (!string.IsNullOrEmpty(sharedKey)) AudioManager.Instance.PlaySFX2D(sharedKey);
        else PlayItemAudio(ItemAudioSource.AudioData, pickupAudio != null ? pickupAudio : fallback, null, 0f, null);
    }

    // One item sound (pickup or use) as its source says, in 2D. False when that source has nothing set.
    static bool PlayItemAudio(ItemAudioSource source, AudioData data, AudioClip clip, float clipVolume, string key)
    {
        if (!AudioManager.HasInstance) return false;
        switch (source)
        {
            case ItemAudioSource.AudioData:
                return data != null && AudioManager.Instance.PlaySFXData2D(data) != null;
            case ItemAudioSource.AudioClip:
                return clip != null && AudioManager.Instance.PlaySFX2D(clip, Mathf.Clamp01(clipVolume)) != null;
            case ItemAudioSource.AudioManagerKey:
                return !string.IsNullOrWhiteSpace(key) && AudioManager.Instance.PlaySFX2D(key.Trim()) != null;
        }
        return false;
    }

    public void PlayUseSound() => PlayItemAudio(useAudioSource, useAudio, useClip, useClipVolume, useAudioKey);

    // Consume: fire OnUse effects for the user.
    // sound off: the eat or drink sound already played as the hand reached the face (ConsumableUse).
    public void Use(Character user, bool sound = true)
    {
        if (!IsConsumable) return;
        if (sound) PlayUseSound();
        if (user is Player p && p.kind == PlayerKind.Table)
            MessageLog.Post(string.IsNullOrWhiteSpace(useMessage) ? $"You use the {itemName}." : useMessage.Trim(), MessageKind.Info);
        ItemEffectProcessor.Fire(this, EffectTrigger.OnUse, EffectContext.For(user, this));
    }

    // Rich-text tooltip body shared by the in-game tooltip and the Item Database preview, in this order: the main
    // number big (a weapon's damage and how fast it swings, a shield's block armor, worn armor), the other stats,
    // effects and spell, the change against what the player
    // is wearing in that slot (only while a player exists), then the description as flavour and the price.
    public const string TextColor = "#E6E1D6", DimColor = "#9AA3AD", GoodColor = "#80CEA0", BadColor = "#E78787", EffectColor = "#A1C5DE";
    public const string FlavourColor = "#B8A27A";

    // The item's main number for the tooltip and its compare bars: a weapon's damage, a shield's or armour's armour.
    public bool TryMainStat(out string label, out float value, out string text)
    {
        StatType? main = MainStatType;
        value = main.HasValue ? FlatBonus(this, main.Value) : 0f;
        label = main == StatType.AttackDamage ? "Damage" : itemType == ItemType.Shield ? "Armour when blocking" : "Armour";
        text = IsWeapon && damageSpread > 0 ? $"{Mathf.Max(1f, value - damageSpread):0}-{value + damageSpread:0}" : $"{value:0.#}";
        return main.HasValue && value > 0f;
    }

    // How much faster (positive) or slower a weapon swings, from its AttackSpeed bonuses.
    public float SwingSpeed => FlatBonus(this, StatType.AttackSpeed);

    // withMain false: the main number and the compare lines are left out (the tooltip shows them as bars).
    public string BuildTooltip(bool withMain = true, bool withPrice = true)
    {
        var blocks = new System.Collections.Generic.List<string>();
        var lines = new System.Collections.Generic.List<string>();
        StatType? main = IsWeapon ? StatType.AttackDamage : (itemType == ItemType.Shield || itemType == ItemType.Equipment) && canBeEquipped ? StatType.Armor : null;
        bool showMain = TryMainStat(out string mainLabel, out float mainValue, out string mainText);
        if (showMain && withMain)
        {
            lines.Add($"<size=140%><color={TextColor}>{mainText}</color></size>  <color={DimColor}>{(IsWeapon ? "Weapon damage" : mainLabel)}</color>");
            if (IsWeapon) lines.Add($"<color={DimColor}>{SwingSpeedWord(SwingSpeed)} swings</color>");
        }
        // The weapon's number is only its own part of a hit (Hitbox: the wielder's Attack Damage, weapon included, then
        // skill and Strength): say so, so the character panel's damage doesn't look like a mistake.
        if (showMain && IsWeapon) lines.Add($"<color={DimColor}>{(withMain ? "" : "Weapon damage. ")}Your own damage, Strength and skill add to it.</color>");
        // The item's own stats in white, the rolled properties of an Uncommon, Rare, Unique or Legendary item in blue.
        if (canBeEquipped && statModifiers != null)
            for (int i = 0; i < statModifiers.Length; i++)
            {
                var modifier = statModifiers[i];
                if (modifier == null) continue;
                bool rolledStat = IsRolledModifier(i);
                if (!rolledStat && showMain && modifier.stat == main.Value && modifier.type == ModifierType.Flat) continue;
                if (!rolledStat && IsWeapon && modifier.stat == StatType.AttackSpeed && modifier.type == ModifierType.Flat) continue;
                string colour = Worse(modifier.stat, modifier.value) ? BadColor : rolledStat ? EffectColor : TextColor;
                lines.Add($"<color={colour}>{StatLine(modifier.stat, modifier.type, modifier.value, BlockOnly(modifier.stat))}</color>");
            }
        Add(blocks, lines);

        if (effects != null)
            foreach (var effect in effects)
            {
                string line = effect?.Describe();
                if (!string.IsNullOrWhiteSpace(line)) lines.Add($"<color={EffectColor}>{line}</color>");
            }
        if (spell != null)
        {
            var caster = PlayerManager.HasInstance ? PlayerManager.Instance.Active?.GetComponent<PlayerSpellcasting>() : null;
            lines.Add(spell.RulesLine(caster != null ? caster.Cost(spell) : spell.manaCost, caster != null ? caster.Power(spell) : spell.power));
        }
        Add(blocks, lines);

        // Against the item it would replace (Equipment.SlotFor: a trinket goes to a free trinket slot first, so
        // with one free there is nothing to compare): every flat stat either item has.
        var gear = canBeEquipped && !IsConsumable && PlayerManager.HasInstance ? PlayerManager.Instance.Active?.Equipment : null;
        var equipped = Replaces();
        if (equipped != null && withMain)
        {
            var stats = new System.Collections.Generic.List<StatType>();
            foreach (var data in new[] { this, equipped })
                if (data.statModifiers != null)
                    foreach (var m in data.statModifiers)
                        if (m != null && m.type == ModifierType.Flat && !stats.Contains(m.stat)) stats.Add(m.stat);
            var changes = new System.Collections.Generic.List<string>();
            foreach (var stat in stats)
            {
                float delta = FlatBonus(this, stat) - FlatBonus(equipped, stat);
                if (Mathf.Abs(delta) > .001f) changes.Add($"<color={(Worse(stat, delta) ? BadColor : GoodColor)}>{StatLine(stat, ModifierType.Flat, delta, BlockOnly(stat))}</color>");
            }
            lines.Add(changes.Count == 0 ? $"<color={DimColor}>Same as your {equipped.itemName}</color>"
                : $"<color={DimColor}>Against your {equipped.itemName}:</color>\n" + string.Join("\n", changes));
            Add(blocks, lines);
        }
        else if (gear != null && gear.IsEquipped(this)) { lines.Add($"<color={DimColor}>Equipped</color>"); Add(blocks, lines); }

        if (!string.IsNullOrWhiteSpace(description)) { lines.Add($"<color={FlavourColor}><i>{description.Trim()}</i></color>"); Add(blocks, lines); }
        if (withPrice && value > 0 && itemType != ItemType.Key && itemType != ItemType.Spoils) { lines.Add($"<color={DimColor}>Worth {value} gold</color>"); Add(blocks, lines); }
        return string.Join("\n\n", blocks);
    }

    // What the active player has in the slot this would go in (Equipment.SlotFor), or null: nothing there,
    // this item itself, or not gear. The tooltip compares against it.
    public ItemData Replaces()
    {
        var gear = canBeEquipped && !IsConsumable && PlayerManager.HasInstance ? PlayerManager.Instance.Active?.Equipment : null;
        var equipped = gear != null && !gear.IsEquipped(this) ? gear.Get(gear.SlotFor(this)) : null;
        return equipped != this ? equipped : null;
    }

    // How fast a weapon swings, from its AttackSpeed bonus.
    public static string SwingSpeedWord(float attackSpeed) =>
        attackSpeed <= -.25f ? "Very slow" : attackSpeed < -.05f ? "Slow" : attackSpeed >= .3f ? "Very fast" : attackSpeed > .05f ? "Fast" : "Normal";

    // The kind of thing it is (the tooltip's type line): "One-handed sword", "Body armour", "Ring", "Potion".
    public string TypeLine()
    {
        bool Named(string word) => itemName != null && itemName.IndexOf(word, System.StringComparison.OrdinalIgnoreCase) >= 0;
        if (spell != null) return "Spell tome";
        switch (itemType)
        {
            case ItemType.Weapon:
                if (Named("knife")) return "Knife";
                return weaponCategory == WeaponCategory.Mace ? "One-handed mace" : "One-handed sword";
            case ItemType.Shield: return "Shield";
            case ItemType.Consumable: return Named("potion") || Named("tonic") || Named("elixir") ? "Potion" : AnimationOnUse == ItemUseAnimation.Eat ? "Food" : "Consumable";
            case ItemType.Key: return "Key";
            case ItemType.Spoils: return "Spoils";
            case ItemType.Tool: return "Tool";
            case ItemType.Equipment:
                if (!canBeEquipped) return "Item";
                if (Equipment.IsTrinket(equipSlot)) return Named("ring") || Named("band") ? "Ring" : Named("amulet") || Named("pendant") ? "Amulet" : Named("charm") ? "Charm" : "Trinket";
                return equipSlot switch
                {
                    EquipmentSlot.Body => "Body armour",
                    EquipmentSlot.Head => "Helm",
                    EquipmentSlot.Gloves => "Gloves",
                    EquipmentSlot.Boots => "Boots",
                    EquipmentSlot.Legs => "Leggings",
                    _ => SlotName(equipSlot),
                };
            default: return "Item";
        }
    }

    // What kind of thing it is, in a few words (shop rows): "Helm, +1 armor", "Weapon, +6 damage", "Heals 6 over 6s".
    public string ShortInfo()
    {
        string effect = null;
        if (effects != null)
            foreach (var e in effects) { effect = e?.Describe(); if (!string.IsNullOrWhiteSpace(effect)) break; }
        if (IsConsumable) return effect ?? "";
        string kind = spell != null ? "Spell tome"
            : itemType == ItemType.Weapon ? "Weapon"
            : itemType == ItemType.Shield ? "Shield"
            : itemType == ItemType.Equipment ? SlotName(equipSlot)
            : itemType.ToString();
        string stat = null;
        // Gear: its main number in all (rolled bonuses included), the same number its tooltip shows.
        if (TryMainStat(out string mainLabel, out _, out string mainText)) stat = $"{mainText} {mainLabel.ToLowerInvariant()}";
        if (stat == null && statModifiers != null)
            foreach (var m in statModifiers)
                if (m != null) { stat = StatLine(m.stat, m.type, m.value, BlockOnly(m.stat)); break; }
        if (spell != null) stat = $"{spell.manaCost:0.#} mana";
        stat ??= effect;
        return string.IsNullOrWhiteSpace(stat) ? kind : $"{kind}, {stat}";
    }

    // The one name for each equipment slot (shop rows, tooltips, the equipment panel in capitals).
    public static string SlotName(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.RightHand => "Mainhand",
        EquipmentSlot.LeftHand => "Offhand",
        EquipmentSlot.Head => "Helm",
        EquipmentSlot.Body => "Armour",
        EquipmentSlot.Legs => "Leggings",
        EquipmentSlot.Trinket1 or EquipmentSlot.Trinket2 => "Trinket",
        _ => slot.ToString(),
    };

    static void Add(System.Collections.Generic.List<string> blocks, System.Collections.Generic.List<string> lines)
    {
        if (lines.Count > 0) blocks.Add(string.Join("\n", lines));
        lines.Clear();
    }

    // One stat in words. Attack speed reads as slower or faster swings, not a fraction.
    // blocking: the stat only counts on a block (a shield's armor).
    public static string StatLine(StatType stat, ModifierType type, float value, bool blocking = false)
    {
        if (stat == StatType.AttackSpeed && type == ModifierType.Flat)
            return $"Swings {Mathf.Abs(value) * 100:0}% {(value < 0 ? "slower" : "faster")}";
        return new StatModifierEntry { stat = stat, type = type, value = value }.Describe() + (blocking ? " while blocking" : "");
    }

    // A shield's armor only counts when a hit is blocked (CharacterStats.TakeDamage).
    bool BlockOnly(StatType stat) => itemType == ItemType.Shield && stat == StatType.Armor;

    static bool Worse(StatType stat, float value) => value < 0;

    static float FlatBonus(ItemData data, StatType stat)
    {
        float sum = 0;
        if (data?.statModifiers != null)
            foreach (var m in data.statModifiers)
                if (m != null && m.stat == stat && m.type == ModifierType.Flat) sum += m.value;
        return sum;
    }

    void OnValidate()
    {
        if (itemType == ItemType.Weapon || itemType == ItemType.Shield) canBeEquipped = true;
        if (maxStackSize < 1) maxStackSize = 1;
#if UNITY_EDITOR
        EditorValidated?.Invoke(this);
#endif
    }

    // ── Editor: generate the bag icon (ItemIconRenderer) ──────────────

    [HorizontalGroup("IconRow", Width = 100)]
    [Button("Generate icon"), DisableIf("@iconSource != null && iconSource.custom")]
    void GenerateIcon()
    {
#if UNITY_EDITOR
        EditorIconRequested?.Invoke(this);
#endif
    }
#if UNITY_EDITOR
    // Set by ItemIconRenderer: the button above, and OnValidate so a generated icon follows its model.
    public static System.Action<ItemData> EditorIconRequested, EditorValidated;
#endif

    // ── Editor: generate a world pickup prefab variant ────────────────

    [HorizontalGroup("PrefabRow", Width = 80)]
    [Button("Generate")]
    void GeneratePrefab()
    {
#if UNITY_EDITOR
        if (string.IsNullOrEmpty(itemName))
        {
            EditorUtility.DisplayDialog("Generate Prefab", "Set the Item Name before generating.", "OK");
            return;
        }

        const string basePrefabPath = "Assets/Game/Items/Prefabs/Pickups/_ItemPickup.prefab";
        const string prefabFolder   = "Assets/Game/Items/Prefabs/Pickups/";

        GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath);
        if (basePrefab == null) { Debug.LogError($"[ItemData] Base prefab not found at {basePrefabPath}"); return; }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
        instance.name = itemName;

        GameObject pickupModel = pickupVisualPrefab != null ? pickupVisualPrefab : worldPrefab;
        if(pickupModel == null)
            pickupModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Core/Prefabs/InventoryManager.prefab")?.GetComponent<InventoryManager>()?.defaultPickupVisual;
        if (pickupModel != null)
        {
            GameObject meshInstance = (GameObject)PrefabUtility.InstantiatePrefab(pickupModel);
            meshInstance.transform.SetParent(instance.transform);
            meshInstance.transform.localPosition = Vector3.zero;
            meshInstance.transform.localRotation = Quaternion.identity;
            meshInstance.transform.localScale = Vector3.one;

            Renderer[] renderers = meshInstance.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(instance.transform.position, Vector3.one);
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            Vector3 localCenter = instance.transform.InverseTransformPoint(bounds.center);

            SphereCollider detection = instance.GetComponent<SphereCollider>();
            if (detection != null)
                detection.radius = Mathf.Max(2f, Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z)) + 0.25f;

            CapsuleCollider capsule = instance.AddComponent<CapsuleCollider>();
            capsule.center    = localCenter;
            capsule.radius    = Mathf.Max(bounds.extents.x, bounds.extents.z);
            capsule.height    = bounds.size.y;
            capsule.direction = 1;
            capsule.isTrigger = false;
        }

        string variantPath = AssetDatabase.GenerateUniqueAssetPath(prefabFolder + itemName + ".prefab");
        GameObject savedVariant = PrefabUtility.SaveAsPrefabAsset(instance, variantPath);
        Object.DestroyImmediate(instance);

        if (savedVariant == null) { Debug.LogError("[ItemData] Failed to save prefab variant."); return; }

        WorldItem worldItem = savedVariant.GetComponent<WorldItem>();
        if (worldItem != null)
        {
            SerializedObject so = new SerializedObject(worldItem);
            so.FindProperty("itemData").objectReferenceValue = this;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SavePrefabAsset(savedVariant);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[ItemData] Created prefab variant at {variantPath}");
#endif
    }
}
