// Gives every dead body prop a mound to walk over instead of its box (BodyMound.Apply): the mound covers the box's
// footprint. Safe to run again (a body already with a mound keeps that footprint).
if (UnityEngine.Application.isPlaying) return "stop Play first";
var log = new System.Text.StringBuilder();
foreach (var path in new[] { "Assets/Game/Levels/Dungeon1/Realms/Props/Dead dwarf.prefab", "Assets/Game/Levels/Dungeon1/Realms/Props/Dead dwarf 2.prefab" })
{
  var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
  try
  {
    UnityEngine.Vector3 centre; UnityEngine.Vector2 size;
    var box = root.GetComponent<UnityEngine.BoxCollider>();
    var mound = root.transform.Find("Mound");
    if (box != null) { centre = box.center; size = new UnityEngine.Vector2(box.size.x, box.size.z); }
    else if (mound != null) { centre = mound.localPosition; size = new UnityEngine.Vector2(mound.localScale.x, mound.localScale.z); }
    else { log.Append($"{path}: no box or mound, left as it is\n"); continue; }
    BodyMound.Apply(root, centre, size);
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
    log.Append($"{System.IO.Path.GetFileNameWithoutExtension(path)}: mound {size.x:F2} x {size.y:F2} m, {BodyMound.Height} m high\n");
  }
  finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
}
return log.ToString();
