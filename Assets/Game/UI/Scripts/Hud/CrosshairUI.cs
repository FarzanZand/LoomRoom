using UnityEngine;

// A small dot in the middle of the screen while the table player fights; it turns into a shield icon
// while the shield is up. The room player gets a smaller, fainter dot of their own while walking about (the user,
// 2026-11-15), gone in menus, cutscenes, the map view and seated at the table (none of those is the Explore state). All
// three graphics are authored in the scene; this only switches them.
public class CrosshairUI : MonoBehaviour
{
    [SerializeField] GameObject dot;
    [SerializeField] GameObject shield;
    [SerializeField, Tooltip("The room player's dot (smaller and fainter than the fight's).")] GameObject roomDot;

    void Update()
    {
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
        bool playing = player != null && player.IsActive && player.IsAlive && (!GameManager.HasInstance || GameManager.Instance.GameplayActive);
        bool show = playing && player.kind == PlayerKind.Table;
        bool room = playing && player.kind == PlayerKind.Room;
        if (roomDot != null && roomDot.activeSelf != room) roomDot.SetActive(room);
        bool guarding = show && player.Combat != null && player.Combat.IsGuarding;
        if (dot != null && dot.activeSelf != (show && !guarding)) dot.SetActive(show && !guarding);
        if (shield != null && shield.activeSelf != guarding) shield.SetActive(guarding);
    }
}
