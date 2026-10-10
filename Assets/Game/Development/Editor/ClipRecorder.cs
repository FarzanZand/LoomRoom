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

    public static string Begin(string clipName, int width = 1080, int height = 1920, float fps = 60, bool audio = true)
    {
        if (!EditorApplication.isPlaying) { Debug.LogError("[ClipRecorder] Recording needs Play mode."); return null; }
        End();
        Directory.CreateDirectory(Folder);
        foreach (var c in Path.GetInvalidFileNameChars()) clipName = clipName.Replace(c, '_');

        var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movie.name = clipName;
        movie.Enabled = true;
        movie.EncoderSettings = new CoreEncoderSettings
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
        settings.FrameRatePlayback = FrameRatePlayback.Constant;
        settings.FrameRate = fps;
        settings.CapFrameRate = true;

        controller = new RecorderController(settings);
        controller.PrepareRecording();
        if (!controller.StartRecording()) { Debug.LogError("[ClipRecorder] The recorder did not start."); controller = null; return null; }
        LastFile = movie.OutputFile + ".mp4";
        Debug.Log($"[ClipRecorder] Recording {width}x{height} @ {fps} to {LastFile}");
        return LastFile;
    }

    public static string End()
    {
        if (controller == null) return null;
        if (controller.IsRecording()) controller.StopRecording();
        controller = null;
        Debug.Log($"[ClipRecorder] Saved {LastFile}");
        return LastFile;
    }

    [MenuItem("Tools/LoomRoom/Recording/Start Vertical Clip (1080x1920)")]
    static void StartVertical() => Begin("Clip");

    [MenuItem("Tools/LoomRoom/Recording/Start Wide Clip (1920x1080)")]
    static void StartWide() => Begin("Clip", 1920, 1080);

    [MenuItem("Tools/LoomRoom/Recording/Stop Clip")]
    static void Stop() => End();

    [MenuItem("Tools/LoomRoom/Recording/Open Videos Folder")]
    static void OpenFolder() { Directory.CreateDirectory(Folder); EditorUtility.RevealInFinder(Folder); }
}
