using UnityEngine;

// F8 (Dev input map) or the dev console's "god" (Editor and development builds only): god mode. While it is on, both
// players take no damage and are kept at full health, stamina and mana every frame, so a fight can be watched or tested
// without dying.
public class DevGodMode : MonoBehaviour
{
    public static bool On { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        On = false;
        CharacterStats.Invulnerable = null;
        if (!Debug.isDebugBuild) return;
        var go = new GameObject("Dev god mode") { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(go);
        go.AddComponent<DevGodMode>();
    }

    InputManager input;

    void Update()
    {
        if (input == null && InputManager.HasInstance) { input = InputManager.Instance; input.DebugGodModeToggled += Toggle; }
        if (!On || !PlayerManager.HasInstance) return;
        foreach (var kind in new[] { PlayerKind.Table, PlayerKind.Room })
        {
            var p = PlayerManager.Instance.GetPlayer(kind);
            if (p == null || p.Stats == null || !p.IsAlive) continue;
            p.Stats.Heal(99999f);
            p.Stats.RestoreMana(99999f);
            p.Stats.RestoreStamina(99999f);
        }
    }

    void Toggle() => Set(!On);

    public static void Set(bool on)
    {
        On = on;
        CharacterStats.Invulnerable = on ? IsPlayer : null;
        NotificationUI.Show(On ? "God mode on" : "God mode off");
    }

    static bool IsPlayer(CharacterStats stats) => stats != null && stats.GetComponentInParent<Player>() != null;

    void OnDestroy() { if (input != null) input.DebugGodModeToggled -= Toggle; }
}
