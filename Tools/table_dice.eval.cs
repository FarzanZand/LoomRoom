// The symbol puzzle's set of dice (ProgressionManager.Dice, TableDice): makes the dice prefab (five of the intro's
// pixel dice, World/Art/Table dice/Table dice.prefab, sized to the bedroom's 6.4 texels a unit: 16 texels a face over
// 2.5 units), the key item (Items/Data/Keys/Set of dice, its icon drawn from the prefab), and the Table dice on the
// big table's border (a child of TableManager, on the west frame's cap round the map, near the south end) with its TableDice; points
// ProgressionManager at the key item. Safe to run again (the scene object is rebuilt in place; save the scene after).
const string Dir = "Assets/Game/World/Art/Table dice", PrefabPath = Dir + "/Table dice.prefab", ItemPath = "Assets/Game/Items/Data/Keys/Set of dice.asset";
const string Intro = "Assets/Game/Cinematics/Intro/Tabletop";
var sb = new System.Text.StringBuilder();
if (!UnityEditor.AssetDatabase.IsValidFolder(Dir)) UnityEditor.AssetDatabase.CreateFolder("Assets/Game/World/Art", "Table dice");
var mesh = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>(Intro + "/Die mesh.asset");
var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(Intro + "/Die.mat");
// Five dice in a loose row along the border, each turned to show another face, as if just rolled.
// Spaced 3.5 apart and turned less than 20 degrees, so no two dice touch (a 2.5 cube turned 20 degrees is 3.2 across).
var spots = new (Vector3 pos, Vector3 rot)[]
{
    (new Vector3(.3f, 0, -7f), new Vector3(0, 12, 0)), (new Vector3(-.4f, 0, -3.5f), new Vector3(90, -15, 0)),
    (new Vector3(.2f, 0, 0f), new Vector3(0, 8, 90)), (new Vector3(-.3f, 0, 3.5f), new Vector3(-90, 18, 0)),
    (new Vector3(.4f, 0, 7f), new Vector3(180, -6, 0)),
};
const float Size = 2.5f;
var root = new GameObject("Table dice");
try
{
    for (int i = 0; i < spots.Length; i++)
    {
        var die = new GameObject($"Die {i + 1}");
        die.transform.SetParent(root.transform, false);
        die.transform.localPosition = spots[i].pos + Vector3.up * (Size / 2f + .01f);
        die.transform.localEulerAngles = spots[i].rot;
        die.transform.localScale = Vector3.one * Size;
        die.AddComponent<MeshFilter>().sharedMesh = mesh;
        die.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }
    var box = root.AddComponent<BoxCollider>(); box.center = new Vector3(0, Size / 2f, 0); box.size = new Vector3(4.5f, Size + .5f, 18.5f);
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
}
finally { UnityEngine.Object.DestroyImmediate(root); }
var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
sb.Append("prefab ok; ");

// The key item.
var item = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemData>(ItemPath);
if (item == null)
{
    UnityEditor.AssetDatabase.CopyAsset("Assets/Game/Items/Data/Keys/Missing tile.asset", ItemPath);
    item = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemData>(ItemPath);
    item.saveId = "key.tableDice"; item.icon = null;
}
item.itemName = "Set of dice";
item.description = "Key item. Five old dice from under the symbol tiles. They belong on the border of the big table, where they bring luck at the market.";
item.worldPrefab = prefab;
item.iconSource ??= new ItemIconSource();
item.iconSource.model = prefab; item.iconSource.custom = false;
UnityEditor.EditorUtility.SetDirty(item);
UnityEditor.AssetDatabase.SaveAssets();
var problems = new System.Collections.Generic.List<string>();
sb.Append($"icons {ItemIconRenderer.GenerateAll(new System.Collections.Generic.List<ItemData> { item }, problems)} {string.Join(",", problems)}; ");

// The scene object on the table's border.
var table = UnityEngine.Object.FindAnyObjectByType<TableManager>(FindObjectsInactive.Include);
var top = table.GetComponent<Renderer>();
var old = table.transform.Find("Table dice");
if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
var placed = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, table.transform);
// On the wooden frame round the map, on the player's (west) side near the south end: the middle of the west frame's cap.
var b = top.bounds;
Renderer cap = null;
foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude)) if (r.name == "Frame cap west") cap = r;
placed.transform.position = cap != null ? new Vector3(cap.bounds.center.x, cap.bounds.max.y, b.min.z + 12f) : new Vector3(b.min.x + 3.5f, b.max.y, b.min.z + 12f);
placed.transform.rotation = Quaternion.identity;
foreach (var r in placed.GetComponentsInChildren<Renderer>(true)) r.renderingLayerMask = top.renderingLayerMask;
var dice = placed.GetComponent<TableDice>() ?? placed.AddComponent<TableDice>();
var list = new System.Collections.Generic.List<GameObject>();
foreach (Transform c in placed.transform) list.Add(c.gameObject);
dice.dice = list.ToArray();
var knocks = new System.Collections.Generic.List<AudioClip>();
for (int n = 1; n <= 3; n++) { var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>($"{Intro}/Dice knock {n}.wav"); if (clip != null) knocks.Add(clip); }
dice.knocks = knocks.ToArray();
dice.putEffect = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Items/Effects/Chest burst.prefab");
UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(dice);
sb.Append($"placed at {placed.transform.position}; ");

// The manager (the scene's, which is not a prefab instance, and the prefab).
var meta = UnityEngine.Object.FindAnyObjectByType<ProgressionManager>(FindObjectsInactive.Include);
meta.diceSet = item; UnityEditor.EditorUtility.SetDirty(meta);
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(table.gameObject.scene);
sb.Append("manager set (save the scene)");
return sb.ToString();
