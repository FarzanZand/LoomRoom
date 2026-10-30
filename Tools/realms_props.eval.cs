// The Realms biomes' props (Levels/Dungeon1/Realms/Props): pixel copies of POLYGON Dungeon Realms models at the dungeon
// world's 128 texels a metre, one prefab each, sized for our 3 m rooms (the pack builds 5 m halls, so its pillars,
// obelisks and banners are scaled down to stand under a one-tile ceiling). PixelRestyle.Build gives each part fresh UVs
// and paints the pack's atlas colour per texel with crease occlusion and grain, measured and baked again until within 5%;
// the atlas's glow sheet (lava, molten metal, crystals) comes along as an emission map. Fire props get a warm light and
// the Fireball's held flame (Spells Pack pixel art) as their child, so moving the prop moves its light.
// Floor props keep one box collider (walked round, and the floor's NavMesh is cut round them); flat ones (rugs, coins,
// the hell circle) none, laid 1.2 cm above the floor so no face lies in the floor's plane. Stone, gold, molten metal and
// crystal get a hand-made finish over the restyle (Finish below: Tools/prop_organic.py, Tools/realms_grade.py); the weapon
// barrel and the chandelier are dressed with weapons and candles (Dress).
// Safe to run again: everything is rebuilt (Temp/Realms/only_props.txt, when there, names the ones to rebuild). Writes
// Temp/Realms/props.txt. Run in edit mode.
if (UnityEngine.Application.isPlaying) return "playing";
const string D = "Assets/Game/Levels/Dungeon1/Realms/Props";
const string SP = "Assets/Synty/PolygonDungeonRealms/Prefabs/";
const float TPU = 128f;
var sb = new System.Text.StringBuilder();
void Folder(string path) { var parts = path.Split('/'); var cur = parts[0]; for (int i = 1; i < parts.Length; i++) { var next = cur + "/" + parts[i]; if (!UnityEditor.AssetDatabase.IsValidFolder(next)) UnityEditor.AssetDatabase.CreateFolder(cur, parts[i]); cur = next; } }
Folder(D); System.IO.Directory.CreateDirectory("Temp/Realms");
var flame = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Game/Combat/Spells/Fireball/Fireball Hand.prefab");
var flicker = System.Type.GetType("TorchFlicker, Assembly-CSharp");

UnityEngine.Material Readable(UnityEngine.Material m)
{
  var copy = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Lit")) { name = m.name };
  UnityEngine.Texture t = null; foreach (var p in new[] { "_Albedo_Map", "_Base_Texture", "_BaseMap", "_MainTex" }) if (m.HasProperty(p) && m.GetTexture(p) != null) { t = m.GetTexture(p); break; }
  copy.SetTexture("_BaseMap", t); copy.SetColor("_BaseColor", UnityEngine.Color.white);
  bool glow = m.HasProperty("_Enable_Emission") && m.GetFloat("_Enable_Emission") > .5f;
  var e = m.HasProperty("_Emission_Map") ? m.GetTexture("_Emission_Map") : null;
  if (glow && e != null) { copy.SetTexture("_EmissionMap", e); copy.SetColor("_EmissionColor", UnityEngine.Color.white); copy.EnableKeyword("_EMISSION"); }
  return copy;
}

(UnityEngine.Mesh, UnityEngine.Material) Restyle(UnityEngine.Mesh mesh, UnityEngine.Material[] mats, string name, string dir, float scale)
{
  var probe = new UnityEngine.GameObject("Texel probe") { hideFlags = UnityEngine.HideFlags.HideAndDontSave };
  probe.transform.localScale = UnityEngine.Vector3.one * scale;
  var pf = probe.AddComponent<UnityEngine.MeshFilter>(); var pr = probe.AddComponent<UnityEngine.MeshRenderer>();
  float aim = TPU * scale, measured = 0; UnityEngine.Mesh outMesh = null; UnityEngine.Material outMat = null;
  for (int pass = 0; pass < 4; pass++)
  {
    UnityEditor.AssetDatabase.DeleteAsset($"{dir}/{name}.asset");
    (outMesh, outMat) = PixelRestyle.Build(mesh, mats, "Props", name, aim, dir);
    if (outMesh == null) break;
    pf.sharedMesh = outMesh; pr.sharedMaterial = outMat;
    var texPath = UnityEditor.AssetDatabase.GetAssetPath(outMat.GetTexture("_BaseMap"));
    UnityEditor.AssetDatabase.ImportAsset(texPath, UnityEditor.ImportAssetOptions.ForceUpdate);
    measured = PixelRestyle.TexelsPerUnit(pr);
    if (measured <= 0f || UnityEngine.Mathf.Abs(measured / TPU - 1f) < .05f) break;
    aim *= TPU / measured;
  }
  UnityEngine.Object.DestroyImmediate(probe);
  sb.Append($"  {name}: {measured:F1} texels a metre\n");
  return (outMesh, outMat);
}

