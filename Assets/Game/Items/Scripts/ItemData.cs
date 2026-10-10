using System.Text;
using Sirenix.OdinInspector;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// One asset per item. Sections show up only when the item type needs them.
[HideMonoScript]
[CreateAssetMenu(fileName = "NewItem", menuName = "Items/Item")]
public class ItemData : ScriptableObject
{
    public string itemName;
    [Tooltip("Stable save identifier; do not change after shipping.")] public string saveId;
    public WeaponCategory weaponCategory;
    [Tooltip("Makes this item a spell tome: held in the left hand, it casts this spell.")] public SpellDefinition spell;
    [TextArea] public string description;
    public Sprite icon;
    [Tooltip("Free-form label for grouping in the Item Database window.")]
    public string tag;

    [HorizontalGroup("PrefabRow"), HideLabel]
    [Tooltip("Mesh prefab shown in the world and in the hand.")]
    public GameObject worldPrefab;
    [Tooltip("Optional pickup-only visual. Empty uses World Prefab. Hand/equipment visuals still use World Prefab.")]
    public GameObject pickupVisualPrefab;

    public ItemType itemType = ItemType.Generic;

    [Min(1)] public int maxStackSize = 1;

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

    [BoxGroup("Equipment"), ShowIf("canBeEquipped"), Min(0.01f)]
    [Tooltip("Visual scale while held. Does not change the world pickup or combat reach.")]
    public float heldScale = 1f;

    [BoxGroup("Equipment"), ShowIf("canBeEquipped")]
    [Tooltip("Grip position relative to the equipment socket.")]
    public Vector3 heldPosition;

    [BoxGroup("Equipment"), ShowIf("canBeEquipped")]
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
    [Tooltip("Used only when consuming equipped items from the hand. Idle keeps the original behavior; Eat moves the hand and item toward the mouth.")]
    public ItemUseAnimation AnimationOnUse = ItemUseAnimation.Idle;

    [BoxGroup("Consumable"), ShowIf("UsesEatAnimation"), Min(.1f)]
    public float eatDuration = .65f;
    [BoxGroup("Consumable"), ShowIf("UsesEatAnimation")]
    [Tooltip("Mouth position relative to the first-person camera.")]
    public Vector3 eatMouthPosition = new Vector3(0, -.12f, .13f);
    [BoxGroup("Consumable"), ShowIf("UsesEatAnimation")]
    public Vector3 eatMouthRotation = new Vector3(-65, 0, 0);

    // ── Honed ─────────────────────────────────────────────────────────
    // Barony's blessing, called Honed here: a +1 or +2 piece of gear. A honed item is a runtime copy of
    // its asset with the bonus baked into its stats, shared per level, so every "Honed Iron Sword +1" is
    // the same object and the bag, hotbar and equipment treat it like any other item. Saved as "<saveId>+1".

    public const int MaxHoned = 2;
    [System.NonSerialized] public ItemData baseItem;
    [System.NonSerialized] public int honed;
    public ItemData BaseItem => baseItem != null ? baseItem : this;
    // Gear with a main stat to raise: weapons (damage), shields and armor (armor), trinkets (their bonuses).
    public bool CanBeHoned => (itemType == ItemType.Weapon || itemType == ItemType.Shield || itemType == ItemType.Equipment) && statModifiers != null && statModifiers.Length > 0;

    static readonly System.Collections.Generic.Dictionary<(ItemData, int), ItemData> honedCopies = new();

    // Enter Play Mode without a domain reload keeps statics: drop the last session's copies.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetHonedCopies() => honedCopies.Clear();

    public ItemData Honed(int level)
    {
        var root = BaseItem;
        level = Mathf.Clamp(level, 0, MaxHoned);
        if (level == 0 || !root.CanBeHoned) return root;
        if (honedCopies.TryGetValue((root, level), out var copy) && copy != null) return copy;
        copy = Instantiate(root);
        copy.hideFlags = HideFlags.DontSave;
        copy.name = $"{root.name} +{level}";
        copy.baseItem = root; copy.honed = level;
        copy.itemName = $"Honed {root.itemName} +{level}";
        copy.saveId = $"{root.saveId}+{level}";
        copy.value = Mathf.RoundToInt(root.value * (1 + .5f * level));
        bool trinket = root.equipSlot == EquipmentSlot.Trinket1 || root.equipSlot == EquipmentSlot.Trinket2;
        foreach (var m in copy.statModifiers)
        {
            if (m == null || m.type != ModifierType.Flat || m.value <= 0) continue;
            if (trinket) m.value = Mathf.Round(m.value * (1 + .5f * level));
            else if (m.stat == (root.IsWeapon ? StatType.AttackDamage : StatType.Armor)) m.value += level;
        }
        honedCopies[(root, level)] = copy;
        return copy;
    }

