// The forest biomes on Dungeon1's floors 4-6 (Forest1, Forest2 the autumn wood, Forest3 the blue moonlit wood), safe to run again (it overwrites the biome's look and lists, the materials
// and the styles; the props come from Tools/forest_props.eval.cs, the textures from Tools/forest_textures.py):
//  - Materials (Levels/Dungeon1/Forest/Materials): the ground textures point-filtered at 128 texels a metre, each repeating a
//    whole number of times per 3 m architecture tile, and their variants (mossy, rooty, damp floors; rooty, stony, mossy banks).
//  - Styles (Levels/Dungeon1/Forest/Styles): Forest1 (clearings: leaf litter, earth sides), Forest2 (fallen gold leaves) and Forest1 path.
//  - Glows (Props/Glow mushrooms <colour>): the glowing mushroom groups with a soft light under them.
//  - The biomes (Levels/Dungeon1/Biomes/Forest1-3): a board, all rooms at once, its own board pieces and look, bigger and
//    fewer rooms, no doors, wild banks with trees round them, ponds; the Catacombs' (Cellars2) enemies and loot.
//  - Dungeon1: the three as stages 10-12 of one floor each, no boss (kept where they are when run again).
// Writes Temp/Forest/setup.txt.
if (UnityEngine.Application.isPlaying) return "playing";
const string F = "Assets/Game/Levels/Dungeon1/Forest";
var sb = new System.Text.StringBuilder();
try {
void Folder(string path) { var parts = path.Split('/'); var cur = parts[0]; for (int i = 1; i < parts.Length; i++) { var next = cur + "/" + parts[i]; if (!UnityEditor.AssetDatabase.IsValidFolder(next)) UnityEditor.AssetDatabase.CreateFolder(cur, parts[i]); cur = next; } }
Folder($"{F}/Materials"); Folder($"{F}/Styles"); Folder("Assets/Game/Levels/Dungeon1/Biomes");
T Load<T>(string p) where T : UnityEngine.Object { var a = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(p); if (a == null) sb.Append($"MISSING {p}\n"); return a; }

UnityEngine.Material Ground(string name, float metres)
{
  var png = $"{F}/Textures/{name}.png";
  UnityEditor.AssetDatabase.ImportAsset(png, UnityEditor.ImportAssetOptions.ForceSynchronousImport);
  var imp = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(png);
  imp.filterMode = UnityEngine.FilterMode.Point; imp.npotScale = UnityEditor.TextureImporterNPOTScale.None; imp.mipmapEnabled = true;
  imp.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed; imp.wrapMode = UnityEngine.TextureWrapMode.Repeat; imp.SaveAndReimport();
  var tex = Load<UnityEngine.Texture2D>(png);
  var path = $"{F}/Materials/{name}.mat";
  var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(path);
  if (mat == null) { mat = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Lit")); UnityEditor.AssetDatabase.CreateAsset(mat, path); }
  mat.SetTexture("_BaseMap", tex); mat.SetColor("_BaseColor", UnityEngine.Color.white);
  // The texture covers `metres` (a 2 m cell's ground, 256 texels, covers 2 m across whatever metres says); UVs repeat
  // every 3 m. A texture repeating once per 2 m cell (the grounds with variants, Tools/forest_textures.py) is offset half a
  // repeat so its edges fall on the cells' edges (odd metres), as the Cellar styles are: a variant fills its own cell.
  float mx = tex.width == 256 ? 2f : metres, my = tex.height == 256 ? 2f : tex.height == 384 ? 3f : metres;
  mat.SetTextureScale("_BaseMap", new UnityEngine.Vector2(3f / mx, 3f / my));
  mat.SetTextureOffset("_BaseMap", new UnityEngine.Vector2(mx == 2f ? .5f : 0f, my == 2f ? .5f : 0f));
  mat.SetFloat("_Smoothness", 0f); mat.SetFloat("_SpecularHighlights", 0f); mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); mat.enableInstancing = true;
  UnityEditor.EditorUtility.SetDirty(mat);
  sb.Append($"{name}: {tex.width} x {tex.height} px over {mx} x {my} m = {tex.width / mx:F0} texels a metre\n");
  return mat;
}
// Every texture repeats a whole number of times per 3 m tile (the cellar's 384 px a tile, 128 texels a metre), so the
// tiles meet without a seam.
var floor = Ground("Forest floor", 3f);
var path = Ground("Forest path", 3f);
var moss = Ground("Forest moss", 1.5f);
var earth = Ground("Earth bank", 3f);
var bed = Ground("Pond bed", 1.5f);
// Variants, laid now and then among the base tiles as the cellar's cracked and mossy tiles are.
DungeonSurfaceVariant[] Variants(float weight, params string[] names)
{
  var v = new System.Collections.Generic.List<DungeonSurfaceVariant>();
  foreach (var n in names) v.Add(new DungeonSurfaceVariant { material = Ground(n, 3f), weight = weight, rows = DungeonVariantRows.Any });
  return v.ToArray();
}
// Moss half as often as the others: it should be found now and then, not on every stretch of bank (the user, 2026-10-28).
var bankVariants = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(Variants(1.6f, "Earth bank roots", "Earth bank stony"), Variants(.6f, "Earth bank mossy")));
var water = Load<UnityEngine.Material>("Assets/Synty/PolygonNatureBiomes/PNB_Enchanted_Forest/Materials/Water_Enchanted.mat");

