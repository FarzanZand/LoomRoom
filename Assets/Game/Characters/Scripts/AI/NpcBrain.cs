using System.Collections;
using Sirenix.OdinInspector;
using UnityEngine;

public enum NPCState { Idle = 0, Wander = 1, Patrol = 2, Talking = 3 }

// Friendly characters: idle, wander, patrol, and turn to face whoever talks to them.
// Shares EnemyMotor with enemies; has no perception or attacks.
[RequireComponent(typeof(Character))]
[RequireComponent(typeof(EnemyMotor))]
public class NpcBrain : MonoBehaviour, IInteractable
{
    [Header("Behaviour")]
    [SerializeField] NPCState defaultState = NPCState.Idle;

    [Header("Interaction")]
    [SerializeField] bool isInteractable = false;
    [ShowIf("isInteractable")]
    [Tooltip("Which player can talk to this NPC. Room NPCs must use Room; tabletop NPCs use Table.")]
    [SerializeField] PlayerKind interactionPlayer = PlayerKind.Room;
    [ShowIf("isInteractable")]
    [Tooltip("Prompt shown to the player. {name} is replaced with the character name.")]
    [SerializeField] string prompt = "Talk to {name}";
    [ShowIf("isInteractable")]
    [Tooltip("Speed in degrees per second the NPC turns to face the player.")]
    [SerializeField] float faceSpeed = 180f;

    [Header("Wander")]
    [ShowIf("@defaultState == NPCState.Wander")]
    [SerializeField] float wanderRadius = 8f;
    [ShowIf("@defaultState == NPCState.Wander")]
    [SerializeField] float minWanderDistance = 2f;
    [ShowIf("@defaultState == NPCState.Wander")]
    [SerializeField] float minIdleTime = 2f;
    [ShowIf("@defaultState == NPCState.Wander")]
    [SerializeField] float maxIdleTime = 6f;
    [ShowIf("@defaultState == NPCState.Wander")]
    [SerializeField] float wanderZoneRadius = 0f;

    [Header("Patrol")]
    [ShowIf("@defaultState == NPCState.Patrol")]
    [SerializeField] Transform[] waypoints;
    [ShowIf("@defaultState == NPCState.Patrol")]
    [SerializeField] bool loopPatrol = true;

    [ShowInInspector, ReadOnly] public NPCState State { get; private set; }
    public Character  Character { get; private set; }
    public EnemyMotor Motor     { get; private set; }

    NPCState stateBeforePause;
    InteractableTrigger interactTrigger;
    Transform faceTarget;
    Vector3   spawnPosition;
    float     wanderTimer;
    int       waypointIndex;
    Coroutine rotateRoutine;

    public string Prompt => prompt.Replace("{name}", Character != null ? Character.DisplayName : name);
    public bool CanInteract(Character who) => isActiveAndEnabled && isInteractable && Character != null && Character.IsAlive &&
        who is Player player && player.IsAlive && player.kind == interactionPlayer;

    void Awake()
    {
        Character = GetComponent<Character>();
        Motor     = GetComponent<EnemyMotor>();
        interactTrigger = GetComponentInChildren<InteractableTrigger>(true);
        spawnPosition = transform.position;
        SetInteractable(isInteractable);
    }

    public void SetInteractable(bool value)
    {
        isInteractable = value;
        if (interactTrigger != null) interactTrigger.gameObject.SetActive(value);
    }

    void Start() => SetState(defaultState);

    void Update()
    {
        if (!Character.IsAlive) return;
        switch (State)
        {
            case NPCState.Wander: HandleWander(); break;
            case NPCState.Patrol: HandlePatrol(); break;
        }
        UpdateFacing();
        UpdateAnimator();
    }

    public void SetState(NPCState state)
    {
        State = state;
        wanderTimer = 0f;
        if (state == NPCState.Idle || state == NPCState.Talking) Motor.Stop();
        else Motor.Resume();
        if (state == NPCState.Patrol && waypoints != null && waypoints.Length > 0 && waypoints[waypointIndex] != null)
            Motor.MoveTo(waypoints[waypointIndex].position);
    }

    // ── Interaction ───────────────────────────────────────────────────

    public void Interact(Character who)
    {
        if (!CanInteract(who)) return;
        Pause();
        faceTarget = who != null ? who.transform : null;
    }

    public void EndInteraction() => Resume();

    public void Pause()
    {
        if (State == NPCState.Talking) return;
        stateBeforePause = State;
        SetState(NPCState.Talking);
    }

    public void Resume()
    {
        faceTarget = null;
        SetState(stateBeforePause);
    }

    void UpdateFacing()
    {
        if (faceTarget == null) return;
        Motor.Face(faceTarget.position, faceSpeed);
        if (Motor.IsFacing(faceTarget.position, 0.5f)) faceTarget = null;
    }

    // Smoothly turns toward the active player (cutscenes).
    [Button]
    public void RotateTowardsPlayer(float rotateSpeed = 360f)
    {
        if (!PlayerManager.HasInstance || PlayerManager.Instance.Active == null) return;
        if (rotateRoutine != null) StopCoroutine(rotateRoutine);
        rotateRoutine = StartCoroutine(RotateRoutine(PlayerManager.Instance.Active.transform, rotateSpeed));
    }

    [Button]
    public void SnapRotationTowardsPlayer()
    {
        if (!PlayerManager.HasInstance || PlayerManager.Instance.Active == null) return;
        SnapRotationTowards(PlayerManager.Instance.Active.transform.position);
    }

    public void SnapRotationTowards(Vector3 position)
    {
        if (rotateRoutine != null) { StopCoroutine(rotateRoutine); rotateRoutine = null; }
        faceTarget = null;
        Motor.SnapFace(position);
    }

    IEnumerator RotateRoutine(Transform target, float speed)
    {
        while (!Motor.IsFacing(target.position, 0.5f))
        {
            Motor.Face(target.position, speed);
            yield return null;
        }
        rotateRoutine = null;
    }

    // ── Wander / patrol ───────────────────────────────────────────────

    void HandleWander() =>
        Motor.WanderStep(ref wanderTimer, spawnPosition, minWanderDistance, wanderRadius, wanderZoneRadius, minIdleTime, maxIdleTime);

    void HandlePatrol() => Motor.PatrolStep(waypoints, ref waypointIndex, loopPatrol);

    // Idle/talking NPCs must not run because of placement or vertical settling.
    void UpdateAnimator() =>
        Motor.UpdateLocomotionAnimator(Character.Animator, (State == NPCState.Wander || State == NPCState.Patrol) && Motor.HasPath, 0.1f);

    void OnDrawGizmosSelected()
    {
        if (defaultState != NPCState.Wander || wanderZoneRadius <= 0f) return;
        Vector3 center = Application.isPlaying ? spawnPosition : transform.position;
        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.2f);
        Gizmos.DrawSphere(center, wanderZoneRadius);
        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(center, wanderZoneRadius);
    }
}
