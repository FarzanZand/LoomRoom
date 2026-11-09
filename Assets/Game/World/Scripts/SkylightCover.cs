using UnityEngine;

// A skylight's opaque cover: closed shows the panel and turns its daylight lights off. Set in the
// Inspector (applied on enable and when the toggle changes, also in edit mode); nothing changes it at runtime.
[ExecuteAlways]
public sealed class SkylightCover : MonoBehaviour
{
    [Tooltip("Close the opaque skylight cover and turn off its daylight contribution.")]
    public bool covered;
    [SerializeField] GameObject cover;
    [SerializeField] Light[] daylight;
    void OnEnable() => Apply();
#if UNITY_EDITOR
    // SetActive is not allowed inside OnValidate itself, so apply right after it.
    void OnValidate() => UnityEditor.EditorApplication.delayCall += () => { if (this != null) Apply(); };
#endif
    void Apply()
    {
        if (cover && cover.activeSelf != covered) cover.SetActive(covered);
        if (daylight == null) return;
        foreach (var light in daylight) if (light && light.enabled == covered) light.enabled = !covered;
    }
}
