using UnityEngine;

[ExecuteAlways]
public sealed class SkylightCover : MonoBehaviour
{
    [Tooltip("Close the opaque skylight cover and turn off its daylight contribution.")]
    public bool covered;
    [SerializeField] GameObject cover;
    [SerializeField] Light[] daylight;
    public void Configure(GameObject panel, Light[] lights) { cover = panel; daylight = lights; Apply(); }
    void OnEnable() => Apply();
    void Update() => Apply();
    public void SetCovered(bool value) { covered = value; Apply(); }
    void Apply()
    {
        if (cover && cover.activeSelf != covered) cover.SetActive(covered);
        if (daylight == null) return;
        foreach (var light in daylight) if (light && light.enabled == covered) light.enabled = !covered;
    }
}
