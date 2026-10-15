using System.Collections.Generic;
using UnityEngine;

// Co-op: a dead table player becomes a ghost on the floor until the party reaches the next one (MultiplayerManager
// revives it there). The ghost walks and looks about as before, but can't fight, cast, use or pick up anything,
// open the bag or touch the world, and enemies neither see nor hurt it (it is dead to them). It carries a pale
// light that lights the way for everyone, it walks faster than the living (PlayerMotor.SpeedScale) and its view goes
// cold through its own post-processing (MultiplayerManager.ghostLook, a global volume over everything). The others see
// a pale see-through body (CoopAvatar) and its corpse where it fell.
public class CoopGhost : MonoBehaviour
{
    // The local table player is a ghost (input handlers that would act on the world check this).
    public static bool Active { get; private set; }

    readonly List<Behaviour> switchedOff = new();
    readonly List<GameObject> hidden = new();
    Light glow;
    Player player;
    UnityEngine.Rendering.Volume look;
    float lookFade, lookWeight;

    public static CoopGhost Begin(Player p, Color colour, float intensity, float range, float speed = 1f, UnityEngine.Rendering.VolumeProfile profile = null, float fade = .8f)
    {
        if (p == null) return null;
        var ghost = p.GetComponent<CoopGhost>();
        if (ghost == null) ghost = p.gameObject.AddComponent<CoopGhost>();
        ghost.player = p;
        ghost.Apply(colour, intensity, range, speed, profile, fade);
        return ghost;
    }

    void Apply(Color colour, float intensity, float range, float speed, UnityEngine.Rendering.VolumeProfile profile, float fade)
    {
        Active = true;
        // Fighting, casting, the bash and using things.
        foreach (var b in new Behaviour[] { player.Combat, player.GetComponent<PlayerSpellcasting>(), player.GetComponent<ShieldBash>(), player.GetComponentInChildren<InteractController>(true) })
            if (b != null && b.enabled) { b.enabled = false; switchedOff.Add(b); }
        // The arms and what they hold.
        foreach (var t in player.GetComponentsInChildren<Transform>(true))
            if (t.name == "FirstPersonVisuals" && t.gameObject.activeSelf) { t.gameObject.SetActive(false); hidden.Add(t.gameObject); }
        if (glow == null)
        {
            glow = new GameObject("Ghost light").AddComponent<Light>();
            glow.transform.SetParent(player.transform, false);
            glow.transform.localPosition = new Vector3(0f, 2.2f, .3f);
            glow.type = LightType.Point;
            glow.shadows = LightShadows.None;
        }
        glow.color = colour; glow.intensity = intensity; glow.range = range; glow.enabled = true;
        if (player.Motor != null) player.Motor.SpeedScale = speed;
        if (profile != null && look == null)
        {
            look = new GameObject("Ghost look").AddComponent<UnityEngine.Rendering.Volume>();
            look.isGlobal = true;
            look.priority = 500f;   // over the room's, the floor's and the screen effects' volumes
            look.sharedProfile = profile;
            look.weight = lookWeight = 0f;
        }
        lookFade = fade;
    }

    void Update()
    {
        if (look == null) return;
        // In over the fade, then a slow breath so the view never settles.
        lookWeight = lookFade > 0f ? Mathf.MoveTowards(lookWeight, 1f, Time.unscaledDeltaTime / lookFade) : 1f;
        look.weight = lookWeight * (.88f + .12f * Mathf.Sin(Time.unscaledTime * 1.3f));
    }

    public void End()
    {
        Active = false;
        foreach (var b in switchedOff) if (b != null) b.enabled = true;
        switchedOff.Clear();
        foreach (var g in hidden) if (g != null) g.SetActive(true);
        hidden.Clear();
        if (glow != null) Destroy(glow.gameObject);
        if (player != null && player.Motor != null) player.Motor.SpeedScale = 1f;
        if (look != null) Destroy(look.gameObject);
        if (ScreenManager.HasInstance) ScreenManager.Instance.SetDeathEffect(0f);
        Destroy(this);
    }

    void OnDestroy()
    {
        if (Active && switchedOff.Count > 0) Active = false;
        if (look != null) Destroy(look.gameObject);
        if (player != null && player.Motor != null) player.Motor.SpeedScale = 1f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Active = false;
}
