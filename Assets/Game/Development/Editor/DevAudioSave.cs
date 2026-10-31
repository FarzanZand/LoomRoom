using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The F10 window's Audio Save for the Audio Manager's libraries (DevSpawner): the SFX and UI Library live on the Audio
// Manager in Room.unity, a scene object Play mode can't save, so each saved entry waits in SessionState
// (DevSpawner.PendingAudioKey: lines of "sfx|key|volume|pitch|variance" or "ui|..." ) and is written into the scene's
// Audio Manager when Play stops; the scene is saved when it had no other unsaved changes, else it is left dirty to save.
[InitializeOnLoad]
static class DevAudioSave
{
    static DevAudioSave() => EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredEditMode) Apply(); };

    static void Apply()
    {
        string pending = SessionState.GetString(DevSpawner.PendingAudioKey, "");
        if (string.IsNullOrEmpty(pending)) return;
        var manager = Object.FindAnyObjectByType<AudioManager>(FindObjectsInactive.Include);
        if (manager == null) { Debug.LogWarning("[DevAudioSave] No Audio Manager in the open scene: the F10 audio changes are kept for the next time Play stops in Room."); return; }
        SessionState.EraseString(DevSpawner.PendingAudioKey);
        var scene = manager.gameObject.scene;
        bool wasDirty = scene.isDirty;
        var so = new SerializedObject(manager);
        int done = 0;
        foreach (var line in pending.Split('\n'))
        {
            var p = line.Split('|');
            if (p.Length < 5) continue;
            var list = so.FindProperty(p[0] == "ui" ? "uiLibrary" : "sfxLibrary");
            if (list == null) continue;
            float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float volume);
            float.TryParse(p[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pitch);
            float.TryParse(p[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float variance);
            for (int i = 0; i < list.arraySize; i++)
            {
                var entry = list.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("key").stringValue != p[1]) continue;
                entry.FindPropertyRelative("volume").floatValue = volume;
                if (p[0] == "ui") entry.FindPropertyRelative("pitch").floatValue = pitch;
                else entry.FindPropertyRelative("pitchVariance").floatValue = variance;
                done++;
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!wasDirty) EditorSceneManager.SaveScene(scene);
        Debug.Log($"[DevAudioSave] {done} Audio Manager library entr{(done == 1 ? "y" : "ies")} from the F10 window written into {scene.name}" + (wasDirty ? " (the scene had other unsaved changes: save it to keep them)." : " and saved."));
    }
}
