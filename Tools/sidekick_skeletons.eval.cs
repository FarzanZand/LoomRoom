// Six skeleton enemies from Synty's Sidekick Fantasy Skeletons (Characters/Enemies/Sidekick), for review in Asset Studio;
// in no biome list yet. Each is a copy of the Crypt Soldier (its brain, attacks, sounds and hit feel) wearing a Sidekick
// body on its own rig and avatar, restyled in the pixel look at 128 texels a metre (PixelRestyle.Build reads the
// character's 32 x 32 colour palette, the Sidekick shader's _ColorMap). Five are Synty's presets; the sixth is the first
// preset's body in the fifth's palette (Sidekick characters share one palette layout, so any palette fits any body).
// Safe to run again. Writes Temp/Realms/sidekick.txt. Run in edit mode.
if (UnityEngine.Application.isPlaying) return "playing";
const string D = "Assets/Game/Characters/Enemies/Sidekick";
const string SK = "Assets/Synty/SidekickCharacters/Characters/FantasySkeletons/";
const float TPU = 128f;
var sb = new System.Text.StringBuilder();
try {
void Folder(string path) { var parts = path.Split('/'); var cur = parts[0]; for (int i = 1; i < parts.Length; i++) { var next = cur + "/" + parts[i]; if (!UnityEditor.AssetDatabase.IsValidFolder(next)) UnityEditor.AssetDatabase.CreateFolder(cur, parts[i]); cur = next; } }
Folder(D);
var template = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Game/Characters/Enemies/Crypt/Crypt Soldier.prefab");

UnityEngine.Material Readable(UnityEngine.Texture palette)
{
  var m = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Lit"));
  m.SetTexture("_BaseMap", palette); m.SetColor("_BaseColor", UnityEngine.Color.white);
  return m;
}

// file, shown name, body preset, palette preset, weapon, health/damage/armour multipliers on the Crypt Soldier's
var specs = new (string file, string shown, int body, int palette, string weapon, float hp, float dmg, float armour)[]
{
  ("Red Hood Skeleton", "red hood skeleton", 1, 1, "Iron Sword", 1f, 1f, 1f),
  ("Robed Skeleton", "robed skeleton", 2, 2, "Iron Mace", 1f, 1.05f, .8f),
  ("Helmed Skeleton", "helmed skeleton", 3, 3, "Steel Sword", 1.1f, 1f, 1.4f),
  ("Gourd Skeleton", "gourd skeleton", 4, 4, "Bronze Mace", .9f, 1f, 1f),
  ("Grave Skeleton", "grave skeleton", 5, 5, "Iron Sword", 1.05f, 1.1f, 1.1f),
  ("Ashen Skeleton", "ashen skeleton", 1, 5, "Steel Mace", 1.15f, 1.1f, 1.2f),
};
foreach (var s in specs)
{
  var path = $"{D}/{s.file}.prefab";
  string bodyName = $"FantasySkeletons_0{s.body}";
  var bodySrc = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>($"{SK}{bodyName}/{bodyName}.prefab");
  var palette = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>($"{SK}FantasySkeletons_0{s.palette}/Textures/T_FantasySkeletons_0{s.palette}ColorMap.png");
  if (bodySrc == null || palette == null) { sb.Append($"MISSING source for {s.file}\n"); continue; }
  if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path) == null) NewCharacterWindow.Create(s.file, template, path);
  var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
  try
  {
    var model = root.transform.Find("Model");
    var anim = model.GetComponent<UnityEngine.Animator>();
    for (int i = model.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(model.GetChild(i).gameObject);
    var src = (UnityEngine.GameObject)UnityEngine.Object.Instantiate(bodySrc);
    var srcAnim = src.GetComponent<UnityEngine.Animator>(); if (srcAnim != null) anim.avatar = srcAnim.avatar;
    var parts = new System.Collections.Generic.List<UnityEngine.Transform>(); foreach (UnityEngine.Transform c in src.transform) parts.Add(c);
    foreach (var c in parts) c.SetParent(model, false);
    UnityEngine.Object.DestroyImmediate(src);
    root.transform.localScale = UnityEngine.Vector3.one;
    var dir = $"Assets/Game/Art/Synty Pixel/Characters/{s.file}"; Folder(dir);
    foreach (var smr in model.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true))
    {
      if (smr.sharedMesh == null) continue;
      var mats = new UnityEngine.Material[smr.sharedMaterials.Length]; for (int i = 0; i < mats.Length; i++) mats[i] = Readable(palette);
      var srcMesh = smr.sharedMesh; float aim = TPU, measured = 0;
      for (int pass = 0; pass < 4; pass++)
      {
        UnityEditor.AssetDatabase.DeleteAsset($"{dir}/{s.file}.asset");
        var (mesh, mat) = PixelRestyle.Build(srcMesh, mats, "Characters", s.file, aim, dir);
        if (mesh == null) break;
        smr.sharedMesh = mesh; smr.sharedMaterials = new[] { mat };
        UnityEditor.AssetDatabase.ImportAsset(UnityEditor.AssetDatabase.GetAssetPath(mat.GetTexture("_BaseMap")), UnityEditor.ImportAssetOptions.ForceUpdate);
        measured = PixelRestyle.TexelsPerUnit(smr);
        if (measured <= 0f || UnityEngine.Mathf.Abs(measured / TPU - 1f) < .05f) break;
        aim *= TPU / measured;
      }
      smr.updateWhenOffscreen = false;
      sb.Append($"  {s.file}: {measured:F1} texels a metre\n");
    }
    var loadout = root.GetComponent<EnemyWeaponLoadout>();
    if (loadout != null) { loadout.hasWeapon = true; loadout.weapon = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemData>($"Assets/Game/Items/Data/Weapons/{s.weapon}.asset"); }
    var data = root.GetComponent<Character>().data as EnemyData;
    var tdata = template.GetComponent<Character>().data as EnemyData;
    data.characterName = s.file; data.creatureKind = CreatureKind.Skeleton;
    data.gore ??= new GoreProfile(); data.gore.kind = BloodKind.BoneDust; data.gore.burstOnDeath = false;
    void Scale(StatType type, float k)
    {
      int ti = tdata.stats.FindIndex(x => x.stat == type); if (ti < 0) return;
      int i = data.stats.FindIndex(x => x.stat == type);
      var e = i >= 0 ? data.stats[i] : new StatEntry { stat = type };
      e.baseValue = UnityEngine.Mathf.Round(tdata.stats[ti].baseValue * k);
      if (i >= 0) data.stats[i] = e; else data.stats.Add(e);
    }
    Scale(StatType.MaxHealth, s.hp); Scale(StatType.AttackDamage, s.dmg); Scale(StatType.Armor, s.armour);
    UnityEditor.EditorUtility.SetDirty(data);
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
    sb.Append($"{s.file}: avatar {anim.avatar?.name} valid {anim.avatar != null && anim.avatar.isValid}\n");
  }
  catch (System.Exception e) { sb.Append($"{s.file} ERR {e.Message}\n{e.StackTrace}\n"); }
  finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
}
UnityEditor.AssetDatabase.SaveAssets();
} catch (System.Exception e) { sb.Append("ERR " + e); }
System.IO.Directory.CreateDirectory("Temp/Realms");
System.IO.File.WriteAllText("Temp/Realms/sidekick.txt", sb.ToString());
return "ok";
