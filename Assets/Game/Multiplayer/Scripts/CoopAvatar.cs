using TMPro;
using UnityEngine;

// What the other players see of someone: in the room their room body, on the table their class's body
// (AdventurerClass.coopBody) with the weapon and shield they hold, walking, swinging, blocking and dying as they do.
// Hidden on the owner's own machine. The body is a humanoid driven by the Humanoid controller, so any Synty character works.
public class CoopAvatar : MonoBehaviour
{
    [Tooltip("Name over the head (optional).")]
    [SerializeField] TMP_Text nameLabel;
    [SerializeField] CapsuleCollider body;
    [Tooltip("Height of the name over the feet, in metres of the world it is in.")]
    [SerializeField] float labelHeight = 2.15f;

    CoopPlayer player;
    GameObject model;
    Animator animator;
    CoopPlace builtPlace;
    string builtClass;
    float builtScale = 1f;
    string heldRight, heldLeft;
    GameObject rightVisual, leftVisual;
    Vector3 lastPosition;
    Vector3 velocity;
    bool shownDead;
    Light carried;

    static readonly int MoveX = Animator.StringToHash("MoveX"), MoveZ = Animator.StringToHash("MoveZ"), Speed = Animator.StringToHash("Speed"),
        MotionSpeed = Animator.StringToHash("MotionSpeed"), Grounded = Animator.StringToHash("Grounded"), HoldingBow = Animator.StringToHash("HoldingBow"),
        AttackSpeed = Animator.StringToHash("AttackSpeed"), HurtSpeed = Animator.StringToHash("HurtSpeed"), Dead = Animator.StringToHash("Dead");

    public Animator Animator => animator;
    // A dead player watching this one sees its light brighter and wider (MultiplayerManager.spectatorLightBoost).
    public float LightBoost { get; set; } = 1f;

    public void Bind(CoopPlayer owner)
    {
        player = owner;
        lastPosition = transform.position;
        bool remote = !owner.IsOwner;
        if (body != null) body.enabled = remote;
        if (nameLabel != null) nameLabel.gameObject.SetActive(remote);
        if (!remote) return;
        Rebuild(true);
    }

    public void Unbind()
    {
        if (model != null) Destroy(model);
        model = null;
    }

    void LateUpdate()
    {
        if (player == null || player.IsOwner || !player.IsSpawned) return;
        var s = player.Current;
        Rebuild(false);
        if (nameLabel != null)
        {
            nameLabel.text = player.DisplayName;
            nameLabel.transform.position = transform.position + Vector3.up * labelHeight * builtScale;
            nameLabel.transform.localScale = Vector3.one * builtScale;
            var cam = PlayerManager.HasInstance ? PlayerManager.Instance.OutputCamera : null;
            if (cam != null) nameLabel.transform.rotation = Quaternion.LookRotation(nameLabel.transform.position - cam.transform.position);
        }
        if (body != null)
        {
            body.height = 1.8f * builtScale; body.radius = .35f * builtScale; body.center = Vector3.up * .9f * builtScale;
            body.enabled = s.place == CoopPlace.Room || s.alive;
        }
        if (animator == null) return;

        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        var moved = (transform.position - lastPosition) / dt;
        lastPosition = transform.position;
        // A warp (a new floor, sitting down) is not a sprint.
        if (moved.magnitude > 30f * builtScale) moved = Vector3.zero;
        velocity = Vector3.Lerp(velocity, moved, 1f - Mathf.Exp(-12f * dt));
        var flat = velocity; flat.y = 0;
        var local = transform.InverseTransformDirection(flat) / builtScale;
        animator.SetFloat(MoveX, local.x, .08f, dt);
        animator.SetFloat(MoveZ, local.z, .08f, dt);
        animator.SetFloat(Speed, local.magnitude, .08f, dt);
        animator.SetFloat(MotionSpeed, local.magnitude > .1f ? 1f : 0f);
        animator.SetBool(Grounded, true);
        animator.SetFloat(AttackSpeed, 1.35f);
        animator.SetFloat(HurtSpeed, 1.3f);
        animator.SetBool(HoldingBow, s.place == CoopPlace.Table && s.blocking);

        bool dead = s.place == CoopPlace.Table && s.inRun && !s.alive;
        if (dead != shownDead)
        {
            shownDead = dead;
            animator.SetBool(Dead, dead);
            if (dead) animator.SetTrigger("Death");
        }
        if (s.place == CoopPlace.Table) UpdateHeld(s.right.ToString(), s.left.ToString());
        UpdateLight(s);
        // The shield turns to face the front as it comes up.
        if (leftVisual != null && MultiplayerManager.Instance.shieldGrip != null)
        {
            var socket = leftVisual.transform.parent;
            var rest = Quaternion.Euler(MultiplayerManager.Instance.shieldGrip.rotation);
            var up = Quaternion.Euler(MultiplayerManager.Instance.shieldBlockRotation);
            socket.localRotation = Quaternion.Slerp(socket.localRotation, s.blocking ? up : rest, 1f - Mathf.Exp(-14f * dt));
        }
    }