var level = Load<TableLevelData>("Assets/Game/Levels/Dungeon1/Dungeon1.asset");
DungeonRoomStyle Style(string name, UnityEngine.Material f, DungeonSurfaceVariant[] floorVariants)
{
  var p = $"{F}/Styles/{name}.asset";
  var s = UnityEditor.AssetDatabase.LoadAssetAtPath<DungeonRoomStyle>(p);
  if (s == null) { s = UnityEngine.ScriptableObject.CreateInstance<DungeonRoomStyle>(); UnityEditor.AssetDatabase.CreateAsset(s, p); }
  s.floor = f; s.bottomWall = earth; s.upperWall = earth; s.ceiling = moss; s.trim = earth; s.hideTrims = true;
  s.wood = level.defaultStyle != null ? level.defaultStyle.wood : null; s.metal = level.defaultStyle != null ? level.defaultStyle.metal : null;
  s.wallBaseWeight = 10; s.floorBaseWeight = 10; s.wallVariants = bankVariants; s.floorVariants = floorVariants; s.wallDecor = new DungeonWeightedPrefab[0];
  UnityEditor.EditorUtility.SetDirty(s);
  return s;
}
var clearing = Style("Forest1", floor, Variants(1.4f, "Forest floor mossy", "Forest floor roots", "Forest floor damp"));
var trail = Style("Forest1 path", path, new DungeonSurfaceVariant[0]);

// Glows: a glowing mushroom group with a soft coloured light just above it (the light is the prop's child).
var glowColours = new (string name, UnityEngine.Color c, int mush)[] { ("cyan", new UnityEngine.Color(.35f, .95f, 1f), 1), ("violet", new UnityEngine.Color(.62f, .35f, 1f), 3), ("gold", new UnityEngine.Color(1f, .78f, .35f), 5) };
var glows = new System.Collections.Generic.List<DungeonWeightedPrefab>();
foreach (var (gname, colour, mush) in glowColours)
{
  var src = Load<UnityEngine.GameObject>($"{F}/Props/Mushrooms {mush}/Mushrooms {mush}.prefab");
  if (src == null) continue;
  var root = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(src);
  UnityEditor.PrefabUtility.UnpackPrefabInstance(root, UnityEditor.PrefabUnpackMode.Completely, UnityEditor.InteractionMode.AutomatedAction);
  root.name = $"Glow mushrooms {gname}";
  var lgo = new UnityEngine.GameObject("Glow light"); lgo.transform.SetParent(root.transform, false); lgo.transform.localPosition = new UnityEngine.Vector3(0, .9f, .3f);
  var l = lgo.AddComponent<UnityEngine.Light>(); l.type = UnityEngine.LightType.Point; l.color = colour; l.intensity = 5f; l.range = 9f; l.shadows = UnityEngine.LightShadows.None;
  var gp = $"{F}/Props/Glow mushrooms {gname}.prefab";
  var saved = UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, gp);
  UnityEngine.Object.DestroyImmediate(root);
  glows.Add(new DungeonWeightedPrefab { prefab = saved, weight = gname == "cyan" ? 2 : 1 });
}

