using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class AudioLibrarySetup
{
    static AudioLibrarySetup() => EditorApplication.update += Tick;
    static void Tick()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/AudioLibrarySetup.request")) return;
        File.Delete("Temp/AudioLibrarySetup.request");
        var mode=EditorSettings.serializationMode;EditorSettings.serializationMode=SerializationMode.ForceText;
        try
        {
            var style=AssetDatabase.LoadAssetAtPath<UIFeedbackSettings>("Assets/Game/UI/Resources/UIFeedback.asset");
            void Configure(AudioManager manager)
            {
                var so=new SerializedObject(manager);var list=so.FindProperty("uiLibrary");
                var entries=new[]{(style.openKey,style.open,.85f),(style.closeKey,style.close,.75f),(style.hoverKey,style.hover,.3f),(style.equipKey,style.equip,.85f),(style.invalidKey,style.invalid,.65f)};
                foreach(var entry in entries)
                {
                    bool exists=false;for(int i=0;i<list.arraySize;i++)if(list.GetArrayElementAtIndex(i).FindPropertyRelative("key").stringValue==entry.Item1)exists=true;
                    if(exists)continue;
                    int index=list.arraySize;list.InsertArrayElementAtIndex(index);var row=list.GetArrayElementAtIndex(index);
                    row.FindPropertyRelative("key").stringValue=entry.Item1;
                    row.FindPropertyRelative("clip").objectReferenceValue=entry.Item2.clips.First(c=>c!=null);
                    row.FindPropertyRelative("volume").floatValue=entry.Item3;row.FindPropertyRelative("pitch").floatValue=1;
                }
                so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(manager);
            }
            const string path="Assets/Game/Core/Prefabs/AudioManager.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try{Configure(root.GetComponent<AudioManager>());PrefabUtility.SaveAsPrefabAsset(root,path);}finally{PrefabUtility.UnloadPrefabContents(root);}
            foreach(var manager in UnityEngine.Object.FindObjectsByType<AudioManager>(FindObjectsInactive.Include)){Configure(manager);PrefabUtility.RecordPrefabInstancePropertyModifications(manager);}
            foreach(string player in new[]{"Room","Table"})
            {
                string prefab="Assets/Game/Players/"+player+"/Prefabs/"+player+"Player.prefab";
                var go=PrefabUtility.LoadPrefabContents(prefab);
                try{var footsteps=go.GetComponent<PlayerFootsteps>();if(footsteps!=null){var so=new SerializedObject(footsteps);so.FindProperty("footstepVolume").floatValue=.85f;so.ApplyModifiedPropertiesWithoutUndo();}PrefabUtility.SaveAsPrefabAsset(go,prefab);}finally{PrefabUtility.UnloadPrefabContents(go);}
            }
            foreach(var footsteps in UnityEngine.Object.FindObjectsByType<PlayerFootsteps>(FindObjectsInactive.Include))
            {var so=new SerializedObject(footsteps);so.FindProperty("footstepVolume").floatValue=.85f;so.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.RecordPrefabInstancePropertyModifications(footsteps);}
            AssetDatabase.SaveAssets();EditorSceneManager.SaveOpenScenes();
            File.WriteAllText("Temp/AudioLibrarySetup.report","PASS: five UI sounds registered on AudioManager prefab and scene, volume .3–.85; footsteps raised to .85. Existing UI library entries preserved.");
        }
        catch(Exception ex){File.WriteAllText("Temp/AudioLibrarySetup.report",ex.ToString());Debug.LogException(ex);}
        finally{EditorSettings.serializationMode=mode;}
    }
}
