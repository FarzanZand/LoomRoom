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

    public ItemType itemType = ItemType.Generic;

    [Min(1)] public int maxStackSize = 1;

    [Tooltip("Sound when picked up. Empty = InventoryManager default.")]
    public AudioData pickupAudio;

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
    [Tooltip("Sound when consumed.")]
    public AudioData useAudio;

    // ── Queries ───────────────────────────────────────────────────────

    public bool IsWeapon     => itemType == ItemType.Weapon;
    public bool IsConsumable => itemType == ItemType.Consumable;
    public bool IsEquippable => canBeEquipped;

    // Consume: fire OnUse effects for the user.
    public void Use(Character user)
    {
        if (!IsConsumable) return;
        if (useAudio != null && AudioManager.HasInstance)
            AudioManager.Instance.PlaySFXData2D(useAudio);
        ItemEffectProcessor.Fire(this, EffectTrigger.OnUse, EffectContext.For(user, this));
    }

    // Tooltip body: stat lines then effect lines.
    public string BuildTooltip()
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(description)) sb.AppendLine(description);

        if (canBeEquipped && statModifiers != null)
            foreach (var m in statModifiers)
                sb.AppendLine(m.Describe());

        if (effects != null)
            foreach (var e in effects)
            {
                string line = e.Describe();
                if (!string.IsNullOrEmpty(line)) sb.AppendLine(line);
            }

        return sb.ToString().TrimEnd();
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

        const string basePrefabPath = "Assets/Game/Items/Content/Prefabs/_ItemPickup.prefab";
        const string prefabFolder   = "Assets/Game/Items/Content/Prefabs/";

        GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath);
        if (basePrefab == null) { Debug.LogError($"[ItemData] Base prefab not found at {basePrefabPath}"); return; }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
        instance.name = itemName;

        if (worldPrefab != null)
        {
            GameObject meshInstance = (GameObject)PrefabUtility.InstantiatePrefab(worldPrefab);
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

        string variantPath = prefabFolder + itemName + ".prefab";
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
