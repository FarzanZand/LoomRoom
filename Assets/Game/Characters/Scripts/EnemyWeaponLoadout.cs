using Sirenix.OdinInspector;
using UnityEngine;

// Shared third-person grip, independent of the player's first-person item offsets.
// Archers (EnemyData.archer) hold their bow in the left hand instead of a weapon in the right,
// and show an arrow in the right hand while the bow is drawn.
[DisallowMultipleComponent]
public class EnemyWeaponLoadout : MonoBehaviour
{
    public bool hasWeapon = true;
    [Tooltip("Item database entry. Uses its World Prefab, never its pickup pouch.")]
    public ItemData weapon;
    [Tooltip("Shared by compatible humanoid enemies; adjust once for the entire rig family.")]
    public EnemyWeaponGrip grip;
    [Tooltip("Archers: the bow's pose on the left-hand bone. Shared like the weapon grip.")]
    public EnemyWeaponGrip bowGrip;
    [Tooltip("Archers: the drawn arrow's pose on the right-hand bone.")]
    public EnemyWeaponGrip arrowGrip;
    [SerializeField] Animator animator;
    [SerializeField, HideInInspector] Transform attachment;
    [SerializeField, HideInInspector] GameObject visual;
    [SerializeField, HideInInspector] Transform bowAttachment;
    [SerializeField, HideInInspector] Transform arrowAttachment;
    ItemData appliedWeapon;
    bool appliedEnabled;
    GameObject appliedBow;
    Character character;
    public Transform Attachment => attachment;
    public GameObject Visual => visual;

    EnemyArchery Archery
    {
        get
        {
            if (character == null) character = GetComponent<Character>();
            return character != null && character.data is EnemyData { archer: true } data ? data.archery : null;
        }
    }
    GameObject WantedBow => Archery?.bow;

    // Where a loosed arrow starts: the bow hand, or chest height in front of the archer.
    public Vector3 ArrowOrigin => bowAttachment != null && bowAttachment.childCount > 0
        ? bowAttachment.position
        : transform.position + Vector3.up * 1.45f + transform.forward * .3f;

    void Reset()
    {
        animator = GetComponentInChildren<Animator>(true);
        LoadDefaultGrips();
    }
    void Start() => RefreshWeapon();
    void LateUpdate()
    {
        if (appliedEnabled != hasWeapon || appliedWeapon != weapon || appliedBow != WantedBow) RefreshWeapon();
        ApplyGrip(attachment, grip);
        ApplyGrip(bowAttachment, bowGrip);
        ApplyGrip(arrowAttachment, arrowGrip);
    }
    void LoadDefaultGrips()
    {
        if (grip == null) grip = Resources.Load<EnemyWeaponGrip>("Synty humanoid weapon grip");
        if (bowGrip == null) bowGrip = Resources.Load<EnemyWeaponGrip>("Synty humanoid bow grip");
        if (arrowGrip == null) arrowGrip = Resources.Load<EnemyWeaponGrip>("Synty humanoid arrow grip");
    }
    static void ApplyGrip(Transform target, EnemyWeaponGrip pose)
    {
        if (target == null || pose == null) return;
        target.SetLocalPositionAndRotation(pose.position, Quaternion.Euler(pose.rotation));
        target.localScale = Vector3.one * Mathf.Max(.01f, pose.scale);
    }

    // Shown from the draw until the arrow is loosed (EnemyAttackRunner).
    public void SetArrowNocked(bool nocked)
    {
        if (arrowAttachment != null) arrowAttachment.gameObject.SetActive(nocked);
    }

    [Button("Refresh weapon preview")]
    public void RefreshWeapon()
    {
        appliedWeapon = weapon; appliedEnabled = hasWeapon;
        var archery = Archery;
        appliedBow = archery?.bow;
        Clear(ref visual);
        ClearChildren(bowAttachment);
        ClearChildren(arrowAttachment);

        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        LoadDefaultGrips();
        if (animator == null || !animator.isHuman || grip == null)
        { Debug.LogWarning("Enemy weapon requires a humanoid Animator and a shared grip profile.", this); return; }

        if (archery != null)
        {
            // A bow needs both hands: no weapon in the right.
            if (archery.bow != null) Spawn(archery.bow, ref bowAttachment, HumanBodyBones.LeftHand, bowGrip, "Bow grip - left hand", "Equipped bow");
            if (archery.arrow != null)
            {
                var arrow = Spawn(archery.arrow.gameObject, ref arrowAttachment, HumanBodyBones.RightHand, arrowGrip, "Arrow grip - right hand", "Nocked arrow");
                if (arrow != null)
                {
                    var flight = arrow.GetComponent<ArrowProjectile>();
                    if (Application.isPlaying) Destroy(flight); else DestroyImmediate(flight);
                }
            }
            SetArrowNocked(false);
            return;
        }

        if (!hasWeapon || weapon == null || weapon.worldPrefab == null) return;
        visual = Spawn(weapon.worldPrefab, ref attachment, HumanBodyBones.RightHand, grip, "Weapon grip - right hand", "Equipped weapon - " + weapon.itemName);
    }

    GameObject Spawn(GameObject prefab, ref Transform socket, HumanBodyBones bone, EnemyWeaponGrip pose, string socketName, string visualName)
    {
        var hand = animator.GetBoneTransform(bone);
        if (hand == null) return null;
        if (socket == null)
        {
            socket = new GameObject(socketName).transform;
            socket.SetParent(hand, false);
        }
        else if (socket.parent != hand) socket.SetParent(hand, false);
        socket.gameObject.SetActive(true);
        ApplyGrip(socket, pose);
        var spawned = Instantiate(prefab, socket, false);
        spawned.name = visualName;
        spawned.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        foreach (var collider in spawned.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (var body in spawned.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.detectCollisions = false; }
        // Edit-mode previews are never saved into the prefab: the weapon is spawned at runtime,
        // and a baked copy would be inherited by every variant of this enemy.
        if (!Application.isPlaying)
        {
            socket.gameObject.hideFlags = HideFlags.DontSave;
            foreach (var t in spawned.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
        }
        return spawned;
    }

    static void Clear(ref GameObject go)
    {
        if (go == null) return;
        go.SetActive(false);
        if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        go = null;
    }

    static void ClearChildren(Transform socket)
    {
        if (socket == null) return;
        for (int i = socket.childCount - 1; i >= 0; i--)
        {
            var child = socket.GetChild(i).gameObject;
            Clear(ref child);
        }
    }
}
