// The Realms enemies (Characters/Enemies/Realms): POLYGON Dungeon Realms characters as our enemies, each a copy of one of
// ours (its brain, attacks, sounds and hit feel) wearing the pack's body on the pack's own skeleton and avatar, so the
// dwarves keep their short legs and the demon its wings while our animations retarget onto them. Their bodies are pixel
// copies at 128 texels a metre (PixelRestyle.Build, skinned, measured and baked again until within 5%).
//  - The Deep Halls: the dead dwarf-hold's guard, the pack's skeletons and its undead knight.
//  - The Forge: the dwarves who still work it (soldiers, a shieldmaiden, a miner) and now and then their king.
//  - The Pit: the pack's winged demon, with skeletons and the undead knight.
// Each biome's enemy list is set here (Tools/realms_setup.eval.cs keeps it). Safe to run again: bodies are rebuilt.
// Writes Temp/Realms/enemies.txt. Run in edit mode.
if (UnityEngine.Application.isPlaying) return "playing";
const string D = "Assets/Game/Characters/Enemies/Realms";
const string SC = "Assets/Synty/PolygonDungeonRealms/Prefabs/Characters/";
const float TPU = 128f;
var sb = new System.Text.StringBuilder();
try {
void Folder(string path) { var parts = path.Split('/'); var cur = parts[0]; for (int i = 1; i < parts.Length; i++) { var next = cur + "/" + parts[i]; if (!UnityEditor.AssetDatabase.IsValidFolder(next)) UnityEditor.AssetDatabase.CreateFolder(cur, parts[i]); cur = next; } }
Folder(D);
T Load<T>(string p) where T : UnityEngine.Object { var a = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(p); if (a == null) sb.Append($"MISSING {p}\n"); return a; }

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

// A skinned part restyled at TPU texels a metre as it stands at `scale` (the enemy root's scale times the part's own).
(UnityEngine.Mesh, UnityEngine.Material) Restyle(UnityEngine.SkinnedMeshRenderer src, string name, string dir, float scale)
{
  var mats = System.Array.ConvertAll(src.sharedMaterials, m => m != null ? Readable(m) : null);
  float aim = TPU * scale, measured = 0; UnityEngine.Mesh outMesh = null; UnityEngine.Material outMat = null;
  var keepMesh = src.sharedMesh; var keepMats = src.sharedMaterials;
  for (int pass = 0; pass < 4; pass++)
  {
    UnityEditor.AssetDatabase.DeleteAsset($"{dir}/{name}.asset");
    (outMesh, outMat) = PixelRestyle.Build(keepMesh, mats, "Characters", name, aim, dir);
    if (outMesh == null) break;
    src.sharedMesh = outMesh; src.sharedMaterials = new[] { outMat };
    UnityEditor.AssetDatabase.ImportAsset(UnityEditor.AssetDatabase.GetAssetPath(outMat.GetTexture("_BaseMap")), UnityEditor.ImportAssetOptions.ForceUpdate);
    // Measured on the skinned pose (BakeMesh), as the body stands in the game: the bind-pose mesh under the renderer's
    // transform missed the rig's scale on some bodies, and four came out 10-20% fine (128 x their scale).
    measured = PixelRestyle.TexelsPerUnit(src, outMat);
    if (measured <= 0f || UnityEngine.Mathf.Abs(measured / TPU - 1f) < .05f) break;
    aim *= TPU / measured;
  }
  sb.Append($"  {name}: {measured:F1} texels a metre\n");
  return (outMesh, outMat);
}

// A body's own finish after the restyle (Tools/realms_grade.py): the pack's skeletons have pinkish, flesh-toned bone,
// turned aged ivory; the Bone Brute (the same body as the hall skeletons) old grey bone with soot so it reads apart; the
// Pit Demon's hide stepped into hard-edged tones instead of one flat orange.
void Grade(string file, UnityEngine.Material mat)
{
  string mode = file == "Hall Skeleton" || file == "Hall Raider" ? "bones" : file == "Bone Brute" ? "charred" : file == "Pit Demon" ? "skin" : null;
  if (mode == null || mat == null) return;
  var texPath = UnityEditor.AssetDatabase.GetAssetPath(mat.GetTexture("_BaseMap"));
  var psi = new System.Diagnostics.ProcessStartInfo("python", $"Tools/realms_grade.py {mode} \"{texPath}\"") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = System.IO.Directory.GetCurrentDirectory() };
  using (var p = System.Diagnostics.Process.Start(psi)) { var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd(); p.WaitForExit(); sb.Append($"  {file} {mode}: {o.Trim()}\n"); }
  UnityEditor.AssetDatabase.ImportAsset(texPath, UnityEditor.ImportAssetOptions.ForceUpdate);
}

