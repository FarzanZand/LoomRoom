using System.Collections;
using UnityEngine;

// The dice room's cup (on the gambler's table, with an InteractableTrigger on its collider): shaking it asks the Dungeon
// Master for his roll (DiceEvent on the room's root). The cup rattles in place as it is shaken. Until it is, it glows with
// the quest outline (QuestGlow), so the eye finds it on the table (the user, 2026-11-11).
public class DiceCup : MonoBehaviour, IInteractable
{
    [Tooltip("How far it jiggles while shaken.")] public float shake = .03f;
    [Min(0), Tooltip("Width of its outline while it waits to be shaken, in metres (.012 was too thin to see).")] public float outlineWidth = .03f;
    DiceEvent dice;
    QuestGlow glow;
    Vector3 rest;
    Quaternion restTurn;

    void Awake() { dice = GetComponentInParent<DiceEvent>(); rest = transform.localPosition; restTurn = transform.localRotation; }

    void Start()
    {
        glow = gameObject.AddComponent<QuestGlow>();
        glow.width = outlineWidth;
    }

    void Update() { if (glow != null) glow.Show(dice != null && dice.CanRoll); }

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
