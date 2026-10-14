using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// One player in a co-op session (the NetworkManager's player prefab). Its owner moves it onto the body they play
// (the room player or the table player) and writes its state. Everywhere else it is that player: a body to look at
// (CoopAvatar) and a stand-in Character enemies hunt and hit; their blows go to the owner, who blocks or takes them.
[RequireComponent(typeof(CoopAvatar))]
public class CoopPlayer : NetworkBehaviour
{
    public readonly NetworkVariable<byte> Slot = new(Coop.NoSlot);
    public readonly NetworkVariable<FixedString64Bytes> PlayerName = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public readonly NetworkVariable<CoopPlayerState> State = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public Character Proxy { get; private set; }
    public CoopAvatar Avatar { get; private set; }
    public string DisplayName => string.IsNullOrEmpty(PlayerName.Value.ToString()) ? $"Player {Slot.Value + 1}" : PlayerName.Value.ToString();
    public CoopPlayerState Current => State.Value;
    public int SlotIndex => Slot.Value == Coop.NoSlot ? -1 : Slot.Value;
    // Down on the table and alive: enemies on this machine can see and hit it (never the local player's own).
    public bool Hunted => !IsOwner && State.Value.place == CoopPlace.Table && State.Value.alive && State.Value.inRun;

    int shownRelease;
    bool wasCasting, wasBashing;
    ItemData wasEating;
    Player watchedTable;
    Unity.Netcode.Components.NetworkTransform motion;

    void Awake()
    {
        Proxy = GetComponent<Character>();
        Avatar = GetComponent<CoopAvatar>();
        motion = GetComponent<Unity.Netcode.Components.NetworkTransform>();
    }

    public override void OnNetworkSpawn()
    {
        if (!MultiplayerManager.HasInstance) return;
        var manager = MultiplayerManager.Instance;
        if (IsServer && Slot.Value == Coop.NoSlot) Slot.Value = manager.FreeSlot(OwnerClientId);
        if (IsOwner) PlayerName.Value = new FixedString64Bytes(manager.LocalName);
        manager.Register(this);
        Avatar.Bind(this);
        if (IsOwner) Watch(PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null);
    }

    public override void OnNetworkDespawn()
    {
        Watch(null);
        if (MultiplayerManager.HasInstance) MultiplayerManager.Instance.Unregister(this);
        Avatar.Unbind();
    }

    void Watch(Player table)
    {
        if (watchedTable != null) watchedTable.Damaged -= OnLocalDamaged;
        watchedTable = table;
        if (watchedTable != null) watchedTable.Damaged += OnLocalDamaged;
    }

    void OnLocalDamaged(DamageInfo info)
    {
        if (info.Amount > 0 && !info.Blocked) HurtRpc(info.HitPoint, info.Direction, info.SwingDirection);
    }

    // Rename while playing (the co-op screen's name box).
    public void Rename(string name)
    {
        if (IsOwner && IsSpawned) PlayerName.Value = new FixedString64Bytes(name ?? "");
    }

    void LateUpdate()
    {
        if (!IsSpawned) return;
        if (IsOwner) WriteOwnState();
        else if (Proxy != null && Proxy.Stats != null)
        {
            var s = State.Value;
            Proxy.Stats.SetNetworkHealth(s.alive ? Mathf.Max(1f, s.health / 255f * Proxy.Stats.MaxHealth) : 0f);
        }
    }

