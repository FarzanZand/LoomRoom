// The Realms biomes on Dungeon1's floors 10-12, made from the POLYGON Dungeon Realms demo scenes, safe to run again (it
// overwrites the styles, the biomes' look and lists, and the three stages):
//  - Realms1 The Deep Halls (Demo_Dungeon_01): dwarf sandstone halls, warm torchlight against a teal dark, braziers,
//    banners, dead dwarves, gold and violet crystals.
//  - Realms2 The Forge (Demo_Forge_01): soot-black basalt and iron plate, orange light everywhere, furnaces, anvils, cogs,
//    carts and ore, embers in the cracks.
//  - Realms3 The Pit (Demo_Hell_01): black volcanic rock veined with lava, red light and green witch-fire, skulls on spikes,
//    spiked fences, obelisks and giant bones.
// Walled dungeon floors under ceilings (the user: the format is dungeon), each in its own room style (Tools/realms_style.py,
// 128 texels a metre, variants per 2 m cell), furnished with the pack's props as pixel copies (Tools/realms_props.eval.cs),
// lit to the demo's colours, the tavern on every floor, no guardian. Enemies: Tools/realms_enemies.eval.cs fills the lists.
// Writes Temp/Realms/setup.txt.
if (UnityEngine.Application.isPlaying) return "playing";
const string R = "Assets/Game/Levels/Shared/Room styles/Realms";
const string P = "Assets/Game/Levels/Dungeon1/Realms/Props";
var sb = new System.Text.StringBuilder();
try {
T Load<T>(string p) where T : UnityEngine.Object { var a = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(p); if (a == null) sb.Append($"MISSING {p}\n"); return a; }
UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
var level = Load<TableLevelData>("Assets/Game/Levels/Dungeon1/Dungeon1.asset");
var crypt = Load<DungeonBiome>("Assets/Game/Levels/Shared/Biomes/Cellars3.asset");
var template = Load<DungeonRoomStyle>("Assets/Game/Levels/Shared/Room styles/Ruins/Ruins1/Ruins1 stone.asset");
var torch = Load<UnityEngine.GameObject>("Assets/Game/Levels/Shared/Props/Furnishing/Wall torch.prefab");
var made = new System.Collections.Generic.List<DungeonBiome>();

UnityEngine.Material Mat(string name, string surface, bool cell)
{
  var png = $"{R}/{name}/{name} {surface}.png"; var mp = $"{R}/{name}/{name} {surface}.mat";
  UnityEditor.AssetDatabase.ImportAsset(png, UnityEditor.ImportAssetOptions.ForceSynchronousImport);
  var ti = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(png);
  if (ti == null) { sb.Append($"MISSING {png}\n"); return null; }
  ti.filterMode = UnityEngine.FilterMode.Point; ti.npotScale = UnityEditor.TextureImporterNPOTScale.None; ti.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
  ti.mipmapEnabled = true; ti.wrapMode = UnityEngine.TextureWrapMode.Repeat; ti.SaveAndReimport();
  var m = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(mp);
  if (m == null) { m = new UnityEngine.Material(template.bottomWall); UnityEditor.AssetDatabase.CreateAsset(m, mp); }
  var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(png);
  m.SetTexture("_BaseMap", tex); if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
  bool floor = surface.Contains("floor");
  // A texture with variants repeats once per 2 m cell (tiling 1.5, offset .5); ceiling and trim once per 3 m tile.
  var scale = cell ? new UnityEngine.Vector2(1.5f, floor ? 1.5f : 1f) : UnityEngine.Vector2.one;
  var offset = cell ? new UnityEngine.Vector2(.5f, floor ? .5f : 0f) : UnityEngine.Vector2.zero;
  foreach (var prop in new[] { "_BaseMap", "_MainTex" }) if (m.HasProperty(prop)) { m.SetTextureScale(prop, scale); m.SetTextureOffset(prop, offset); }
  UnityEditor.EditorUtility.SetDirty(m); return m;
}

DungeonRoomStyle Style(int n)
{
  string name = $"Realms{n}";
  var path = $"{R}/{name}/{name}.asset";
  var style = UnityEditor.AssetDatabase.LoadAssetAtPath<DungeonRoomStyle>(path);
  if (style == null) { style = UnityEngine.Object.Instantiate(template); UnityEditor.AssetDatabase.CreateAsset(style, path); }
  UnityEditor.EditorUtility.CopySerialized(template, style); style.name = name;
  DungeonSurfaceVariant V(string s, float w, DungeonVariantRows rows = DungeonVariantRows.Any) => new DungeonSurfaceVariant { material = Mat(name, s, true), weight = w, rows = rows };
  style.bottomWall = Mat(name, "wall", true); style.upperWall = style.bottomWall;
  style.floor = Mat(name, "floor", true);
  style.ceiling = Mat(name, "ceiling", false);
  style.trim = Mat(name, "trim", false); style.hideTrims = false;
  style.wallBaseWeight = 10; style.floorBaseWeight = 10;
  var walls = new System.Collections.Generic.List<DungeonSurfaceVariant> { V("wall shift 1", 6), V("wall shift 2", 6), V("wall soot", 2f, DungeonVariantRows.BottomOnly) };
  walls.Add(n == 1 ? V("wall rune", 1.2f) : V("wall ember", n == 3 ? 3f : 1.5f));
  style.wallVariants = walls.ToArray();
  var floors = new System.Collections.Generic.List<DungeonSurfaceVariant> { V("floor shift 1", 6), V("floor shift 2", 6), V("floor soot", 2f) };
  if (n != 1) floors.Add(V("floor ember", n == 3 ? 3f : 1.5f));
  style.floorVariants = floors.ToArray();
  // The demo halls' walls are never bare: pilasters every few metres, banners, crests and alcoves between them; in the
  // forge lava falls and grates, in the pit black rock and chains (Tools/realms_props.eval.cs; "Full height" pieces are
  // stacked up a tall hall's wall).
  DungeonWeightedPrefab Dec(string n, float w) => new DungeonWeightedPrefab { prefab = Load<UnityEngine.GameObject>($"{P}/{n}.prefab"), weight = w };
  style.wallDecor = n == 1 ? new[] { Dec("Full height pilaster", 3), Dec("Full height pilaster carved", 2), Dec("Full height banner", 2), Dec("Wall shield crest", 1.2f), Dec("Full height alcove", .8f) }
    : n == 2 ? new[] { Dec("Full height pilaster basalt", 3), Dec("Wall grate", 1.2f), Dec("Full height chains", 1), Dec("Full height banner", .6f) }
    : new[] { Dec("Full height black rock", 3), Dec("Full height chains", 1.8f) };
  // Polish 4: no lava falls on the walls: they are flat glowing cards that read as orange slabs and blew out the view up
  // close. The lava is in the floor (channels, pits, the pots).
  style.wallDecorPerTenFaces = 6f;
  UnityEditor.EditorUtility.SetDirty(style);
  return style;
}

// Life in the air (Tools/realms_life.eval.cs) and shared furnishing, by path.
DungeonPropRule Any(string path, DungeonPropPlacement where, float perTen, int min, int max, float chance = 1f) =>
  new DungeonPropRule { prefab = Load<UnityEngine.GameObject>(path), placement = where, footprint = UnityEngine.Vector2Int.one, perTenCells = perTen, perRoom = new UnityEngine.Vector2Int(min, max), chance = chance };
DungeonPropRule Life(string n, float chance) => new DungeonPropRule { prefab = Load<UnityEngine.GameObject>($"{P}/Life/{n}.prefab"), placement = DungeonPropPlacement.Centre, footprint = UnityEngine.Vector2Int.one, perTenCells = .25f, perRoom = new UnityEngine.Vector2Int(1, 1), chance = chance, rooms = (DungeonRoomRoles)62 };
DungeonPropRule Rule(string prop, DungeonPropPlacement where, float perTen, int min, int max, float chance = 1f, int fx = 1, int fz = 1, DungeonRoomRoles rooms = (DungeonRoomRoles)(-1))
{
  var g = Load<UnityEngine.GameObject>($"{P}/{prop}.prefab");
  // Denser than the Crypt: the demo scenes are crowded with dressing.
  var r = new DungeonPropRule { prefab = g, placement = where, footprint = new UnityEngine.Vector2Int(fx, fz), perTenCells = perTen * 2.6f, perRoom = new UnityEngine.Vector2Int(min, max + 1), chance = chance };
  if ((int)rooms != -1) r.rooms = rooms;
  // Reserve every 2 m cell the prop's collider covers (a hoard wider than its one cell walled a doorway off).
  var box = g != null ? g.GetComponent<UnityEngine.BoxCollider>() : null;
  if (box != null) r.footprint = new UnityEngine.Vector2Int(UnityEngine.Mathf.Max(fx, UnityEngine.Mathf.CeilToInt(box.size.x / 2f - .05f)), UnityEngine.Mathf.Max(fz, UnityEngine.Mathf.CeilToInt(box.size.z / 2f - .05f)));
  return r;
}
const DungeonPropPlacement W = DungeonPropPlacement.AgainstWall, C = DungeonPropPlacement.Corner, M = DungeonPropPlacement.Centre, A = DungeonPropPlacement.Anywhere;
const DungeonRoomRoles Rich = DungeonRoomRoles.Treasure | DungeonRoomRoles.Storage;

DungeonBiome Make(int n, string display, string entry, float templates, DungeonPropRule[] rules, UnityEngine.Color torchColour, DungeonLightingSettings light, UnityEngine.Color shadow, UnityEngine.Color lamp, float filmic, UnityEngine.Vector2 fog)
{
  string name = $"Realms{n}";
  var style = Style(n);
  var path = $"Assets/Game/Levels/Dungeon1/Biomes/{name}.asset";
  var biome = UnityEditor.AssetDatabase.LoadAssetAtPath<DungeonBiome>(path);
  if (biome == null) { biome = UnityEngine.ScriptableObject.CreateInstance<DungeonBiome>(); UnityEditor.AssetDatabase.CreateAsset(biome, path); }
  var keepEnemies = biome.enemies != null && biome.enemies.Length > 0 && biome.enemies[0].enemy != null && UnityEditor.AssetDatabase.GetAssetPath(biome.enemies[0].enemy).Contains("/Enemies/Realms/") ? biome.enemies : null;
  biome.CopyContentFrom(crypt);
  if (keepEnemies != null) biome.enemies = keepEnemies;
  biome.displayName = display; biome.entryMessage = entry; biome.entryLines = new DungeonMaster.Line[0];
  biome.kind = DungeonFloorKind.Dungeon;
  biome.ownLook = true; biome.ownBoardPieces = false;
  // Polish 1: the Crypt's hand-built rooms built of the realm's own stone (their painted kit pillars were grey crypt stone),
  // and the ones that are the Crypt itself (bone niches, coffins, pews, moonlight, the witch) left out of the pool.
  biome.plainKit = true;
  var cryptRooms = new System.Collections.Generic.HashSet<string> { "Crypt niches", "Ossuary", "Burial hall", "Chapel", "Bone chapel", "Not a witch", "Rat nest", "Moonlit fountain", "Shrine of Ashes" };
  biome.roomTemplates = System.Array.FindAll(biome.roomTemplates, t => t != null && t.template != null && !cryptRooms.Contains(t.template.name));
  biome.roomStyles = new[] { new DungeonStyleChoice { style = style, weight = 1 } };
  biome.corridorStyles = new[] { new DungeonStyleChoice { style = style, weight = 1 } };
  biome.roomShapes = DungeonBiome.CloneAll(crypt.roomShapes);
  biome.propRules = rules;
  // Fewer hand-built rooms than the Crypt, so more rooms are furnished with the pack's props (a hand-built room keeps its own).
  biome.templateChance = templates;
  // A rare big room built two tiles high as a hall (the user: some tall, made epic; then, 2026-10-30, big halls rare, most
  // rooms one tile high), dressed to the ceiling.
  biome.tallRoomChance = .07f; biome.tallRoomLeastCells = 20;
  var breakables = new System.Collections.Generic.List<DungeonWeightedPrefab>();
  if (level.destructibles != null) breakables.AddRange(level.destructibles);
  if (level.baseBiome != null && level.baseBiome.destructibles != null) breakables.AddRange(level.baseBiome.destructibles);
  if (crypt.destructibles != null) breakables.AddRange(crypt.destructibles);
  biome.destructibles = breakables.ToArray();
  biome.merchantChance = 1;
  biome.guardian = new DungeonMilestone { enabled = false };
  biome.overrideDungeonSettings = true;
  biome.roomFeatures = DungeonBiome.Clone(crypt.overrideDungeonSettings && crypt.roomFeatures != null ? crypt.roomFeatures : level.roomFeatures ?? new DungeonRoomFeatures());
  // Polish 3: deep under the ground there is no moonlight, and only the pit has witch-fire (a green room read as a fault in
  // the halls and the forge); no dark rooms either, the realms are lit throughout.
  biome.roomFeatures.moonlitRooms = false; biome.roomFeatures.darkRooms = false; biome.roomFeatures.eerieRooms = n == 3;
  biome.wilds = new BoardWilds { enabled = false };
  biome.lighting = DungeonBiome.Lighting.Custom; biome.lightingSettings = light;
  // The demo scenes' coloured dark (teal in the halls, red-orange haze in the forge, red in the pit) reaches every
  // corner, fading into its fog colour with distance: lit throughout with a low ambient, so torches still make the pools.
  biome.litThroughout = true; biome.fog = fog; biome.lightRadius = 0f;
  biome.shadowTint = shadow; biome.lampTint = lamp; biome.filmicLook = filmic;
  biome.lightingPrefabs = new[] { new DungeonWallLight { prefab = torch, weight = 1, overrideColor = true, color = torchColour } };
  // The pit is lit hardest: its entrance room was close to black under the red fog (2026-10-30), so its torches are
  // brighter and more of them, and its red ambient higher (its fog kept, the user likes it).
  biome.overrideTorchColor = false; biome.torchBrightness = n == 1 ? 1.05f : n == 2 ? 1.6f : 3f;
  biome.torchesPerRoom = n == 1 ? biome.torchesPerRoom : n == 2 ? new UnityEngine.Vector2Int(UnityEngine.Mathf.Max(2, biome.torchesPerRoom.x), UnityEngine.Mathf.Max(3, biome.torchesPerRoom.y + 1)) : new UnityEngine.Vector2Int(3, UnityEngine.Mathf.Max(5, biome.torchesPerRoom.y + 1));
  UnityEditor.EditorUtility.SetDirty(biome);
  sb.Append($"{name}: style {style.name}, rules {rules.Length}, breakables {biome.destructibles.Length}, enemies {biome.enemies.Length}\n");
  made.Add(biome);
  return biome;
}

DungeonLightingSettings L(UnityEngine.Color sky, UnityEngine.Color ambient, UnityEngine.Color ground, UnityEngine.Color fog, UnityEngine.Color sun, float sunIntensity) => new DungeonLightingSettings
{
  skyTint = sky, skyExposure = .6f, lightColor = sun, lightIntensity = sunIntensity,
  ambientSky = ambient, ambientHorizon = UnityEngine.Color.Lerp(ambient, ground, .5f), ambientGround = ground, fogColor = fog
};

// The Deep Halls: the dungeon demo's warm orange torches (1, .66, .38) against its teal fog (.10, .47, .47) and grey sky.
Make(1, "The Deep Halls", "Dwarf halls. Nobody has swept in a while.", .12f, new[]
{
  Rule("Dwarf brazier", C, .35f, 0, 2),
  Rule("Dwarf banner", W, .5f, 0, 3), Rule("Dwarf banner torn", W, .3f, 0, 2),
  Rule("Dwarf pillar", C, .3f, 0, 2), Rule("Dwarf obelisk", W, .15f, 0, 1), Rule("Broken obelisk", A, .15f, 0, 1),
  Rule("Realms barrel", W, .3f, 0, 2), Rule("Barrel stack", C, .2f, 0, 1, 1f, 2, 1), Rule("Crate stack", C, .2f, 0, 1),
  Rule("Weapon barrel", W, .25f, 0, 1),   // no weapon rack: the pack's is a stone bracket that read as a broken block, and weapons stood through it
  Rule("Stone table", M, .12f, 0, 1, .6f, 1, 1, DungeonRoomRoles.Rest | DungeonRoomRoles.Storage | DungeonRoomRoles.Combat), Rule("Stone bench", W, .2f, 0, 2), Rule("Stone stool", A, .15f, 0, 2),
  Rule("Stone shelf", W, .2f, 0, 1), Rule("Dwarf sarcophagus", W, .12f, 0, 1),
  Rule("Dead dwarf", A, .2f, 0, 1), Rule("Dead dwarf 2", A, .15f, 0, 1),
  Rule("Gold pile", A, .3f, 1, 2, 1f, 1, 1, Rich), Rule("Gold coins", A, .3f, 0, 2),
  Rule("Crystal cluster", C, .2f, 0, 1, .7f), Rule("Crystal cluster 2", C, .15f, 0, 1, .6f),
  Rule("Dwarf rug", M, .15f, 0, 1, .5f, 2, 1), Rule("Gold hoard", M, .2f, 1, 1, 1f, 1, 1, Rich), Rule("Gold hoard", C, .1f, 0, 1, .35f),
  Any("Assets/Game/Levels/Shared/Props/Furnishing/Cobweb.prefab", C, .5f, 1, 3), Life("Dust motes", .9f),
  Rule("Hanging chandelier", M, .2f, 1, 2), Rule("Hanging beam", A, .3f, 1, 3), Rule("Dwarf throne", W, .06f, 0, 1, .6f, 1, 1, DungeonRoomRoles.Treasure | DungeonRoomRoles.Rest | DungeonRoomRoles.Combat), Rule("Dwarf totem", C, .15f, 0, 1), Rule("Rubble", A, .3f, 0, 2), Rule("Rubble 2", A, .2f, 0, 1), Rule("Fallen pillar", A, .1f, 0, 1, .6f, 2, 1),
  // Polish 2: the ceilings were bare; more beams and chandeliers overhead, as the demo halls have.
  Rule("Hanging beam", A, .3f, 1, 2), Rule("Hanging chandelier", A, .1f, 0, 1, .6f),
  // Iteration 6: the middle of a hall was bare floor; pillars, a brazier, a table or a totem stand in it, as in the dungeon demo's halls.
  Rule("Dwarf pillar", M, .2f, 0, 2, .8f), Rule("Dwarf brazier", M, .15f, 0, 1, .8f), Rule("Stone table", M, .12f, 0, 1, .6f, 1, 1), Rule("Dwarf totem", M, .12f, 0, 1, .6f), Rule("Barrel stack", M, .1f, 0, 1, .5f, 2, 1),
}, new UnityEngine.Color(1f, .66f, .38f),
  L(new UnityEngine.Color(.21f, .23f, .26f), new UnityEngine.Color(.13f, .27f, .29f), new UnityEngine.Color(.07f, .13f, .14f), new UnityEngine.Color(.06f, .3f, .31f), new UnityEngine.Color(1f, .9f, .75f), .3f),
  new UnityEngine.Color(.5f, .85f, .88f), new UnityEngine.Color(1.08f, .93f, .78f), .7f, new UnityEngine.Vector2(6f, 42f));

// The Forge: the forge demo's orange light (1, .56, .10) everywhere, its flat brown-orange ambient (.35, .19, 0) and red-orange fog.
Make(2, "The Forge", "The forges are still lit. Someone keeps them lit.", .12f, new[]
{
  Rule("Furnace", W, .25f, 0, 1), Rule("Smelting pot", C, .25f, 0, 1), Rule("Molten pit", M, .12f, 0, 1, .7f),
  Rule("Anvil", A, .3f, 0, 2), Rule("Golem arm", A, .1f, 0, 1, .6f, 2, 1), Rule("Golem head", C, .1f, 0, 1, .6f),
  Rule("Cog box", W, .2f, 0, 1), Rule("Cog pile", A, .25f, 0, 2), Rule("Cog pile 2", A, .2f, 0, 1), Rule("Big cog", W, .15f, 0, 1),
  Rule("Ore crate", W, .25f, 0, 2), Rule("Ore crates", C, .2f, 0, 1),
  Rule("Mine cart", A, .15f, 0, 1), Rule("Ore cart", A, .12f, 0, 1), Rule("Dwarf cannon", W, .08f, 0, 1, .5f),
  Rule("Ore pile", A, .2f, 0, 1, 1f, 1, 1), Rule("Ore pile 2", A, .15f, 0, 1),
  Rule("Smelter mould", M, .08f, 0, 1, .5f, 2, 1), Rule("Lava duct", C, .2f, 0, 2),
  Rule("Forge hammer", A, .15f, 0, 1), Rule("Forge tongs", A, .15f, 0, 1),
  Rule("Realms barrel", W, .25f, 0, 2), Rule("Crate stack", C, .2f, 0, 1), Rule("Weapon barrel", W, .15f, 0, 1),
  Rule("Gold pile", A, .25f, 1, 1, 1f, 1, 1, Rich), Rule("Gold hoard", M, .15f, 0, 1, .6f, 1, 1, Rich),
  Life("Rising embers", .9f),
  Rule("Hanging cage lamp", M, .2f, 1, 2), Rule("Hanging girder", A, .3f, 1, 3), Rule("Hanging girder", A, .25f, 1, 2), Rule("Hanging chains", A, .2f, 0, 2), Rule("Hanging chain ring", M, .1f, 0, 1, .6f), Rule("Lava channel", A, .15f, 0, 1, .7f, 1, 2),
}, new UnityEngine.Color(1f, .56f, .12f),
  L(new UnityEngine.Color(.3f, .17f, .08f), new UnityEngine.Color(.38f, .2f, .07f), new UnityEngine.Color(.2f, .09f, .04f), new UnityEngine.Color(.5f, .15f, .09f), new UnityEngine.Color(1f, .62f, .3f), .35f),
  new UnityEngine.Color(.9f, .62f, .48f), new UnityEngine.Color(1.14f, .86f, .62f), .6f, new UnityEngine.Vector2(6f, 42f));

// The Pit: the hell demo's red light (1, .27, 0), its red fog (.57, .2, .15) and blue-tinted shadows, green witch-fire accents.
Make(3, "The Pit", "Down here it is warm. Too warm.", .12f, new[]
{
  Rule("Hell brazier", C, .3f, 0, 2), Rule("Hell brazier wide", M, .1f, 0, 1, .5f),
  Rule("Spiked fence", W, .35f, 0, 2), Rule("Spiked fence 2", W, .25f, 0, 2), Rule("Spiked post", W, .2f, 0, 1),
  Rule("Skull spike", A, .25f, 0, 2), Rule("Skull spike 2", A, .2f, 0, 1), Rule("Skull spike 3", A, .2f, 0, 1),
  Rule("Spike cluster", C, .2f, 0, 1), Rule("Black spike", C, .25f, 0, 1), Rule("Black spike 2", C, .2f, 0, 1),
  Rule("Hell obelisk", C, .15f, 0, 1), Rule("Hell pillar", C, .15f, 0, 1), Rule("Hell symbol", W, .12f, 0, 1),
  Rule("Black rock", A, .3f, 0, 2), Rule("Black crag", W, .2f, 0, 1), Rule("Horned skull", A, .2f, 0, 1),
  Rule("Giant skull", C, .08f, 0, 1, .6f, 2, 1), Rule("Giant rib", W, .12f, 0, 2),
  Rule("Hell circle", M, .1f, 0, 1, .5f, 2, 2),
  Rule("Gold pile", A, .2f, 1, 1, 1f, 1, 1, Rich),
  Life("Rising embers", .7f), Life("Falling ash", .7f), Life("Witch-fire", .45f),
  Rule("Hanging chains", A, .35f, 1, 3), Rule("Hanging chain ring", M, .15f, 0, 1, .7f), Rule("Hanging cage lamp", M, .2f, 1, 2),
  // The pit glows: lava in the floor, more fire, the green circles of the hell demo, bones and spikes everywhere.
  Rule("Lava channel", A, .25f, 0, 2, .9f, 1, 2), Rule("Hell brazier", W, .3f, 1, 2), Rule("Hell circle", M, .1f, 0, 1, .8f, 2, 2),
  Rule("Horned skull", A, .3f, 1, 2), Rule("Spike cluster", W, .2f, 0, 2), Rule("Rubble", A, .3f, 1, 2),
  // Polish 2: the pit read as dark red emptiness; more fire and lava to see by, cage lamps overhead.
  Rule("Hell brazier", C, .3f, 1, 2), Rule("Lava channel", M, .15f, 0, 1, .8f, 1, 2), Rule("Hanging cage lamp", A, .2f, 1, 2),
  // Iteration 4: the middle of a room was bare floor; black rock, spikes and pillars stand out in it, lit by more fire.
  Rule("Black spike", M, .2f, 0, 2, .8f), Rule("Hell pillar", M, .12f, 0, 1, .7f), Rule("Black rock", M, .2f, 0, 1, .8f), Rule("Hell brazier", M, .15f, 0, 1, .8f), Rule("Skull spike", M, .15f, 0, 1, .7f),
}, new UnityEngine.Color(1f, .45f, .14f),
  L(new UnityEngine.Color(.15f, .2f, .24f), new UnityEngine.Color(1f, .405f, .27f), new UnityEngine.Color(.513f, .216f, .176f), new UnityEngine.Color(.46f, .14f, .09f), new UnityEngine.Color(1f, .5f, .3f), .5f),
  new UnityEngine.Color(.82f, .62f, .7f), new UnityEngine.Color(1.18f, .78f, .58f), .55f, new UnityEngine.Vector2(8f, 48f));

// Dungeon1's Realms floors: one realm each.
var stages = new System.Collections.Generic.List<TableLevelData.BiomeStage>(level.stages);
// Kept where they stand in the run (the user moved them to floors 4-6, 2026-10-30); a first run adds them at the end.
int at = stages.FindIndex(s => s != null && made.Contains(s.biome));
stages.RemoveAll(s => s != null && made.Contains(s.biome));
if (at < 0 || at > stages.Count) at = stages.Count;
for (int k = 0; k < made.Count; k++) stages.Insert(at + k, new TableLevelData.BiomeStage { biome = made[k], floors = 1, boss = false });
level.stages = stages.ToArray();
// Two scripted event rooms on every floor (the user, 2026-10-30), the tavern not counted.
// The level's event rooms per floor are left to the level asset (they count for every floor, not only the realms').
UnityEditor.EditorUtility.SetDirty(level);
UnityEditor.AssetDatabase.SaveAssets();
sb.Append($"run: {level.RunSummary}\n");
} catch (System.Exception e) { sb.Append("ERR " + e); }
System.IO.Directory.CreateDirectory("Temp/Realms");
System.IO.File.WriteAllText("Temp/Realms/setup.txt", sb.ToString());
return "ok";
