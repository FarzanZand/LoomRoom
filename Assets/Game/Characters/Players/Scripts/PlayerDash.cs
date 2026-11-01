using System;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;

// The dash (CombatManager > Player > Dash): Sprint (Shift) pressed in a fight throws the table player a few metres to the
// side or back, the way the movement keys point at that moment (never forward; back when none are held), for a flat
// stamina cost. An even glide that eases to a stop (the user's reference, 2026-11-01). It moves the player only:
// hits still land while dashing. Feel: the view widens and eases back, leans into a sideways dash and nods up out of a
// backward one, dips a little, all slight; the arms and held items trail behind and tip over against it, then spring back
// (PlayerViewPresentation.Offset, Tilt); streaks can rush past (off by default); a whoosh on the push-off. Added to
// the table player by PlayerCombat.
// Before PlayerMotor, so the press moves the player the same frame.
[DefaultExecutionOrder(-5)]
public class PlayerDash : MonoBehaviour
{
    Player player;
    PlayerMotor motor;
    PlayerViewPresentation view;
    ParticleSystem streaks;
    Transform streakRoot;

    bool dashing;
    float startedAt = -10f, readyAt;
    bool queued;
    float covered, lastStep;   // share of the dash's time played, and last frame's planned step in metres
    Vector3 direction, viewDirection, lastPosition;   // world (flat) and in the view's frame at the press

    // Springs for the view's lean and the arms' trail and tilt.
    Vector3 lean, leanVelocity, trail, trailVelocity, tilt, tiltVelocity;
    float dip, dipVelocity;

    static DashTuning Tuning => CombatManager.HasInstance ? CombatManager.Instance.dash : null;

    public bool Dashing => dashing;
    // What the camera adds while a dash plays out (DashCameraMotion, PlayerFX).
    public Vector3 CameraRotation => lean;
    public float CameraDip => dip;
    public float FovKick { get; private set; }

    void Awake()
    {
        player = GetComponent<Player>();
        motor = GetComponent<PlayerMotor>();
        view = GetComponentInChildren<PlayerViewPresentation>(true);
    }

    void OnEnable()
    {
        if (InputManager.HasInstance) InputManager.Instance.SprintPressed += OnSprintPressed;
    }

    void OnDisable()
    {
        if (InputManager.HasInstance) InputManager.Instance.SprintPressed -= OnSprintPressed;
        Stop();
        queued = false;
        lean = leanVelocity = trail = trailVelocity = tilt = tiltVelocity = Vector3.zero;
        dip = dipVelocity = FovKick = 0f;
        if (view != null) { view.Offset = Vector3.zero; view.Tilt = Vector3.zero; }
    }

    void Start()
    {
        var cam = player != null && player.CameraRig != null ? player.CameraRig.Camera : null;
        if (cam != null && cam.GetComponent<DashCameraMotion>() == null) cam.gameObject.AddComponent<DashCameraMotion>();
    }

    static Vector2 HeldKeys => InputManager.HasInstance ? InputManager.Instance.Move : Vector2.zero;

    // A press during a dash, or just before its cooldown is over, is kept and dashes as soon as it can (toward the keys
    // held then), so a quick double tap chains instead of being lost.
    void OnSprintPressed()
    {
        var t = Tuning;
        if (TryStart(HeldKeys) || t == null) return;
        if (dashing || Time.time < readyAt && Time.time >= readyAt - t.buffer) queued = true;
    }

