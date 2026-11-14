using UnityEngine;

// Shows a PropInfo's tooltip while the room player's crosshair rests on it (the user, 2026-11-14), up and right of the
// crosshair, and takes it away when the look leaves it. Made by the first PropInfo; nothing to set up.
public class PropInfoLook : MonoBehaviour
{
    // How far off the props answer, in room units (about 22 to the metre).
    const float Reach = 110f;

    static PropInfoLook instance;
    PropInfo showing;
    readonly RaycastHit[] hits = new RaycastHit[32];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;

    public static void Ensure()
    {
        if (instance != null || !Application.isPlaying) return;
        instance = new GameObject("Prop info look").AddComponent<PropInfoLook>();
    }

    void OnDestroy() { if (instance == this) instance = null; }

    void LateUpdate()
    {
        var found = Looked();
        if (found == showing) return;
        if (!TooltipUI.HasInstance) { showing = null; return; }
        if (found == null) { TooltipUI.Instance.Hide(); showing = null; return; }
        var (title, kind, body) = found.Describe();
        // Just up and right of the crosshair (the screen's middle).
        TooltipUI.Instance.ShowTextAt(title, kind, body, new Vector2(Screen.width * .5f - 1f, Screen.height * .5f - 1f));
        showing = found;
    }

    // The nearest shown prop on the crosshair's line, in reach and in sight; none outside play or for the table player.
    PropInfo Looked()
    {
        if (GameManager.HasInstance && !GameManager.Instance.GameplayActive) return null;
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
        if (player == null || player.kind != PlayerKind.Room) return null;
        var cam = Camera.main;
        if (cam == null) return null;
        var ray = new Ray(cam.transform.position, cam.transform.forward);
        PropInfo best = null; float nearest = Reach;
        foreach (var info in PropInfo.All)
        {
            if (info == null || info.Describe == null || !info.Shown(out var bounds)) continue;
            if (!bounds.IntersectRay(ray, out float distance) || distance > nearest) continue;
            if (!Clear(ray, distance, info)) continue;
            best = info; nearest = distance;
        }
        return best;
    }

    // Nothing solid in front of it but itself and the player.
    bool Clear(Ray ray, float distance, PropInfo info)
    {
        int count = Physics.RaycastNonAlloc(ray, hits, distance, ~0, QueryTriggerInteraction.Ignore);
        var player = PlayerManager.Instance.Active;
        for (int i = 0; i < count; i++)
        {
            var t = hits[i].transform;
            if (info.Owns(t) || player != null && t.IsChildOf(player.transform)) continue;
            return false;
        }
        return true;
    }
}
