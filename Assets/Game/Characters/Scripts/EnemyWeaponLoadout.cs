using Sirenix.OdinInspector;
using UnityEngine;

// Shared third-person grip, independent of the player's first-person item offsets.
[DisallowMultipleComponent]
public class EnemyWeaponLoadout : MonoBehaviour
{
    public bool hasWeapon = true;
    [Tooltip("Item database entry. Uses its World Prefab, never its pickup pouch.")]
    public ItemData weapon;
    [Tooltip("Shared by compatible humanoid enemies; adjust once for the entire rig family.")]
    public EnemyWeaponGrip grip;
    [SerializeField] Animator animator;
    [SerializeField, HideInInspector] Transform attachment;
    [SerializeField, HideInInspector] GameObject visual;
    ItemData appliedWeapon;
    bool appliedEnabled;
    public Transform Attachment => attachment;
    public GameObject Visual => visual;

    void Reset()
    {
        animator = GetComponentInChildren<Animator>(true);
        grip = Resources.Load<EnemyWeaponGrip>("Synty humanoid weapon grip");
    }
    void Start() => RefreshWeapon();
    void LateUpdate()
    {
        if (appliedEnabled != hasWeapon || appliedWeapon != weapon) RefreshWeapon();
        ApplyGrip();
    }
    void ApplyGrip()
    {
        if (attachment == null || grip == null) return;
        attachment.SetLocalPositionAndRotation(grip.position, Quaternion.Euler(grip.rotation));
        attachment.localScale = Vector3.one * Mathf.Max(.01f, grip.scale);
    }
    [Button("Refresh weapon preview")]
    public void RefreshWeapon()
    {
        appliedWeapon = weapon; appliedEnabled = hasWeapon;
        if (visual != null)
        {
            visual.SetActive(false);
            if (Application.isPlaying) Destroy(visual); else DestroyImmediate(visual);
            visual = null;
        }
        if (!hasWeapon || weapon == null || weapon.worldPrefab == null) return;
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (grip == null) grip = Resources.Load<EnemyWeaponGrip>("Synty humanoid weapon grip");
        if (animator == null || !animator.isHuman || grip == null)
        { Debug.LogWarning("Enemy weapon requires a humanoid Animator and a shared grip profile.", this); return; }
        var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        if (hand == null) return;
        if (attachment == null)
        {
            attachment = new GameObject("Weapon grip - right hand").transform;
            attachment.SetParent(hand, false);
        }
        else if (attachment.parent != hand) attachment.SetParent(hand, false);
        ApplyGrip();
        visual = Instantiate(weapon.worldPrefab, attachment, false);
        visual.name = "Equipped weapon - " + weapon.itemName;
        visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        foreach (var collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (var body in visual.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.detectCollisions = false; }
    }
}