DungeonWeightedPrefab[] Picks(params (string name, float w)[] list)
{
  var r = new System.Collections.Generic.List<DungeonWeightedPrefab>();
  foreach (var (n, w) in list)
  {
    var folder = n.Contains(" green") || n.Contains(" gold") || n.Contains(" red") || n.Contains(" teal") ? n.Substring(0, n.LastIndexOf(' ')) : n;
    var go = Load<UnityEngine.GameObject>($"{F}/Props/{folder}/{n}.prefab");
    if (go != null) r.Add(new DungeonWeightedPrefab { prefab = go, weight = w });
  }
  return r.ToArray();
}

// The three forests share one recipe; each has its own look (Spec). What lives there and what it drops: the Catacombs'.
var catacombs = Load<DungeonBiome>("Assets/Game/Levels/Shared/Biomes/Cellars2.asset");
var floorFall = Ground("Forest floor fall", 3f);
var clearingFall = Style("Forest2", floorFall, Variants(2f, "Forest floor fall red", "Forest floor fall mossy"));
DungeonPropRule Rule(string n, int placement, float per10, int max, float chance)
{
  var go = Load<UnityEngine.GameObject>($"{F}/Props/{n}/{n}.prefab");
  return new DungeonPropRule { prefab = go, placement = (DungeonPropPlacement)placement, footprint = new UnityEngine.Vector2Int(1, 1), perTenCells = per10, perRoom = new UnityEngine.Vector2Int(0, max), chance = chance, rooms = (DungeonRoomRoles)62 };
}
// Life in the air (Props/Fireflies, Props/Falling leaves): a particle system over a clearing, no light of its own.
// Fireflies are the spell pack's pixel glow blinking as they drift; leaves are a sheet of four painted leaves
// drifting down, lit by the board light like everything else (24 texels over 19 cm: 128 texels a metre).
UnityEngine.GameObject Air(string name, bool leaves, UnityEngine.Color colour)
{
  var dir = $"{F}/Props/{name}"; Folder(dir);
  var root = new UnityEngine.GameObject(name);
  var ps = root.AddComponent<UnityEngine.ParticleSystem>();
  var main = ps.main; main.loop = true; main.playOnAwake = true; main.prewarm = true; main.simulationSpace = UnityEngine.ParticleSystemSimulationSpace.World;
  var shape = ps.shape; shape.shapeType = UnityEngine.ParticleSystemShapeType.Box;
  var noise = ps.noise; noise.enabled = true;
  var emission = ps.emission;
  var col = ps.colorOverLifetime; col.enabled = true;
  var renderer = root.GetComponent<UnityEngine.ParticleSystemRenderer>();
  renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
  if (leaves)
  {
    main.startLifetime = 12f; main.startSpeed = 0f; main.startSize = .1875f; main.maxParticles = 40;
    main.startRotation = new UnityEngine.ParticleSystem.MinMaxCurve(0f, 6.28f);
    emission.rateOverTime = 3f;
    shape.position = new UnityEngine.Vector3(0, 5f, 0); shape.scale = new UnityEngine.Vector3(8f, .5f, 8f);
    var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = UnityEngine.ParticleSystemSimulationSpace.World;
    vel.x = new UnityEngine.ParticleSystem.MinMaxCurve(-.08f, .12f); vel.y = new UnityEngine.ParticleSystem.MinMaxCurve(-.55f, -.4f); vel.z = new UnityEngine.ParticleSystem.MinMaxCurve(-.08f, .12f);
    noise.strength = .45f; noise.frequency = .35f; noise.scrollSpeed = .2f;
    var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new UnityEngine.ParticleSystem.MinMaxCurve(-2.2f, 2.2f);
    var sheet = ps.textureSheetAnimation; sheet.enabled = true; sheet.numTilesX = 2; sheet.numTilesY = 2;
    sheet.frameOverTime = new UnityEngine.ParticleSystem.MinMaxCurve(0f); sheet.startFrame = new UnityEngine.ParticleSystem.MinMaxCurve(0f, 3.99f);
    var g = new UnityEngine.Gradient(); g.SetKeys(new[] { new UnityEngine.GradientColorKey(UnityEngine.Color.white, 0), new UnityEngine.GradientColorKey(UnityEngine.Color.white, 1) },
      new[] { new UnityEngine.GradientAlphaKey(0, 0), new UnityEngine.GradientAlphaKey(1, .08f), new UnityEngine.GradientAlphaKey(1, .9f), new UnityEngine.GradientAlphaKey(0, 1) });
    col.color = g;
    var png = $"{F}/Textures/Falling leaves.png";
    UnityEditor.AssetDatabase.ImportAsset(png, UnityEditor.ImportAssetOptions.ForceSynchronousImport);
    var imp = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(png);
    imp.filterMode = UnityEngine.FilterMode.Point; imp.npotScale = UnityEditor.TextureImporterNPOTScale.None; imp.mipmapEnabled = false; imp.alphaIsTransparency = true;
    imp.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed; imp.wrapMode = UnityEngine.TextureWrapMode.Clamp; imp.SaveAndReimport();
    var mp = $"{dir}/{name}.mat";
    var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(mp);
    if (mat == null) { mat = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Particles/Simple Lit")); UnityEditor.AssetDatabase.CreateAsset(mat, mp); }
    mat.SetTexture("_BaseMap", Load<UnityEngine.Texture2D>(png)); mat.SetColor("_BaseColor", UnityEngine.Color.white);
    mat.SetFloat("_AlphaClip", 1f); mat.SetFloat("_Cutoff", .5f); mat.EnableKeyword("_ALPHATEST_ON"); mat.SetFloat("_Cull", 0f);
    UnityEditor.EditorUtility.SetDirty(mat);
    renderer.sharedMaterial = mat;
  }
  else
  {
    main.startLifetime = new UnityEngine.ParticleSystem.MinMaxCurve(6f, 10f); main.startSpeed = 0f; main.startSize = new UnityEngine.ParticleSystem.MinMaxCurve(.07f, .11f); main.maxParticles = 14;
    main.startColor = colour;
    emission.rateOverTime = 1.5f;
    shape.position = new UnityEngine.Vector3(0, 1.4f, 0); shape.scale = new UnityEngine.Vector3(7f, 1.6f, 7f);
    noise.strength = .6f; noise.frequency = .4f; noise.scrollSpeed = .3f;
    // Each one blinks on and off a few times as it drifts.
    var g = new UnityEngine.Gradient(); g.SetKeys(new[] { new UnityEngine.GradientColorKey(UnityEngine.Color.white, 0), new UnityEngine.GradientColorKey(UnityEngine.Color.white, 1) },
      new[] { new UnityEngine.GradientAlphaKey(0, 0), new UnityEngine.GradientAlphaKey(1, .12f), new UnityEngine.GradientAlphaKey(.15f, .3f), new UnityEngine.GradientAlphaKey(1, .45f),
              new UnityEngine.GradientAlphaKey(.1f, .62f), new UnityEngine.GradientAlphaKey(1, .78f), new UnityEngine.GradientAlphaKey(0, 1) });
    col.color = g;
    renderer.sharedMaterial = Load<UnityEngine.Material>("Assets/Game/Combat/Spells/_Pack/Materials/Glow_LWRP.mat");
  }
  var saved = UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, $"{dir}/{name}.prefab");
  UnityEngine.Object.DestroyImmediate(root);
  return saved;
}
// Pools of light in the clearings, so the forest is not lit evenly (the ambient is kept low, below):
//  - Moonbeam <colour>: the dungeon's moonlight (a spot from 11 m up, its shaft and motes) without its night-sky card,
//    falling into a clearing; its light is the prop's child.
//  - Lit lantern stand: the forest's lantern stand with a warm point light at its lantern.
UnityEngine.GameObject Beam(string name, UnityEngine.Color colour, float strength)
{
  var src = Load<UnityEngine.GameObject>("Assets/Game/Levels/Shared/Props/Effects/Moonlight.prefab");
  var root = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(src);
  UnityEditor.PrefabUtility.UnpackPrefabInstance(root, UnityEditor.PrefabUnpackMode.Completely, UnityEditor.InteractionMode.AutomatedAction);
  root.name = name;
  var sky = root.transform.Find("Night sky"); if (sky != null) UnityEngine.Object.DestroyImmediate(sky.gameObject);
  foreach (var light in root.GetComponentsInChildren<UnityEngine.Light>()) { light.color = colour; light.intensity = strength; light.spotAngle = 40f; light.innerSpotAngle = 18f; light.range = 20f; light.shadows = UnityEngine.LightShadows.Soft; }
  var saved = UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, $"{F}/Props/{name}.prefab");
  UnityEngine.Object.DestroyImmediate(root);
  return saved;
}
Beam("Moonbeam", new UnityEngine.Color(.75f, .85f, 1f), 260f);
Beam("Moonbeam warm", new UnityEngine.Color(1f, .78f, .55f), 240f);
Beam("Moonbeam blue", new UnityEngine.Color(.55f, .7f, 1f), 300f);
{
  var src = Load<UnityEngine.GameObject>($"{F}/Props/Lantern stand/Lantern stand.prefab");
  var root = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(src);
  UnityEditor.PrefabUtility.UnpackPrefabInstance(root, UnityEditor.PrefabUnpackMode.Completely, UnityEditor.InteractionMode.AutomatedAction);
  root.name = "Lit lantern stand";
  var b = new UnityEngine.Bounds(root.transform.position, UnityEngine.Vector3.zero);
  foreach (var r in root.GetComponentsInChildren<UnityEngine.Renderer>()) b.Encapsulate(r.bounds);
  var lgo = new UnityEngine.GameObject("Lantern light"); lgo.transform.SetParent(root.transform, false);
  lgo.transform.position = new UnityEngine.Vector3(b.center.x, b.max.y - .35f, b.center.z);
  var l = lgo.AddComponent<UnityEngine.Light>(); l.type = UnityEngine.LightType.Point; l.color = new UnityEngine.Color(1f, .68f, .38f); l.intensity = 9f; l.range = 9f; l.shadows = UnityEngine.LightShadows.Soft;
  lgo.AddComponent<TorchFlicker>();
  UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, $"{F}/Props/Lit lantern stand.prefab");
  UnityEngine.Object.DestroyImmediate(root);
}
DungeonPropRule Pool(string n, int placement, float chance) => new DungeonPropRule { prefab = Load<UnityEngine.GameObject>($"{F}/Props/{n}.prefab"), placement = (DungeonPropPlacement)placement, footprint = new UnityEngine.Vector2Int(1, 1), perTenCells = .25f, perRoom = new UnityEngine.Vector2Int(1, 2), chance = chance, rooms = (DungeonRoomRoles)62 };

