using UnityEngine;

// The small banner floating over a place on the realm map not yet visited (CampaignManager.Markers): it turns about its
// upright to face the view and bobs slowly. On the banner prefab, Resources/Campaign new place banner.
public class CampaignBanner : MonoBehaviour
{
    [Tooltip("How far it rises and sinks, in the map's units.")] public float bob = .25f;
    [Tooltip("Seconds for one rise and fall.")] public float period = 2.4f;

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
        var look = cam.transform.position - transform.position; look.y = 0f;
        if (look.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(-look.normalized, Vector3.up);
    }
}
