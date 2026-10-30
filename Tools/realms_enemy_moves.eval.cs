// The Realms enemies' moves beyond the swing (run after Tools/realms_enemies.eval.cs; data only, no art rebuilt):
//  - Pit Demon: throws the Crypt Acolyte's Fireball and closes in to melee between casts.
//  - Forge King: calls down the Acolyte's Fire Circle under the player, then wades in.
//  - Hall Raider: a bow archer with the Crypt Archer's bow, arrow and timing (EnemyData.archer: the bow in the left hand, the shot joins its attacks).
// Safe to run again. Writes Temp/Realms/moves.txt.
if (UnityEngine.Application.isPlaying) return "playing";
const string D = "Assets/Game/Characters/Enemies/Realms";
var sb = new System.Text.StringBuilder();
var acolyte = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Game/Characters/Enemies/Crypt/Crypt Acolyte.prefab").GetComponent<Character>().data as EnemyData;
var archerData = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Game/Characters/Enemies/Crypt/Crypt Archer.prefab").GetComponent<Character>().data as EnemyData;
EnemySpell Copy(EnemySpell s) => UnityEngine.JsonUtility.FromJson<EnemySpell>(UnityEngine.JsonUtility.ToJson(s));
void Edit(string name, System.Action<EnemyData> change)
{
  var path = $"{D}/{name}.prefab";
  var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
  try
  {
    var data = root.GetComponent<Character>().data as EnemyData;
    change(data);
    UnityEditor.EditorUtility.SetDirty(data);
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
    sb.Append($"{name}: caster {data.caster} ({data.spells.Count} spells) archer {data.archer}\n");
  }
  finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
}
// The Acolyte's spells found by name, so a reorder of its list can't swap the moves.
EnemySpell Spell(string n) { foreach (var e in acolyte.spells) if (e != null && e.spell != null && e.spell.name == n) return e; sb.Append($"MISSING the Acolyte's {n}
"); return null; }
var fireball = Spell("Fireball"); var fireCircle = Spell("Fire Circle");
if (fireball != null) Edit("Pit Demon", d => { d.caster = true; d.spells = new System.Collections.Generic.List<EnemySpell> { Copy(fireball) }; d.closesIn = true; });
if (fireCircle != null) Edit("Forge King", d => { d.caster = true; d.spells = new System.Collections.Generic.List<EnemySpell> { Copy(fireCircle) }; d.closesIn = true; });
Edit("Hall Raider", d => { d.archer = true; d.archery = UnityEngine.JsonUtility.FromJson<EnemyArchery>(UnityEngine.JsonUtility.ToJson(archerData.archery)); });
UnityEditor.AssetDatabase.SaveAssets();
System.IO.Directory.CreateDirectory("Temp/Realms");
System.IO.File.WriteAllText("Temp/Realms/moves.txt", sb.ToString());
return "ok";
