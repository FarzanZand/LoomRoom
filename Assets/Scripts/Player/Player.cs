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

    public PlayerMotor       Motor     { get; private set; }
    public PlayerLook        Look      { get; private set; }
    public PlayerFootsteps   Footsteps { get; private set; }
    public PlayerCombat      Combat    { get; private set; }
    public PlayerCameraRig   CameraRig { get; private set; }
    public InteractController Interact { get; private set; }
    public Inventory         Bag       { get; private set; }
    public Inventory         Hotbar    { get; private set; }
    public Equipment         Equipment { get; private set; }

    public bool IsActive => PlayerManager.HasInstance && PlayerManager.Instance.Active == this;

    // Starting items are applied once per play session even if the player is swapped
    // out and back in (the root gets SetActive toggled, which re-runs OnEnable).
    bool startingItemsApplied;
    Vector3    spawnPosition;
    Quaternion spawnRotation;

    public void SetSpawnPoint(Vector3 position, Quaternion rotation)
    {
        spawnPosition = position;
        spawnRotation = rotation;
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

        foreach (var inv in GetComponents<Inventory>())
        {
            if (inv.role == InventoryRole.Hotbar) Hotbar = inv;
            else                                  Bag    = inv;
        }
        if (settings == null) settings = ScriptableObject.CreateInstance<PlayerSettings>();
    }

    protected override void Start()
    {
        base.Start();
        if (spawnPosition == Vector3.zero && spawnRotation == default)
            SetSpawnPoint(transform.position, transform.rotation);
        ApplyStartingItems();
    }

    void ApplyStartingItems()
    {
        if (startingItemsApplied || data == null) return;
        startingItemsApplied = true;

        if (InventoryManager.HasInstance)
            foreach (var item in data.startingItems)
                if (item != null) InventoryManager.Instance.Pickup(item, this);

        if (Equipment != null)
            foreach (var item in data.startingEquipment)
                if (item != null) Equipment.Equip(item);
    }

    protected override void OnDied()
    {
        base.OnDied();
        if (GameManager.HasInstance) GameManager.Instance.Push(GameState.Dead);
        if (respawnDelay > 0f) StartCoroutine(RespawnRoutine());
    }

    System.Collections.IEnumerator RespawnRoutine()
    {
        yield return new WaitForSeconds(respawnDelay);
        if (ScreenManager.HasInstance) ScreenManager.Instance.FadeInOut(0.5f, 0.3f, 0.8f);
        yield return new WaitForSeconds(0.6f);

        // CharacterController fights direct transform writes; disable it for the teleport.
        if (Motor != null && Motor.Controller != null) Motor.Controller.enabled = false;
        transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        if (Motor != null && Motor.Controller != null) Motor.Controller.enabled = true;
        Look?.SetYaw(spawnRotation.eulerAngles.y);

        Stats?.Revive();
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Dead);
    }
}
