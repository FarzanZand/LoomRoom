using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;

// Play mode test driver for the camera filters (ScreenManager > Camera filter): shows every filter in turn where the
// player stands, saves a screenshot of each (UI included) to Temp/FilterCapture/<shot>_<filter>.png and measures the
// Retro Screen pass's GPU time per filter. Start it from an eval: ScreenFilterCapture.Run("room"). Leaves the filter
// as it found it. Results go to Temp/FilterCapture/<shot>_timings.txt.
public class ScreenFilterCapture : MonoBehaviour
{
    public static bool Busy { get; private set; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetBusy() => Busy = false;
    static readonly string[] PassNames = { "Retro Screen", "FullScreenPassRendererFeature", "Blit Retro Screen" };

    public static void Run(string shot, int timingFrames = 60)
    {
        if (Busy || !ScreenManager.HasInstance) { Debug.LogWarning("[ScreenFilterCapture] busy or no ScreenManager"); return; }
        new GameObject("Screen filter capture").AddComponent<ScreenFilterCapture>().StartCoroutine(Capture(shot, timingFrames));
    }

    static IEnumerator Capture(string shot, int timingFrames)
    {
        Busy = true;
        var screen = ScreenManager.Instance;
        var before = screen.cameraFilter;
        string folder = Path.Combine(Application.dataPath, "..", "Temp", "FilterCapture");
        Directory.CreateDirectory(folder);
        var lines = new List<string>();
        var recorders = new List<Recorder>();
        foreach (var name in PassNames) { var r = Recorder.Get(name); r.enabled = true; recorders.Add(r); }

        foreach (ScreenFilter filter in System.Enum.GetValues(typeof(ScreenFilter)))
        {
            screen.cameraFilter = filter;
            screen.ApplyRenderingEffects();
            for (int i = 0; i < 4; i++) yield return null;
            yield return new WaitForEndOfFrame();
            var shotTexture = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(folder, $"{shot}_{filter}.png"), shotTexture.EncodeToPNG());
            Destroy(shotTexture);

            var gpu = new double[recorders.Count];
            double frameMs = 0;
            for (int f = 0; f < timingFrames; f++)
            {
                yield return null;
                for (int i = 0; i < recorders.Count; i++) gpu[i] += recorders[i].gpuElapsedNanoseconds / 1e6;
                frameMs += Time.unscaledDeltaTime * 1000.0;
            }
            var parts = new List<string>();
            for (int i = 0; i < recorders.Count; i++) parts.Add($"{PassNames[i]} gpu {gpu[i] / timingFrames:0.000} ms");
            lines.Add($"{filter}: {string.Join(", ", parts)}; frame {frameMs / timingFrames:0.00} ms");
        }
        screen.cameraFilter = before;
        screen.ApplyRenderingEffects();
        foreach (var r in recorders) r.enabled = false;
        lines.Insert(0, $"{shot} at {Screen.width}x{Screen.height}, gpu recorder supported: {SystemInfo.supportsGpuRecorder}");
        File.WriteAllLines(Path.Combine(folder, $"{shot}_timings.txt"), lines);
        Debug.Log($"[ScreenFilterCapture] {shot}: {lines.Count - 1} filters captured to {folder}");
        Busy = false;
        Destroy(Object.FindAnyObjectByType<ScreenFilterCapture>()?.gameObject);
    }
}