// name, shown name, our enemy it copies, the pack character, weapon, scale, health/damage/armour/xp multipliers on the template's,
// gore, creature kind. The dwarves stand at .9 (the king at 1): at 1.1 and 1.2 their heads were at 1.76-1.92 m, over the
// 1.57 m of the hall skeletons beside them; now about 1.45 m, stout and short, the king a head taller than his guard.
var specs = new (string file, string shown, string template, string body, string weapon, float scale, float hp, float dmg, float armour, float xp, BloodKind blood, CreatureKind kind)[]
{
  ("Hall Skeleton", "hall skeleton", "Crypt/Crypt Soldier", "Chr_Skeleton_01", "Iron Sword", 1f, 1f, 1f, 1f, 1f, BloodKind.BoneDust, CreatureKind.Skeleton),
  ("Hall Raider", "hall raider", "Crypt/Crypt Soldier", "Chr_Skeleton_02", "Iron Mace", 1f, 1.1f, 1.05f, 1f, 1.1f, BloodKind.BoneDust, CreatureKind.Skeleton),
  ("Bone Brute", "bone brute", "Crypt/Crypt Warden", "Chr_Skeleton_03", "Steel Mace", 1.1f, 1f, 1f, 1f, 1f, BloodKind.BoneDust, CreatureKind.Skeleton),
  ("Undead Knight", "undead knight", "Crypt/Crypt Warden", "Chr_Undead_Knight_01", "Steel Sword", 1.08f, 1.25f, 1.1f, 1.4f, 1.3f, BloodKind.BoneDust, CreatureKind.Skeleton),
  ("Forge Guard", "forge guard", "Crypt/Crypt Soldier", "Chr_BR_Dwarf_Soldier_Male_01", "Steel Mace", 0.9f, 1.3f, 1.05f, 1.6f, 1.2f, BloodKind.Blood, CreatureKind.Humanoid),
  ("Forge Shieldmaiden", "forge shieldmaiden", "Crypt/Crypt Soldier", "Chr_BR_Dwarf_Soldier_Female_01", "Iron Sword", 0.9f, 1.15f, 1.1f, 1.4f, 1.2f, BloodKind.Blood, CreatureKind.Humanoid),
  ("Forge Miner", "forge miner", "Crypt/Crypt Soldier", "Chr_BR_Dwarf_Miner_01", "Iron Mace", 0.9f, 1.1f, 1f, .8f, 1f, BloodKind.Blood, CreatureKind.Humanoid),
  ("Forge King", "forge king", "Crypt/Crypt Warden", "Chr_BR_Dwarf_King_01", "Crystal Mace", 1f, 1.6f, 1.25f, 1.6f, 2.2f, BloodKind.Blood, CreatureKind.Humanoid),
  ("Pit Demon", "pit demon", "Crypt/Demon", "Chr_BR_Demon_01", "Steel Sword", 1.2f, 1.3f, 1.15f, 1.1f, 1.4f, BloodKind.Blood, CreatureKind.Humanoid),
};

