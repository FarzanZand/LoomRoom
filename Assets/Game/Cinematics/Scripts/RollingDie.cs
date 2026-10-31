using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// A six-sided die rolled on the table. Faces by local direction: +Y 1, +X 2, +Z 3, -Z 4, -X 5, -Y 6
// (opposites add to seven); the mesh and texture follow the same order (Tools/dice_texture.py).
[RequireComponent(typeof(Rigidbody))]
public class RollingDie : MonoBehaviour
{
    static readonly (Vector3 dir, int value)[] Faces =
    {
        (Vector3.up, 1), (Vector3.right, 2), (Vector3.forward, 3), (Vector3.back, 4), (Vector3.left, 5), (Vector3.down, 6),
    };

    [Tooltip("Gravity multiplier. The room is built at about twenty times life size, so normal gravity makes a die float.")]
    public float gravityScale = 7f;

    Rigidbody body;
    float stillFor;

    public Rigidbody Body => body != null ? body : body = GetComponent<Rigidbody>();

    // The face looking up.
    public int Value
    {
        get
        {
            int best = 1; float most = -2f;
            foreach (var (dir, value) in Faces)
            {
                float up = Vector3.Dot(transform.TransformDirection(dir), Vector3.up);
                if (up > most) { most = up; best = value; }
            }
            return best;
        }
    }

    // Settled: barely moving for a short while.
    public bool Settled => stillFor > .25f;

    void FixedUpdate()
    {
        if (!Body.isKinematic) Body.AddForce(Physics.gravity * (gravityScale - 1f), ForceMode.Acceleration);
        bool still = Body.linearVelocity.sqrMagnitude < .02f && Body.angularVelocity.sqrMagnitude < .05f;
        stillFor = still ? stillFor + Time.fixedDeltaTime : 0f;
    }

    // Every die on the table, for ClearOf.
    static readonly List<RollingDie> live = new();
    void OnEnable() => live.Add(this);
    void OnDisable() => live.Remove(this);

    // A figure is set down at a spot (ClassFigures): any die lying within Reach of it (plus its own half width) slides
    // out along Away until it is clear, so the last throw's dice can stay on the table without a figure inside one.
    public static void ClearOf(Vector3 spot, Vector3 away, float reach)
    {
        away.y = 0f;
        if (away.sqrMagnitude < 1e-6f) return;
        away.Normalize();
        foreach (var die in live)
        {
            if (die == null) continue;
            var size = die.TryGetComponent<Collider>(out var c) ? c.bounds.extents : Vector3.one * .5f;
            float clear = reach + Mathf.Max(size.x, size.z);
            var to = die.transform.position - spot; to.y = 0f;
            if (to.sqrMagnitude >= clear * clear) continue;
            float along = Vector3.Dot(to, away);
            float side = (to - away * along).magnitude;
            float need = Mathf.Sqrt(Mathf.Max(0f, clear * clear - side * side));
            die.StartCoroutine(die.Slide(away * (need - along)));
        }
    }

    IEnumerator Slide(Vector3 by)
    {
        Body.isKinematic = true;
        var from = transform.position;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / .25f)
        {
            transform.position = from + by * (1f - (1f - t) * (1f - t));
            yield return null;
        }
        transform.position = from + by;
    }

    public void Throw(Vector3 velocity, Vector3 spin)
    {
        stillFor = 0f;
        Body.isKinematic = false;
        Body.linearVelocity = velocity;
        Body.angularVelocity = spin;
    }
}