Air("Fireflies", false, new UnityEngine.Color(.95f, 1f, .55f));
Air("Fireflies blue", false, new UnityEngine.Color(.55f, .9f, 1f));
Air("Falling leaves", true, UnityEngine.Color.white);

var mounds = new System.Collections.Generic.List<UnityEngine.GameObject>();
foreach (var n in new[] { "Formation 1", "Formation 2", "Formation 5", "Formation 9", "Formation 12", "Fern tree 1", "Fern tree 2" }) { var g = Load<UnityEngine.GameObject>($"{F}/Props/{n}/{n}.prefab"); if (g != null) mounds.Add(g); }
UnityEngine.Color C(float r, float g, float b) => new UnityEngine.Color(r, g, b);
// The ambient is held low so the forest is lit in pools (moonbeams, lanterns, glows, the board light) instead of evenly
// (the user: everything looked equally lit); the ground under the canopy darker still.
const float Ambient = .68f;
var made = new System.Collections.Generic.List<DungeonBiome>();

// name, shown name, entry line, clearing style, bank top, tree weights (green, red, gold, teal, fern trees), the light
// (ambient sky, horizon, ground, sun, board light colour and strength, shadow and lamp tints, fog colour, fog), glows.
DungeonBiome Make(string name, string display, string entry, DungeonRoomStyle roomStyle, UnityEngine.Material bankTop,
  (float green, float red, float gold, float teal, float fern) wt,
  UnityEngine.Color sky, UnityEngine.Color horizon, UnityEngine.Color ground, UnityEngine.Color sun, UnityEngine.Color board, float boardStrength,
  UnityEngine.Color shadow, UnityEngine.Color lamp, UnityEngine.Color fogColour, UnityEngine.Vector2 fog, int glowEvery,
  float lean, float from, UnityEngine.Color pondGlow, float pondGlowStrength, params DungeonPropRule[] air)
{
  var biomePath = $"Assets/Game/Levels/Dungeon1/Biomes/{name}.asset";
  var biome = UnityEditor.AssetDatabase.LoadAssetAtPath<DungeonBiome>(biomePath);
  if (biome == null) { biome = UnityEngine.ScriptableObject.CreateInstance<DungeonBiome>(); UnityEditor.AssetDatabase.CreateAsset(biome, biomePath); }
  biome.CopyContentFrom(catacombs);
  biome.displayName = display; biome.entryMessage = entry; biome.entryLines = new DungeonMaster.Line[0];
  biome.kind = DungeonFloorKind.Boardgame; biome.boardReveal = BoardRoomReveal.WholeFloor;
  biome.ownBoardPieces = true; biome.ownLook = true;
  biome.roomStyles = new[] { new DungeonStyleChoice { style = roomStyle, weight = 1 } };
  biome.corridorStyles = new[] { new DungeonStyleChoice { style = trail, weight = 1 } };
  biome.roomTemplates = new DungeonBiome.RoomTemplateChoice[0]; biome.templateChance = 0;
  biome.roomCount = 12; biome.roomSizeScale = 1.6f; biome.noDoors = true;
  // Clearings, never boxes: blobs, trimmed corners, and rings round an island of rock or a tree.
  biome.roomShapes = new[] { new DungeonShapeChoice { kind = DungeonShapeKind.Cave, weight = 4 }, new DungeonShapeChoice { kind = DungeonShapeKind.CutCorners, weight = 1.5f }, new DungeonShapeChoice { kind = DungeonShapeKind.Ring, weight = 1.2f }, new DungeonShapeChoice { kind = DungeonShapeKind.PillarHall, weight = .5f } };
  biome.merchantChance = 1;   // a tavern on every floor: in the woods an open-air inn in a clearing (a board has no walls or roof)
  biome.guardian = new DungeonMilestone { enabled = false };
  biome.lightingPrefabs = new DungeonWallLight[0];
  biome.props = new UnityEngine.GameObject[0];
  biome.destructibles = new DungeonWeightedPrefab[0];   // own look: no cellar barrels and crates
  // Furnishing in the clearings: a formation, a giant mushroom or a ruin in the middle, logs, roots and ferns about.
  biome.propRules = new[]
  {
    Rule("Formation 9", 2, .04f, 1, .35f), Rule("Formation 12", 2, .04f, 1, .35f),
    Rule("Giant mushroom 1", 2, .05f, 1, .5f), Rule("Giant mushroom 4", 2, .05f, 1, .4f), Rule("Ruin 2", 2, .04f, 1, .35f),
    Rule("Log 3", 2, .08f, 1, .6f), Rule("Log 4", 2, .06f, 1, .5f), Rule("Root cluster 1", 2, .1f, 2, .7f), Rule("Root cluster 3", 2, .1f, 2, .6f),
    Rule("Fern 1", 2, .3f, 4, 1f), Rule("Fern 2", 2, .3f, 4, 1f), Rule("Fern tree 1", 2, .06f, 1, .5f), Rule("Mushrooms 2", 2, .15f, 2, .8f),
    Rule("Pebbles 5", 2, .12f, 2, .7f), Rule("Fallen leaves 1", 2, .4f, 5, 1f), Rule("Fallen leaves 3", 2, .3f, 4, 1f),
  };
  var withAir = new System.Collections.Generic.List<DungeonPropRule>(biome.propRules); withAir.AddRange(air); biome.propRules = withAir.ToArray();
  biome.overrideDungeonSettings = true;
  var features = DungeonBiome.Clone(catacombs.roomFeatures ?? new DungeonRoomFeatures());
  features.ambushes = false; features.darkRooms = false; features.candlelitRooms = false; features.moonlitRooms = false; features.eerieRooms = false; features.sunkenFloors = false; features.archerLedges = false;
  biome.roomFeatures = features;
  // Lit throughout: a forest at night, not a cellar's dark (no light radius closing in round the player).
  biome.lighting = DungeonBiome.Lighting.Custom; biome.lightBoard = true; biome.litThroughout = true;
  biome.lightingSettings = new DungeonLightingSettings { skyTint = C(.22f, .3f, .55f), skyExposure = 1f, lightColor = sun, lightIntensity = 1f, ambientSky = sky * Ambient, ambientHorizon = horizon * Ambient, ambientGround = ground * Ambient * .8f, fogColor = fogColour };
  biome.fog = fog;
  biome.filmicLook = .8f; biome.shadowTint = shadow; biome.lampTint = lamp;
  // Board pieces of its own.
  biome.boardEdgeMaterial = earth; biome.boardGridMaterial = null; biome.boardPitMaterial = bed; biome.boardPitWater = water;
  biome.boardVerticality = 1.6f; biome.boardDoorPosts = new UnityEngine.GameObject[0];
  biome.boardMoundProps = mounds.ToArray();
  // A low moon raking across the board from one side: long shadows from trees, banks and rocks (less flat than overhead).
  biome.boardLightColor = board; biome.boardLightIntensity = boardStrength; biome.boardLightAngle = lean; biome.boardLightFrom = from;
  biome.boardTileTaps = level.baseBiome != null ? level.baseBiome.boardTileTaps : new UnityEngine.AudioClip[0];
  // The wild banks, their faces broken by rock formations.
  var w = biome.wilds ?? new BoardWilds();
  w.enabled = true; w.depth = 5; w.height = new UnityEngine.Vector2(1.8f, 4.4f); w.top = bankTop; w.side = earth;
  w.trees = Picks(("Tree small 1 green", 2 * wt.green), ("Tree small 1 red", 2 * wt.red), ("Tree small 1 gold", 2 * wt.gold), ("Tree small 1 teal", 2 * wt.teal),
                  ("Tree small 2 green", 2 * wt.green), ("Tree small 2 red", 2 * wt.red), ("Tree small 2 gold", 2 * wt.gold), ("Tree small 2 teal", 2 * wt.teal),
                  ("Tree medium 1 green", 1.5f * wt.green), ("Tree medium 1 red", 1.5f * wt.red), ("Tree medium 1 gold", 1.5f * wt.gold), ("Tree medium 1 teal", 1.5f * wt.teal),
                  ("Tree medium 2 green", 1.5f * wt.green), ("Tree medium 2 red", 1.5f * wt.red), ("Tree medium 2 gold", 1.5f * wt.gold), ("Tree medium 2 teal", 1.5f * wt.teal),
                  ("Fern tree 1", wt.fern), ("Fern tree 2", wt.fern));
  // Trees keep their size (a scaled copy would change its texel size); the six kinds give the variety.
  w.treeChance = .3f; w.treeScale = new UnityEngine.Vector2(1f, 1f);
  w.undergrowth = Picks(("Fern 1", 2), ("Fern 2", 2), ("Fern 3", 1.5f), ("Undergrowth 5", 1), ("Undergrowth 7", 1), ("Undergrowth 9", 1), ("Undergrowth 10", 1),
                        ("Mushrooms 4", .5f), ("Mushrooms 6", .5f), ("Moss 1", .8f), ("Moss 3", .8f), ("Fallen leaves 2", 1), ("Pebbles 3", .5f), ("Branch 2", .4f));
  w.undergrowthPerCell = 1.3f;
  w.foot = Picks(("Roots 1", 1), ("Roots 2", 1), ("Roots 4", 1), ("Roots 6", .7f), ("Low roots 1", 1), ("Low roots 2", 1), ("Moss 2", 1), ("Moss 4", .8f), ("Fern 2", 1.5f), ("Fern 3", 1.2f), ("Undergrowth 8", 1));
  w.footChance = .55f;
  w.lip = Picks(("Fern 1", 2), ("Fern 2", 2), ("Fern 3", 1), ("Moss 1", 1), ("Moss 3", 1), ("Undergrowth 9", 1), ("Undergrowth 10", 1));
  w.lipChance = .7f;
  // The ruins' vines (Tools/ruins_setup.eval.cs makes them) hung now and then down the banks' earth faces.
  var hang = new System.Collections.Generic.List<DungeonWeightedPrefab>();
  foreach (var vn in new[] { "Vine 02", "Vine 03", "Vine 04", "Vine 05", "Vine 06", "Vine 07", "Vine draped 02" })
  { var g = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>($"Assets/Game/Levels/Dungeon1/Ruins/Props/Vines/{vn}.prefab"); if (g != null) hang.Add(new DungeonWeightedPrefab { prefab = g, weight = 1 }); }
  w.hangings = hang.ToArray(); w.hangChance = .2f;
  w.edges = BoardWilds.Edges.Formations;
  w.faces = Picks(("Formation 1", 1), ("Formation 2", 1), ("Formation 5", 1), ("Formation 9", 1), ("Formation 12", 1));
  w.faceChance = .3f; w.faceStandOut = .5f;
  w.glows = glows.ToArray(); w.glowEvery = glowEvery;
  w.pondChance = .4f; w.pondSize = new UnityEngine.Vector2Int(2, 3); w.pondGlow = pondGlow; w.pondGlowStrength = pondGlowStrength;
  w.pondEdge = Picks(("Pebbles 5", 1), ("Pebbles 3", 1), ("Low roots 2", .6f), ("Undergrowth 6", 1), ("Fern 3", 1));
  biome.wilds = w;
  UnityEditor.EditorUtility.SetDirty(biome);
  sb.Append($"{name}: trees {w.trees.Length}, rules {biome.propRules.Length}, fog {fog}\n");
  made.Add(biome);
  return biome;
}

