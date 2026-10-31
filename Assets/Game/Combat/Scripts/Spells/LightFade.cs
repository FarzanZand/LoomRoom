using UnityEngine;

// Fades a light out and removes it (the flick's mark light, let go as the enemy flies).
public class LightFade : MonoBehaviour
{
    public float seconds = .4f;
    Light lit; float from, t;
    void Awake() { lit = GetComponent<Light>(); from = lit != null ? lit.intensity : 0f; }
    void Update()
    {
        t += Time.deltaTime;
        if (lit != null) lit.intensity = from * Mathf.Clamp01(1f - t / seconds);
        if (t >= seconds) Destroy(gameObject);
    }
}
