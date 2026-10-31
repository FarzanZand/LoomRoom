using System.Collections;
using UnityEngine;

// The dice room's cup (on the gambler's table, with an InteractableTrigger on its collider): shaking it asks the Dungeon
// Master for his roll (DiceEvent on the room's root). The cup rattles in place as it is shaken.
public class DiceCup : MonoBehaviour, IInteractable
{
    [Tooltip("How far it jiggles while shaken.")] public float shake = .03f;
    DiceEvent dice;
    Vector3 rest;
    Quaternion restTurn;

    void Awake() { dice = GetComponentInParent<DiceEvent>(); rest = transform.localPosition; restTurn = transform.localRotation; }

    public string Prompt => dice != null && dice.CanRoll ? dice.cupPrompt : "";
    public bool CanInteract(Character who) => who is Player && dice != null && dice.CanRoll;
    public void Interact(Character who)
    {
        if (!CanInteract(who)) return;
        StartCoroutine(Rattle());
        dice.Shake(who);
        CoopWorld.EventUsed(dice, 0);
    }

    IEnumerator Rattle()
    {
        for (float t = 0; t < .6f; t += Time.deltaTime)
        {
            float k = Mathf.Sin(t * 70f) * (1f - t / .6f);
            transform.localPosition = rest + new Vector3(k * shake, Mathf.Abs(k) * shake * .5f, 0f);
            transform.localRotation = restTurn * Quaternion.Euler(0f, 0f, k * 12f);
            yield return null;
        }
        transform.localPosition = rest; transform.localRotation = restTurn;
    }
}