// Forest1: a mixed autumn wood under a bright, near-neutral moon; no fog, so the room stays in view (the user's choice).
// Less even ambient and a stronger raking moon than first made, so shapes read (the user: less flat).
Make("Forest1", "The Forest", "The trees close in.", clearing, moss, (1f, .5f, .4f, .15f, .6f),
  C(1.12f, 1.18f, 1.3f), C(.84f, .84f, .92f), C(.46f, .41f, .41f), C(.88f, .9f, 1f), C(.9f, .9f, 1f), 3900f,
  C(.72f, .82f, .95f), C(1.04f, .96f, .9f), C(.3f, .33f, .4f), UnityEngine.Vector2.zero, 6,
  48f, 220f, C(.35f, .85f, 1f), 2.5f, Rule("Fireflies", 2, .2f, 1, .8f), Pool("Moonbeam", 2, .8f), Pool("Lit lantern stand", 0, .6f));
// Forest2: deep in the fall, red and gold almost everywhere, the ground under fallen gold leaves, a warmer light.
// A little redder than warm (the user: not too heavy on the red).
Make("Forest2", "The Autumn Wood", "Leaves fall all around.", clearingFall, floorFall, (.25f, 1.1f, 1f, .05f, .3f),
  C(1.16f, 1f, .94f), C(.88f, .72f, .66f), C(.5f, .35f, .31f), C(1f, .84f, .74f), C(1f, .86f, .78f), 3300f,
  C(.94f, .8f, .76f), C(1.1f, .92f, .82f), C(.38f, .28f, .26f), UnityEngine.Vector2.zero, 7,
  55f, 250f, C(.4f, .8f, .9f), 2f, Rule("Falling leaves", 2, .2f, 1, .9f), Rule("Fireflies", 2, .1f, 1, .3f), Pool("Moonbeam warm", 2, .75f), Pool("Lit lantern stand", 0, .7f));
