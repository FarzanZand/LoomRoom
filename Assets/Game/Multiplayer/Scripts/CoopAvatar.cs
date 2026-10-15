using TMPro;
using UnityEngine;

// What the other players see of someone: in the room their room body; on the table, alive, the one body (a copy of the
// local table player's OneBodyRig body, the same rig and FirstPersonTable controller, playing the owner's own arm states
// and parameters as CoopPlayer sends them, bending with their look); dead or a ghost their class's body
// (AdventurerClass.coopBody, the Humanoid controller) as before, with the weapon and shield they hold.
// Hidden on the owner's own machine. The body is a humanoid driven by the Humanoid controller, so any Synty character works.
public class CoopAvatar : MonoBehaviour
{
    [Tooltip("Name over the head (optional).")]
    [SerializeField] TMP_Text nameLabel;
    [SerializeField] CapsuleCollider body;
    [Tooltip("Height of the name over the feet, in metres of the world it is in.")]
    [SerializeField] float labelHeight = 2.15f;
    [Tooltip("Metres (at the body's own scale) up to which the name label shrinks with distance like the body; further it keeps its size on screen.")]
    [SerializeField] float labelFullSize = 4f;
    static readonly int ZTestId = Shader.PropertyToID("unity_GUIZTestMode");
    bool labelOnTop;

    CoopPlayer player;
    GameObject model;
    Animator animator;
    CoopPlace builtPlace;
    string builtClass;
    float builtScale = 1f;
    string heldRight, heldLeft;
    GameObject rightVisual, leftVisual;
    GameObject usedVisual;
    float useEnds;
    Vector3 lastPosition;
    Vector3 velocity;
    bool shownDead, shownSeated;
    bool builtGhost, builtOneBody;
    OneBodyRig rig;
    Transform rigRight, rigLeft, rigSpell;
    readonly int[] shownStates = new int[15];
    float targetPitch;
    Vector3 lastSolid;
    float solidAfter;
    Light carried;

    static readonly int MoveX = Animator.StringToHash("MoveX"), MoveZ = Animator.StringToHash("MoveZ"), Speed = Animator.StringToHash("Speed"),
        MotionSpeed = Animator.StringToHash("MotionSpeed"), Grounded = Animator.StringToHash("Grounded"), Crouch = Animator.StringToHash("Crouch"),
        Charge = Animator.StringToHash("Charge"), Armed = Animator.StringToHash("Armed"), LocoSpeed = Animator.StringToHash("LocoSpeed"), Block = Animator.StringToHash("Block"), HurtSpeed = Animator.StringToHash("HurtSpeed"), UseSpeed = Animator.StringToHash("UseSpeed"), Dead = Animator.StringToHash("Dead");

