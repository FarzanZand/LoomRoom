// Dungeon1's run ends after floor 9 (the user, 2026-10-30: "Skip those floor for now. Future problem. Go back to room after
// 9th floor"): the three forest stages (Forest1-3, floors 10-12) come out of the run, the biomes stay as assets. The last
// floor's stairs read "Complete dungeon and return" by themselves. Safe to run again. Writes Temp/Realms/nine.txt.
if (UnityEngine.Application.isPlaying) return "playing";
var level = UnityEditor.AssetDatabase.LoadAssetAtPath<TableLevelData>("Assets/Game/Levels/Dungeon1/Dungeon1.asset");
var stages = new System.Collections.Generic.List<TableLevelData.BiomeStage>(level.stages);
int removed = stages.RemoveAll(s => s != null && s.biome != null && s.biome.name.StartsWith("Forest"));
level.stages = stages.ToArray();
level.description = "Nine floors: cellars, catacombs, the crypt, three deep realms and three ruins. The Crypt Lord waits on the third.";
UnityEditor.EditorUtility.SetDirty(level);
UnityEditor.AssetDatabase.SaveAssets();
System.IO.Directory.CreateDirectory("Temp/Realms");
System.IO.File.WriteAllText("Temp/Realms/nine.txt", $"removed {removed}: {level.RunSummary}");
return "ok";