// Forest3: the blue-purple moonlit night the user picked (the glowing pond picture, 2026-10-27), deep blue fog, more glows.
Make("Forest3", "The Moonlit Wood", "The moon is cold here.", clearing, moss, (1f, .12f, .08f, 1.1f, .8f),
  C(.95f, 1.1f, 1.8f), C(.62f, .6f, 1.1f), C(.32f, .26f, .4f), C(.62f, .7f, 1f), C(.62f, .7f, 1f), 1900f,
  C(.5f, .62f, 1f), C(1.04f, .96f, .9f), C(.17f, .27f, .5f), new UnityEngine.Vector2(12f, 70f), 4,
  40f, 200f, C(.3f, .75f, 1f), 4f, Rule("Fireflies blue", 2, .2f, 1, .9f), Pool("Moonbeam blue", 2, .85f), Pool("Lit lantern stand", 0, .35f));

// Dungeon1's floors 10, 11 and 12 (the user's order since 2026-10-30): one forest each. Run again, they go back where
// they are; on a level without them, before the ruins (Tools/ruins_setup.eval.cs) when they are there.
var stages = new System.Collections.Generic.List<TableLevelData.BiomeStage>(level.stages);
int at = stages.FindIndex(s => s != null && made.Contains(s.biome));
stages.RemoveAll(s => s != null && made.Contains(s.biome));
if (at < 0) at = stages.FindIndex(s => s != null && s.biome != null && s.biome.name.StartsWith("Ruins"));
if (at < 0) at = stages.Count;
foreach (var b in made) stages.Insert(at++, new TableLevelData.BiomeStage { biome = b, floors = 1, boss = false });
level.stages = stages.ToArray();
UnityEditor.EditorUtility.SetDirty(level);
UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.AssetDatabase.ImportAsset("Assets/Game/Levels/Dungeon1/Forest/Props", UnityEditor.ImportAssetOptions.ImportRecursive | UnityEditor.ImportAssetOptions.ForceUpdate);   // what it wrote, imported
sb.Append($"run: {level.RunSummary}\n");
} catch (System.Exception e) { sb.Append("ERR " + e); }
System.IO.File.WriteAllText("Temp/Forest/setup.txt", sb.ToString());
return "ok";
