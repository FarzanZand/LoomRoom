using System.Collections;
using TMPro;
using UnityEngine;

// The pencil character sheet the Dungeon Master slides across the table in the tabletop intro: the
// class at the top and a row per rolled attribute, filled in as the dice come up.
public class CharacterSheet : MonoBehaviour
{
    public TMP_Text className, description;
    [Tooltip("One per rolled attribute, in roll order: its label and the number written next to it.")]
    public TMP_Text[] labels = new TMP_Text[0], values = new TMP_Text[0];
    [Tooltip("Pencilled across the sheet when the character dies.")]
    public TMP_Text stamp;
    [Tooltip("Where it lies in front of the player once slid across.")]
    public Transform rest;
    [Tooltip("Where it starts, on the Dungeon Master's side.")]
    public Transform from;
    [Tooltip("Where it is pushed once filled in, out of the way of the figure.")]
    public Transform aside;
    [Min(.05f)] public float slideSeconds = .8f;
    [Min(.01f), Tooltip("Seconds per character as a number is pencilled in.")] public float writeSeconds = .12f;

    void Awake() => gameObject.SetActive(false);

    public void Clear(string cls, string blurb, string[] attributeNames)
    {
        if (className != null) className.text = cls;
        if (description != null) description.text = blurb;
        for (int i = 0; i < labels.Length; i++) if (labels[i] != null) labels[i].text = i < attributeNames.Length ? attributeNames[i] : "";
        foreach (var v in values) if (v != null) v.text = "";
        if (stamp != null) stamp.gameObject.SetActive(false);
    }

    // Slid across the table, from his side to the player's.
    public IEnumerator SlideIn()
    {
        gameObject.SetActive(true);
        if (rest == null) yield break;
        var start = from != null ? from : rest;
        for (float t = 0f; t < slideSeconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / slideSeconds);
            transform.SetPositionAndRotation(Vector3.Lerp(start.position, rest.position, k), Quaternion.Slerp(start.rotation, rest.rotation, k));
            yield return null;
        }
        transform.SetPositionAndRotation(rest.position, rest.rotation);
    }

    // Pushed to the side of the table, still readable.
    public IEnumerator SlideAside()
    {
        if (aside == null) yield break;
        Vector3 p0 = transform.position; Quaternion r0 = transform.rotation;
        for (float t = 0f; t < slideSeconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / slideSeconds);
            transform.SetPositionAndRotation(Vector3.Lerp(p0, aside.position, k), Quaternion.Slerp(r0, aside.rotation, k));
            yield return null;
        }
        transform.SetPositionAndRotation(aside.position, aside.rotation);
    }

    public IEnumerator Write(int row, string text)
    {
        if (row < 0 || row >= values.Length || values[row] == null) yield break;
        for (int i = 1; i <= text.Length; i++)
        {
            values[row].text = text.Substring(0, i);
            yield return new WaitForSeconds(writeSeconds);
        }
    }

    public void Hide() => gameObject.SetActive(false);

    // Back in front of the player, the character still on it.
    public void ShowAgain()
    {
        gameObject.SetActive(true);
        if (rest != null) transform.SetPositionAndRotation(rest.position, rest.rotation);
        if (stamp != null) stamp.gameObject.SetActive(false);
    }

    // Crossed out: the stamp lands with a little overshoot.
    public IEnumerator CrossOut()
    {
        if (stamp == null) yield break;
        stamp.gameObject.SetActive(true);
        var t = stamp.rectTransform;
        for (float k = 0f; k < 1f; k += Time.deltaTime / .2f)
        {
            t.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, k * k);
            yield return null;
        }
        t.localScale = Vector3.one;
    }
}
