using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Events;

// The scripted table reveal, as a list of beat-timed steps instead of a hand-written
// coroutine. Each step waits N beats of the intro music, then does everything it has
// filled in. Add or reorder steps in the Inspector.
public class TableIntroController : MonoBehaviour
{
    [Serializable]
    public class BeatStep
    {
        [Tooltip("Name shown in this step's collapsed header. Leave empty to use an automatic action label.")]
        public string label;

        public string DisplayLabel
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(label)) return label;
                if (!string.IsNullOrEmpty(animatorTrigger)) return animatorTrigger;
                if (activate != null)
                    foreach (var go in activate)
                        if (go != null) return "Reveal " + go.name;
                if (revealLighting) return "Lighting reveal";
                if (mood != null) return "Mood: " + mood.name;
                if (unfreezePlayer) return "Return player control";
                return "Pause / custom action";
            }
        }

        [LabelText("Wait Beats"), Min(0f)]
        [Tooltip("Additional beats after the previous step, not a timestamp. Zero runs alongside the previous step.")]
        public float beats = 4f;
        [Tooltip("Objects switched on at this step.")]
        public GameObject[] activate;
        [HideInInspector] public Transform pointTarget;
        [Tooltip("Play the Dungeon Master pointing gesture before this reveal, without rotating him.")]
        public bool pointBeforeReveal;
        [Tooltip("Sound played (2D).")]
        public AudioData sfx;
        [Tooltip("Or a key from the AudioManager SFX library.")]
        public string sfxKey;
        [Tooltip("Animator + trigger fired at this step.")]
        public Animator animator;
        public string animatorTrigger;
        [Tooltip("NPC snapped to face the player at this step.")]
        public NpcBrain npcFacePlayer;
        [Tooltip("ObjectController whose move starts at this step.")]
        public ObjectController startMove;
        [Tooltip("Run the reveal configured on the Lighting Manager GameObject.")]
        public bool revealLighting;
        [Tooltip("Optional scene mood applied at this step. Overrides the directional-light-only fade below. Edit the preset inline to tune sky, ambient, fog and main light.")]
        [InlineEditor]
        public SceneMood mood;
        [ShowIf("@mood != null"), Min(0f)]
        public float moodFadeDuration = 3f;
        [Tooltip("Fade the directional light at this step.")]
        public bool fadeLight;
        [ShowIf("fadeLight")] public float lightIntensity = 1.4f;
        [ShowIf("fadeLight")] public Color lightColor = Color.white;
        [ShowIf("fadeLight")] public float lightFadeDuration = 3f;
        [Tooltip("Hand control back to the player at this step.")]
        public bool unfreezePlayer;
        public UnityEvent onStep;
    }

    [Header("Music")]
    public AudioClip tableIntroAudio;
    public float musicBPM = 120f;
    [Min(0f), Tooltip("Seconds after the player swap before music starts. Beat steps still count from the swap, so this offsets the music against the reveal. Zero preserves the original timing.")]
    public float audioPlayDelay;

    public LightingManager lightingManager;
    [Header("Dungeon Master pointing")]
    public Animator dungeonMasterAnimator;
    [Min(0f)] public float pointLeadBeats = 2f;

    [Header("Opening")]
    [Tooltip("Screen fade in/hold/out when the intro starts.")]
    public float fadeInDuration = 2f, fadedDuration = 3f, fadeOutDuration = 5f;
    [Tooltip("Beats before the player is swapped to the table. Music starts Audio Play Delay seconds later.")]
    public float beatsBeforeSwap = 8f;

    [Header("Steps (run after the swap, in order)")]
    [ListDrawerSettings(ShowFoldout = true, NumberOfItemsPerPage = 20,
        ListElementLabelName = "DisplayLabel", DefaultExpandedState = false, ShowIndexLabels = true)]
    public List<BeatStep> steps = new();

    [FoldoutGroup("Timing Preview", expanded: false)]
    [ShowInInspector, ReadOnly, MultiLineProperty(10)]
    string TimingPreview
    {
        get
        {
            float beat = 0f;
            float opening = Mathf.Max(fadeInDuration, BeatSeconds * beatsBeforeSwap);
            var lines = new List<string>();
            for (int i = 0; i < steps.Count; i++)
            {
                beat += Mathf.Max(0f, steps[i].beats);
                float seconds = beat * BeatSeconds;
                lines.Add($"{i + 1}: beat {beat:0.##} after swap | intro {opening + seconds:0.00}s | music {seconds - Mathf.Max(0f, audioPlayDelay):0.00}s  {steps[i].DisplayLabel}");
            }
            return string.Join("\n", lines);
        }
    }

    float BeatSeconds => musicBPM > 0f ? 60f / musicBPM : 0.5f;

    public bool IsPlaying { get; private set; }

    public void PlayTableIntro()
    {
        if (!IsPlaying) StartCoroutine(Routine());
    }

    IEnumerator Routine()
    {
        var pm = PlayerManager.Instance;
        var tm = TableManager.Instance;
        IsPlaying = true;
        musicFinishedWaiting = false;
        stepsFinished = false;
        pm?.SetControlsFrozen(true);

        if (ProgressionManager.HasInstance) ProgressionManager.Instance.tableEntered = true;
        float swapTime = Mathf.Max(fadeInDuration, BeatSeconds * beatsBeforeSwap);
        if (ScreenManager.HasInstance)
            yield return ScreenManager.Instance.TransitionThroughBlack(fadeInDuration,
                swapTime - fadeInDuration, Mathf.Max(0f, fadeInDuration + fadedDuration - swapTime),
                fadeOutDuration, Swap);
        else
        {
            yield return new WaitForSecondsRealtime(swapTime);
            Swap();
        }

        void Swap()
        {
            if (tm != null && tm.DM != null && tm.dmPlacement != null)
                tm.DM.transform.rotation = tm.dmPlacement.rotation;
            pm?.SwapToPlayerImmediately(PlayerKind.Table);
            StartCoroutine(PlayIntroMusic());
            StartCoroutine(PlaySteps());
        }
    }

    IEnumerator PlayIntroMusic()
    {
        if (audioPlayDelay > 0f) yield return new WaitForSecondsRealtime(audioPlayDelay);
        if (tableIntroAudio != null && AudioManager.HasInstance)
            AudioManager.Instance.PlayMusic(tableIntroAudio);
        musicFinishedWaiting = true;
        FinishIfReady();
    }

    bool musicFinishedWaiting, stepsFinished;

    void FinishIfReady()
    {
        if (musicFinishedWaiting && stepsFinished) IsPlaying = false;
    }

    void OnDisable()
    {
        StopAllCoroutines();
        IsPlaying = false;
    }

    IEnumerator PlaySteps()
    {
        double nextStepTime = Time.realtimeSinceStartupAsDouble;
        foreach (var step in steps)
        {
            nextStepTime += Mathf.Max(0f, BeatSeconds * step.beats);
            if (step.pointBeforeReveal && dungeonMasterAnimator != null)
            {
                double pointTime = nextStepTime - Mathf.Max(0f, pointLeadBeats) * BeatSeconds;
                while (Time.realtimeSinceStartupAsDouble < pointTime) yield return null;
                PlayPointGesture();
            }
            while (Time.realtimeSinceStartupAsDouble < nextStepTime) yield return null;
            RunStep(step);
        }
        stepsFinished = true;
        FinishIfReady();
    }

    void PlayPointGesture()
    {
        dungeonMasterAnimator.ResetTrigger("Point");
        dungeonMasterAnimator.SetTrigger("Point");
    }

    void RunStep(BeatStep step)
    {
        if (step.activate != null)
            foreach (var go in step.activate) if (go != null) go.SetActive(true);

        if (AudioManager.HasInstance)
        {
            if (step.sfx != null) AudioManager.Instance.PlaySFXData2D(step.sfx);
            else if (!string.IsNullOrEmpty(step.sfxKey)) AudioManager.Instance.PlaySFX2D(step.sfxKey);
        }

        if (step.animator != null && !string.IsNullOrEmpty(step.animatorTrigger))
        {
            step.animator.SetTrigger(step.animatorTrigger);
        }

        if (step.npcFacePlayer != null) step.npcFacePlayer.SnapRotationTowardsPlayer();
        if (step.startMove != null) step.startMove.StartMove();

        if (step.revealLighting && lightingManager != null)
        {
            lightingManager.PlayReveal();
        }
        else if (step.mood != null && lightingManager != null)
        {
            lightingManager.BlendToMood(step.mood, Mathf.Max(0f, step.moodFadeDuration));
        }
        else if (step.fadeLight && WorldManager.HasInstance)
        {
            WorldManager.Instance.FadeDirectionalLight(step.lightIntensity, step.lightFadeDuration);
            WorldManager.Instance.FadeDirectionalLightColor(step.lightColor, step.lightFadeDuration);
        }

        if (step.unfreezePlayer) PlayerManager.Instance?.SetControlsFrozen(false);

        step.onStep?.Invoke();
    }
}
