// The tavern's fire (F9 1cba8f24, the user, 2026-11-16: "Update the fireplace with the one you have in the room with
// the new fire, looks better"). Run as an eval outside Play (safe to run again):
//   unity command eval_file --file Tools/tavern_fire.eval.cs
// Edits only the Hearth in Levels/Shared/Rooms/Scripted/Tavern.prefab; everything else in the room is left as it is.
// The hearth's five thin candle flames go, and the dungeon's own fire burns on the logs as it does in the bedroom's
// hearth: three torch fires (Props/Effects/Torch fire: tall tongues round a bright heart, embers) side by side, the
// middle one taller, large enough that they overlap into one fire filling the firebox; the logs at full size under
// them. The fire's light stands out in front of the hearth (close over the logs it bleached them white). The flames
// are listed on the TavernHearth, so the fire still burns low after a rest; TavernDress.Dress builds the same fire.
const string Room = "Assets/Game/Levels/Shared/Rooms/Scripted/Tavern.prefab";
const string Torch = "Assets/Game/Levels/Shared/Props/Effects/Torch fire.prefab";
if (UnityEditor.EditorApplication.isPlaying) return "playing: stop first";
var torch = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Torch);
if (torch == null) return "missing " + Torch;
var root = UnityEditor.PrefabUtility.LoadPrefabContents(Room);
string result;
try
{
    var hearth = root.transform.Find("Hearth");
    var th = hearth != null ? hearth.GetComponent<TavernHearth>() : null;
    if (th == null) return "no Hearth with a TavernHearth in " + Room;
    result = TavernDress.HearthFire(hearth, torch);
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, Room);
}
finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
return result;