// name, source (under Prefabs/), height in metres it should stand (0 = keep the pack's size, below 0 = its longest side), fire light colour (alpha 0 =
// none), flat (no collider, lifted off the floor).
var list = new (string name, string src, float height, UnityEngine.Color fire, bool flat)[]
{
  // The Deep Halls (the dungeon demo): braziers, banners, barrels, the dead dwarves, gold, crystals, pillars.
  ("Dwarf brazier", "Props/SM_Prop_Dwarf_Brazier_01", 1.5f, new UnityEngine.Color(1f, .62f, .3f, 1), false),
  ("Dwarf banner", "Environments/../Props/SM_Prop_Banner_01", 2.7f, default, false),
  ("Dwarf banner torn", "Props/SM_Prop_Banner_Damaged_01", 2.4f, default, false),
  ("Realms barrel", "Props/SM_Prop_Barrel_02", 0f, default, false),
  ("Barrel stack", "Props/SM_Prop_Barrel_Stack_01", 2.1f, default, false),
  ("Crate stack", "Props/SM_Prop_Crate_Stack_01", 1.8f, default, false),
  ("Gold pile", "Props/SM_Prop_Gold_Pile_02", .32f, default, false),
  ("Gold coins", "Props/SM_Prop_Gold_Coins_05", 0f, default, true),
  ("Gold hoard", "Props/SM_Prop_Gold_Pile_Large_01", .9f, new UnityEngine.Color(1f, .75f, .35f, 0), false),
  ("Dead dwarf", "Props/SM_Prop_DeadBody_Dwarf_02", 0f, default, false),
  ("Dead dwarf 2", "Props/SM_Prop_DeadBody_Dwarf_04", 0f, default, false),
  ("Weapon rack", "Props/SM_Prop_Dwarf_Weapon_Rack_01", 1.6f, default, false),
  ("Weapon barrel", "Props/SM_Prop_Dwarf_Weapon_Barrel_01", 0f, default, false),
  ("Stone bench", "Props/SM_Prop_Dwarf_Chair_Bench_01", 0f, default, false),
  ("Stone stool", "Props/SM_Prop_Dwarf_Chair_Stool_01", 0f, default, false),
  ("Stone table", "Props/SM_Prop_Dwarf_Table_01", 0f, default, false),
  ("Stone shelf", "Props/SM_Prop_Dwarf_Shelf_01", 0f, default, false),
  ("Dwarf sarcophagus", "Props/SM_Prop_Sarcophagus_01", 0f, default, false),
  ("Dwarf rug", "Props/SM_Prop_Rug_01", -3.2f, default, true),
  ("Dwarf pillar", "Environments/SM_Env_Dwarf_Pillar_02", 2.8f, default, false),
  ("Dwarf obelisk", "Environments/SM_Env_Dwarf_Obelisk_01", 2.6f, default, false),
  ("Broken obelisk", "Environments/SM_Env_Dwarf_Obelisk_01_Broken_01", 1.3f, default, false),
  ("Crystal cluster", "Environments/SM_Env_Crystals_Cluster_Large_01", 1.6f, new UnityEngine.Color(.45f, .35f, 1f, 0), false),
  ("Crystal cluster 2", "Environments/SM_Env_Crystals_Cluster_Large_03", 1.3f, default, false),
  ("Rubble", "Environments/SM_Env_Rubble_Rocks_01", 0f, default, true),
  ("Rubble 2", "Environments/SM_Env_Rubble_Rocks_03", 0f, default, true),
  ("Fallen pillar", "Environments/SM_Env_Rubble_Pillar_01", 1.4f, default, false),
  // The Forge: anvils, cogs, carts, furnaces, ore, molten metal.
  ("Anvil", "Props/SM_Prop_Anvil_01", .9f, default, false),
  ("Golem arm", "Props/SM_Prop_Dwarf_Forge_Golem_Arm_01", 1.4f, default, false),
  ("Golem head", "Props/SM_Prop_Dwarf_Forge_Golem_Head_01", 1.3f, default, false),
  ("Cog box", "Props/SM_Prop_Dwarf_Cog_Box_01", 1.2f, default, false),
  ("Cog pile", "Props/SM_Prop_Dwarf_Cog_Pile_01", 0f, default, false),
  ("Cog pile 2", "Props/SM_Prop_Dwarf_Cog_Pile_03", 0f, default, false),
  ("Big cog", "Props/SM_Prop_Dwarf_Cog_Large_02", 0f, default, false),
  ("Ore crate", "Props/SM_Prop_Tech_Container_01", 0f, new UnityEngine.Color(1f, .8f, .25f, 0), false),
  ("Ore crates", "Props/SM_Prop_Tech_Container_Pile_01", 0f, new UnityEngine.Color(1f, .8f, .25f, 0), false),
  ("Furnace", "Props/SM_Prop_Tech_Furnace_01", 2.4f, new UnityEngine.Color(1f, .5f, .15f, 1), false),
  ("Mine cart", "Props/SM_Prop_Dwarf_Cart_01", 1.2f, default, false),
  ("Ore cart", "Props/SM_Prop_Dwarf_Cart_02", 1.2f, default, false),
  ("Dwarf cannon", "Props/SM_Prop_Dwarf_Cannon_01", 0f, default, false),
  ("Molten pit", "Props/SM_Prop_Dwarf_Fire_Pit_01", 0f, new UnityEngine.Color(1f, .45f, .12f, 1), false),
  ("Smelting pot", "Props/SM_Prop_Dwarf_Smelting_Pot_01", 1.4f, new UnityEngine.Color(1f, .5f, .15f, 1), false),
  ("Smelter mould", "Props/SM_Prop_Dwarf_Smelter_Mould_01", -2.6f, new UnityEngine.Color(1f, .55f, .15f, 0), false),
  ("Ore pile", "Environments/SM_Env_Ore_Pile_01", -2.2f, default, true),
  ("Ore pile 2", "Environments/SM_Env_Ore_Pile_02", -2.0f, default, true),
  ("Lava duct", "Environments/SM_Env_Dwarf_Lava_Duct_Pillar_01", 2.8f, new UnityEngine.Color(1f, .45f, .12f, 0), false),
  ("Forge hammer", "Props/SM_Prop_Forge_Hammer_01", 0f, default, true),
  ("Forge tongs", "Props/SM_Prop_Forge_Tongs_01", 0f, default, true),
  // The Pit (the hell demo): red-lit braziers, spiked fences, skulls on spikes, obelisks, black rock, giant bones.
  ("Hell brazier", "Props/SM_Prop_Hell_Brazier_01", 1.4f, new UnityEngine.Color(1f, .3f, .08f, 1), false),
  ("Hell brazier wide", "Props/SM_Prop_Hell_Brazier_02", 1.2f, new UnityEngine.Color(1f, .3f, .08f, 1), false),
  ("Spiked fence", "Props/SM_Prop_Hell_Fence_01", 2.2f, default, false),
  ("Spiked fence 2", "Props/SM_Prop_Hell_Fence_02", 2.2f, default, false),
  ("Spiked post", "Props/SM_Prop_Hell_Fence_04", 2.4f, default, false),
  ("Hell fountain", "Props/SM_Prop_Hell_Fountain_01", 1.4f, new UnityEngine.Color(1f, .35f, .1f, 1), false),
  ("Horned skull", "Props/SM_Prop_Hell_Skull_02", 0f, default, false),
  ("Skull spike", "Props/SM_Prop_Hell_Spike_Skull_01", 2.0f, default, false),
  ("Skull spike 2", "Props/SM_Prop_Hell_Spike_Skull_02", 2.4f, default, false),
  ("Skull spike 3", "Props/SM_Prop_Hell_Spike_Skull_03", 2.4f, default, false),
  ("Spike cluster", "Props/SM_Prop_Hell_Spikes_01", 1.8f, default, false),
  ("Hell symbol", "Props/SM_Prop_Hell_Symbol_03", 2.2f, default, false),
  ("Hell circle", "Props/SM_Prop_Hell_Circle_01", 0f, new UnityEngine.Color(.2f, 1f, .45f, 0), true),
  ("Black spike", "Environments/SM_Env_Hell_Spikes_03", 2.4f, default, false),
  ("Black spike 2", "Environments/SM_Env_Hell_Spikes_04", 2.6f, default, false),
  ("Hell obelisk", "Environments/SM_Env_Hell_Obelisk_01", 2.8f, new UnityEngine.Color(.2f, 1f, .45f, 0), false),
  ("Hell pillar", "Environments/SM_Env_Hell_Pillar_01", 2.9f, default, false),
  ("Black rock", "Environments/SM_Env_Hell_Rock_02", 1.3f, default, false),
  ("Black crag", "Environments/SM_Env_Hell_Rock_08", 2.5f, default, false),
  ("Giant skull", "Environments/SM_Env_Hell_Bone_Skull_01", 1.3f, default, false),
  ("Giant rib", "Environments/SM_Env_Bone_Rib_01", 2.8f, default, false),
  // Architecture along the walls (wall decor of the Realms styles): "Full height ..." stands floor to ceiling and is
  // stacked up a tall hall's wall; "Tall ..." stands one tile high and is never stacked (a stacked lava fall showed
  // its ends as a dark band); "Wall ..." hangs at its own height. Each sinks into the wall behind it.
  ("Full height pilaster", "Environments/SM_Env_Dwarf_Pillar_Half_01", 3f, default, false),
  ("Full height pilaster carved", "Environments/SM_Env_Dwarf_Pillar_Half_03", 3f, default, false),
  // The forge's own: the same carved pilaster painted in its soot-dark basalt (the Deep Halls' sandstone one stood out
  // pale and beige against the forge's black walls).
  ("Full height pilaster basalt", "Environments/SM_Env_Dwarf_Pillar_Half_03", 3f, default, false),
  ("Full height banner", "Props/SM_Prop_Banner_04", 2.75f, default, false),
  ("Full height alcove", "Environments/SM_Env_Dwarf_Wall_Alcove_Angular_01", 3f, default, false),
  ("Tall lava fall", "Environments/SM_Env_Lava_Flow_01", 3f, new UnityEngine.Color(1f, .5f, .15f, 0), false),
  ("Full height chains", "Environments/SM_Env_Hell_Chains_02", 2.9f, default, false),
  ("Full height black rock", "Environments/SM_Env_Hell_Rock_05", 3f, default, false),
  ("Wall shield crest", "Props/SM_Prop_Hanging_Weapon_01", .95f, default, false),
  ("Wall grate", "Environments/SM_Env_Dwarf_Grate_Wall_01", 1.5f, new UnityEngine.Color(1f, .45f, .12f, 0), false),
  // Hung from the ceiling (raised with a tall hall's ceiling): the top sinks a little into the ceiling.
  ("Hanging chandelier", "Props/SM_Prop_Dwarf_Chandelier_05", 1.3f, new UnityEngine.Color(1f, .7f, .4f, 1), false),
  ("Hanging cage lamp", "Props/SM_Prop_Dwarf_Chandelier_02", 1.5f, new UnityEngine.Color(1f, .55f, .2f, 1), false),
  ("Hanging beam", "Environments/SM_Env_Dwarf_Beam_04", -6f, default, false),
  ("Hanging girder", "Props/SM_Prop_Steel_Beam_01", -6f, default, false),
  ("Hanging chains", "Environments/SM_Env_Hell_Chains_02", 2.1f, default, false),
  ("Hanging chain ring", "Environments/SM_Env_Hell_Ring_Chains_01", -2.6f, default, false),
  // More for the floors.
  ("Dwarf throne", "Props/SM_Prop_Dwarf_Throne_01", 2.3f, default, false),
  ("Dwarf totem", "Props/SM_Prop_Dwarf_Totem_01", 2.1f, default, false),
  ("Lava channel", "Environments/SM_Env_Dwarf_Forge_Gutter_Straight_01", -3.8f, new UnityEngine.Color(1f, .45f, .12f, 0), true),
};

