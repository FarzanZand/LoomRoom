// Hand-built rooms that also take the floor's furnishing around their own pieces (DungeonRoomTemplate.addFurnishing):
// the plainer chunks (DungeonChunkKit.Furnished) and the roomier scripted events (EventRoomsSetup: all but the Duel
// bridge). The user, 2026-10-30, via Zelda's review: the Cellars' hand-built rooms and the event rooms were bare floors.
// Sets the flags on the built prefabs (the setups set them on a rebuild). Safe to run again. Writes Temp/Realms/furnishing.txt.
if (UnityEngine.Application.isPlaying) return "playing";
var sb = new System.Text.StringBuilder();
void Set(string path, bool events)
{
  var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
  try
  {
    var t = root.GetComponent<DungeonRoomTemplate>();
    if (t == null) { sb.Append($"no template {path}\n"); return; }
    t.replaceGeneratedProps = false; t.addFurnishing = true;
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
    sb.Append($"{root.name}: furnished\n");
  }
  finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
}
foreach (var n in DungeonChunkKit.Furnished) Set($"Assets/Game/Levels/Shared/Rooms/{n}.prefab", false);
foreach (var n in new[] { "Card table", "Dice room", "Giant's hoard", "Bell room" }) Set($"Assets/Game/Levels/Shared/Rooms/Scripted/{n}.prefab", true);
UnityEditor.AssetDatabase.SaveAssets();
System.IO.Directory.CreateDirectory("Temp/Realms");
System.IO.File.WriteAllText("Temp/Realms/furnishing.txt", sb.ToString());
return "ok";