    // From the press, toward move (the movement keys, in the view's frame): switched on, ready, on the ground, not busy
    // with something that holds the body, and enough stamina.
    public bool TryStart(Vector2 move)
    {
        var t = Tuning;
        if (t == null || !t.enabled || dashing || Time.time < readyAt || player == null || !player.IsActive || !player.IsAlive) return false;
        if (GameManager.HasInstance && !GameManager.Instance.GameplayActive) return false;
        if (player.Ghost || motor == null || (t.groundedOnly && !motor.IsGrounded)) return false;
        var bash = player.GetComponent<ShieldBash>();
        if (bash != null && bash.Busy) return false;
        if (player.Equipment != null && player.Equipment.IsUsingItem) return false;

        // Sideways and back only, never forward: the forward part of the keys is dropped (forward and a side key dash
        // straight to that side), forward alone does nothing, and no key at all steps back.
        if (move.sqrMagnitude < .04f) move = Vector2.down;
        else
        {
            move.y = Mathf.Min(0f, move.y);
            if (move.sqrMagnitude < .04f) return false;
        }

        var stats = player.Stats;
        if (stats != null && stats.HasStat(StatType.MaxStamina) && !DevGodMode.On &&
            !stats.TryUseStamina(t.staminaCost, player.PlayerData != null ? player.PlayerData.staminaRegenDelay : 1f))
            return false;   // TryUseStamina reports it

        var yaw = player.Look != null ? player.Look.YawTransform : transform;
        move.Normalize();
        viewDirection = new Vector3(move.x, 0f, move.y);
        var forward = yaw.forward; forward.y = 0f; forward.Normalize();
        var right = yaw.right; right.y = 0f; right.Normalize();
        direction = (forward * move.y + right * move.x).normalized;

        dashing = true;
        startedAt = Time.time;
        lastPosition = transform.position;
        covered = 0f; lastStep = 0f;

        // The view throws its weight into the dash at once: a kick to the springs, settled by them.
        leanVelocity += new Vector3(viewDirection.z * t.pitch, 0f, -viewDirection.x * t.roll) * t.kick;
        dipVelocity -= t.dip * t.kick;
        trailVelocity -= viewDirection * t.armsTrail * t.kick;
        tiltVelocity += Tilted(t) * t.kick;

        if (t.showStreaks) Streaks(true);
        if (t.noiseRadius > 0f) NoiseEvents.Report(transform.position, t.noiseRadius, player);
        if (t.dashAudio != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFXData2D(t.dashAudio);
        Dashed?.Invoke();
        return true;
    }

    public event Action Dashed;

    void Update()
    {
        var t = Tuning;
        if (motor != null) motor.SprintHoldOnly = t != null && t.enabled;
        if ((dashing || queued) && (t == null || !player.IsActive || !player.IsAlive || (GameManager.HasInstance && !GameManager.Instance.GameplayActive)))
        {
            Stop();
            queued = false;
        }
        if (dashing) UpdateDash(t);
        if (queued && !dashing && Time.time >= readyAt)
        {
            queued = false;
            TryStart(HeldKeys);
        }
        Settle(t);
    }

    void UpdateDash(DashTuning t)
    {
        // Measured, not assumed: a wall or an enemy in the way stops it short.
        var moved = transform.position - lastPosition; moved.y = 0f;
        lastPosition = transform.position;
        bool blocked = covered > .25f && moved.magnitude < lastStep * .2f;
        if (covered >= 1f || blocked) { End(t); return; }
        // Speed falls off as (1 - t)^falloff, so the distance covered by time t is distance x (1 - (1 - t)^(falloff + 1)):
        // each frame moves exactly its stretch of that, whatever the frame rate, and the whole dash is the distance.
        float dt = Mathf.Max(1e-4f, Time.deltaTime);
        float next = Mathf.Min(1f, covered + dt / Mathf.Max(.05f, t.duration));
        float power = t.falloff + 1f;
        lastStep = Distance(t) * (Mathf.Pow(1f - covered, power) - Mathf.Pow(1f - next, power));
        covered = next;
        motor.EvadeVelocity = direction * (lastStep / dt);
    }

    void End(DashTuning t)
    {
        Stop();
        readyAt = Time.time + (t != null ? t.cooldown : .3f);
        // The feet catching: a small second dip as it stops.
        if (t != null) dipVelocity -= t.dip * t.kick * t.stopDip;
    }

    void Stop()
    {
        dashing = false;
        if (motor != null) motor.EvadeVelocity = Vector3.zero;
        Streaks(false);
    }

    // ── Feel ──────────────────────────────────────────────────────────

    void Settle(DashTuning t)
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        float since = Time.time - startedAt;
        float hz = t != null ? t.springHz : 4f, damping = t != null ? t.springDamping : .55f;
        // While it plays the lean is held toward the dash's side and nod; after, everything springs home.
        float hold = dashing && t != null ? 1f - Mathf.Clamp01(since / Mathf.Max(.05f, t.duration)) : 0f;
        var leanTarget = t != null ? new Vector3(viewDirection.z * t.pitch, 0f, -viewDirection.x * t.roll) * hold : Vector3.zero;
        var trailTarget = t != null ? -viewDirection * t.armsTrail * hold : Vector3.zero;
        var tiltTarget = t != null ? Tilted(t) * hold : Vector3.zero;
        float dipTarget = t != null ? -t.dip * hold : 0f;
        Spring(ref lean, ref leanVelocity, leanTarget, hz, damping, dt);
        Spring(ref trail, ref trailVelocity, trailTarget, hz * 1.2f, damping, dt);
        Spring(ref tilt, ref tiltVelocity, tiltTarget, hz, damping, dt);
        Spring(ref dip, ref dipVelocity, dipTarget, hz, damping, dt);
        if (view != null) { view.Offset = trail; view.Tilt = tilt; }

        // Wider at once, then eased back: up over the first fifth, down over the rest.
        float fov = 0f;
        if (t != null && since < t.fovSeconds)
        {
            float rise = t.fovSeconds * .18f;
            fov = since < rise ? Mathf.SmoothStep(0f, 1f, since / rise) : 1f - EaseOut((since - rise) / (t.fovSeconds - rise));
            fov *= t.fovKick;
        }
        FovKick = fov;
    }