// The restyle's own shading is wood grain for warm stone (the sandstone came out in horizontal streaks) and soft blotches
// for the rest (an airbrushed look the house style forbids), and the pack's gold, molten metal and crystal came out as flat
// fills. These props get the house's hand-made finish instead, from the fresh restyle each time:
//  - stone (Tools/prop_organic.py): Synty's colour per texel stepped into five shades, coursed blocks with dark joints,
//    crease grime, lit edges, speckles and cracks, laid in metres. The Deep Halls' sandstone keeps a pink-grey of its own
//    (the user: different from the walls' olive is fine), darker so it no longer glows against the walls; the forge's
//    pieces are soot-dark basalt.
//  - gold, molten, ore, crystal (Tools/realms_grade.py): hard-edged tone clusters, glints, crust and heat.
(string mode, string args)[] Finish(string n)
{
  const string sand = "--sat .5 --bright .6 --grain .45 --cracks .5 --dirt .45";
  const string basalt = "--sat .22 --bright .34 --grain .7 --cracks .7 --dirt .5";
  const string course = " --courses .375";
  switch (n)
  {
    case "Dwarf pillar": case "Dwarf obelisk": case "Broken obelisk": case "Fallen pillar": case "Full height pilaster":
    case "Full height pilaster carved": case "Full height alcove": case "Dwarf sarcophagus": case "Dwarf totem":
      return new[] { ("stone", sand + course) };
    case "Dwarf throne": case "Stone table": case "Stone bench": case "Stone stool": case "Stone shelf": case "Weapon rack":
    case "Rubble": case "Rubble 2":
      return new[] { ("stone", sand) };
    case "Full height pilaster basalt": return new[] { ("stone", basalt + course) };
    case "Cog box": case "Wall grate": case "Golem head": return new[] { ("stone", basalt) };
    case "Lava duct": return new[] { ("stone", basalt + course), ("molten", "") };
    case "Molten pit": case "Smelter mould": case "Lava channel": case "Furnace": return new[] { ("stone", basalt), ("molten", "") };
    case "Smelting pot": case "Tall lava fall": return new[] { ("molten", "") };
    case "Gold pile": case "Gold coins": case "Gold hoard": return new[] { ("gold", "") };
    case "Ore crate": case "Ore crates": return new[] { ("ore", "") };
    case "Crystal cluster": case "Crystal cluster 2": return new[] { ("crystal", "") };
  }
  return new (string, string)[0];
}

