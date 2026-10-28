using System.Collections;
using UnityEngine;

// The bell room's rope (with an InteractableTrigger on its collider): pulling it rings the bell (BellEvent on the room's
// root). The rope dips as it is pulled.
public class BellRope : MonoBehaviour, IInteractable
{
    [Tooltip("Pulled down this far, then let back up.")] public float pull = .35f;
    BellEvent bell;
    Vector3 rest;

    void Awake() { bell = GetComponentInParent<BellEvent>(); rest = transform.localPosition; }

    public string Prompt => bell != null ? bell.RopePrompt : "";
    public bool CanInteract(Character who) => who is Player && bell != null && bell.CanRing;
    public void Interact(Character who)
    {
        if (!CanInteract(who)) return;
        StartCoroutine(Pull());
        bell.Ring(who as Player);
    }

    IEnumerator Pull()
    {
        for (float t = 0; t < .5f; t += Time.deltaTime)
        {
            float u = t / .5f;
            transform.localPosition = rest + Vector3.down * pull * Mathf.Sin(u * Mathf.PI);
            yield return null;
        }
        transform.localPosition = rest;
    }
}
