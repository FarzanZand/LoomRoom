using UnityEngine;

// The mark floating over a place on the realm map not yet visited (CampaignManager.Markers): it turns to face the view
// and bobs slowly. On Resources/Campaign new place mark (the yellow question mark, which faces the eye fully so it
// reads from the raised map view too) and on the older Resources/Campaign new place banner (about its upright only).
public class CampaignBanner : MonoBehaviour
{
    [Tooltip("How far it rises and sinks, in the map's units.")] public float bob = .25f;
    [Tooltip("Seconds for one rise and fall.")] public float period = 2.4f;
    [Tooltip("Faces the eye fully, tipping back when looked down on (a flat mark seen from the raised map view); off, it only turns about its upright.")]
    public bool faceFully;

    Vector3 rest;
    float phase;

    void OnEnable() { rest = transform.localPosition; phase = Random.value * period; }

    // Moved by its owner (set over a new place): the bob goes on from there.
    public void SetRest(Vector3 local) { rest = local; transform.localPosition = local; }

    void LateUpdate()
    {
        float t = (Time.time + phase) / Mathf.Max(.1f, period) * Mathf.PI * 2f;
        transform.localPosition = rest + Vector3.up * (Mathf.Sin(t) * bob);
        var cam = Camera.main;
        if (cam == null) return;
        if (faceFully) { transform.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up); return; }
        var look = cam.transform.position - transform.position; look.y = 0f;
        if (look.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(-look.normalized, Vector3.up);
    }
}
