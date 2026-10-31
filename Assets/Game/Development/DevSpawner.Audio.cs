#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The F10 window's Audio kind (the user's request, 2026-10-31): every sound effect, listed by category (the SFX Library,
// the UI Library, the AudioData assets, clips played straight from components) or, under Last played, by when it was
// last heard (AudioPlayLog). The search finds a sound by its name, its clip or its category. Picked, it plays with Enter
// or Play, and its volume, pitch and pitch variance are stepped live with - and +; Save keeps them: an AudioData asset is
// written at once (in the Editor), a library entry is written into Room.unity's Audio Manager when Play stops
// (DevAudioSave). A clip played straight from a component can be heard but not set here: its volume is the component's.
public partial class DevSpawner
{
    const string LastPlayed = "Last played";
    public const string PendingAudioKey = "LoomRoom.DevAudio.Pending";

    [System.Serializable]
    public class Stepper
    {
        public GameObject row;
        public TMP_Text value;
        public Button less, more;
    }

    [Header("Audio: volume, pitch and pitch variance of the picked sound")]
    [SerializeField] Stepper volumeStep = new(), pitchStep = new(), varianceStep = new();

    // One sound as the window sees it: a library entry, an AudioData asset or a bare clip.
    class Sound
    {
        public SFXEntry sfx;
        public UIEntry ui;
        public AudioData data;
        public AudioClip clip;
        public string key;

        public float LastHeard => sfx != null ? AudioPlayLog.LastSfx(key) : ui != null ? AudioPlayLog.LastUI(key) : AudioPlayLog.Last(data != null ? data : clip);
        public bool HasVolume => sfx != null || ui != null || data != null;
        public bool HasPitch => data != null || ui != null && ui.source == UIEntry.Source.Clip;
        public bool HasVariance => sfx != null || data != null;

        public float Volume
        {
            get => sfx != null ? sfx.volume : ui != null ? ui.volume : data != null ? data.volume : 1f;
            set { value = Mathf.Clamp01(value); if (sfx != null) sfx.volume = value; else if (ui != null) ui.volume = value; else if (data != null) data.volume = value; }
        }
        public float Pitch
        {
            get => data != null ? data.pitch : ui != null ? ui.pitch : 1f;
            set { if (data != null) data.pitch = Mathf.Clamp(value, 0f, 2f); else if (ui != null) ui.pitch = Mathf.Clamp(value, .5f, 2f); }
        }
        public float Variance
        {
            get => sfx != null ? sfx.pitchVariance : data != null ? data.pitchVariance : 0f;
            set { value = Mathf.Clamp(value, 0f, .5f); if (sfx != null) sfx.pitchVariance = value; else if (data != null) data.pitchVariance = value; }
        }

        // The clips it plays, by name.
        public string Clips
        {
            get
            {
                if (sfx != null) return sfx.clip != null ? sfx.clip.name : "no clip";
                if (ui != null) return ui.source == UIEntry.Source.Data ? (ui.data != null ? $"{ui.data.name} (Audio Data)" : "no data") : ui.clip != null ? ui.clip.name : "no clip";
                if (data != null) return data.clips == null || data.clips.Length == 0 ? "no clips" : string.Join(", ", data.clips.Where(c => c != null).Select(c => c.name));
                return clip != null ? clip.name : "";
            }
        }

        // Heard as the game plays it, flat (not from a place in the world).
        public void Play()
        {
            var audio = AudioManager.Instance;
            if (sfx != null) audio.PlaySFX2D(key);
            else if (ui != null) audio.PlayUI(key);
            else if (data != null) audio.PlaySFXData2D(data);
            else if (clip != null) audio.PlaySFX2D(clip);
        }
    }

    void HookSteppers()
    {
        Hook(volumeStep, -.05f, s => s.Volume, (s, v) => s.Volume = v);
        Hook(pitchStep, -.05f, s => s.Pitch, (s, v) => s.Pitch = v);
        Hook(varianceStep, -.01f, s => s.Variance, (s, v) => s.Variance = v);
    }

    void Hook(Stepper step, float by, System.Func<Sound, float> get, System.Action<Sound, float> set)
    {
        if (step == null) return;
        void Change(float d)
        {
            var sound = picked?.sound;
            if (sound == null) return;
            set(sound, Mathf.Round((get(sound) + d) * 100f) / 100f);
            ShowDetail();
            Refocus();
        }
        if (step.less != null) step.less.onClick.AddListener(() => Change(by));
        if (step.more != null) step.more.onClick.AddListener(() => Change(-by));
    }

    void ShowSteppers(Sound sound)
    {
        Show(volumeStep, sound != null && sound.HasVolume, sound?.Volume ?? 0f, "0.00");
        Show(pitchStep, sound != null && sound.HasPitch, sound?.Pitch ?? 0f, "0.00");
        Show(varianceStep, sound != null && sound.HasVariance, sound?.Variance ?? 0f, "0.00");
    }

    static void Show(Stepper step, bool on, float value, string format)
    {
        if (step == null || step.row == null) return;
        step.row.SetActive(on);
        if (on && step.value != null) step.value.text = value.ToString(format, CultureInfo.InvariantCulture);
    }

