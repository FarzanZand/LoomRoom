// The class posters' data, safe to run again: ProgressionManager > Class posters points at the game's classes (the
// scene's manager and its prefab), and each class's posterLine gets its first text if it has none. Writes
// Temp/Posters/setup.txt.
if (UnityEngine.Application.isPlaying) return "playing";
var sb = new System.Text.StringBuilder();
var rules = UnityEditor.AssetDatabase.LoadAssetAtPath<AdventureRules>("Assets/Game/Progression/Data/Adventure Rules.asset");
foreach (var pm in UnityEngine.Object.FindObjectsByType<ProgressionManager>(UnityEngine.FindObjectsInactive.Include))
{ pm.classRules = rules; UnityEditor.EditorUtility.SetDirty(pm); sb.Append($"scene manager {pm.name}\n"); }
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:Prefab ProgressionManager"))
{
  var path = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
  var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
  var pm = root.GetComponent<ProgressionManager>();
  if (pm != null) { pm.classRules = rules; UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path); sb.Append($"prefab {path}\n"); }
  UnityEditor.PrefabUtility.UnloadPrefabContents(root);
}
var lines = new System.Collections.Generic.Dictionary<string, string>
{
  ["Warrior"] = "The Warrior. Every wall needs one.",
  ["Cleric"] = "The Cleric. Someone has to keep the rest of them standing.",
  ["Wizard"] = "The Wizard. Try not to set the poster on fire.",
  ["Arcanist"] = "The Arcanist. Steel and spells. He earned his place up there.",
};
foreach (var kv in lines)
{
  var c = UnityEditor.AssetDatabase.LoadAssetAtPath<AdventurerClass>($"Assets/Game/Characters/Classes/{kv.Key}.asset");
  if (c == null) continue;
  if (c.posterLine == null) c.posterLine = new DungeonMaster.Line();
  if (c.posterLine.IsEmpty) { c.posterLine.text = kv.Value; c.posterLine.mumble = true; c.posterLine.inPerson = false; UnityEditor.EditorUtility.SetDirty(c); sb.Append($"{kv.Key}: {kv.Value}\n"); }
}
UnityEditor.AssetDatabase.SaveAssets();
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
System.IO.File.WriteAllText("Temp/Posters/setup.txt", sb.ToString()); return "ok";
