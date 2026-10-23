using DG.Tweening;
using UnityEngine;

// Sneaking (the table player crouched): an eye on the HUD, open and orange while an enemy sees them, shut while none
// does. Crouched, the ghost's look in grey comes over the view (look, Stealth look: Ghost look with its tints made
// neutral grey) at no more than half strength: a little of it while crouched, the half while hidden (seen by no one),
// where what they hear is also muffled (a low-pass filter on the audio listener); it eases back as they are seen or
// stand up.
public class StealthIndicatorUI : MonoBehaviour
{
    [SerializeField] CanvasGroup visibility;
    [SerializeField] StealthEyeGraphic eye;
    [SerializeField] Color hiddenColor = new(.85f, .82f, .72f), seenColor = new(.95f, .58f, .35f);
    [SerializeField, Min(0)] float transitionDuration = .18f;
    [Header("Hidden")]
    [SerializeField, Tooltip("The view while sneaking (Characters/Players/Settings/Stealth look), blended in over everything.")]
    UnityEngine.Rendering.VolumeProfile look;
    [SerializeField, Range(0, 1), Tooltip("How strong the look is while hidden (1 = as strong as the profile).")] float hiddenLook = .5f;
    [SerializeField, Range(0, 1), Tooltip("How strong the look is while crouched but seen.")] float crouchedLook = .2f;
    [SerializeField, Min(10), Tooltip("Low-pass cutoff (Hz) of everything heard while hidden; 22000 is unmuffled.")] float muffledCutoff = 1600f;
    [SerializeField, Min(.01f), Tooltip("Seconds the look and the muffling take to come and go.")] float hiddenFade = .5f;
    Tween transition;
    bool? lastSeen;
    float hiddenAmount, crouchAmount;
    AudioLowPassFilter muffle;
    UnityEngine.Rendering.Volume volume;

    const float OpenCutoff = 22000f;

    void OnEnable() { if (visibility != null) visibility.alpha = 0; lastSeen = null; }
    void OnDisable()
    {
        transition?.Kill(); if (visibility != null) visibility.alpha = 0; lastSeen = null;
        hiddenAmount = crouchAmount = 0f; Apply();
    }

    void OnDestroy() { if (volume != null) Destroy(volume.gameObject); }

    void Update()
    {
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
        bool sneaking = player != null && player.kind == PlayerKind.Table && player.IsActive && player.IsAlive && player.Motor != null && player.Motor.IsCrouching;
        bool seen = false;
        if (sneaking)
            foreach (var brain in EnemyBrain.Active)
                if (brain != null && brain.Character != null && brain.Character.IsAlive && brain.Perception != null && brain.Perception.Target == player && brain.Perception.TargetVisible) { seen = true; break; }
        bool exploring = !(GameManager.HasInstance && GameManager.Instance.State != GameState.Explore);
        bool hidden = sneaking && !seen && exploring;
        hiddenAmount = Mathf.MoveTowards(hiddenAmount, hidden ? 1f : 0f, Time.unscaledDeltaTime / hiddenFade);
        crouchAmount = Mathf.MoveTowards(crouchAmount, sneaking && exploring ? 1f : 0f, Time.unscaledDeltaTime / hiddenFade);
        Apply();

        if (eye == null || visibility == null) return;
        visibility.alpha = sneaking ? 1 : 0;
        if (!sneaking) { transition?.Kill(); lastSeen = null; return; }
        if (lastSeen == seen) return;
        transition?.Kill();
        eye.color = seen ? seenColor : hiddenColor;
        float target = seen ? 1 : 0;
        if (!lastSeen.HasValue) eye.Openness = target;
        else transition = DOTween.To(() => eye.Openness, value => eye.Openness = value, target, transitionDuration).SetEase(Ease.OutCubic).SetUpdate(true);
        lastSeen = seen;
    }

    void Apply()
    {
        float k = hiddenAmount * hiddenAmount * (3f - 2f * hiddenAmount);
        float c = crouchAmount * crouchAmount * (3f - 2f * crouchAmount);
        // The look: a little while crouched, more while hidden; steady, no pulsing.
        float weight = Mathf.Lerp(c * crouchedLook, hiddenLook, k);
        if (look != null && (weight > 0f || volume != null))
        {
            if (volume == null)
            {
                // Its own object on the default layer (cameras' volume masks take it; the HUD's UI layer they may not).
                volume = new GameObject("Stealth look").AddComponent<UnityEngine.Rendering.Volume>();
                volume.gameObject.layer = 0;
                volume.isGlobal = true; volume.priority = 40f; volume.sharedProfile = look;
            }
            volume.weight = weight;
            volume.enabled = weight > 0f;
        }
        // The filter lives on the listener (the camera the player hears through); added the first time it is needed.
        if (muffle == null)
        {
            if (k <= 0f) return;
            var listener = FindAnyObjectByType<AudioListener>();
            if (listener == null) return;
            muffle = listener.GetComponent<AudioLowPassFilter>();
            if (muffle == null) { muffle = listener.gameObject.AddComponent<AudioLowPassFilter>(); muffle.lowpassResonanceQ = 1f; }
        }
        // Eased in the log of the frequency, so the muffling sounds even all the way.
        muffle.cutoffFrequency = Mathf.Exp(Mathf.Lerp(Mathf.Log(OpenCutoff), Mathf.Log(muffledCutoff), k));
        muffle.enabled = k > 0f;
    }
}
