using System.Collections.Generic;
using UnityEngine;

// Wall decor that can be knocked down for fun (the user, 2026-10-30: the shield with crossed axes). A blow rings it
// with a clang and its pieces (the children in Pieces: the shield and each axe) come off the wall as loose bodies that
// fall, knock on the floor and lie there for the rest of the floor. It gives nothing and has no health bar.
public class KnockDownDecor : MonoBehaviour, IDamageable
{
    [Tooltip("The parts that come off, each its own child with its own mesh.")] public Transform[] pieces = new Transform[0];
    [Tooltip("Played when struck (one picked at random).")] public AudioClip[] clang = new AudioClip[0];
    [Range(0f, 1f)] public float clangVolume = .9f;
    [Tooltip("A piece knocking on the floor.")] public AudioClip land;
    [Range(0f, 1f)] public float landVolume = .5f;
    [Min(0f), Tooltip("How hard the pieces are thrown off the wall (out of its face, along the blow).")] public float push = 1.6f;
    [Min(0f)] public float spin = 4f;

    bool fallen;

    public void TakeDamage(DamageInfo info)
    {
        if (fallen) return;
        fallen = true;
        if (clang.Length > 0 && AudioManager.HasInstance)
            AudioManager.Instance.PlaySFX(clang[Random.Range(0, clang.Length)], transform.position, clangVolume, 1f, .08f, 2f, 25f);
        foreach (var c in GetComponents<Collider>()) c.enabled = false;
        // Off the wall: out of its face (its forward) and a little along the blow.
        var away = transform.forward;
        if (info.Direction.sqrMagnitude > .01f) away = (away + new Vector3(info.Direction.x, 0f, info.Direction.z).normalized * .5f).normalized;
        var bodies = new List<Rigidbody>();
        var floor = transform.parent;
        foreach (var piece in pieces)
        {
            if (piece == null) continue;
            piece.SetParent(floor, true);
            var renderer = piece.GetComponentInChildren<MeshRenderer>();
            var box = piece.gameObject.AddComponent<BoxCollider>();
            if (renderer != null && piece.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
            { box.center = filter.sharedMesh.bounds.center; box.size = Vector3.Max(filter.sharedMesh.bounds.size, Vector3.one * .03f); }
            var body = piece.gameObject.AddComponent<Rigidbody>();
            body.mass = 2f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = away * push * Random.Range(.7f, 1.2f) + Vector3.up * Random.Range(0f, .6f);
            body.angularVelocity = Random.insideUnitSphere * spin;
            piece.gameObject.AddComponent<KnockDownPiece>().Init(this);
            bodies.Add(body);
        }
        // Nobody trips over them: the players and the living walk through (as a body's pieces do).
        DeathPhysics.IgnorePlayers(bodies);
    }

    public void Landed(Vector3 at, float speed)
    {
        if (land != null && AudioManager.HasInstance && speed > .8f)
            AudioManager.Instance.PlaySFX(land, at, landVolume * Mathf.Clamp01(speed / 4f), 1f, .1f, 1.5f, 18f);
    }
}
