using System;
using System.Collections.Generic;
using MFPC;
using UnityEngine;
using UnityEngine.InputSystem;

public class InteractController : MonoBehaviour
{
    public event Action<InteractableTrigger> OnActiveChanged;

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
            if (sqrDist < minSqrDist)
            {
                minSqrDist = sqrDist;
                closest = inRange[i];
            }
        }

        SetActiveTrigger(closest);
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