var made = new System.Collections.Generic.Dictionary<string, UnityEngine.GameObject>();
// Temp/Realms/only_enemies.txt, when there: rebuild only the enemies it names (one a line); the lists are set as usual.
var only = System.IO.File.Exists("Temp/Realms/only_enemies.txt") ? new System.Collections.Generic.HashSet<string>(System.IO.File.ReadAllLines("Temp/Realms/only_enemies.txt")) : null;
foreach (var s in specs)
{
  if (only != null && !only.Contains(s.file)) { made[s.file] = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>($"{D}/{s.file}.prefab"); continue; }
  var path = $"{D}/{s.file}.prefab";
  var template = Load<UnityEngine.GameObject>($"Assets/Game/Characters/Enemies/{s.template}.prefab");
  var bodySrc = Load<UnityEngine.GameObject>($"{SC}{s.body}.prefab");
  if (template == null || bodySrc == null) continue;
  if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path) == null) NewCharacterWindow.Create(s.file, template, path);
  var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
  try
  {
    var model = root.transform.Find("Model");
    var anim = model.GetComponent<UnityEngine.Animator>();
    // The template's skeleton and bodies out, the pack character's skeleton and its active parts in, on its own avatar.
    for (int i = model.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(model.GetChild(i).gameObject);
    var src = (UnityEngine.GameObject)UnityEngine.Object.Instantiate(bodySrc);
    var srcAnim = src.GetComponent<UnityEngine.Animator>();
    anim.avatar = srcAnim != null ? srcAnim.avatar : anim.avatar;
    var parts = new System.Collections.Generic.List<UnityEngine.Transform>(); foreach (UnityEngine.Transform c in src.transform) parts.Add(c);
    foreach (var c in parts)
    {
      var smr = c.GetComponent<UnityEngine.SkinnedMeshRenderer>();
      if (smr != null && !c.gameObject.activeSelf) { UnityEngine.Object.DestroyImmediate(c.gameObject); continue; }
      c.SetParent(model, false);
    }
    UnityEngine.Object.DestroyImmediate(src);
    root.transform.localScale = UnityEngine.Vector3.one * s.scale;
    // The body in the pixel look.
    var dir = $"Assets/Game/Art/Synty Pixel/Characters/{s.file}"; Folder(dir);
    int n = 0;
    foreach (var smr in model.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true))
    {
      if (smr.sharedMesh == null) continue;
      var (mesh, mat) = Restyle(smr, n == 0 ? s.file : $"{s.file} {smr.name}", dir, s.scale);
      if (n == 0) Grade(s.file, mat);
      n++;
      if (mesh == null) continue;
      smr.sharedMesh = mesh; smr.sharedMaterials = new[] { mat }; smr.updateWhenOffscreen = false;
    }
    // The attachments on the bones (helmets, beards, shoulder plates, the demon's wings) restyled the same way.
    foreach (var mr in model.GetComponentsInChildren<UnityEngine.MeshRenderer>(true))
    {
      var mf = mr.GetComponent<UnityEngine.MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
      var mats = System.Array.ConvertAll(mr.sharedMaterials, m => m != null ? Readable(m) : null);
      var srcMesh = mf.sharedMesh; float aim = TPU * mr.transform.lossyScale.x, measured = 0; string pn = $"{s.file} {mr.name.Replace("SM_Chr_Attach_", "")}";
      for (int pass = 0; pass < 4; pass++)
      {
        UnityEditor.AssetDatabase.DeleteAsset($"{dir}/{pn}.asset");
        var (mesh, mat) = PixelRestyle.Build(srcMesh, mats, "Characters", pn, aim, dir);
        if (mesh == null) break;
        mf.sharedMesh = mesh; mr.sharedMaterials = new[] { mat };
        UnityEditor.AssetDatabase.ImportAsset(UnityEditor.AssetDatabase.GetAssetPath(mat.GetTexture("_BaseMap")), UnityEditor.ImportAssetOptions.ForceUpdate);
        measured = PixelRestyle.TexelsPerUnit(mr);
        if (measured <= 0f || UnityEngine.Mathf.Abs(measured / TPU - 1f) < .05f) break;
        aim *= TPU / measured;
      }
      sb.Append($"  {pn}: {measured:F1} texels a metre\n"); n++;
    }
    foreach (var burst in root.GetComponentsInChildren<DeathBurst>(true)) if (s.kind != CreatureKind.Skeleton) UnityEngine.Object.DestroyImmediate(burst);
    var loadout = root.GetComponent<EnemyWeaponLoadout>();
    if (loadout != null) { loadout.hasWeapon = true; loadout.weapon = Load<ItemData>($"Assets/Game/Items/Data/Weapons/{s.weapon}.asset"); }
    var data = root.GetComponent<Character>().data as EnemyData;
    var tdata = template.GetComponent<Character>().data as EnemyData;
    data.characterName = s.file;   // shown as written, capitalised like the Crypt's ("Bone Brute")
    data.creatureKind = s.kind;
    data.gore ??= new GoreProfile();
    data.gore.kind = s.blood; data.gore.color = s.file.Contains("Demon") ? new UnityEngine.Color(.45f, .02f, .05f) : new UnityEngine.Color(.72f, .05f, .04f); data.gore.burstOnDeath = false;
    void Scale(StatType type, float k)
    {
      int ti = tdata.stats.FindIndex(x => x.stat == type); if (ti < 0) return;
      int i = data.stats.FindIndex(x => x.stat == type);
      var e = i >= 0 ? data.stats[i] : new StatEntry { stat = type };
      e.baseValue = UnityEngine.Mathf.Round(tdata.stats[ti].baseValue * k);
      if (i >= 0) data.stats[i] = e; else data.stats.Add(e);
    }
    Scale(StatType.MaxHealth, s.hp); Scale(StatType.AttackDamage, s.dmg); Scale(StatType.Armor, s.armour);
    data.experience = UnityEngine.Mathf.Round(tdata.experience * s.xp);
    UnityEditor.EditorUtility.SetDirty(data);
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
    sb.Append($"{s.file}: avatar {anim.avatar?.name}, parts {n}\n");
  }
  catch (System.Exception e) { sb.Append($"{s.file} ERR {e.Message}\n{e.StackTrace}\n"); }
  finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
  made[s.file] = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
}

