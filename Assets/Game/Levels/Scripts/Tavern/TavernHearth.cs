using System.Collections;
using UnityEngine;

// The tavern's fire: resting by it sits the table player down in the nearest chair facing the fire, and their health
// ticks up by a share of its maximum over a few seconds while they sit (the HUD stays in view), once (each floor builds
// its own tavern, so once a floor). Then they stand back up where they were and the fire burns low: its lights dim and
// its flames shrink, and it says so when looked at.
public class TavernHearth : MonoBehaviour, IInteractable
{
    [Range(0, 1), Tooltip("Share of maximum health given back.")]
    public float heal = .2f;
    [Min(.5f), Tooltip("Seconds the health takes to tick up while seated.")]
    public float restSeconds = 5f;
    [Tooltip("Chairs to sit in (the nearest is taken). Empty: rest standing where you are.")]
    public Transform[] seats = new Transform[0];
    [Tooltip("Eye height while seated (the view's local height; standing is 1).")]
    public float seatedEyeHeight = .62f;
    [Min(.1f), Tooltip("Seconds to sit down and to stand up.")]
    public float sitSeconds = .7f;
    [Tooltip("Posted to the feed once rested. {Amount} is the health given back.")]
    public string restedMessage = "You rest by the fire. +{Amount} health.";
    [Tooltip("Said by the Dungeon Master as you sit (optional).")]
    public DungeonMaster.Line restedLine = new() { text = "Warm your hands. It won't last.", mumble = true, inPerson = true };
    [Tooltip("Lights that dim once the fire has been used (its own glow).")]
    public Light[] lights = new Light[0];
    [Tooltip("Flames that shrink once it has been used.")]
    public ParticleSystem[] flames = new ParticleSystem[0];
    [Range(0, 1), Tooltip("How bright the lights stay afterwards.")]
    public float embers = .3f;
    public AudioClip restClip;

    bool used, sitting;
    Player sitter;
    Collider[] seatColliders = new Collider[0];
    public bool Used => used;

    public string Prompt => used ? "The fire has burned low" : $"Rest by the fire (+{Mathf.RoundToInt(heal * 100)}% health)";
    public bool CanInteract(Character who) => !used && DungeonFeatureUtility.TablePlayer(who) != null;

    public void Interact(Character who)
    {
        var player = DungeonFeatureUtility.TablePlayer(who);
        if (used || player == null) return;
        used = true;
        StartCoroutine(Rest(player));
    }

    IEnumerator Rest(Player player)
    {
        sitter = player; sitting = true;
        if (GameManager.HasInstance) { GameManager.Instance.Push(GameState.Cutscene); GameManager.Instance.KeepHud(true); }
        var fire = transform.position + Vector3.up * .5f;
        var standAt = player.transform.position;
        var seat = NearestSeat(player.transform.position);
        // The chair's own colliders would hold the player out of it.
        seatColliders = seat != null ? seat.GetComponentsInChildren<Collider>() : new Collider[0];
        foreach (var c in seatColliders) c.enabled = false;
        var sitAt = seat != null ? new Vector3(seat.position.x, standAt.y, seat.position.z) : standAt;
        if (restClip != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(restClip, transform.position);
        if (restedLine != null && !restedLine.IsEmpty) DungeonMaster.Say(restedLine);
        yield return Move(player, standAt, sitAt, 1f, seatedEyeHeight, fire);

        var stats = player.Stats;
        float total = stats.MaxHealth * heal, given = 0f, owed = 0f;
        for (float t = 0f; t < restSeconds; t += Time.deltaTime)
        {
            // Whole points at a time, so the bar visibly ticks up.
            owed += total * Time.deltaTime / restSeconds;
            float step = Mathf.Floor(owed);
            if (step >= 1f) { float before = stats.CurrentHealth; stats.Heal(step); given += stats.CurrentHealth - before; owed -= step; }
            yield return null;
        }
        if (owed > 0f) { float before = stats.CurrentHealth; stats.Heal(owed); given += stats.CurrentHealth - before; }
        MessageLog.Post(restedMessage.Replace("{Amount}", Mathf.RoundToInt(given).ToString()), MessageKind.Info);
        BurnLow();

        yield return Move(player, sitAt, standAt, seatedEyeHeight, 1f, fire);
        Release();
    }

    // Walks the player between two spots while the eye eases between two heights, looking at the fire.
    IEnumerator Move(Player player, Vector3 from, Vector3 to, float fromHeight, float toHeight, Vector3 fire)
    {
        var look = player.Look;
        float fromYaw = look != null ? look.YawTransform.eulerAngles.y : 0f, fromPitch = look != null ? look.Pitch : 0f;
        for (float t = 0f; t < sitSeconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / sitSeconds);
            player.Warp(Vector3.Lerp(from, to, k));
            if (look != null)
            {
                look.HeightOverride = Mathf.Lerp(fromHeight, toHeight, k);
                CameraEase.YawPitchTo(look, fire, out float yaw, out float pitch);
                look.SetYaw(Mathf.LerpAngle(fromYaw, yaw, k));
                look.SetPitch(Mathf.Lerp(fromPitch, pitch * .5f, k));
            }
            yield return null;
        }
        player.Warp(to);
        if (look != null) look.HeightOverride = toHeight;
    }

    Transform NearestSeat(Vector3 from)
    {
        Transform best = null; float bd = float.MaxValue;
        foreach (var s in seats) { if (s == null || !s.gameObject.activeInHierarchy) continue; float d = (s.position - from).sqrMagnitude; if (d < bd) { bd = d; best = s; } }
        return best;
    }

    void BurnLow()
    {
        foreach (var l in lights) if (l != null) l.intensity *= embers;
        foreach (var f in flames)
            if (f != null) { var main = f.main; main.startSizeMultiplier *= .5f; var emission = f.emission; emission.rateOverTimeMultiplier *= .4f; }
    }

    // Stands the player up and gives control back; also run if the tavern goes while they sit.
    void Release()
    {
        if (!sitting) return;
        sitting = false;
        foreach (var c in seatColliders) if (c != null) c.enabled = true;
        if (sitter != null && sitter.Look != null) sitter.Look.HeightOverride = null;
        if (GameManager.HasInstance) { GameManager.Instance.KeepHud(false); GameManager.Instance.Pop(GameState.Cutscene); }
        sitter = null;
    }

    void OnDisable() => Release();
}
