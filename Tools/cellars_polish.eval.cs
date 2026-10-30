// Dungeon1's first three floors (Cellars1 The Cellars, Cellars2 The Catacombs, Cellars3 The Crypt) dressed by what the
// Realms taught (the user, 2026-10-30: "improvements on the cellar maps now that you learned more"): a generated room was
// a few pieces along its walls under a bare ceiling. Each biome now adds its own furnishing on top of the level's: chains
// and candles overhead, pieces standing in the middle of a room as well as along its walls, cobwebs and dust in the
// corners, more of what its place is about (stores in the cellars, bones in the catacombs, tombs and candles in the crypt),
// and the Crypt has a few big rooms built two tiles high. Hand-built rooms keep their own dressing. Safe to run again (it
// sets each biome's own furnishing list and tall rooms, nothing else). Writes Temp/Realms/cellars.txt.
if (UnityEngine.Application.isPlaying) return "playing";
const string F = "Assets/Game/Levels/Shared/Props/Furnishing";
const string G = "Assets/Game/Levels/Shared/Props/Groups";
var sb = new System.Text.StringBuilder();
try {
UnityEngine.GameObject Load(string path) { var g = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path); if (g == null) sb.Append($"MISSING {path}\n"); return g; }
const DungeonPropPlacement W = DungeonPropPlacement.AgainstWall, C = DungeonPropPlacement.Corner, M = DungeonPropPlacement.Centre, A = DungeonPropPlacement.Anywhere;
const DungeonRoomRoles NotEntrance = DungeonRoomRoles.Combat | DungeonRoomRoles.Treasure | DungeonRoomRoles.Rest | DungeonRoomRoles.Storage | DungeonRoomRoles.Exit;
DungeonPropRule Rule(string path, DungeonPropPlacement where, float perTen, int min, int max, float chance = 1f, DungeonRoomRoles rooms = NotEntrance)
{
  var g = Load(path);
  var r = new DungeonPropRule { prefab = g, placement = where, footprint = UnityEngine.Vector2Int.one, perTenCells = perTen, perRoom = new UnityEngine.Vector2Int(min, max), chance = chance, rooms = rooms };
  // Reserve every 2 m cell the prop's collider covers, so nothing wider than its cell walls a doorway off.
  var box = g != null ? g.GetComponentInChildren<UnityEngine.BoxCollider>() : null;
  if (box != null)
  {
    var size = UnityEngine.Vector3.Scale(box.size, box.transform.lossyScale);
    r.footprint = new UnityEngine.Vector2Int(UnityEngine.Mathf.Max(1, UnityEngine.Mathf.CeilToInt(size.x / 2f - .05f)), UnityEngine.Mathf.Max(1, UnityEngine.Mathf.CeilToInt(size.z / 2f - .05f)));
  }
  return r;
}
DungeonPropRule P(string n, DungeonPropPlacement where, float perTen, int min, int max, float chance = 1f, DungeonRoomRoles rooms = NotEntrance) => Rule($"{F}/{n}.prefab", where, perTen, min, max, chance, rooms);
DungeonPropRule Gr(string n, DungeonPropPlacement where, float perTen, int min, int max, float chance = 1f, DungeonRoomRoles rooms = NotEntrance) => Rule($"{G}/{n}.prefab", where, perTen, min, max, chance, rooms);
const DungeonRoomRoles Stores = DungeonRoomRoles.Storage | DungeonRoomRoles.Rest | DungeonRoomRoles.Treasure;

void Set(string name, DungeonPropRule[] rules, float tall)
{
  var path = $"Assets/Game/Levels/Shared/Biomes/{name}.asset";
  var b = UnityEditor.AssetDatabase.LoadAssetAtPath<DungeonBiome>(path);
  if (b == null) { sb.Append($"MISSING {path}\n"); return; }
  b.propRules = System.Array.FindAll(rules, r => r.prefab != null);
  b.tallRoomChance = tall; b.tallRoomLeastCells = 20;
  UnityEditor.EditorUtility.SetDirty(b);
  sb.Append($"{name}: {b.propRules.Length} rules, tall {tall}\n");
}

// Shared by all three: chains overhead (lifted to a tall room's ceiling), cobwebs in the corners, candles by the walls.
DungeonPropRule[] Common() => new[]
{
  P("Hanging chain", A, .3f, 1, 3, .9f), P("Cobweb", C, .4f, 1, 3), P("Candle cluster", W, .25f, 0, 2, .8f),
};
DungeonPropRule[] With(DungeonPropRule[] a, DungeonPropRule[] b) { var l = new System.Collections.Generic.List<DungeonPropRule>(a); l.AddRange(b); return l.ToArray(); }

// The Cellars: someone's stores, gone to the rats. Barrels and crates in heaps, shelves of pots, a table in the middle.
Set("Cellars1", With(Common(), new[]
{
  Gr("Barrels huddled", C, .25f, 0, 2, .9f), Gr("Crates and sacks", W, .3f, 0, 2, .9f), Gr("Shelf with pots", W, .2f, 0, 1, .8f),
  Gr("Barrel with candles", M, .12f, 0, 1, .6f), P("Sacks", C, .25f, 0, 2), P("Storage shelf", W, .2f, 0, 1, .8f, Stores | DungeonRoomRoles.Combat),
  P("Long table", M, .12f, 0, 1, .6f), P("Rug", M, .12f, 0, 1, .5f, Stores), P("Broken bookcase", W, .12f, 0, 1, .5f),
}), 0f);

// The Catacombs: the dead stacked in the walls. Skulls and bones everywhere, candles left for them, a tomb in the middle.
Set("Cellars2", With(Common(), new[]
{
  P("Skull pile", W, .35f, 1, 3, .9f), P("Bone pile", A, .3f, 0, 2), P("Bone heap", C, .2f, 0, 1, .8f),
  P("Candle rack", W, .15f, 0, 1, .7f), P("Sarcophagus", M, .1f, 0, 1, .55f), P("Skull pile", M, .12f, 0, 1, .5f),
  P("Brazier", C, .12f, 0, 1, .6f),
}), 0f);

// The Crypt: tombs and the rites kept for them. Coffins in the middle of halls, candle racks, statues, pews before a brazier,
// and a few great halls two tiles high (chains hang from their higher ceilings).
Set("Cellars3", With(Common(), new[]
{
  P("Sarcophagus", M, .15f, 0, 1, .7f), P("Sarcophagus", W, .15f, 0, 1, .6f), P("Candle rack", W, .2f, 0, 2, .8f),
  P("Gargoyle statue", W, .12f, 0, 1, .6f), P("Brazier", M, .12f, 0, 1, .6f), P("Pew", M, .1f, 0, 2, .45f, DungeonRoomRoles.Rest | DungeonRoomRoles.Combat),
  P("Skull pile", W, .2f, 0, 2, .7f), P("Bone pile", A, .2f, 0, 1),
}), .15f);

UnityEditor.AssetDatabase.SaveAssets();
} catch (System.Exception e) { sb.Append("ERR " + e); }
System.IO.Directory.CreateDirectory("Temp/Realms");
System.IO.File.WriteAllText("Temp/Realms/cellars.txt", sb.ToString());
return "ok";
