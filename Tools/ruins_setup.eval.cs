// The ruins biomes on Dungeon1's floors 7-9 (Ruins1 grey stone and oak at dusk, Ruins2 limestone and pine in a pale light,
// Ruins3 basalt and black oak at night), safe to run again (it overwrites the biomes' look and lists):
//  - Walled dungeon floors under ceilings in the Ruins styles (Levels/Shared/Room styles/Ruins/RuinsN: RuinsN stone for
//    most rooms and the passages, RuinsN hall now and then; the timber styles were deleted on 2026-10-30), lit as the
//    Crypt is, with wild walls (DungeonBiome.wilds,
//    roofed): the room's own masonry with stone pillars half sunk into it, vines hung on it, roots, moss and rubble at its foot,
//    the floor's torches hung on it.
//  - The Crypt's (Cellars3) enemies and loot, no guardian, no tavern (its room is built for walls and a ceiling).
//  - Dungeon1: the three as stages 7-9, one floor each, no boss (kept where they are when run again).
// The props are the forest's (Tools/forest_props.eval.cs). Writes Temp/Forest/ruins.txt.
if (UnityEngine.Application.isPlaying) return "playing";
const string F = "Assets/Game/Levels/Dungeon1/Forest";
const string R = "Assets/Game/Levels/Shared/Room styles/Ruins";
var sb = new System.Text.StringBuilder();
try {
T Load<T>(string p) where T : UnityEngine.Object { var a = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(p); if (a == null) sb.Append($"MISSING {p}\n"); return a; }
DungeonWeightedPrefab[] Picks(params (string name, float w)[] list)
{
  var r = new System.Collections.Generic.List<DungeonWeightedPrefab>();
  foreach (var (n, w) in list)
  {
    if (w <= 0f) continue;
    var folder = n.Contains(" green") || n.Contains(" gold") || n.Contains(" red") || n.Contains(" teal") ? n.Substring(0, n.LastIndexOf(' ')) : n;
    var path = n.StartsWith("Glow ") ? $"{F}/Props/{n}.prefab" : $"{F}/Props/{folder}/{n}.prefab";
    var go = Load<UnityEngine.GameObject>(path);
    if (go != null) r.Add(new DungeonWeightedPrefab { prefab = go, weight = w });
  }
  return r.ToArray();
}
var level = Load<TableLevelData>("Assets/Game/Levels/Dungeon1/Dungeon1.asset");
var crypt = Load<DungeonBiome>("Assets/Game/Levels/Shared/Biomes/Cellars3.asset");
var moss = Load<UnityEngine.Material>($"{F}/Materials/Forest moss.mat");
// Vines for the walls (Levels/Dungeon1/Ruins/Props/Vines): PolygonDarkFantasy's flat vine cards with the painted pixel
// copy of their atlas (Tools/ruins_vines.py, 128 texels a metre), cut out, both sides drawn. Only the ones no wider than a
// cell; the generator hangs them with their top just under the ceiling.
const string V = "Assets/Game/Levels/Dungeon1/Ruins/Props/Vines";
var vinePng = V + "/Vines pixel.png";
UnityEditor.AssetDatabase.ImportAsset(vinePng, UnityEditor.ImportAssetOptions.ForceSynchronousImport);
var vimp = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(vinePng);
vimp.filterMode = UnityEngine.FilterMode.Point; vimp.npotScale = UnityEditor.TextureImporterNPOTScale.None; vimp.mipmapEnabled = false; vimp.alphaIsTransparency = true;
vimp.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed; vimp.wrapMode = UnityEngine.TextureWrapMode.Clamp; vimp.SaveAndReimport();
var vineMat = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(V + "/Vines pixel.mat");
if (vineMat == null) { vineMat = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Lit")); UnityEditor.AssetDatabase.CreateAsset(vineMat, V + "/Vines pixel.mat"); }
vineMat.SetTexture("_BaseMap", Load<UnityEngine.Texture2D>(vinePng)); vineMat.SetColor("_BaseColor", UnityEngine.Color.white);
vineMat.SetFloat("_AlphaClip", 1f); vineMat.SetFloat("_Cutoff", .5f); vineMat.EnableKeyword("_ALPHATEST_ON"); vineMat.SetFloat("_Cull", 2f);   // the cards carry both sides already: drawn double-sided the dark back fought the lit front
vineMat.SetFloat("_Smoothness", 0f); vineMat.SetFloat("_SpecularHighlights", 0f); vineMat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
UnityEditor.EditorUtility.SetDirty(vineMat);
var vines = new System.Collections.Generic.List<DungeonWeightedPrefab>();
foreach (var vn in new[] { "02", "03", "04", "05", "06", "07", "Draped_02" })
{
  var src = Load<UnityEngine.GameObject>($"Assets/Synty/PolygonDarkFantasy/Prefabs/Environment/SM_Env_Vine_{vn}.prefab");
  if (src == null) continue;
  var root = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(src);
  UnityEditor.PrefabUtility.UnpackPrefabInstance(root, UnityEditor.PrefabUnpackMode.Completely, UnityEditor.InteractionMode.AutomatedAction);
  root.name = $"Vine {vn.Replace("_", " ").ToLower()}";
  foreach (var r in root.GetComponentsInChildren<UnityEngine.MeshRenderer>()) { r.sharedMaterial = vineMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
  foreach (var c in root.GetComponentsInChildren<UnityEngine.Collider>()) UnityEngine.Object.DestroyImmediate(c);
  // A readable copy of Synty's mesh next to the prefabs: the floor's merge skips unreadable meshes, and each vine was its own draw.
  foreach (var mf in root.GetComponentsInChildren<UnityEngine.MeshFilter>())
  {
    var m = mf.sharedMesh; if (m == null || m.isReadable) continue;
    string mp = $"{V}/{m.name} readable.asset"; var copy = Load<UnityEngine.Mesh>(mp);
    if (copy == null) { copy = UnityEngine.Object.Instantiate(m); copy.name = m.name + " readable"; UnityEditor.AssetDatabase.CreateAsset(copy, mp); }
    PixelRestyle.MarkReadable(copy);
    mf.sharedMesh = copy;
  }
  var saved = UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, $"{V}/{root.name}.prefab");
  UnityEngine.Object.DestroyImmediate(root);
  vines.Add(new DungeonWeightedPrefab { prefab = saved, weight = vn.StartsWith("Draped") ? .6f : 1f });
}
sb.Append($"vines {vines.Count}\n");
var made = new System.Collections.Generic.List<DungeonBiome>();

// The ruins' own dressing at the realms' density (the Crypt's rules were tombs and pews, and only along the walls, so a
// ruin read as empty and flat): fallen stone, braziers and tables in the middle of rooms, chains and beams overhead,
// cobwebs, candles and bones by the walls. Pieces from Levels/Shared/Props/Furnishing and the realms' stone props.
const string Furn = "Assets/Game/Levels/Shared/Props/Furnishing", Stone = "Assets/Game/Levels/Dungeon1/Realms/Props";
const DungeonPropPlacement W = DungeonPropPlacement.AgainstWall, C = DungeonPropPlacement.Corner, M = DungeonPropPlacement.Centre, A = DungeonPropPlacement.Anywhere;
DungeonPropRule Rule(string path, DungeonPropPlacement where, float perTen, int min, int max, float chance = 1f, int fx = 1, int fz = 1)
{
  var g = Load<UnityEngine.GameObject>(path);
  var rule = new DungeonPropRule { prefab = g, placement = where, footprint = new UnityEngine.Vector2Int(fx, fz), perTenCells = perTen * 2.6f, perRoom = new UnityEngine.Vector2Int(min, max + 1), chance = chance };
  // every 2 m cell the prop's collider covers is reserved (a wide piece must not wall a doorway off)
  var box = g != null ? g.GetComponent<UnityEngine.BoxCollider>() : null;
  if (box != null) rule.footprint = new UnityEngine.Vector2Int(UnityEngine.Mathf.Max(fx, UnityEngine.Mathf.CeilToInt(box.size.x / 2f - .05f)), UnityEngine.Mathf.Max(fz, UnityEngine.Mathf.CeilToInt(box.size.z / 2f - .05f)));
  return rule;
}
DungeonPropRule[] RuinsRules(int n) => System.Array.FindAll(new[]
{
  // in the middle of rooms
  Rule($"{Stone}/Rubble.prefab", A, .3f, 0, 2), Rule($"{Stone}/Rubble 2.prefab", A, .2f, 0, 1),
  Rule($"{Stone}/Fallen pillar.prefab", M, .12f, 0, 1, .7f, 2, 1), Rule($"{Stone}/Broken obelisk.prefab", M, .08f, 0, 1, .6f),
  Rule($"{Furn}/Brazier.prefab", M, .12f, 0, 1, .8f), Rule($"{Stone}/Stone table.prefab", M, .07f, 0, 1, .5f),
  Rule($"{Stone}/Black rock.prefab", M, n == 3 ? .12f : .05f, 0, 1, .6f),
  // overhead
  Rule($"{Furn}/Hanging chain.prefab", A, .25f, 1, 2), Rule($"{Stone}/Hanging chains.prefab", A, .12f, 0, 1, .7f),
  Rule($"{Stone}/Hanging beam.prefab", A, .15f, 0, 2, .6f),
  // by the walls and in the corners
  Rule($"{Furn}/Cobweb.prefab", C, .5f, 1, 3), Rule($"{Furn}/Candle cluster.prefab", W, .3f, 0, 2),
  Rule($"{Furn}/Bone pile.prefab", A, .2f, 0, 2), Rule($"{Furn}/Skull pile.prefab", C, .15f, 0, 1),
  Rule($"{Furn}/Bone heap.prefab", W, .1f, 0, 1, .6f), Rule($"{Furn}/Gargoyle statue.prefab", W, .07f, 0, 1, .5f),
  Rule($"{Furn}/Broken bookcase.prefab", W, .08f, 0, 1, .5f), Rule($"{Stone}/Stone bench.prefab", W, .08f, 0, 1, .5f),
  Rule($"{Stone}/Barrel stack.prefab", C, .08f, 0, 1, .5f), Rule($"{Stone}/Crate stack.prefab", C, .08f, 0, 1, .5f),
}, x => x.prefab != null);

DungeonBiome Make(int n, string display, string entry, DungeonBiome.Lighting lighting, string glow, int glowEvery)
{
  string name = $"Ruins{n}";
  var stone = Load<DungeonRoomStyle>($"{R}/{name}/{name} stone.asset");
  // Every surface a kit piece or door asks for comes from the Ruins textures (128 texels a metre), never the level's
  // 32 px fallbacks: the stone's upper wall is its wall.
  if (stone != null)
  {
    stone.upperWall = stone.bottomWall;
    // No cornice or footing: the wild walls have none, and one over a passage was the only trim left (a plain box).
    stone.hideTrims = true;
    // Variants laid among the base tiles (Tools/ruins_variety.py): the stones coloured again (two copies, laid often so
    // the 2 m repeat doesn't show), a mossy and a sooty wall (bottom tiles only: on a tile above, their foot sat on the
    // plain tile below), a mossy and a rust-stained floor. Walls and floors repeat once per 2 m cell (tiling 1.5, offset
    // .5 as the Cellar styles), so a variant fills its cell and fades out at its edges. A copy of the wall's material each.
    UnityEngine.Material Variant(string surface)
    {
      var png = $"{R}/{name}/{name} {surface}.png"; var mp = $"{R}/{name}/{name} {surface}.mat";
      UnityEditor.AssetDatabase.ImportAsset(png, UnityEditor.ImportAssetOptions.ForceSynchronousImport);
      var ti = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(png);
      if (ti == null) { sb.Append($"MISSING {png}\n"); return null; }
      ti.filterMode = UnityEngine.FilterMode.Point; ti.npotScale = UnityEditor.TextureImporterNPOTScale.None; ti.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
      ti.mipmapEnabled = true; ti.wrapMode = UnityEngine.TextureWrapMode.Repeat; ti.SaveAndReimport();
      var m = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(mp);
      if (m == null) { m = new UnityEngine.Material(stone.bottomWall); UnityEditor.AssetDatabase.CreateAsset(m, mp); }
      var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(png);
      m.SetTexture("_BaseMap", tex); if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
      bool floor = surface.Contains("floor");
      var scale = new UnityEngine.Vector2(1.5f, floor ? 1.5f : 1f); var offset = new UnityEngine.Vector2(.5f, floor ? .5f : 0f);
      foreach (var prop in new[] { "_BaseMap", "_MainTex" })
        if (m.HasProperty(prop)) { m.SetTextureScale(prop, scale); m.SetTextureOffset(prop, offset); }
      UnityEditor.EditorUtility.SetDirty(m); return m;
    }
    DungeonSurfaceVariant V(string surface, float weight, DungeonVariantRows rows = DungeonVariantRows.Any) => new DungeonSurfaceVariant { material = Variant(surface), weight = weight, rows = rows };
    stone.bottomWall = Variant("stone wall"); stone.upperWall = stone.bottomWall; stone.floor = Variant("stone floor");
    stone.wallBaseWeight = 10; stone.floorBaseWeight = 10;
    stone.wallVariants = new[] { V("stone wall shift 1", 6), V("stone wall shift 2", 6), V("stone wall moss", 2.5f, DungeonVariantRows.BottomOnly), V("stone wall soot", 1.5f, DungeonVariantRows.BottomOnly) };
    stone.floorVariants = new[] { V("stone floor shift 1", 6), V("stone floor shift 2", 6), V("stone floor moss", 2f), V("stone floor ochre", 1.5f) };
    // The hall's floor and its variants (made here, picked up below).
    foreach (var hallFloor in new[] { "stone floor b", "stone floor b shift 1", "stone floor b shift 2", "stone floor b moss", "stone floor b ochre" }) Variant(hallFloor);
    UnityEditor.EditorUtility.SetDirty(stone);
  }
  // The second room style is a hall of the same stone (the timber style's wall repeated one cobble panel in a black frame
  // every tile and looked bad, the user): the stone's wall (two wall drawings met along a wild wall in a hard line) with
  // more moss, the other floor drawing (floors of two rooms only meet in a doorway), the stone ceiling.
  var hallPath = $"{R}/{name}/{name} hall.asset";
  var hall = UnityEditor.AssetDatabase.LoadAssetAtPath<DungeonRoomStyle>(hallPath);
  if (hall == null && stone != null) { hall = UnityEngine.Object.Instantiate(stone); UnityEditor.AssetDatabase.CreateAsset(hall, hallPath); }
  if (hall != null && stone != null)
  {
    UnityEditor.EditorUtility.CopySerialized(stone, hall); hall.name = $"{name} hall";
    UnityEngine.Material M(string surface) => UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>($"{R}/{name}/{name} {surface}.mat");
    hall.bottomWall = M("stone wall"); hall.upperWall = hall.bottomWall; hall.floor = M("stone floor b");
    DungeonSurfaceVariant H(string surface, float weight, DungeonVariantRows rows = DungeonVariantRows.Any) => new DungeonSurfaceVariant { material = M(surface), weight = weight, rows = rows };
    hall.wallVariants = new[] { H("stone wall shift 1", 6), H("stone wall shift 2", 6), H("stone wall moss", 4, DungeonVariantRows.BottomOnly), H("stone wall soot", 1.5f, DungeonVariantRows.BottomOnly) };
    hall.floorVariants = new[] { H("stone floor b shift 1", 6), H("stone floor b shift 2", 6), H("stone floor b moss", 3), H("stone floor b ochre", 1.5f) };
    UnityEditor.EditorUtility.SetDirty(hall);
  }
  var path = $"Assets/Game/Levels/Dungeon1/Biomes/{name}.asset";
  var biome = UnityEditor.AssetDatabase.LoadAssetAtPath<DungeonBiome>(path);
  if (biome == null) { biome = UnityEngine.ScriptableObject.CreateInstance<DungeonBiome>(); UnityEditor.AssetDatabase.CreateAsset(biome, path); }
  biome.CopyContentFrom(crypt);
  // No Rat nest in a ruin (the user, 2026-10-30), and big halls rare: the ruins' wild walls don't stack up a tall room.
  biome.roomTemplates = System.Array.FindAll(biome.roomTemplates, t => t != null && t.template != null && t.template.name != "Rat nest");
  biome.tallRoomChance = 0f;
  // Water standing in every pit (the user, 2026-10-30): the forest ponds' water, a little under the floor.
  // Murky standing water drawn in the house style (Tools/pit_water.py), not the forest ponds' glossy teal sheet.
  biome.pitWater = Load<UnityEngine.Material>("Assets/Game/Levels/Dungeon1/Ruins/Props/Pit water/Pit water.mat"); biome.pitWaterBelow = .4f;
  // The Crypt's enemies but 3 melee, 1 archer, 1 caster (the user, 2026-10-30): no rats, bats or urn crawlers.
  DungeonBiome.Spawn En(string n, float w) => new DungeonBiome.Spawn { enemy = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>($"Assets/Game/Characters/Enemies/Crypt/{n}.prefab"), weight = w, fromFloor = 1 };
  biome.enemies = new[] { En("Crypt Soldier", 3), En("Crypt Warden", 2), En("Demon", 1), En("Crypt Archer", 2), En("Crypt Acolyte", 2) };
  biome.displayName = display; biome.entryMessage = entry; biome.entryLines = new DungeonMaster.Line[0];
  biome.kind = DungeonFloorKind.Dungeon;
  // Its own look: the Ruins styles everywhere (a hand-built room's own style too), no cellar starting room.
  biome.ownLook = true; biome.ownBoardPieces = false;
  biome.roomStyles = new[] { new DungeonStyleChoice { style = stone, weight = 3 }, new DungeonStyleChoice { style = hall, weight = 2 } };
  biome.corridorStyles = new[] { new DungeonStyleChoice { style = stone, weight = 1 } };
  biome.roomShapes = DungeonBiome.CloneAll(crypt.roomShapes);
  biome.propRules = RuinsRules(n);
  // The breakables every level shares and the level's own (an own look leaves the base out otherwise).
  var breakables = new System.Collections.Generic.List<DungeonWeightedPrefab>();
  if (level.destructibles != null) breakables.AddRange(level.destructibles);
  if (level.baseBiome != null && level.baseBiome.destructibles != null) breakables.AddRange(level.baseBiome.destructibles);
  if (crypt.destructibles != null) breakables.AddRange(crypt.destructibles);
  biome.destructibles = breakables.ToArray();
  biome.merchantChance = 1;   // a tavern on every floor, in its own plaster (its wild walls take its style)
  biome.guardian = new DungeonMilestone { enabled = false };
  biome.overrideDungeonSettings = true;
  var features = DungeonBiome.Clone(crypt.overrideDungeonSettings && crypt.roomFeatures != null ? crypt.roomFeatures : level.roomFeatures ?? new DungeonRoomFeatures());
  // Moonlit rooms are rooms whose roof has fallen in: the moon shines down through the hole.
  features.moonlitRooms = true;
  biome.roomFeatures = features;
  // A dungeon: dark and torchlit as the Crypt is, each ruin in its own light (the lighting presets).
  biome.lighting = lighting; biome.litThroughout = false; biome.fog = UnityEngine.Vector2.zero;
  // The dungeon's own light radius (the user: do not widen it).
  biome.lightRadius = 0f;
  // Wild walls under the ceiling: the masonry of each room's own style, rock outcrops and fallen ruin set into it, roots,
  // moss and rubble at its foot. Nothing grows on top (the ceiling hides it).
  var w = biome.wilds ?? new BoardWilds();
  w.enabled = true; w.roofed = true; w.depth = 2; w.height = new UnityEngine.Vector2(3.2f, 3.6f);
  w.top = moss; w.side = null;
  w.trees = new DungeonWeightedPrefab[0]; w.treeChance = 0f; w.treeScale = UnityEngine.Vector2.one;
  w.undergrowth = new DungeonWeightedPrefab[0]; w.undergrowthPerCell = 0f;
  w.lip = new DungeonWeightedPrefab[0]; w.lipChance = 0f;
  w.foot = Picks(("Roots 1", 1), ("Roots 2", 1), ("Roots 4", .8f), ("Low roots 1", 1), ("Low roots 2", 1), ("Moss 2", 1.4f), ("Moss 4", 1.2f), ("Pebbles 5", 1), ("Pebbles 3", 1), ("Mushrooms 2", glow != null ? .4f : 0f), ("Branch 4", .3f));
  w.footChance = .75f;
  w.edges = BoardWilds.Edges.Formations;
  // Set into the walls: the dungeon's own painted stone pillar (a half-buried column of the old building), now and
  // then a heap of bones, and only a little of the forest's earth outcrops (brown, they read as mud against stone).
  var faces = new System.Collections.Generic.List<DungeonWeightedPrefab>(Picks(("Formation 1", .35f), ("Formation 5", .25f)));
  foreach (var (pp, wgt) in new[] { ("Assets/Game/Levels/Shared/Room pieces/Models/Kit pillar.prefab", 3f), ("Assets/Game/Levels/Shared/Props/Furnishing/Bone heap.prefab", .8f) })
  { var g = Load<UnityEngine.GameObject>(pp); if (g != null) faces.Add(new DungeonWeightedPrefab { prefab = g, weight = wgt }); }
  w.faces = faces.ToArray();
  w.faceChance = .3f; w.faceStandOut = .35f;
  w.glows = glow != null ? Picks(($"Glow mushrooms {glow}", 1)) : new DungeonWeightedPrefab[0]; w.glowEvery = glowEvery;
  w.hangings = vines.ToArray(); w.hangChance = .3f;
  w.pondChance = 0f; w.pondEdge = new DungeonWeightedPrefab[0];
  biome.wilds = w;
  UnityEditor.EditorUtility.SetDirty(biome);
  sb.Append($"{name}: styles {stone?.name}/{hall?.name}, breakables {biome.destructibles.Length}, faces {w.faces.Length}, foot {w.foot.Length}\n");
  made.Add(biome);
  return biome;
}

// Ruins1 grey stone and oak in amber torchlight, Ruins2 pale limestone and pine in a cold moonlit stone light, Ruins3
// basalt and black oak in an eerie green with glowing mushrooms.
Make(1, "The Ruins", "Old walls, older than the crypt.", DungeonBiome.Lighting.AmberCrypt, null, 12);
Make(2, "The White Ruins", "Pale stone, half buried.", DungeonBiome.Lighting.MoonlitStone, null, 12);
Make(3, "The Black Ruins", "Something still lives here.", DungeonBiome.Lighting.Standard, "cyan", 14);

// Dungeon1's floors 7, 8 and 9: one ruin each, after the realms. Run again, they go back where they are; on a level
// without them, at the end.
var stages = new System.Collections.Generic.List<TableLevelData.BiomeStage>(level.stages);
int at = stages.FindIndex(s => s != null && made.Contains(s.biome));
stages.RemoveAll(s => s != null && made.Contains(s.biome));
if (at < 0) at = stages.Count;
foreach (var b in made) stages.Insert(at++, new TableLevelData.BiomeStage { biome = b, floors = 1, boss = false });
level.stages = stages.ToArray();
UnityEditor.EditorUtility.SetDirty(level);
UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.AssetDatabase.ImportAsset("Assets/Game/Levels/Dungeon1/Ruins/Props", UnityEditor.ImportAssetOptions.ImportRecursive | UnityEditor.ImportAssetOptions.ForceUpdate);   // what it wrote, imported
sb.Append($"run: {level.RunSummary}\n");
} catch (System.Exception e) { sb.Append("ERR " + e); }
System.IO.File.WriteAllText("Temp/Forest/ruins.txt", sb.ToString());
return "ok";