string Run(string args)
{
  var psi = new System.Diagnostics.ProcessStartInfo("python", args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = System.IO.Directory.GetCurrentDirectory() };
  using var p = System.Diagnostics.Process.Start(psi);
  var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd(); p.WaitForExit();
  return (p.ExitCode == 0 ? "" : "FAILED ") + o.Trim();
}

void Paint(string pname, string sourceMesh, UnityEngine.Mesh mesh, UnityEngine.Material mat, UnityEngine.Vector3 worldScale)
{
  var finish = Finish(pname); if (finish.Length == 0) return;
  var tex = mat.GetTexture("_BaseMap") as UnityEngine.Texture2D; if (tex == null) return;
  var texPath = UnityEditor.AssetDatabase.GetAssetPath(tex);
  var emiTex = mat.IsKeywordEnabled("_EMISSION") ? mat.GetTexture("_EmissionMap") : null;
  var emiPath = emiTex != null ? UnityEditor.AssetDatabase.GetAssetPath(emiTex) : null;
  foreach (var (mode, args) in finish)
  {
    string o;
    if (mode == "stone")
    {
      var meshPath = UnityEditor.AssetDatabase.GetAssetPath(mesh);
      var gb = $"Temp/Realms/gb/{System.IO.Path.GetFileNameWithoutExtension(texPath)}.bin";
      // Synty's own swatch per texel when the pack's prefab is found by the mesh's name, else the restyle's colours.
      bool synty = PixelRestyle.SyntySource(sourceMesh, out _, out _);
      PixelRestyle.BakeGBuffer(meshPath, tex.width, tex.height, gb, synty ? "synty:" + sourceMesh : meshPath, synty ? null : texPath);
      var size = UnityEngine.Vector3.Scale(mesh.bounds.size, worldScale);
      o = Run($"Tools/prop_organic.py \"{gb}\" {tex.width} {tex.height} \"{texPath}\" --material stone --extent {size.x:F3},{size.y:F3},{size.z:F3} {args}");
    }
    else o = Run($"Tools/realms_grade.py {mode} \"{texPath}\"" + (emiPath != null ? $" \"{emiPath}\"" : ""));
    sb.Append($"    {pname} {mode}: {o}\n");
  }
  UnityEditor.AssetDatabase.ImportAsset(texPath, UnityEditor.ImportAssetOptions.ForceUpdate);
  if (emiPath != null) UnityEditor.AssetDatabase.ImportAsset(emiPath, UnityEditor.ImportAssetOptions.ForceUpdate);
}

