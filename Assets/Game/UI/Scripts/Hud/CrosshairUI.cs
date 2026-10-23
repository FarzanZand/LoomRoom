using UnityEngine;

// A small dot in the middle of the screen while the table player fights; it turns into a shield icon
// while the shield is up. Both graphics are authored in the scene; this only switches them.
public class CrosshairUI : MonoBehaviour
{
    [SerializeField] GameObject dot;
    [SerializeField] GameObject shield;

    void Update()
    {
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
        bool show = player != null && player.kind == PlayerKind.Table && player.IsActive && player.IsAlive &&
            (!GameManager.HasInstance || GameManager.Instance.GameplayActive);
        bool guarding = show && player.Combat != null && player.Combat.IsGuarding;
        if (dot != null && dot.activeSelf != (show && !guarding)) dot.SetActive(show && !guarding);
        if (shield != null && shield.activeSelf != guarding) shield.SetActive(guarding);
    }
}
