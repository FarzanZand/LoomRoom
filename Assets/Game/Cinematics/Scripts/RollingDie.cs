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

    public void Throw(Vector3 velocity, Vector3 spin)
    {
        stillFor = 0f;
        Body.isKinematic = false;
        Body.linearVelocity = velocity;
        Body.angularVelocity = spin;
    }
}
