using UnityEngine;

// A controllable character. Sits on the object with the CharacterController and
// caches the player-only components around it. Room and Table are the same class
// with different components present: the Room player simply has no PlayerCombat.
public class Player : Character
{
    public PlayerKind kind = PlayerKind.Room;
    [Tooltip("Control options (sensitivity, hold/toggle). Shared between players is fine.")]
    public PlayerSettings settings;
    [Tooltip("Seconds after death before the player respawns at their spawn point with full health. 0 = stay dead.")]
    public float respawnDelay = 3f;

    [Header("Prefab Wiring")]
    [SerializeField] GameObject activationRoot;
    [SerializeField] BodyFollower bodyPresentation;
    [SerializeField] PlayerViewPresentation viewPresentation;
    public GameObject ActivationRoot => activationRoot != null ? activationRoot : gameObject;
    public PlayerViewPresentation ViewPresentation => viewPresentation;

    public void SynchronizePresentation()
    {
        if (bodyPresentation != null) bodyPresentation.Synchronize();
    }

    public PlayerMotor       Motor     { get; private set; }
    public PlayerLook        Look      { get; private set; }
    public PlayerFootsteps   Footsteps { get; private set; }
    public PlayerCombat      Combat    { get; private set; }
    public PlayerCameraRig   CameraRig { get; private set; }
    public InteractController Interact { get; private set; }
    public Inventory         Bag       { get; private set; }
    public Inventory         Hotbar    { get; private set; }
    public Equipment         Equipment { get; private set; }
    public Wallet            Wallet    { get; private set; }
    // Movement, stamina and starting kit. Null if the Character holds another kind of data.
    public PlayerData        PlayerData => data as PlayerData;

    public bool IsActive => PlayerManager.HasInstance && PlayerManager.Instance.Active == this;

    // Starting items are applied once per play session even if the player is swapped
    // out and back in (the root gets SetActive toggled, which re-runs OnEnable).
    bool startingItemsApplied;
    bool respawnPending;
    bool       hasSpawnPoint;
    Vector3    spawnPosition;
    Quaternion spawnRotation;

    public void SetSpawnPoint(Vector3 position, Quaternion rotation)
    {
        spawnPosition = position;
        spawnRotation = rotation;
        hasSpawnPoint = true;
    }

    protected override void Awake()
    {
        base.Awake();
        Motor     = GetComponent<PlayerMotor>();
        Look      = GetComponent<PlayerLook>();
        Footsteps = GetComponent<PlayerFootsteps>();
        Combat    = GetComponent<PlayerCombat>();
        CameraRig = GetComponentInChildren<PlayerCameraRig>(true);
        Interact  = GetComponent<InteractController>();
        Equipment = GetComponent<Equipment>();
        Wallet    = GetComponent<Wallet>();

        foreach (var inv in GetComponents<Inventory>())
        {
            if (inv.role == InventoryRole.Hotbar) Hotbar = inv;
            else                                  Bag    = inv;
        }
        if (settings == null)
        {
            Debug.LogWarning($"{name}: no PlayerSettings assigned; using defaults.", this);
            settings = ScriptableObject.CreateInstance<PlayerSettings>();
            fallbackSettings = settings;
        }
    }

    PlayerSettings fallbackSettings;   // created above, destroyed with the player

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (fallbackSettings != null) Destroy(fallbackSettings);
    }

    protected override void Start()
    {
        base.Start();
        // Where it stands at Start, unless a level already set a spawn point.
        if (!hasSpawnPoint) SetSpawnPoint(transform.position, transform.rotation);
        ApplyStartingItems();
    }

    // Level-authored gear replaces the character's defaults, even before first activation.
    public void UseLevelLoadout() => startingItemsApplied = true;

    void ApplyStartingItems()
    {
        var kit = PlayerData;
        if (startingItemsApplied || kit == null) return;
        startingItemsApplied = true;

        if (InventoryManager.HasInstance)
            foreach (var item in kit.startingItems)
                if (item != null) InventoryManager.Instance.Pickup(item, this, playSound: false, announcePickup: false);

        if (Equipment == null) return;
        foreach (var item in kit.startingEquipment)
        {
            if (item == null) continue;
            bool owned = (Bag != null && Bag.IndexOf(item) >= 0) || (Hotbar != null && Hotbar.IndexOf(item) >= 0);
            if (!owned && InventoryManager.HasInstance)
                owned = InventoryManager.Instance.Pickup(item, this, preferHotbar: true, playSound: false, announcePickup: false);
            if (owned) Equipment.Equip(item, playSound: false);
        }
    }

    public void Warp(Vector3 position, float yawDelta = 0f)
    {
        // CharacterController fights direct transform writes; disable it for the teleport.
        var controller = Motor != null ? Motor.Controller : null;
        if (controller != null) controller.enabled = false;
        transform.position = position;
        if (controller != null) controller.enabled = true;
        if (yawDelta != 0f) Look?.RotateYaw(yawDelta);
        SynchronizePresentation();
    }

    // Teleports and faces the way `rotation` does: the body takes the rotation, the view its yaw.
    public void Warp(Vector3 position, Quaternion rotation)
    {
        Warp(position);
        transform.rotation = rotation;
        Look?.SetYaw(rotation.eulerAngles.y);
    }

    protected override void OnDied()
    {
        base.OnDied();
        if (GameManager.HasInstance) GameManager.Instance.Push(GameState.Dead);
        if (respawnDelay > 0f)
        {
            respawnPending = true;
            StartCoroutine(RespawnRoutine());
        }
    }

    System.Collections.IEnumerator RespawnRoutine()
    {
        yield return new WaitForSeconds(respawnDelay);
        if (ScreenManager.HasInstance) ScreenManager.Instance.FadeInOut(0.5f, 0.3f, 0.8f);
        yield return new WaitForSeconds(0.6f);
        Respawn();
    }

    void Respawn()
    {
        respawnPending = false;
        Warp(spawnPosition, spawnRotation);

        Stats?.Revive();
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Dead);
    }

    // Deactivating (player swap) kills the respawn coroutine: release Dead now, restart it on return.
    void OnDisable()
    {
        if (respawnPending && GameManager.HasInstance) GameManager.Instance.Pop(GameState.Dead);
    }

    void OnEnable()
    {
        if (!respawnPending) return;
        if (IsAlive) { respawnPending = false; return; }
        if (GameManager.HasInstance) GameManager.Instance.Push(GameState.Dead);
        StartCoroutine(RespawnRoutine());
    }
}
