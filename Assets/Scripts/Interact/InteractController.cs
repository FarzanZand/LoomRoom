using System;
using System.Collections.Generic;
using MFPC;
using UnityEngine;
using UnityEngine.InputSystem;

public class InteractController : MonoBehaviour
{
    public event Action<InteractableTrigger> OnActiveChanged;

    [SerializeField] float facingDotThreshold = 0.5f; // ~60 degrees

    private readonly List<InteractableTrigger> inRange = new();
    private InteractableTrigger activeTrigger;
    private PlayerInputActions inputActions;
    private bool blocked;

    private void Awake()
    {
        inputActions = new PlayerInputActions();
    }

    private void OnEnable()
    {
        inputActions.Enable();
        inputActions.Player.Interact.performed += OnInteractInput;
    }

    private void OnDisable()
    {
        inputActions.Player.Interact.performed -= OnInteractInput;
        inputActions.Disable();
        SetActiveTrigger(null);
    }

    private void Update()
    {
        RefreshClosest();
    }

    public void Register(InteractableTrigger trigger)
    {
        if (!inRange.Contains(trigger))
            inRange.Add(trigger);
    }

    public void Unregister(InteractableTrigger trigger)
    {
        inRange.Remove(trigger);
    }

    public void SetBlocked(bool value)
    {
        blocked = value;
        if (blocked) SetActiveTrigger(null);
    }

    private void RefreshClosest()
    {
        if (blocked) return;

        InteractableTrigger closest = null;
        float minSqrDist = float.MaxValue;

        for (int i = inRange.Count - 1; i >= 0; i--)
        {
            if (inRange[i] == null) { inRange.RemoveAt(i); continue; }
            float sqrDist = (inRange[i].transform.position - transform.position).sqrMagnitude;
            if (sqrDist < minSqrDist && IsFacing(inRange[i].transform.position))
            {
                minSqrDist = sqrDist;
                closest = inRange[i];
            }
        }

        SetActiveTrigger(closest);
    }

    private bool IsFacing(Vector3 targetPosition)
    {
        Vector3 toTarget = targetPosition - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.001f) return true;
        Vector3 forward = transform.forward;
        forward.y = 0f;
        return Vector3.Dot(forward.normalized, toTarget.normalized) >= facingDotThreshold;
    }

    private void SetActiveTrigger(InteractableTrigger trigger)
    {
        if (trigger == activeTrigger) return;
        activeTrigger = trigger;
        OnActiveChanged?.Invoke(activeTrigger);
    }

    private void OnInteractInput(InputAction.CallbackContext _)
    {
        if (blocked) return;
        activeTrigger?.Interact(gameObject);
    }
}