    public Animator Animator => animator;
    // How fast the locomotion clips step, in metres a second (measured from the foot's travel).
    const float WalkClip = 1.8f, RunClip = 2.9f, CrouchClip = .8f;
    // How much brighter and wider this teammate's carried light is drawn (1: as it is).
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
        // Still in the lobby (joined, waiting for the host's Begin): not in the room yet.
        if (model != null && model.activeSelf == s.inLobby) model.SetActive(!s.inLobby);
        if (nameLabel != null && nameLabel.gameObject.activeSelf == s.inLobby) nameLabel.gameObject.SetActive(!s.inLobby);
        // A ghost drifts a little off the floor.
        if (model != null && builtGhost) model.transform.localPosition = Vector3.up * (.18f + Mathf.Sin(Time.time * 1.7f) * .06f);
        if (usedVisual != null && Time.time >= useEnds) EndUse();
        if (nameLabel != null)
        {
            // From the table a player in the room is a giant outside the board: no label over the walls for them.
            bool shown = !(s.place == CoopPlace.Room && PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table);
            if (nameLabel.enabled != shown) nameLabel.enabled = shown;
            if (nameLabel.text != player.DisplayName) nameLabel.text = player.DisplayName;
            nameLabel.transform.position = transform.position + Vector3.up * labelHeight * builtScale;
            var cam = PlayerManager.HasInstance ? PlayerManager.Instance.OutputCamera : null;
            // Drawn over walls so teammates can find each other; past labelFullSize metres it keeps its size on
            // screen (instead of shrinking away) and dims a little.
            if (!labelOnTop) { var m = nameLabel.fontMaterial; m.SetFloat(ZTestId, (float)UnityEngine.Rendering.CompareFunction.Always); m.renderQueue = 4000; labelOnTop = true; }
            float far = cam != null ? Vector3.Distance(cam.transform.position, nameLabel.transform.position) / (labelFullSize * builtScale) : 1f;
            nameLabel.transform.localScale = Vector3.one * builtScale * Mathf.Max(1f, far);
            nameLabel.alpha = far > 1f ? Mathf.Lerp(1f, .65f, Mathf.InverseLerp(1f, 4f, far)) : 1f;
            if (cam != null) nameLabel.transform.rotation = Quaternion.LookRotation(nameLabel.transform.position - cam.transform.position);
        }
        // A jump (room to table, a new floor): the body is not solid for a moment, so it never lands inside
        // someone and shoves them through a floor.
        // Only a body on the table is solid (enemies strike it, players bump it); a room-sized one never is, as its
        // place and position can arrive a moment apart and a giant capsule over the board would shove players through it.
        if ((transform.position - lastSolid).sqrMagnitude > 16f) solidAfter = Time.time + 1.5f;
        lastSolid = transform.position;
        if (body != null)
        {
            body.height = 1.8f; body.radius = .35f; body.center = Vector3.up * .9f;
            body.enabled = s.place == CoopPlace.Table && builtPlace == CoopPlace.Table && s.alive && Time.time >= solidAfter;
        }
        if (animator == null) return;
        if (builtOneBody)
        {
            if (rig != null) rig.remotePitch = Mathf.LerpAngle(rig.remotePitch, targetPitch, 1f - Mathf.Exp(-12f * Time.deltaTime));
            UpdateHeld(s.right.ToString(), s.left.ToString());
            UpdateLight(s);
            return;
        }

        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        var moved = (transform.position - lastPosition) / dt;
        lastPosition = transform.position;
        // A warp (a new floor, sitting down) is not a sprint.
        if (moved.magnitude > 30f * builtScale) moved = Vector3.zero;
        velocity = Vector3.Lerp(velocity, moved, 1f - Mathf.Exp(-12f * dt));
        var flat = velocity; flat.y = 0;
        var local = transform.InverseTransformDirection(flat) / builtScale;
        // The clips step at their own pace (walk about 1.8 m/s, run 2.9, crouched walk 0.8; players walk 3.6, sprint 6
        // and sneak 1.75): blend by speed up to the run, then play faster so the feet keep up (to a limit).
        float speed = local.magnitude;
        float blend = speed <= WalkClip ? .9f * speed / WalkClip : speed <= RunClip ? Mathf.Lerp(.9f, 2.6f, (speed - WalkClip) / (RunClip - WalkClip)) : 2.6f;
        var dir = speed > .01f ? local / speed : Vector3.zero;
        bool crouched = s.crouching;
        float pace = crouched ? Mathf.Max(1f, speed / CrouchClip) : Mathf.Max(1f, speed / RunClip);
        animator.SetFloat(MoveX, dir.x * blend, .08f, dt);
        animator.SetFloat(MoveZ, dir.z * blend, .08f, dt);
        animator.SetFloat(Speed, crouched ? Mathf.Min(1.2f, 1.2f * speed / CrouchClip) : speed, .08f, dt);
        animator.SetFloat(LocoSpeed, Mathf.Min(2.2f, pace), .15f, dt);
        animator.SetFloat(MotionSpeed, local.magnitude > .1f ? 1f : 0f);
        // What the player does, as they do it (the body's own controller, Build Body Animations).
        bool onTable = s.place == CoopPlace.Table;
        animator.SetBool(Grounded, !s.airborne);
        animator.SetBool(Crouch, s.crouching);
        animator.SetBool(Armed, onTable && s.alive && !builtGhost);
        animator.SetBool(Charge, onTable && s.charging);
        animator.SetBool(Block, onTable && s.blocking);
        animator.SetFloat(HurtSpeed, 1.3f);