    // A teammate on the table carries a small light of their own, so they can be seen in the dark.
    void UpdateLight(CoopPlayerState s)
    {
        var manager = MultiplayerManager.Instance;
        bool on = s.place == CoopPlace.Table && s.alive && manager.partnerLightIntensity > 0;
        if (on && carried == null)
        {
            carried = new GameObject("Carried light").AddComponent<Light>();
            carried.transform.SetParent(transform, false);
            carried.transform.localPosition = new Vector3(0f, 1.7f, .25f);
            carried.type = LightType.Point;
            carried.shadows = LightShadows.None;
        }
        if (carried == null) return;
        carried.enabled = on;
        carried.color = manager.partnerLightColor;
        carried.intensity = manager.partnerLightIntensity * LightBoost;
        carried.range = manager.partnerLightRange * Mathf.Sqrt(LightBoost) * (LightBoost > 1f ? 1.5f : 1f);
    }

    // A new body when the player changes world or class.
    void Rebuild(bool force)
    {
        if (!MultiplayerManager.HasInstance) return;
        var s = player.Current;
        string cls = s.classId.ToString();
        if (!force && model != null && builtPlace == s.place && (s.place == CoopPlace.Room || builtClass == cls)) return;
        // Leaving the table dead: the body stays where it fell, for the floor.
        if (model != null && builtPlace == CoopPlace.Table && s.place == CoopPlace.Room && shownDead) LeaveCorpse();
        else if (model != null) Destroy(model);
        model = null; animator = null; rightVisual = leftVisual = null; heldRight = heldLeft = null; shownDead = false;

        var manager = MultiplayerManager.Instance;
        var prefab = s.place == CoopPlace.Room ? manager.RoomBody(player.SlotIndex) : manager.TableBody(cls);
        builtPlace = s.place; builtClass = cls;
        builtScale = s.place == CoopPlace.Room ? manager.roomScale : 1f;
        if (prefab == null) return;
        model = Instantiate(prefab, transform, false);
        model.name = "Body";
        model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        foreach (var c in model.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        // The room player's own body is on a layer its camera skips; someone else's is seen like anything else,
        // and in the room it is lit as the room is (rendering layer Room).
        foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
        if (s.place == CoopPlace.Room)
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.renderingLayerMask |= LightingManager.RoomLayer;
        animator = model.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            if (manager.bodyController != null) animator.runtimeAnimatorController = manager.bodyController;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
        lastPosition = transform.position;
    }

    void LeaveCorpse()
    {
        var floor = TableManager.Loader != null && TableManager.Loader.Dungeon != null ? TableManager.Loader.Dungeon.transform : null;
        if (floor == null) { Destroy(model); return; }
        model.transform.SetParent(floor, true);
        model.name = $"{player.DisplayName}'s body";
    }

    void UpdateHeld(string right, string left)
    {
        if (animator == null || !animator.isHuman) return;
        if (right != heldRight) { heldRight = right; Hold(ref rightVisual, right, HumanBodyBones.RightHand, MultiplayerManager.Instance.weaponGrip); }
        if (left != heldLeft) { heldLeft = left; Hold(ref leftVisual, left, HumanBodyBones.LeftHand, MultiplayerManager.Instance.shieldGrip); }
    }

    void Hold(ref GameObject visual, string saveId, HumanBodyBones bone, EnemyWeaponGrip grip)
    {
        if (visual != null) Destroy(visual.transform.parent.gameObject);
        visual = null;
        var item = Coop.Item(saveId);
        if (item == null || item.worldPrefab == null || (item.itemType != ItemType.Weapon && item.itemType != ItemType.Shield)) return;
        var hand = animator.GetBoneTransform(bone);
        if (hand == null) return;
        var socket = new GameObject($"Grip - {bone}").transform;
        socket.SetParent(hand, false);
        if (grip != null) { socket.SetLocalPositionAndRotation(grip.position, Quaternion.Euler(grip.rotation)); socket.localScale = Vector3.one * Mathf.Max(.01f, grip.scale); }
        visual = Instantiate(item.worldPrefab, socket, false);
        visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        foreach (var c in visual.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (var b in visual.GetComponentsInChildren<Rigidbody>(true)) { b.isKinematic = true; b.detectCollisions = false; }
        foreach (var m in visual.GetComponentsInChildren<MonoBehaviour>(true)) m.enabled = false;
    }

    // The swing and its whoosh (the held weapon's own, or the game's default).
    public void Swing(bool heavy)
    {
        if (animator != null) animator.SetTrigger("Attack");
        if (!AudioManager.HasInstance || player == null) return;
        var weapon = Coop.Item(player.Current.right.ToString());
        var profile = weapon != null ? weapon.weapon : null;
        AudioData audio = profile == null || profile.useDefaultEffects
            ? (CombatManager.HasInstance ? (heavy ? CombatManager.Instance.defaultHeavySwingAudio ?? CombatManager.Instance.defaultSwingAudio : CombatManager.Instance.defaultSwingAudio) : null)
            : heavy && profile.heavySwingAudio != null ? profile.heavySwingAudio : profile.swingAudio;
        if (audio != null) AudioManager.Instance.PlaySFXData(audio, transform.position + Vector3.up * 1.2f * builtScale);
    }
    public void Cast() { if (animator != null) animator.SetTrigger("CastSpell"); }
    public void Hurt() { if (animator != null && player != null && player.Current.alive) animator.SetTrigger("Hurt"); }
}
