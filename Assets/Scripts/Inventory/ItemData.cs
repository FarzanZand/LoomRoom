using Sirenix.OdinInspector;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public enum ItemType { Generic, Weapon, Shield, Tool, Consumable, Key }

[CreateAssetMenu(fileName = "NewItem", menuName = "Inventory/Item")]
public class ItemData : ScriptableObject
{
    public string itemName;
    [TextArea] public string description;
    public Sprite icon;
    [HorizontalGroup("PrefabRow"), HideLabel]
    public GameObject worldPrefab;

    [HorizontalGroup("PrefabRow", Width = 80)]
    [Button("Generate")]
    private void GeneratePrefab()
    {
#if UNITY_EDITOR
        if (string.IsNullOrEmpty(itemName))
        {
            EditorUtility.DisplayDialog("Generate Prefab", "Set the Item Name before generating.", "OK");
            return;
        }

        const string basePrefabPath = "Assets/Items/Prefabs/_ItemPickup.prefab";
        const string prefabFolder = "Assets/Items/Prefabs/";

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

            // Resize interaction sphere and add physics capsule
            Renderer[] renderers = meshInstance.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(instance.transform.position, Vector3.one);
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            Vector3 localCenter = instance.transform.InverseTransformPoint(bounds.center);

            SphereCollider detection = instance.GetComponent<SphereCollider>();
            if (detection != null)
                detection.radius = Mathf.Max(2f, Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z)) + 0.25f;

            CapsuleCollider capsule = instance.AddComponent<CapsuleCollider>();
            capsule.center = localCenter;
            capsule.radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
            capsule.height = bounds.size.y;
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
    public ItemType itemType;
    

    public bool canBeEquipped = false;

    [BoxGroup("Stacking")]
    public int maxStackSize = 1;

    [BoxGroup("Equipment"), ShowIf("canBeEquipped")]
    public bool equipOnPickup = false;

    [BoxGroup("Equipment"), ShowIf("canBeEquipped")]
    public EquipmentSlot equipSlot = EquipmentSlot.RightHand;

    [BoxGroup("Equipment"), ShowIf("IsWeapon")]
    public float attackRange = 0.8f;

    [BoxGroup("Equipment"), ShowIf("IsWeapon")]
    public float attackDamage = 10f;

    [BoxGroup("Equipment"), ShowIf("IsWeapon")]
    public AudioClip hitSound;

    [BoxGroup("Equipment"), ShowIf("IsWeapon")]
    public GameObject hitParticlePrefab;

    [BoxGroup("Consumable"), ShowIf("IsConsumable")]
    public ItemEffect itemEffect;

    [BoxGroup("Consumable"), ShowIf("ShowEffectValue")]
    public float effectValue;

    [BoxGroup("Consumable"), ShowIf("ShowCustomEffect")]
    public ItemEffectBase customEffect;
    [ShowIf("IsConsumable")] public AudioClip consumeAudio;
    [ShowIf("IsConsumable")] public float consumeAudioVolume;

    private bool IsConsumable => itemType == ItemType.Consumable;
    private bool IsWeapon => itemType == ItemType.Weapon;
    private bool ShowEffectValue => IsConsumable && itemEffect != ItemEffect.Custom;
    private bool ShowCustomEffect => IsConsumable && itemEffect == ItemEffect.Custom;

    public void UseEffect()
    {
        if (itemType != ItemType.Consumable) return;

        if(consumeAudio != null)
            AudioManager.Instance.PlaySFX2D(consumeAudio, consumeAudioVolume);
        switch (itemEffect)
        {
            case ItemEffect.Heal:
                Debug.Log($"healed for {effectValue} health");
                break;
            case ItemEffect.Feed:
                Debug.Log($"{itemName} eaten.");
                break;
            case ItemEffect.Custom:
                customEffect?.Apply(this);
                break;
        }
    }
}
