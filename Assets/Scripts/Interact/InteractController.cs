using System;
using System.Collections.Generic;
using MFPC;
using UnityEngine;
using UnityEngine.InputSystem;

public class InteractController : MonoBehaviour
{
    public event Action<IInteractable> OnActiveChanged;

    private readonly List<InteractableTrigger> inRange = new();
    private IInteractable active;
    private PlayerInputActions inputActions;

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
        SetActiveInteractable(null);
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

    private void RefreshClosest()
    {
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

        SetActiveInteractable(closest?.Interactable);
    }

    private void SetActiveInteractable(IInteractable interactable)
    {
        if (interactable == active) return;
        active = interactable;
        OnActiveChanged?.Invoke(active);
    }

    private void OnInteractInput(InputAction.CallbackContext _)
    {
        active?.Interact(gameObject);
    }
}