    // A saved id back to its item: "iron_sword" or a honed "iron_sword+1".
    public static ItemData FromSaveId(string id, System.Collections.Generic.IEnumerable<ItemData> catalog)
    {
        if (string.IsNullOrEmpty(id) || catalog == null) return null;
        int level = 0, plus = id.LastIndexOf('+');
        if (plus > 0 && int.TryParse(id.Substring(plus + 1), out level)) id = id.Substring(0, plus);
        foreach (var item in catalog) if (item != null && item.saveId == id) return item.Honed(level);
        return null;
    }

    // ── Queries ───────────────────────────────────────────────────────

    public bool IsWeapon     => itemType == ItemType.Weapon;
    public bool IsConsumable => itemType == ItemType.Consumable;
    bool UsesEatAnimation => IsConsumable && canBeEquipped && AnimationOnUse == ItemUseAnimation.Eat;
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

    // Consume: fire OnUse effects for the user.
    public void Use(Character user)
    {
        if (!IsConsumable) return;
        PlayItemAudio(useAudioSource, useAudio, useClip, useClipVolume, useAudioKey);
        if (user is Player p && p.kind == PlayerKind.Table)
            MessageLog.Post(string.IsNullOrWhiteSpace(useMessage) ? $"You use the {itemName}." : useMessage.Trim(), MessageKind.Info);
        ItemEffectProcessor.Fire(this, EffectTrigger.OnUse, EffectContext.For(user, this));
    }

    // Rich-text tooltip body shared by the in-game tooltip and the Item Database preview. Blocks, separated by a
    // blank line: description and stats (a honed stat shows its plain value plus the honed part), effects and spell,
    // then the change against what the player is wearing in that slot (only while a player exists).
    public const string TextColor = "#E6E1D6", DimColor = "#9AA3AD", GoodColor = "#80CEA0", BadColor = "#E78787", EffectColor = "#A1C5DE";
    public static string HonedTextColor => "#" + ColorUtility.ToHtmlStringRGB(Color.Lerp(UIManager.HasInstance ? UIManager.Instance.honed : UIManager.DefaultHoned, Color.white, .45f));

    public string BuildTooltip()
    {
        var blocks = new System.Collections.Generic.List<string>();
        var lines = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrWhiteSpace(description)) lines.Add($"<color={DimColor}>{description.Trim()}</color>");
        if (canBeEquipped && statModifiers != null)
            foreach (var modifier in statModifiers)
            {
                if (modifier == null) continue;
                string line = $"<color={(Worse(modifier.stat, modifier.value) ? BadColor : TextColor)}>{StatLine(modifier.stat, modifier.type, modifier.value, BlockOnly(modifier.stat))}</color>";
                float plain = PlainValue(modifier);
                if (honed > 0 && Mathf.Abs(modifier.value - plain) > .001f) line += $"  <color={HonedTextColor}>({plain:0.#} + {modifier.value - plain:0.#} honed)</color>";
                lines.Add(line);
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
            lines.Add($"{(caster != null ? caster.Cost(spell) : spell.manaCost):0.#} mana, {(caster != null ? caster.Power(spell) : spell.power):0.#} {(spell.spell == LeftHandSpell.Heal ? "healing" : "damage")}");
        }
        Add(blocks, lines);

        // Against the item it would replace (Equipment.SlotFor: a trinket goes to a free trinket slot first, so
        // with one free there is nothing to compare): every flat stat either item has.
        var gear = canBeEquipped && !IsConsumable && PlayerManager.HasInstance ? PlayerManager.Instance.Active?.Equipment : null;
        var equipped = gear != null && !gear.IsEquipped(this) ? gear.Get(gear.SlotFor(this)) : null;
        if (equipped != null && equipped != this)
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
            lines.Add(changes.Count == 0 ? $"<color={DimColor}>Same as your {equipped.itemName}.</color>"
                : $"<color={DimColor}>Compared to your {equipped.itemName}:</color>\n" + string.Join("\n", changes));
            Add(blocks, lines);
        }
        return string.Join("\n\n", blocks);
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
        if (statModifiers != null)
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
        EquipmentSlot.Body => "Armor",
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
    // blocking: the stat only counts on a block (a shield's armor, as in Barony).
    public static string StatLine(StatType stat, ModifierType type, float value, bool blocking = false)
    {
        if (stat == StatType.AttackSpeed && type == ModifierType.Flat)
            return $"Swings {Mathf.Abs(value) * 100:0}% {(value < 0 ? "slower" : "faster")}";
        return new StatModifierEntry { stat = stat, type = type, value = value }.Describe() + (blocking ? " while blocking" : "");
    }

    // A shield's armor only counts when a hit is blocked (CharacterStats.TakeDamage).
    bool BlockOnly(StatType stat) => itemType == ItemType.Shield && stat == StatType.Armor;

    static bool Worse(StatType stat, float value) => value < 0;

    // The value a modifier has on the plain (unhoned) item.
    float PlainValue(StatModifierEntry modifier)
    {
        if (BaseItem.statModifiers != null)
            foreach (var m in BaseItem.statModifiers)
                if (m != null && m.stat == modifier.stat && m.type == modifier.type) return m.value;
        return modifier.value;
    }

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
    }

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