// The pack's weapon barrel came empty and its chandelier with a bare tray: the barrel gets the pack's own weapons standing
// in it, the chandelier a candle on each corner, all restyled at 128 like the rest. (The weapon rack is left out of the
// biomes: a stone bracket the weapons stood through.)
UnityEngine.Bounds Shown(UnityEngine.GameObject g)
{
  var b = new UnityEngine.Bounds(); bool any = false;
  foreach (var r in g.GetComponentsInChildren<UnityEngine.MeshRenderer>(true)) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
  return b;
}
// A part from the pack (or another Synty pack), its longest side `length` metres and stood up along y, then turned by
// `tilt`, its foot (its lowest point) at `foot`.
void AddPart(UnityEngine.GameObject holder, string prefabPath, string partName, string dir, float length, UnityEngine.Vector3 foot, UnityEngine.Quaternion tilt)
{
  var src = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(prefabPath);
  var smf = src != null ? src.GetComponentInChildren<UnityEngine.MeshFilter>() : null;
  if (smf == null || smf.sharedMesh == null) { sb.Append($"MISSING part {prefabPath}\n"); return; }
  var mb = smf.sharedMesh.bounds.size;
  float scale = length / UnityEngine.Mathf.Max(mb.x, UnityEngine.Mathf.Max(mb.y, mb.z));
  var mats = System.Array.ConvertAll(smf.GetComponent<UnityEngine.MeshRenderer>().sharedMaterials, m => m != null ? Readable(m) : null);
  var (mesh, mat) = Restyle(smf.sharedMesh, mats, partName, dir, scale);
  if (mesh == null) return;
  PixelRestyle.MarkReadable(mesh);
  var go = new UnityEngine.GameObject(partName); go.transform.SetParent(holder.transform, false);
  go.AddComponent<UnityEngine.MeshFilter>().sharedMesh = mesh;
  var r = go.AddComponent<UnityEngine.MeshRenderer>(); r.sharedMaterial = mat;
  go.transform.localScale = UnityEngine.Vector3.one * scale;
  var up = mb.x >= mb.y && mb.x >= mb.z ? UnityEngine.Quaternion.Euler(0, 0, 90) : mb.z >= mb.y ? UnityEngine.Quaternion.Euler(90, 0, 0) : UnityEngine.Quaternion.identity;
  go.transform.localRotation = tilt * up;
  var b = r.bounds;
  go.transform.position += new UnityEngine.Vector3(foot.x - b.center.x, foot.y - b.min.y, foot.z - b.center.z);
}
void Dress(string pname, UnityEngine.GameObject holder, string dir)
{
  const string W = "Assets/Synty/PolygonDungeonRealms/Prefabs/Weapons/";
  var b = Shown(holder);
  UnityEngine.Quaternion Q(float x, float z) => UnityEngine.Quaternion.Euler(x, 0, z);
  if (pname == "Weapon barrel")
  {
    // three standing in the barrel, splayed against its rim
    var w = new[] { ("SM_Wep_Spear_03", 1.6f, -.1f, 0f, 9f), ("SM_Wep_Axe_Large_02", 1.15f, .1f, .04f, -10f), ("SM_Wep_Mace_Large_02", 1.05f, 0f, -.1f, 4f) };
    foreach (var (n, len, dx, dz, lean) in w)
      AddPart(holder, W + n + ".prefab", $"Barrel {n.Replace("SM_Wep_", "")}", dir, len, new UnityEngine.Vector3(b.center.x + dx, b.min.y + b.size.y * .3f, b.center.z + dz), Q(dz * 80f, lean));
  }
  else if (pname == "Hanging chandelier")
  {
    // a candle on each corner of the tray: found by casting down onto the chandelier from halfway down it
    var cols = new System.Collections.Generic.List<UnityEngine.MeshCollider>();
    foreach (var mf in holder.GetComponentsInChildren<UnityEngine.MeshFilter>()) cols.Add(mf.gameObject.AddComponent<UnityEngine.MeshCollider>());
    UnityEngine.Physics.SyncTransforms();
    int k = 0;
    foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
    {
      var o = new UnityEngine.Vector3(b.center.x + sx * b.extents.x * .82f, b.min.y + b.size.y * .5f, b.center.z + sz * b.extents.z * .82f);
      // only the chandelier's own colliders (a plain Physics.Raycast could land on whatever stands in the open scene)
      UnityEngine.RaycastHit hit = default; bool found = false; float best = float.MaxValue;
      foreach (var col in cols) if (col.Raycast(new UnityEngine.Ray(o, UnityEngine.Vector3.down), out var h, b.size.y) && h.distance < best) { best = h.distance; hit = h; found = true; }
      if (!found) continue;
      AddPart(holder, "Assets/Synty/PolygonDarkFantasy/Prefabs/Props/SM_Prop_Candle_03.prefab", $"Candle {++k}", dir, .16f, hit.point + UnityEngine.Vector3.up * .002f, UnityEngine.Quaternion.identity);
      if (flame != null)
      {
        var f = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(flame);
        f.name = $"Candle flame {k}"; f.transform.SetParent(holder.transform, false);
        f.transform.position = hit.point + UnityEngine.Vector3.up * .19f; f.transform.localScale = UnityEngine.Vector3.one * .14f;
      }
    }
    foreach (var c in cols) UnityEngine.Object.DestroyImmediate(c);
    sb.Append($"  Hanging chandelier: {k} candles\n");
  }
}