    IEnumerable<Entry> AudioEntries()
    {
        var audio = AudioManager.HasInstance ? AudioManager.Instance : null;
        var known = new HashSet<Object>();
        var sounds = new List<(Sound sound, string name, string kind, string group)>();
        if (audio != null)
        {
            if (audio.SfxLibrary != null)
                foreach (var e in audio.SfxLibrary)
                    if (e != null && !string.IsNullOrEmpty(e.key)) { sounds.Add((new Sound { sfx = e, key = e.key }, e.key, "Sound effect", "SFX library")); if (e.clip != null) known.Add(e.clip); }
            if (audio.UiLibrary != null)
                foreach (var e in audio.UiLibrary)
                    if (e != null && !string.IsNullOrEmpty(e.key))
                    {
                        sounds.Add((new Sound { ui = e, key = e.key }, e.key, "UI sound", "UI library"));
                        if (e.clip != null) known.Add(e.clip);
                        if (e.data != null) known.Add(e.data);
                    }
        }
        // Every AudioData asset (in a development build: the ones heard so far).
#if UNITY_EDITOR
        var datas = UnityEditor.AssetDatabase.FindAssets("t:AudioData", new[] { "Assets" }).Select(UnityEditor.AssetDatabase.GUIDToAssetPath)
            .Select(UnityEditor.AssetDatabase.LoadAssetAtPath<AudioData>).Where(d => d != null).OrderBy(d => d.name).ToList();
#else
        var datas = AudioPlayLog.Datas.Where(d => d != null).OrderBy(d => d.name).ToList();
#endif
        foreach (var d in datas)
        {
            sounds.Add((new Sound { data = d }, d.name, "Audio Data", "Audio data"));
            known.Add(d);
            if (d.clips != null) foreach (var c in d.clips) if (c != null) known.Add(c);
        }
        // Clips heard that no library entry or AudioData plays: a component's own sound.
        foreach (var c in AudioPlayLog.Clips)
            if (c != null && !known.Contains(c)) sounds.Add((new Sound { clip = c }, c.name, "Clip", "Clips"));

        foreach (var (sound, name, kindLine, groupName) in sounds)
        {
            var s = sound;
            var n = name;
            yield return new Entry
            {
                key = groupName + "/" + n, name = n, kind = kindLine + " · " + Short(s.Clips), group = groupName, sound = s,
                about = () => SoundText(s),
                primaryLabel = "Play", primary = () => { s.Play(); return $"Playing {n}."; },
                secondaryLabel = s.HasVolume ? "Save" : null, secondary = s.HasVolume ? () => Save(s, n) : null,
                blocked = () => AudioManager.HasInstance ? null : "No Audio Manager here.",
            };
        }
    }

    static string SoundText(Sound s)
    {
        var lines = new List<string>();
        float at = s.LastHeard;
        lines.Add(at < 0f ? "Not heard since Play began." : $"Last heard {Ago(Time.unscaledTime - at)}.");
        if (s.sfx != null || s.ui != null) lines.Add((s.ui != null && s.ui.source == UIEntry.Source.Data ? "Its volume multiplies its Audio Data's. " : "") + "Save writes it into the Room scene when Play stops.");
        else if (s.data != null) lines.Add($"Plays one of: {s.Clips}. Save writes the asset.");
        else lines.Add("Its component sets its volume: play only.");
        if (AudioManager.HasInstance)
        {
            var a = AudioManager.Instance;
            lines.Add($"Mixer: master {Percent(a.GetVolume(AudioManager.Channel.Master))}, effects {Percent(a.GetVolume(AudioManager.Channel.Sfx))}, music {Percent(a.GetVolume(AudioManager.Channel.Music))}.");
        }
        return string.Join("\n\n", lines);
    }

    // The clip names in the kind line (searched too), cut short.
    static string Short(string text) => text.Length <= 44 ? text : text.Substring(0, 42) + "...";

    static string Percent(float v) => $"{Mathf.RoundToInt(v * 100f)}%";
    static string Ago(float seconds) => seconds < 2f ? "just now" : seconds < 90f ? $"{seconds:0} s ago" : $"{seconds / 60f:0} min ago";

    string Save(Sound s, string name)
    {
        if (s.data != null)
        {
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(s.data);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(s.data);
            return $"Saved {name} to its asset.";
#else
            return "Saving needs the Editor; here the change lasts until the game closes.";
#endif
        }
#if UNITY_EDITOR
        // The libraries are on the Room scene's Audio Manager, which Play can't save: kept and written when Play stops.
        string line = string.Join("|", s.ui != null ? "ui" : "sfx", s.key, F(s.Volume), F(s.Pitch), F(s.Variance));
        var pending = UnityEditor.SessionState.GetString(PendingAudioKey, "").Split('\n').Where(l => l.Length > 0 && !l.StartsWith(line.Substring(0, line.IndexOf('|', line.IndexOf('|') + 1) + 1))).ToList();
        pending.Add(line);
        UnityEditor.SessionState.SetString(PendingAudioKey, string.Join("\n", pending));
        return $"Kept {name}: written into the Audio Manager when Play stops.";
#else
        return "Saving needs the Editor; here the change lasts until the game closes.";
#endif
    }

    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
#endif
