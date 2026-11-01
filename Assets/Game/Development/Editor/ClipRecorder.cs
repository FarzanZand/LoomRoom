using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

// Records the Game view to Assets/Videos/<name> <time>.mp4 (git-ignored) for trailers and TikTok clips. Vertical 1080x1920
// by default, so the game renders portrait instead of being cropped from widescreen. Play mode only:
// ClipRecorder.Begin("Cellars flythrough") ... ClipRecorder.End() (from code, the CLI or the menu).
public static class ClipRecorder
{
    static RecorderController controller;

    public static string LastFile { get; private set; }
    public static bool IsRecording => controller != null && controller.IsRecording();
    public static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "Videos"));

    // Game music is muted while recording (on by default) so edits can lay their own track; sound effects stay.
    public static bool MuteMusic = true;

    // Records in real time (frames repeated or dropped to hold the rate) instead of slowing the game to a constant rate.
    // Sound always plays in real time, so at a constant rate it drifts behind the picture whenever the editor can't keep
    // up (typing blips heard after their letters); turn this on for takes where sound must stay on the picture.
    public static bool RealTime;

    static void SetMusicMuted(bool muted)
    {
        if (!AudioManager.HasInstance) return;
        var mixer = typeof(AudioManager).GetField("mixer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(AudioManager.Instance) as UnityEngine.Audio.AudioMixer;
        if (mixer == null) return;
        float linear = muted ? 0f : AudioManager.Instance.MusicVolume;
        mixer.SetFloat(AudioManager.P_MUSIC, linear <= .0001f ? -80f : Mathf.Log10(linear) * 20f);
    }

    // prores: Apple ProRes 422 HQ in a .mov instead of the built-in MP4 (H.264), whose bitrate runs out at hard cuts into
    // dark, dithered scenes (the frames after the cut ghost and smear); turn the .mov into an MP4 with ffmpeg after.
    public static string Begin(string clipName, int width = 1080, int height = 1920, float fps = 60, bool audio = true, bool prores = false)
    {
        if (!EditorApplication.isPlaying) { Debug.LogError("[ClipRecorder] Recording needs Play mode."); return null; }
        End();
        Directory.CreateDirectory(Folder);
        foreach (var c in Path.GetInvalidFileNameChars()) clipName = clipName.Replace(c, '_');

        var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movie.name = clipName;
        movie.Enabled = true;
        if (prores) movie.EncoderSettings = new ProResEncoderSettings { Format = ProResEncoderSettings.OutputFormat.ProRes422HQ };
        else movie.EncoderSettings = new CoreEncoderSettings
        {
            Codec = CoreEncoderSettings.OutputCodec.MP4,
            EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
        };
        movie.CaptureAudio = audio;
        movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = width, OutputHeight = height };
        movie.OutputFile = Path.Combine(Folder, $"{clipName} {System.DateTime.Now:yyyy-MM-dd HH-mm-ss}");

        var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        settings.AddRecorderSettings(movie);
        settings.SetRecordModeToManual();
        settings.FrameRatePlayback = RealTime ? FrameRatePlayback.Variable : FrameRatePlayback.Constant;
        settings.FrameRate = fps;
        settings.CapFrameRate = true;

        controller = new RecorderController(settings);
        controller.PrepareRecording();
        if (!controller.StartRecording()) { Debug.LogError("[ClipRecorder] The recorder did not start."); controller = null; return null; }
        LastFile = movie.OutputFile + (prores ? ".mov" : ".mp4");
        if (MuteMusic) SetMusicMuted(true);
        Debug.Log($"[ClipRecorder] Recording {width}x{height} @ {fps} to {LastFile}");
        return LastFile;
    }

    public static string End()
    {
        if (controller == null) return null;
        if (controller.IsRecording()) controller.StopRecording();
        controller = null;
        SetMusicMuted(false);
        Debug.Log($"[ClipRecorder] Saved {LastFile}");
        return LastFile;
    }

    static void StartVertical() => Begin("Clip");

    static void StartWide() => Begin("Clip", 1920, 1080);

    static void Stop() => End();

    static void OpenFolder() { Directory.CreateDirectory(Folder); EditorUtility.RevealInFinder(Folder); }
}