    // The arms and weapon tip over against the dash, turning about the eye: away from a sideways dash (the weapon's head
    // lags) and forward out of a backward one.
    Vector3 Tilted(DashTuning t) => new Vector3(-viewDirection.z * t.armsTilt * .5f, -viewDirection.x * t.armsTilt * .35f, viewDirection.x * t.armsTilt);

    // Sideways the side distance, straight back the back distance, a diagonal in between by how far back it points.
    float Distance(DashTuning t) => Mathf.Lerp(t.sideDistance, t.backDistance, Mathf.Clamp01(-viewDirection.z));

    static float EaseOut(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x) * (1f - x); }

    static void Spring(ref Vector3 x, ref Vector3 v, Vector3 target, float hz, float damping, float dt)
    {
        float w = 2f * Mathf.PI * hz;
        v += (w * w * (target - x) - 2f * damping * w * v) * dt;
        x += v * dt;
    }

    static void Spring(ref float x, ref float v, float target, float hz, float damping, float dt)
    {
        float w = 2f * Mathf.PI * hz;
        v += (w * w * (target - x) - 2f * damping * w * v) * dt;
        x += v * dt;
    }

    void Streaks(bool on)
    {
        var t = Tuning;
        var cam = player != null && player.CameraRig != null ? player.CameraRig.Camera : null;
        if (on && streaks == null && t != null && t.streaks != null && cam != null)
        {
            var go = Instantiate(t.streaks, cam.transform);
            streakRoot = go.transform;
            streakRoot.localPosition = Vector3.zero;
            streaks = go.GetComponentInChildren<ParticleSystem>();
        }
        if (streaks == null) return;
        if (on)
        {
            // The streaks fly at the view along the dash, round the edge of the view: a sideways dash's ring is turned only
            // part of the way, so its lines still sweep across the screen instead of bunching at the screen's edge.
            var way = viewDirection.sqrMagnitude > .01f ? viewDirection : Vector3.forward;
            way = (way + Vector3.forward * (t.streakForward * (1f - Mathf.Abs(way.z)))).normalized;
            streakRoot.localRotation = Quaternion.LookRotation(way, Vector3.up);
            streaks.Play(true);
        }
        else streaks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }
}

[Serializable]
public class DashTuning
{
    [Tooltip("Sprint (Shift) pressed dashes the table player to the side or back, the way the movement keys point (never forward; back when none are held). Off: Shift only sprints, as before.")]
    public bool enabled = true;
    [ShowIf(nameof(enabled)), Min(0f), Tooltip("Stamina one dash costs (every class starts with 40, so two from full). Refused when there isn't that much left.")]
    public float staminaCost = 20f;
    [ShowIf(nameof(enabled)), Min(.5f), FormerlySerializedAs("distance"), Tooltip("Metres a dash straight to the side covers when nothing is in the way.")]
    public float sideDistance = 2.6f;
    [ShowIf(nameof(enabled)), Min(.5f), Tooltip("Metres a dash straight back covers when nothing is in the way (a diagonal is in between).")]
    public float backDistance = 1.82f;
    [ShowIf(nameof(enabled)), Min(.05f), Tooltip("Seconds a dash takes.")]
    public float duration = .45f;
    [ShowIf(nameof(enabled)), Range(0f, 4f), Tooltip("How front-loaded it is: speed falls off as (1 - t)^this. 0 = even speed, higher = a harder burst and a softer stop.")]
    public float falloff = .7f;
    [ShowIf(nameof(enabled)), Min(0f), Tooltip("Seconds after a dash ends before the next one can start.")]
    public float cooldown = .2f;
    [ShowIf(nameof(enabled)), Range(0f, .5f), Tooltip("A press during a dash, or this many seconds before its cooldown is over, is kept and dashes as soon as it can.")]
    public float buffer = .15f;
    [ShowIf(nameof(enabled)), Min(0f), Tooltip("Metres within which enemies hear the push-off (as they hear running). 0 = silent.")]
    public float noiseRadius = 8f;
    [ShowIf(nameof(enabled)), Tooltip("Only from the ground.")]
    public bool groundedOnly = true;

