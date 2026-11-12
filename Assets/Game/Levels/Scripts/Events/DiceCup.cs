using System.Collections;
using UnityEngine;

// The dice room's cup (on the gambler's table, with an InteractableTrigger on its collider): shaking it asks the Dungeon
// Master for his roll (DiceEvent on the room's root). The cup rattles in place as it is shaken. Until it is, it glows with
// the quest outline (QuestGlow), so the eye finds it on the table (the user, 2026-11-11).
public class DiceCup : MonoBehaviour, IInteractable
{
    [Tooltip("How far it jiggles from side to side while shaken, in metres.")] public float shake = .03f;
    [Tooltip("How high it is lifted off the table while shaken, in metres.")] public float lift = .08f;
    [Tooltip("How long the shake lasts, in seconds.")] public float rattleSeconds = .8f;
    [Min(0), Tooltip("Width of its outline while it waits to be shaken, in metres (.012 was too thin to see, .03 too heavy: the user, 2026-11-12).")] public float outlineWidth = .015f;
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

    // Picked up off the table, rattled hard from side to side and set back down. (The Synty mug hung upside down from a
    // pivot in the air, so the old small wobble swung it a long way; the leather cup stands on its base and the same
    // wobble barely moved it: the user, 2026-11-12, "it does not shake".)
    IEnumerator Rattle()
    {
        for (float t = 0; t < rattleSeconds; t += Time.deltaTime)
        {
            float u = t / rattleSeconds;
            float up = Mathf.Sin(Mathf.Clamp01(u / .15f) * Mathf.PI * .5f) * Mathf.Clamp01((1f - u) / .15f);   // up, held, down
            float k = Mathf.Sin(t * 42f);
            transform.localPosition = rest + new Vector3(k * shake * up, up * lift + Mathf.Abs(k) * shake * .3f * up, 0f);
            transform.localRotation = restTurn * Quaternion.Euler(Mathf.Sin(t * 31f) * 7f * up, 0f, k * 18f * up);
            yield return null;
        }
        transform.localPosition = rest; transform.localRotation = restTurn;
    }
}