int made = 0;
var only = System.IO.File.Exists("Temp/Realms/only_props.txt") ? new System.Collections.Generic.HashSet<string>(System.IO.File.ReadAllLines("Temp/Realms/only_props.txt")) : null;
foreach (var (pname, src, height, fire, flat) in list)
{
  if (only != null && !only.Contains(pname)) continue;
  var path = SP + src.Replace("Environments/../", "") + ".prefab";
  var source = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
  if (source == null) { sb.Append($"MISSING {path}\n"); continue; }
  var dir = $"{D}/{pname}"; Folder(dir);
  var root = (UnityEngine.GameObject)UnityEngine.Object.Instantiate(source);
  root.name = pname; root.transform.position = UnityEngine.Vector3.zero; root.transform.rotation = UnityEngine.Quaternion.identity;
  foreach (var c in root.GetComponentsInChildren<UnityEngine.Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
  foreach (var l in root.GetComponentsInChildren<UnityEngine.LODGroup>(true)) UnityEngine.Object.DestroyImmediate(l);
  var rends = root.GetComponentsInChildren<UnityEngine.MeshRenderer>(true);
  var bounds = new UnityEngine.Bounds(); bool any = false;
  foreach (var r in rends) { if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds); }
  // A positive height is how tall it stands; a negative one its longest side across the floor (rugs, piles).
  float across = UnityEngine.Mathf.Max(bounds.size.x, bounds.size.z);
  float scale = height > 0f && bounds.size.y > .01f ? height / bounds.size.y : height < 0f && across > .01f ? -height / across : 1f;
  root.transform.localScale = UnityEngine.Vector3.one * scale;
  // Wall pieces are pressed flat against the wall: no deeper than half a metre (a full pillar stood a metre out and
  // pinched a corridor shut). The mesh itself is squashed before the restyle, so its texels stay square.
  bool wallPiece = pname.StartsWith("Full height") || pname.StartsWith("Wall ") || pname.StartsWith("Tall ");
  float depthScale = wallPiece && bounds.size.z * scale > .5f ? .5f / (bounds.size.z * scale) : 1f;
  // ...and no wider than a 2 m wall cell (less a hand), so pieces on neighbouring cells never overlap in the wall's plane
  // (the 3 m alcove lay over the pilaster beside it and flickered).
  float widthScale = wallPiece && bounds.size.x * scale > 1.9f ? 1.9f / (bounds.size.x * scale) : 1f;
  int part = 0;
  foreach (var r in rends)
  {
    var mf = r.GetComponent<UnityEngine.MeshFilter>();
    if (mf == null || mf.sharedMesh == null) continue;
    if (depthScale < 1f || widthScale < 1f)
    {
      var sq = UnityEngine.Object.Instantiate(mf.sharedMesh); sq.name = mf.sharedMesh.name;
      var v = sq.vertices; var tr = r.transform; float cz = bounds.center.z, cx = bounds.center.x;
      for (int i = 0; i < v.Length; i++) { var w = tr.TransformPoint(v[i]); w.z = cz + (w.z - cz) * depthScale; w.x = cx + (w.x - cx) * widthScale; v[i] = tr.InverseTransformPoint(w); }
      sq.vertices = v; sq.RecalculateNormals(); sq.RecalculateBounds(); mf.sharedMesh = sq;
    }
    var mats = System.Array.ConvertAll(r.sharedMaterials, m => m != null ? Readable(m) : null);
    var sourceMesh = mf.sharedMesh.name;
    var (mesh, mat) = Restyle(mf.sharedMesh, mats, part == 0 ? pname : $"{pname} {part}", dir, scale * r.transform.lossyScale.x / UnityEngine.Mathf.Max(1e-4f, scale));
    part++;
    if (mesh == null) continue;
    PixelRestyle.MarkReadable(mesh);
    Paint(pname, sourceMesh, mesh, mat, r.transform.lossyScale);
    mf.sharedMesh = mesh; r.sharedMaterials = new[] { mat };
    r.shadowCastingMode = bounds.size.y * scale < .4f ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
  }
  // Sit it on the floor with its pivot at the middle of its footprint (the furnishing rules' convention).
  var b2 = new UnityEngine.Bounds(); any = false;
  foreach (var r in root.GetComponentsInChildren<UnityEngine.MeshRenderer>(true)) { if (!any) { b2 = r.bounds; any = true; } else b2.Encapsulate(r.bounds); }
  var holder = new UnityEngine.GameObject(pname);
  root.transform.SetParent(holder.transform, true);
  root.name = "Model";
  bool wall = pname.StartsWith("Full height") || pname.StartsWith("Wall ") || pname.StartsWith("Tall "), hanging = pname.StartsWith("Hanging");
  // How the generator sets it in a taller room (lifted with the ceiling, stacked up the wall), and whether it is flat
  // enough on its wall to cast no shadow.
  if (wall || hanging)
  {
    var mount = holder.AddComponent<PropMount>();
    mount.kind = hanging ? PropMount.Kind.HangsFromCeiling : pname.StartsWith("Full height") ? PropMount.Kind.FullHeight : PropMount.Kind.Standing;
    mount.castsNoShadow = pname == "Full height banner" || pname == "Full height chains" || pname.StartsWith("Wall ");
  }
  if (wall)
  {
    // Its back half sunk into the wall (the wall's face is the prefab's z 0, the room toward +z), centred along it;
    // a "Wall ..." piece hangs with its middle 1.9 m up.
    float sink = UnityEngine.Mathf.Min(b2.size.z * .5f, .35f) + .02f;
    float lift = pname.StartsWith("Wall ") ? 1.9f - b2.size.y * .5f : 0f;
    // No face of the piece that looks into the room may lie in the wall's plane: the generator sets the prefab's z 0 on the
    // cell edge, which is the wall's face (the wall box reaches out from it), and the trims along its foot and top stand
    // 4 cm proud of it. A face closer than 1.2 cm to either plane flickered against the wall (an alcove's recess, a
    // pilaster's flat back band, a banner's cloth), so the piece is moved a few millimetres, the least that clears both,
    // and something of it must still stand out of the wall.
    var roomFacing = new System.Collections.Generic.List<float>();
    foreach (var mfz in root.GetComponentsInChildren<UnityEngine.MeshFilter>(true))
    {
      if (mfz.sharedMesh == null) continue;
      var vz = mfz.sharedMesh.vertices; var tz = mfz.sharedMesh.triangles; var mz = mfz.transform.localToWorldMatrix;
      for (int i = 0; i < tz.Length; i += 3)
      {
        UnityEngine.Vector3 a = mz.MultiplyPoint3x4(vz[tz[i]]), bq = mz.MultiplyPoint3x4(vz[tz[i + 1]]), cq = mz.MultiplyPoint3x4(vz[tz[i + 2]]);
        var nq = UnityEngine.Vector3.Cross(bq - a, cq - a);
        if (nq.sqrMagnitude < 1e-12f || nq.normalized.z < .9f) continue;
        roomFacing.Add((a.z + bq.z + cq.z) / 3f);
      }
    }
    bool Clear(float s)
    {
      float front = -1f;
      foreach (var z in roomFacing)
      {
        float at = z - (b2.min.z + s); front = UnityEngine.Mathf.Max(front, at);
        if (UnityEngine.Mathf.Abs(at) < .012f || UnityEngine.Mathf.Abs(at - .04f) < .012f) return false;
      }
      return front > .012f;
    }
    float tried = sink;
    for (int k = 1; k <= 60 && !Clear(tried); k++) tried = sink + (k % 2 == 1 ? 1 : -1) * ((k + 1) / 2) * .004f;
    if (!Clear(tried)) { sb.Append($"  {pname}: no clear place found near {sink:F3}\n"); tried = sink; }
    else if (tried != sink) sb.Append($"  {pname}: moved {sink - tried:F3} m off the wall's plane\n");
    sink = tried;
    root.transform.position -= new UnityEngine.Vector3(b2.center.x, b2.min.y - lift, b2.min.z + sink);
  }
  else if (hanging) root.transform.position -= new UnityEngine.Vector3(b2.center.x, b2.max.y - 3.02f, b2.center.z);
  else root.transform.position -= new UnityEngine.Vector3(b2.center.x, b2.min.y - (flat ? .012f : 0f), b2.center.z);
  if (wall || hanging)
  {
    // Walked past, not into: only what stands out more than a hand from the wall keeps a box (low on the wall).
    var stand = b2.size.z - (UnityEngine.Mathf.Min(b2.size.z * .5f, .35f) + .02f);
    if (false)
    {
      var box = holder.AddComponent<UnityEngine.BoxCollider>();
      box.center = new UnityEngine.Vector3(0, 1.5f, stand * .5f); box.size = new UnityEngine.Vector3(b2.size.x * .8f, 3f, stand);
    }
  }
  // A dead body is walked over like a little hill, not blocked (the user, 2026-10-30): a low mound over its footprint.
  else if (pname.StartsWith("Dead ")) BodyMound.Apply(holder, UnityEngine.Vector3.zero, new UnityEngine.Vector2(b2.size.x * .9f, b2.size.z * .9f));
  else if (!flat)
  {
    var box = holder.AddComponent<UnityEngine.BoxCollider>();
    box.center = new UnityEngine.Vector3(0, b2.size.y * .5f, 0); box.size = new UnityEngine.Vector3(b2.size.x * .9f, b2.size.y, b2.size.z * .9f);
  }
  if (fire.a > .5f || (fire.r + fire.g + fire.b > 0f))
  {
    // Its own light as its child: lit fire props strong and flickering, glowing ones (crystals, ore, lava ducts) soft.
    bool burning = fire.a > .5f;
    var lg = new UnityEngine.GameObject("Light"); lg.transform.SetParent(holder.transform, false);
    lg.transform.localPosition = hanging ? new UnityEngine.Vector3(0, 3.02f - b2.size.y * .55f, 0) : wall ? new UnityEngine.Vector3(0, (pname.StartsWith("Wall ") ? 1.9f : b2.size.y * .5f), .45f) : new UnityEngine.Vector3(0, b2.size.y * (burning ? 1.1f : .6f), 0);
    var light = lg.AddComponent<UnityEngine.Light>(); light.type = UnityEngine.LightType.Point;
    light.color = new UnityEngine.Color(fire.r, fire.g, fire.b); light.intensity = burning ? 2.4f : 1.1f; light.range = burning ? 7f : 4f;
    light.shadows = UnityEngine.LightShadows.None;
    if (burning)
    {
      if (flicker != null) lg.AddComponent(flicker);
      if (flame != null && !hanging)
      {
        var f = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(flame);
        f.name = "Flame"; f.transform.SetParent(holder.transform, false);
        f.transform.localPosition = new UnityEngine.Vector3(0, b2.size.y * .92f, 0);
        f.transform.localScale = UnityEngine.Vector3.one * UnityEngine.Mathf.Clamp(b2.size.x * .9f, .6f, 1.6f);
      }
    }
  }
  Dress(pname, holder, dir);
  UnityEditor.PrefabUtility.SaveAsPrefabAsset(holder, $"{D}/{pname}.prefab");
  UnityEngine.Object.DestroyImmediate(holder);
  made++;
}
UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.AssetDatabase.ImportAsset(D, UnityEditor.ImportAssetOptions.ImportRecursive | UnityEditor.ImportAssetOptions.ForceUpdate);
System.IO.File.WriteAllText("Temp/Realms/props.txt", $"made {made}\n" + sb);
return "ok " + made;
