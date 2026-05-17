using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.AI;

public enum NPCState { Idle, Wander, Patrol }

public class NPCController : MovementBase, IInteractable
{
    [Header("Behaviour")]
    [SerializeField] NPCState defaultState = NPCState.Idle;

    [Header("Interaction")]
    [SerializeField] bool isInteractable = false;

    [ShowIf("isInteractable")]
    [Tooltip("Speed in degrees per second the NPC turns to face the player.")]
    [SerializeField] float faceSpeed = 180f;

    NPCState currentState;
    NPCState stateBeforePause;
    InteractableTrigger interactTrigger;
    Transform faceTarget;

    protected override bool ShowWanderFields() => defaultState == NPCState.Wander;
    protected override bool ShowPatrolFields() => defaultState == NPCState.Patrol;

    protected override void Awake()
    {
        base.Awake();
        interactTrigger = GetComponentInChildren<InteractableTrigger>();
        SetInteractable(isInteractable);
    }

    public void SetInteractable(bool value)
    {
        isInteractable = value;
        if (interactTrigger != null)
            interactTrigger.gameObject.SetActive(value);
    }

    void Start()
    {
        SetState(defaultState);
    }

    void Update()
    {
        if (!IsAlive) return;
        UpdateGroundCheck();
        UpdateState();
        UpdateFacing();
        UpdateAnimatorParams();
    }

    public void SetState(NPCState state)
    {
        currentState = state;
        wanderTimer = 0f;

        if (state == NPCState.Idle)
            StopMoving();
        else if (state == NPCState.Patrol)
            StartPatrol();
    }

    public override void Pause()
    {
        stateBeforePause = currentState;
        base.Pause();
        currentState = NPCState.Idle;
    }

    public override void Resume()
    {
        faceTarget = null;
        base.Resume();
        SetState(stateBeforePause);
    }

    public void Interact(GameObject interactor)
    {
        if (!isInteractable) return;
        Pause();
        faceTarget = interactor.transform;
    }

    public void EndInteraction()
    {
        Resume();
    }

    void UpdateFacing()
    {
        if (faceTarget == null) return;
        FaceTo(faceTarget, faceSpeed);
        Vector3 dir = faceTarget.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
        {
            if (Quaternion.Angle(transform.rotation, Quaternion.LookRotation(dir)) < 0.5f)
                faceTarget = null;
        }
    }

    void UpdateState()
    {
        switch (currentState)
        {
            case NPCState.Wander: HandleWanderMovement(); break;
            case NPCState.Patrol: HandlePatrolMovement(); break;
        }
    }

    void OnDrawGizmosSelected()
    {
        if (defaultState != NPCState.Wander || wanderZoneRadius <= 0f) return;

        Vector3 center = wanderZoneCenter != null ? wanderZoneCenter.position :
                         (Application.isPlaying ? spawnPosition : transform.position);

        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.2f);
        Gizmos.DrawSphere(center, wanderZoneRadius);
        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(center, wanderZoneRadius);
    }
}