    // The owner: follow the body being played and describe it.
    void WriteOwnState()
    {
        if (!PlayerManager.HasInstance || !MultiplayerManager.HasInstance) return;
        var players = PlayerManager.Instance;
        var active = players.Active;
        var table = players.GetPlayer(PlayerKind.Table);
        if (active != null)
        {
            float yaw = active.Look != null ? active.Look.YawTransform.eulerAngles.y : active.transform.eulerAngles.y;
            var to = active.transform.position;
            // A jump (between the room and the table, a new floor, a warp) is a teleport, not a slide across the world.
            if (motion != null && (to - transform.position).sqrMagnitude > 36f) motion.Teleport(to, Quaternion.Euler(0f, yaw, 0f), transform.localScale);
            else transform.SetPositionAndRotation(to, Quaternion.Euler(0f, yaw, 0f));
        }
        var manager = MultiplayerManager.Instance;
        var s = State.Value;
        s.place = players.ActiveKind == PlayerKind.Table ? CoopPlace.Table : CoopPlace.Room;
        s.alive = table != null && table.IsAlive;
        s.inRun = manager.LocalInRun;
        s.atTable = manager.LocalAtTable;
        s.ready = manager.LocalReady;
        s.inLobby = manager.LocalInLobby;
        s.ghost = manager.Ghost;
        s.crouching = active != null && active.Motor != null && active.Motor.IsCrouching;
        s.seated = s.place == CoopPlace.Room && IntroController.Seated;
        bool onTable = s.place == CoopPlace.Table && table != null;
        var bash = onTable ? table.GetComponent<ShieldBash>() : null;
        s.blocking = onTable && table.Combat != null && table.Combat.IsGuarding;
        int release = onTable && table.Combat != null ? table.Combat.ReleaseShown : 0;
        s.charging = onTable && table.Combat != null && table.Combat.IsAttacking && release == 0;
        s.airborne = active != null && active.Motor != null && !active.Motor.IsGrounded && active.Motor.AirborneTime > .15f;
        s.health = table != null && table.Stats != null && table.Stats.MaxHealth > 0 ? (byte)Mathf.Clamp(Mathf.CeilToInt(table.Stats.CurrentHealth / table.Stats.MaxHealth * 255f), 0, 255) : (byte)0;
        var progress = table != null ? table.GetComponent<AdventurerProgress>() : null;
        s.classId = new FixedString32Bytes(progress != null && progress.selectedClass != null ? progress.selectedClass.id : "");
        s.right = new FixedString128Bytes(onTable && table.Equipment != null ? Coop.SaveId(table.Equipment.Get(EquipmentSlot.RightHand)) : "");
        s.left = new FixedString128Bytes(onTable && table.Equipment != null ? Coop.SaveId(table.Equipment.Get(EquipmentSlot.LeftHand)) : "");
        if (!s.Equals(State.Value)) State.Value = s;

        // One-shot moments the others see, as the arms play them: a swing as its release starts, a bash, a spell.
        if (release != shownRelease && release != 0)
        {
            SwingRpc((byte)release);
            if (CoopTiming) Debug.Log($"[Coop timing] release {release} sent at {System.DateTime.Now.TimeOfDay.TotalMilliseconds:0}");
        }
        shownRelease = release;
        bool bashing = bash != null && bash.Busy;
        if (bashing && !wasBashing) BashRpc();
        wasBashing = bashing;
        var spells = onTable ? table.GetComponent<PlayerSpellcasting>() : null;
        bool casting = spells != null && spells.Casting;
        if (casting && !wasCasting) CastRpc();
        wasCasting = casting;
        var eating = onTable && table.Equipment != null ? table.Equipment.UsingItem : null;
        if (eating != null && eating != wasEating)
            UseRpc(new FixedString128Bytes(Coop.SaveId(eating)), eating.AnimationOnUse == ItemUseAnimation.Drink, table.Equipment.UsingTime);
        wasEating = eating;
    }

    // Development: log when a swing is sent and shown, on the wall clock (two editors on one computer compare them).
    public static bool CoopTiming;

    [Rpc(SendTo.NotOwner)]
    void SwingRpc(byte kind)
    {
        if (CoopTiming) Debug.Log($"[Coop timing] release {kind} shown at {System.DateTime.Now.TimeOfDay.TotalMilliseconds:0}");
        Avatar.Swing(kind);
    }
    [Rpc(SendTo.NotOwner)] void BashRpc() => Avatar.Bash();
    [Rpc(SendTo.NotOwner)] void CastRpc() => Avatar.Cast();
    [Rpc(SendTo.NotOwner)] void UseRpc(FixedString128Bytes item, bool drink, float seconds) => Avatar.Use(item.ToString(), drink, seconds);
    // Hurt: the others see the flinch and the blood.
    [Rpc(SendTo.NotOwner)]
    void HurtRpc(Vector3 point, Vector3 direction, Vector3 swing)
    {
        Avatar.Hurt();
        if (GoreManager.HasInstance && Proxy != null && State.Value.place == CoopPlace.Table)
            GoreManager.Instance.Hit(Proxy, new DamageInfo { Amount = 1f, HitPoint = point != Vector3.zero ? point : transform.position + Vector3.up * 1.2f, Direction = direction, SwingDirection = swing, Target = Proxy });
    }

    // Host -> owner: an enemy's blow reached this player's stand-in on the host.
    [Rpc(SendTo.Owner)] public void HitRpc(NetDamage hit) => CoopWorld.ReceiveEnemyHit(hit);
}
