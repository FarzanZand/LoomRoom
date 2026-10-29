// The Realms biomes' props (Levels/Dungeon1/Realms/Props): pixel copies of POLYGON Dungeon Realms models at the dungeon
// world's 128 texels a metre, one prefab each, sized for our 3 m rooms (the pack builds 5 m halls, so its pillars,
// obelisks and banners are scaled down to stand under a one-tile ceiling). PixelRestyle.Build gives each part fresh UVs
// and paints the pack's atlas colour per texel with crease occlusion and grain, measured and baked again until within 5%;
// the atlas's glow sheet (lava, molten metal, crystals) comes along as an emission map. Fire props get a warm light and
// the Fireball's held flame (Spells Pack pixel art) as their child, so moving the prop moves its light.
// Floor props keep one box collider (walked round, and the floor's NavMesh is cut round them); flat ones (rugs, coins,
// the hell circle) none, laid 1.2 cm above the floor so no face lies in the floor's plane.
// Safe to run again: everything is rebuilt. Writes Temp/Realms/props.txt. Run in edit mode.
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
};

// The crystals are pure violet in the pack: pulled toward grey (the house style keeps a muted base), texel by texel.
void Mute(UnityEngine.Material m, float k)
{
  var t = m.GetTexture("_BaseMap") as UnityEngine.Texture2D; if (t == null) return;
  var path = UnityEditor.AssetDatabase.GetAssetPath(t);
  var bytes = System.IO.File.ReadAllBytes(path); var tex = new UnityEngine.Texture2D(2, 2); UnityEngine.ImageConversion.LoadImage(tex, bytes);
  var px = tex.GetPixels();
  for (int i = 0; i < px.Length; i++) { var c = px[i]; float g = c.grayscale; px[i] = new UnityEngine.Color(UnityEngine.Mathf.Lerp(c.r, g, k), UnityEngine.Mathf.Lerp(c.g, g * 1.02f, k), UnityEngine.Mathf.Lerp(c.b, g * 1.1f, k), c.a) * .9f; px[i].a = c.a; }
  tex.SetPixels(px); tex.Apply();
  System.IO.File.WriteAllBytes(path, UnityEngine.ImageConversion.EncodeToPNG(tex));
  UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate);
}

int made = 0;
var only = System.IO.File.Exists("Temp/Realms/only.txt") ? new System.Collections.Generic.HashSet<string>(System.IO.File.ReadAllLines("Temp/Realms/only.txt")) : null;
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
  int part = 0;
  foreach (var r in rends)
  {
    var mf = r.GetComponent<UnityEngine.MeshFilter>();
    if (mf == null || mf.sharedMesh == null) continue;
    var mats = System.Array.ConvertAll(r.sharedMaterials, m => m != null ? Readable(m) : null);
    var (mesh, mat) = Restyle(mf.sharedMesh, mats, part == 0 ? pname : $"{pname} {part}", dir, scale * r.transform.lossyScale.x / UnityEngine.Mathf.Max(1e-4f, scale));
    part++;
    if (mesh == null) continue;
    PixelRestyle.MarkReadable(mesh);
    if (pname.StartsWith("Crystal")) Mute(mat, .45f);
    mf.sharedMesh = mesh; r.sharedMaterials = new[] { mat };
    r.shadowCastingMode = bounds.size.y * scale < .4f ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
  }
  // Sit it on the floor with its pivot at the middle of its footprint (the furnishing rules' convention).
  var b2 = new UnityEngine.Bounds(); any = false;
  foreach (var r in root.GetComponentsInChildren<UnityEngine.MeshRenderer>(true)) { if (!any) { b2 = r.bounds; any = true; } else b2.Encapsulate(r.bounds); }
  var holder = new UnityEngine.GameObject(pname);
  root.transform.SetParent(holder.transform, true);
  root.name = "Model";
  root.transform.position -= new UnityEngine.Vector3(b2.center.x, b2.min.y - (flat ? .012f : 0f), b2.center.z);
  if (!flat)
  {
    var box = holder.AddComponent<UnityEngine.BoxCollider>();
    box.center = new UnityEngine.Vector3(0, b2.size.y * .5f, 0); box.size = new UnityEngine.Vector3(b2.size.x * .9f, b2.size.y, b2.size.z * .9f);
  }
  if (fire.a > .5f || (fire.r + fire.g + fire.b > 0f))
  {
    // Its own light as its child: lit fire props strong and flickering, glowing ones (crystals, ore, lava ducts) soft.
    bool burning = fire.a > .5f;
    var lg = new UnityEngine.GameObject("Light"); lg.transform.SetParent(holder.transform, false);
    lg.transform.localPosition = new UnityEngine.Vector3(0, b2.size.y * (burning ? 1.1f : .6f), 0);
    var light = lg.AddComponent<UnityEngine.Light>(); light.type = UnityEngine.LightType.Point;
    light.color = new UnityEngine.Color(fire.r, fire.g, fire.b); light.intensity = burning ? 2.4f : 1.1f; light.range = burning ? 7f : 4f;
    light.shadows = UnityEngine.LightShadows.None;
    if (burning)
    {
      if (flicker != null) lg.AddComponent(flicker);
      if (flame != null)
      {
        var f = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(flame);
        f.name = "Flame"; f.transform.SetParent(holder.transform, false);
        f.transform.localPosition = new UnityEngine.Vector3(0, b2.size.y * .92f, 0);
        f.transform.localScale = UnityEngine.Vector3.one * UnityEngine.Mathf.Clamp(b2.size.x * .9f, .6f, 1.6f);
      }
    }
  }
  UnityEditor.PrefabUtility.SaveAsPrefabAsset(holder, $"{D}/{pname}.prefab");
  UnityEngine.Object.DestroyImmediate(holder);
  made++;
}
UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.AssetDatabase.ImportAsset(D, UnityEditor.ImportAssetOptions.ImportRecursive | UnityEditor.ImportAssetOptions.ForceUpdate);
System.IO.File.WriteAllText("Temp/Realms/props.txt", $"made {made}\n" + sb);
return "ok " + made;
