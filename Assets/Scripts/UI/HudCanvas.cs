using UnityEngine;

// Hides the HUD canvas when GameManager says so (cutscenes).
public class HudCanvas : MonoBehaviour
{
    [SerializeField] Canvas canvas;

    void Awake()
    {
        if (canvas == null) canvas = GetComponent<Canvas>();
    }

    void OnEnable()
    {
        if (GameManager.HasInstance) GameManager.Instance.HudVisibilityChanged += SetVisible;
    }

    void Start()
    {
        if (GameManager.HasInstance)
        {
            GameManager.Instance.HudVisibilityChanged -= SetVisible;
            GameManager.Instance.HudVisibilityChanged += SetVisible;
        }
    }

    void OnDisable()
    {
        if (GameManager.HasInstance) GameManager.Instance.HudVisibilityChanged -= SetVisible;
    }

    void SetVisible(bool visible)
    {
        if (canvas != null) canvas.enabled = visible;
    }
}