    [ShowIf(nameof(enabled)), FoldoutGroup("Feel"), Range(0f, 20f), Tooltip("Degrees the view widens as the dash starts, eased back.")]
    public float fovKick = 1.5f;
    [ShowIf(nameof(enabled)), FoldoutGroup("Feel"), Min(.05f), Tooltip("Seconds the wider view takes to come back.")]
    public float fovSeconds = .4f;
    [ShowIf(nameof(enabled)), FoldoutGroup("Feel"), Range(0f, 15f), Tooltip("Degrees the view leans into a sideways dash.")]
    public float roll = 1.5f;
    [ShowIf(nameof(enabled)), FoldoutGroup("Feel"), Range(0f, 10f), Tooltip("Degrees the view nods: down into a forward dash, up out of a backward one.")]
    public float pitch = .8f;
    [ShowIf(nameof(enabled)), FoldoutGroup("Feel"), Range(0f, .3f), Tooltip("Metres the view dips as the legs push off.")]
    public float dip = .03f;
    [ShowIf(nameof(enabled)), FoldoutGroup("Feel"), Range(0f, 2f), Tooltip("Share of the push-off's dip the view dips again as the dash stops (the feet catching).")]
    public float stopDip = 1f;
    [ShowIf(nameof(enabled)), FoldoutGroup("Feel"), Range(0f, .3f), Tooltip("Metres the arms and held items trail behind the dash, sprung back after.")]
    public float armsTrail = .06f;
    [ShowIf(nameof(enabled)), FoldoutGroup("Feel"), Range(0f, 20f), Tooltip("Degrees the arms and held items tip over against the dash about the eye (the weapon's head lagging), sprung back after.")]
    public float armsTilt = 7f;
    [ShowIf(nameof(enabled)), FoldoutGroup("Feel"), Range(0f, 20f), Tooltip("How hard the press kicks the lean, dip and trail (a share of each, per second): the snap at the start.")]
    public float kick = 3f;
    [ShowIf(nameof(enabled)), FoldoutGroup("Feel"), Range(1f, 12f), Tooltip("How quickly the lean, dip and trail follow and settle (springs, cycles a second).")]
    public float springHz = 4f;
    [ShowIf(nameof(enabled)), FoldoutGroup("Feel"), Range(.1f, 1.5f), Tooltip("Spring damping: below 1 overshoots a little and settles, 1 and more never overshoots.")]
    public float springDamping = .6f;
    [ShowIf(nameof(enabled)), FoldoutGroup("Feel"), Tooltip("Pale streaks rush past the view while dashing.")]
    public bool showStreaks;
    [ShowIf("@enabled && showStreaks"), FoldoutGroup("Feel"), Tooltip("Streaks that rush past the view while dashing, turned along the dash (a child of the camera).")]
    public GameObject streaks;
    [ShowIf(nameof(enabled)), FoldoutGroup("Feel"), Range(0f, 3f), Tooltip("How far a sideways dash's streaks are turned back toward the view's front (0 = straight from the side, out of sight).")]
    public float streakForward = 1.2f;
    [ShowIf(nameof(enabled)), FoldoutGroup("Sounds"), LabelText("Push-off sound"), InlineEditor(InlineEditorObjectFieldModes.Foldout),
     Tooltip("Played as the dash starts (Audio/Data/Dash: a gravel scuff, a low body whoosh, cloth, a thump; Tools/dash_sounds.py).")]
    public AudioData dashAudio;
}
