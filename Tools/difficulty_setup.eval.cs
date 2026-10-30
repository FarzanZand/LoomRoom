// Dungeon1's difficulty after growth became compounded (the user, 2026-10-30): the per-floor rates are set so floor 9 is as
// hard as it was with the old linear growth (damage x3.8 = 1.182^8 instead of 1 + .35 x 8, health x2.6 = 1.127^8 instead
// of 1 + .2 x 8; the floors between come out a little easier), and elite enemies come in each biome: rarely in the cellars,
// more often deeper. Safe to run again. Writes Temp/Realms/difficulty.txt.
if (UnityEngine.Application.isPlaying) return "playing";
var sb = new System.Text.StringBuilder();
var balance = UnityEditor.AssetDatabase.LoadAssetAtPath<DungeonBalance>("Assets/Game/Levels/Dungeon1/Dungeon balance.asset");
// A difficulty jump as each new biome begins (the user: a bigger step after a boss): the Realms (floor 4, after the Crypt
// Lord) 2 floors' worth, the Ruins (floor 7) 1, on top of 15% growth a floor for damage and health (the user, 2026-10-30:
// "Keep growth per floor at 15 % and still do the extra jump between acts"), so floor 9 is 11 floors' worth deep.
balance.damageGrowthPerFloor = .15f;
balance.healthGrowthPerFloor = .15f;
var lvl = UnityEditor.AssetDatabase.LoadAssetAtPath<TableLevelData>("Assets/Game/Levels/Dungeon1/Dungeon1.asset");
foreach (var st in lvl.stages) if (st != null && st.biome != null) st.extraFloors = st.biome.name == "Realms1" ? 2f : st.biome.name == "Ruins1" ? 1f : 0f;
UnityEditor.EditorUtility.SetDirty(lvl);
UnityEditor.EditorUtility.SetDirty(balance);
sb.Append($"growth damage {balance.damageGrowthPerFloor} health {balance.healthGrowthPerFloor}\n");
void Elite(string path, float chance)
{
  var b = UnityEditor.AssetDatabase.LoadAssetAtPath<DungeonBiome>(path);
  if (b == null) { sb.Append($"MISSING {path}\n"); return; }
  b.eliteChance = chance; b.eliteFloors = 3f;
  UnityEditor.EditorUtility.SetDirty(b);
  sb.Append($"{b.name}: elites {chance:P0}\n");
}
foreach (var n in new[] { "Cellars1", "Cellars2", "Cellars3" }) Elite($"Assets/Game/Levels/Shared/Biomes/{n}.asset", n == "Cellars1" ? .04f : .06f);
foreach (var n in new[] { "Realms1", "Realms2", "Realms3", "Ruins1", "Ruins2", "Ruins3" }) Elite($"Assets/Game/Levels/Dungeon1/Biomes/{n}.asset", n.StartsWith("Realms") ? .08f : .1f);
UnityEditor.AssetDatabase.SaveAssets();
var level = UnityEditor.AssetDatabase.LoadAssetAtPath<TableLevelData>("Assets/Game/Levels/Dungeon1/Dungeon1.asset");
for (int f = 1; f <= level.FloorCount; f++)
{
  float depth = DungeonBalance.Depth(f, level.ExtraFloors(f));
  sb.Append($"floor {f}: depth {depth:0.#} damage x{balance.damageMultiplier * DungeonBalance.Grown(balance.damageGrowthPerFloor, depth):0.00} health x{balance.healthMultiplier * DungeonBalance.Grown(balance.healthGrowthPerFloor, depth):0.00}\n");
}
System.IO.Directory.CreateDirectory("Temp/Realms");
System.IO.File.WriteAllText("Temp/Realms/difficulty.txt", sb.ToString());
return "ok";