        // In a chair (the intro): the seated pose, and up again after.
        if (s.seated != shownSeated && animator != null)
        {
            shownSeated = s.seated;
            animator.SetTrigger(s.seated ? "Sit" : "Stand");
        }

        bool dead = s.place == CoopPlace.Table && s.inRun && !s.alive && !builtGhost;
        if (dead != shownDead)
        {
            shownDead = dead;
            animator.SetBool(Dead, dead);
            if (dead) animator.SetTrigger("Death");
            // Alive again (revived on a new floor; for a moment the body can be shown dead as the ghost ends): the
            // death pose has no way out, so the body starts over from standing.
            else animator.Rebind();
        }
        // A ghost's hands are empty.
        if (s.place == CoopPlace.Table) UpdateHeld(builtGhost ? "" : s.right.ToString(), builtGhost ? "" : s.left.ToString());
        UpdateLight(s);
        // The shield turns to face the front as it comes up.
        if (leftVisual != null && MultiplayerManager.Instance.shieldGrip != null)
        {
            var socket = leftVisual.transform.parent;
            var rest = Quaternion.Euler(MultiplayerManager.Instance.shieldGrip.rotation);
            var up = rest * Quaternion.Euler(MultiplayerManager.Instance.shieldBlockTurn);
            socket.localRotation = Quaternion.Slerp(socket.localRotation, s.blocking ? up : rest, 1f - Mathf.Exp(-14f * dt));
        }
    }

    // A teammate on the table carries a small light of their own, so they can be seen in the dark.
    void UpdateLight(CoopPlayerState s)
    {
        var manager = MultiplayerManager.Instance;
        bool ghostLight = s.place == CoopPlace.Table && s.ghost;
        bool on = s.place == CoopPlace.Table && (s.alive || ghostLight) && manager.partnerLightIntensity > 0;
        if (on && carried == null)
        {
            carried = new GameObject("Carried light").AddComponent<Light>();
            carried.transform.SetParent(transform, false);
            // Above the head and a little ahead, as the player's own light: at the head it blew the helmet out white.
            carried.transform.localPosition = new Vector3(0f, 2.5f, .45f);
            carried.type = LightType.Point;
            carried.shadows = LightShadows.None;
        }
        if (carried == null) return;
        carried.enabled = on;
        carried.color = ghostLight ? manager.ghostLightColor : manager.partnerLightColor;
        carried.intensity = ghostLight ? manager.ghostLightIntensity : manager.partnerLightIntensity * LightBoost;
        carried.range = ghostLight ? manager.ghostLightRange : manager.partnerLightRange * Mathf.Sqrt(LightBoost) * (LightBoost > 1f ? 1.5f : 1f);
    }

    // A new body when the player changes world or class.
    void Rebuild(bool force)
    {
        if (!MultiplayerManager.HasInstance) return;
        var s = player.Current;
        string cls = s.classId.ToString();
        bool ghost = s.place == CoopPlace.Table && s.ghost;
        // Alive on the table: the one body (when the local table player has one to copy).
        bool oneBody = s.place == CoopPlace.Table && !ghost && s.alive && OneBodySource() != null;
        if (!force && model != null && builtPlace == s.place && builtGhost == ghost && builtOneBody == oneBody && (s.place == CoopPlace.Room || builtClass == cls)) return;
        // Leaving the table dead, or getting up as a ghost: the body stays where it fell, for the floor.
        if (model != null && builtPlace == CoopPlace.Table && shownDead && (s.place == CoopPlace.Room || ghost)) LeaveCorpse();
        else if (model != null) Destroy(model);
        model = null; animator = null; rightVisual = leftVisual = usedVisual = null; heldRight = heldLeft = null; shownDead = false; shownSeated = false;
        rig = null; rigRight = rigLeft = rigSpell = null; builtOneBody = false;
        System.Array.Clear(shownStates, 0, shownStates.Length);

        var manager = MultiplayerManager.Instance;
        builtPlace = s.place; builtClass = cls; builtGhost = ghost;
        if (oneBody) { BuildOneBody(); return; }
        var prefab = s.place == CoopPlace.Room ? manager.RoomBody(player.SlotIndex) : manager.TableBody(cls);
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
        // A ghost: the same body, pale and see-through, never casting a shadow.
        if (ghost && manager.ghostMaterial != null)
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = manager.ghostMaterial;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        animator = model.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            if (manager.bodyController != null) animator.runtimeAnimatorController = manager.bodyController;
            // The room bodies come from the room player's own model, whose Animator is off (it is never seen).
            animator.enabled = true;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
        lastPosition = transform.position;
    }

    // The local table player's one body, to copy for a teammate.
    static OneBodyRig OneBodySource()
    {
        var table = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        return table != null ? table.GetComponentInChildren<OneBodyRig>(true) : null;
    }

    // A copy of the one body, made while switched off so its rig never wakes up still tied to the local player's
    // first-person arms; it then follows this avatar, with no arms cut and no held copies of its own.
    void BuildOneBody()
    {
        var source = OneBodySource();
        var holder = new GameObject("one body holder");
        holder.SetActive(false);
        var copy = Instantiate(source.gameObject, holder.transform);
        rig = copy.GetComponent<OneBodyRig>();
        rig.player = null; rig.firstPersonArms = null; rig.follow = transform; rig.remotePitch = 0f;
        animator = rig.animator != null ? rig.animator : copy.GetComponentInChildren<Animator>(true);
        foreach (var t in copy.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "RightHand_Socket") rigRight = t;
            else if (t.name == "LeftHand_Socket") rigLeft = t;
            else if (t.name == "Spell Origin") rigSpell = t;
        }
        foreach (var socket in new[] { rigRight, rigLeft })
            if (socket != null) for (int i = socket.childCount - 1; i >= 0; i--) DestroyImmediate(socket.GetChild(i).gameObject);
        foreach (var c in copy.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (var t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
        copy.name = "Body";
        copy.transform.SetParent(transform, false);
        copy.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        Destroy(holder);
        model = copy;
        if (animator != null) { animator.enabled = true; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; }
        builtOneBody = true;
        lastPosition = transform.position;
    }

    // The owner's arm states and parameters (CoopPlayer.SendArms): parameters set as they are there, and a layer
    // whose state changed starts it here too.
    public void ApplyArms(in ArmsPose pose)
    {
        if (!builtOneBody || animator == null || !animator.isActiveAndEnabled) return;
        targetPitch = pose.pitch;
        var values = pose.values; var states = pose.states;
        int i = 0;
        foreach (var prm in animator.parameters)
        {
            if (prm.type == AnimatorControllerParameterType.Trigger) continue;
            if (i >= values.Length) break;
            float v = values[i++];
            if (prm.type == AnimatorControllerParameterType.Float) animator.SetFloat(prm.nameHash, v);
            else if (prm.type == AnimatorControllerParameterType.Int) animator.SetInteger(prm.nameHash, Mathf.RoundToInt(v));
            else animator.SetBool(prm.nameHash, v > .5f);
        }
        for (int l = 0; l < states.Length && l < animator.layerCount && l < shownStates.Length; l++)
        {
            int h = states[l];
            if (h == 0 || h == shownStates[l]) continue;
            shownStates[l] = h;
            int now = animator.IsInTransition(l) ? animator.GetNextAnimatorStateInfo(l).fullPathHash : animator.GetCurrentAnimatorStateInfo(l).fullPathHash;
            if (now != h && animator.HasState(l, h)) animator.CrossFadeInFixedTime(h, .08f, l);
        }
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
        if (builtOneBody)
        {
            if (right != heldRight) { heldRight = right; HoldOn(ref rightVisual, right, rigRight); }
            if (left != heldLeft)
            {
                heldLeft = left; HoldOn(ref leftVisual, left, rigLeft);
                // A spell in the left hand: its held flame on the body's spell origin, as the owner's first person shows it.
                var spell = Coop.Item(left)?.spell;
                if (leftVisual == null && spell != null && spell.heldVisual != null && (rigSpell != null || rigLeft != null))
                {
                    leftVisual = Instantiate(spell.heldVisual, rigSpell != null ? rigSpell : rigLeft).gameObject;
                    foreach (var t in leftVisual.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
                }
            }
            return;
        }
        if (animator == null || !animator.isHuman) return;
        if (right != heldRight) { heldRight = right; Hold(ref rightVisual, right, HumanBodyBones.RightHand, MultiplayerManager.Instance.weaponGrip); }
        if (left != heldLeft) { heldLeft = left; Hold(ref leftVisual, left, HumanBodyBones.LeftHand, MultiplayerManager.Instance.shieldGrip); }
    }

    void Hold(ref GameObject visual, string saveId, HumanBodyBones bone, EnemyWeaponGrip grip)
    {
        if (visual != null) Destroy(visual.transform.parent.gameObject);
        var item = Coop.Item(saveId);
        visual = item != null && (item.itemType == ItemType.Weapon || item.itemType == ItemType.Shield) ? Attach(item, bone, grip) : null;
        if (visual != null && usedVisual != null && bone == HumanBodyBones.RightHand) visual.SetActive(false);
    }

    void HoldOn(ref GameObject visual, string saveId, Transform socket)
    {
        if (visual != null) Destroy(visual);
        visual = null;
        var item = Coop.Item(saveId);
        if (socket == null || item == null || item.worldPrefab == null || (item.itemType != ItemType.Weapon && item.itemType != ItemType.Shield)) return;
        visual = Look(item, socket);
        if (usedVisual != null && socket == rigRight) visual.SetActive(false);
    }

    // The item's world model on a hand socket as the first-person hands hold it; only a look.
    GameObject Look(ItemData item, Transform socket)
    {
        var visual = Instantiate(item.worldPrefab, socket, false);
        visual.transform.SetLocalPositionAndRotation(item.heldPosition, Quaternion.Euler(item.heldRotation));
        visual.transform.localScale *= Mathf.Max(.01f, item.heldScale);
        foreach (var c in visual.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (var b in visual.GetComponentsInChildren<Rigidbody>(true)) { b.isKinematic = true; b.detectCollisions = false; }
        foreach (var m in visual.GetComponentsInChildren<MonoBehaviour>(true)) m.enabled = false;
        foreach (var t in visual.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
        return visual;
    }

    // The item's world model in a hand, on a grip socket as in first person (the grips are the first-person hand
    // sockets) with the item's own grip offset; only a look (no colliders, physics or scripts).
    GameObject Attach(ItemData item, HumanBodyBones bone, EnemyWeaponGrip grip)
    {
        if (item == null || item.worldPrefab == null) return null;
        var hand = animator.GetBoneTransform(bone);
        if (hand == null) return null;
        var socket = new GameObject($"Grip - {bone}").transform;
        socket.SetParent(hand, false);
        if (grip != null) { socket.SetLocalPositionAndRotation(grip.position, Quaternion.Euler(grip.rotation)); socket.localScale = Vector3.one * Mathf.Max(.01f, grip.scale); }
        var visual = Instantiate(item.worldPrefab, socket, false);
        visual.transform.SetLocalPositionAndRotation(item.heldPosition, Quaternion.Euler(item.heldRotation));
        visual.transform.localScale *= Mathf.Max(.01f, item.heldScale);
        foreach (var c in visual.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (var b in visual.GetComponentsInChildren<Rigidbody>(true)) { b.isKinematic = true; b.detectCollisions = false; }
        foreach (var m in visual.GetComponentsInChildren<MonoBehaviour>(true)) m.enabled = false;
        return visual;
    }

    // Eating or drinking, as the first-person arms play it: the item in the right hand in place of the weapon, the
    // right arm's Eat or Drink clip fitted to the same time.
    public void Use(string saveId, bool drink, float seconds)
    {
        // The one body plays the owner's Eat or Drink itself (ApplyArms); the item is shown in the hand meanwhile.
        if (builtOneBody)
        {
            EndUse();
            var food = Coop.Item(saveId);
            if (rightVisual != null) rightVisual.SetActive(false);
            usedVisual = food != null && food.worldPrefab != null && rigRight != null ? Look(food, rigRight) : new GameObject("Used item");
            useEnds = Time.time + Mathf.Max(.2f, seconds);
            return;
        }
        if (animator == null || !animator.isHuman) return;
        EndUse();
        var clip = Clip(drink ? "Coop Drink" : "Coop Eat");
        if (clip == null) return;
        seconds = Mathf.Max(.2f, seconds);
        animator.SetFloat(UseSpeed, clip.length / seconds);
        animator.SetTrigger(drink ? "Drink" : "Eat");
        if (rightVisual != null) rightVisual.SetActive(false);
        usedVisual = Attach(Coop.Item(saveId), HumanBodyBones.RightHand, MultiplayerManager.Instance.weaponGrip);
        if (usedVisual == null) usedVisual = new GameObject("Used item");   // nothing to show: still times the weapon's return
        useEnds = Time.time + seconds;
    }

    void EndUse()
    {
        if (usedVisual != null) Destroy(usedVisual.transform.parent != null && usedVisual.transform.parent.name.StartsWith("Grip - ") ? usedVisual.transform.parent.gameObject : usedVisual);
        usedVisual = null;
        if (rightVisual != null) rightVisual.SetActive(true);
    }

    AnimationClip Clip(string name)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return null;
        foreach (var c in animator.runtimeAnimatorController.animationClips) if (c != null && c.name == name) return c;
        return null;
    }

    // A swing as the first-person release starts (1 light, 2 the alternate light, 3 heavy) and its whoosh (the held
    // weapon's own, or the game's default).
    public void Swing(byte kind)
    {
        bool heavy = kind == 3;
        if (animator != null && !builtOneBody) animator.SetTrigger(heavy ? "SwingHeavy" : kind == 2 ? "SwingB" : "SwingA");
        if (!AudioManager.HasInstance || player == null) return;
        var weapon = Coop.Item(player.Current.right.ToString());
        var profile = weapon != null ? weapon.weapon : null;
        AudioData audio = profile == null || profile.useDefaultEffects
            ? (CombatManager.HasInstance ? (heavy ? CombatManager.Instance.defaultHeavySwingAudio ?? CombatManager.Instance.defaultSwingAudio : CombatManager.Instance.defaultSwingAudio) : null)
            : heavy && profile.heavySwingAudio != null ? profile.heavySwingAudio : profile.swingAudio;
        if (audio != null) AudioManager.Instance.PlaySFXData(audio, transform.position + Vector3.up * 1.2f * builtScale);
    }
    // The shield bash, with its sound where the body is.
    public void Bash()
    {
        if (animator != null && !builtOneBody) animator.SetTrigger("Bash");
        var audio = CombatManager.HasInstance ? CombatManager.Instance.shieldBashAudio : null;
        if (audio != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFXData(audio, transform.position + Vector3.up * 1.2f * builtScale);
    }

    // The one body gets these from the owner's own states (ApplyArms).
    public void Cast() { if (animator != null && !builtOneBody) animator.SetTrigger("CastSpell"); }
    public void Hurt() { if (animator != null && !builtOneBody && player != null && player.Current.alive) animator.SetTrigger("Hurt"); }
}
