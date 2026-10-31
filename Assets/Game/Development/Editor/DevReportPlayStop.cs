using System;
using System.IO;
using UnityEditor;

// Tells Main when Play stops, so the developer prompt's requests reach it at once (the user, 2026-10-30: "You should get
// them when stop play automatically"). On leaving Play mode it writes DevReports/.play-stopped with the time; Main keeps
// `python Tools/devreport.py wait` running, which ends (and so wakes Main) once there are new requests and Play has stopped
// since the newest of them.
[InitializeOnLoad]
public static class DevReportPlayStop
{
    public const string MarkerName = ".play-stopped";

    static DevReportPlayStop()
    {
        EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredEditMode) Mark(); };
        // Scripts changed during Play compile once it ends, after its stop has passed: a load outside Play counts as one.
        if (!EditorApplication.isPlayingOrWillChangePlaymode) Mark();
    }

    static void Mark()
    {
        try
        {
            Directory.CreateDirectory(DevReportStore.Root);
            File.WriteAllText(Path.Combine(DevReportStore.Root, MarkerName), DateTime.UtcNow.ToString("o"));
        }
        catch (Exception e) { UnityEngine.Debug.LogWarning($"[DevReportPlayStop] Could not mark Play stopped: {e.Message}"); }
    }
}