DungeonBiome.Spawn S(string f, float w, int from = 1) => new DungeonBiome.Spawn { enemy = made.TryGetValue(f, out var g) ? g : null, weight = w, fromFloor = from };
// An enemy from outside the Realms (the Deep Halls' caster is the Crypt Acolyte).
DungeonBiome.Spawn X(string path, float w) => new DungeonBiome.Spawn { enemy = Load<UnityEngine.GameObject>(path), weight = w, fromFloor = 1 };
void Set(string biome, params DungeonBiome.Spawn[] list)
{
  var b = Load<DungeonBiome>($"Assets/Game/Levels/Dungeon1/Biomes/{biome}.asset"); if (b == null) return;
  b.enemies = System.Array.FindAll(list, x => x.enemy != null);
  b.minEnemiesPerRoom = 2; b.maxEnemiesPerRoom = 3;
  UnityEditor.EditorUtility.SetDirty(b);
  sb.Append($"{biome}: {b.enemies.Length} enemies\n");
}
// Each realm: 3 melee, 1 archer, 1 caster (the user, 2026-10-30).
Set("Realms1", S("Hall Skeleton", 2), S("Bone Brute", 1), S("Undead Knight", .8f), S("Hall Raider", 2), X("Assets/Game/Characters/Enemies/Crypt/Crypt Acolyte.prefab", 1));
Set("Realms2", S("Forge Guard", 2), S("Forge Shieldmaiden", 1.5f), S("Forge Miner", 2), S("Hall Raider", 1), S("Forge King", .35f));
Set("Realms3", S("Undead Knight", 1), S("Bone Brute", 1), S("Hall Skeleton", 1), S("Hall Raider", .8f), S("Pit Demon", 1f));   // 2.2 made a third of the Pit casters
UnityEditor.AssetDatabase.SaveAssets();
} catch (System.Exception e) { sb.Append("ERR " + e); }
System.IO.Directory.CreateDirectory("Temp/Realms");
System.IO.File.WriteAllText("Temp/Realms/enemies.txt", sb.ToString());
return "ok";
