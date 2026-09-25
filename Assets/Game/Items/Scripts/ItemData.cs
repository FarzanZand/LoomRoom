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
    [Tooltip("Used only when consuming equipped items from the hand. Idle keeps the original behavior; Eat moves the hand and item toward the mouth.")]
    public ItemUseAnimation AnimationOnUse = ItemUseAnimation.Idle;

    [BoxGroup("Consumable"), ShowIf("UsesEatAnimation"), Min(.1f)]
    public float eatDuration = .65f;
    [BoxGroup("Consumable"), ShowIf("UsesEatAnimation")]
    [Tooltip("Mouth position relative to the first-person camera.")]
    public Vector3 eatMouthPosition = new Vector3(0, -.12f, .13f);
    [BoxGroup("Consumable"), ShowIf("UsesEatAnimation")]
    public Vector3 eatMouthRotation = new Vector3(-65, 0, 0);

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

    public bool PlayPickupOverride()
    {
        if (!AudioManager.HasInstance || pickupAudioSource == ItemAudioSource.AudioData) return false;
        if (PickupUsesClip && pickupClip != null)
        {
            AudioManager.Instance.PlaySFX2D(pickupClip, Mathf.Clamp01(pickupClipVolume));
            return true;
        }
        if (PickupUsesKey && !string.IsNullOrWhiteSpace(pickupAudioKey))
            return AudioManager.Instance.PlaySFX2D(pickupAudioKey.Trim()) != null;
        return false;
    }

    // Consume: fire OnUse effects for the user.
    public void Use(Character user)
    {
        if (!IsConsumable) return;
        if (AudioManager.HasInstance)
        {
            if (UseUsesData && useAudio != null) AudioManager.Instance.PlaySFXData2D(useAudio);
            else if (UseUsesClip && useClip != null) AudioManager.Instance.PlaySFX2D(useClip, Mathf.Clamp01(useClipVolume));
            else if (UseUsesKey && !string.IsNullOrWhiteSpace(useAudioKey)) AudioManager.Instance.PlaySFX2D(useAudioKey.Trim());
        }
        ItemEffectProcessor.Fire(this, EffectTrigger.OnUse, EffectContext.For(user, this));
    }

    // Rich-text tooltip body shared by the in-game tooltip and the Item Database preview.
    // The comparison with equipped gear and the hint appear only while a player exists.
    public string BuildTooltip()
    {
        var body = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(description))
            body.Append("<size=18><color=#B7BABF>").Append(description.Trim()).Append("</color></size>");
        if (canBeEquipped && statModifiers != null)
            foreach (var modifier in statModifiers)
            {
                if (modifier == null) continue;
                if (body.Length > 0) body.Append("\n\n");
                string line = modifier.Describe();
                int split = line.IndexOf(' ');
                body.Append("<color=").Append(modifier.value < 0 ? "#E78787>" : "#80CEA0>")
                    .Append(line.Substring(0, split)).Append("</color>").Append(line.Substring(split));
            }
        if (effects != null)
            foreach (var effect in effects)
            {
                string line = effect?.Describe();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (body.Length > 0) body.Append("\n\n");
                body.Append("<color=#A1C5DE>").Append(line).Append("</color>");
            }
        if (itemType == ItemType.Shield) body.Append("\n\nArmor applies only to frontal hits while blocking. Guarding and blocked hits consume stamina.");
        if (canBeEquipped && PlayerManager.HasInstance)
        {
            var equipped = PlayerManager.Instance.Active?.Equipment?.Get(equipSlot);
            if (!IsConsumable)
                foreach (var stat in new[] { StatType.AttackDamage, StatType.Armor })
                {
                    float delta = FlatBonus(this, stat) - FlatBonus(equipped, stat);
                    if (Mathf.Abs(delta) > .001f)
                        body.Append($"\n\n<color={(delta > 0 ? "#80CEA0" : "#E78787")}>{delta:+0.#;-0.#} {StatModifierEntry.Label(stat)}</color> vs equipped");
                }
            body.Append(IsConsumable ? "\n\n<size=17>Equip, close inventory, then use from your hand.</size>" : "\n\n<size=17>Equip from inventory or drag to its equipment slot.</size>");
        }
        return body.ToString();
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
