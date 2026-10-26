using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// The tavern's look and furnishing (asked for on 2026-10-26: "use the space more so not all walls are empty and all
// furniture is in the middle"). Run from an eval:
//   TavernDress.Style()   the Tavern room style: Tools/tavern_style.py's textures on copies of the Church materials,
//                         each tiled to the Church's 384 texels an architecture tile
//   TavernDress.Dress()   lays the room out again: every piece under Tavern.prefab's root is replaced except the keeper's
//                         Merchant socket and the Hearth's workings (TavernHearth, its Fire light, flames and
//                         Interaction); hand edits to the furniture are lost, so run it once and edit the prefab by hand
// The room as built is 10 m across (local x -5..5) and 14 m deep (z -7..7), the one way in at the middle of z = -7, the
// ceiling at 3 m. Every Synty prop is a pixel copy at the dungeon's 128 texels a unit (Prop: PixelRestyle.Build, measured
// and baked again until within 5%), saved as a prefab in Levels/Shared/Props/Tavern.
public static class TavernDress
{
    const string RoomPath = "Assets/Game/Levels/Shared/Rooms/Scripted/Tavern.prefab";
    const string PropDir = "Assets/Game/Levels/Shared/Props/Tavern";
    const string PixelDir = PropDir + "/Pixel";
    const string StyleDir = "Assets/Game/Levels/Shared/Room styles/Tavern";
    const string StylePath = "Assets/Game/Levels/Shared/Room styles/Tavern.asset";
    const float Texels = 128f;

    // ── The style ──

    public static string Style()
    {
        var church = AssetDatabase.LoadAssetAtPath<DungeonRoomStyle>("Assets/Game/Levels/Shared/Room styles/Church.asset");
        Material Mat(string name, Material like, float tiling)
        {
            string tex = $"{StyleDir}/{name}.png", path = $"{StyleDir}/{name}.mat";
            var importer = (TextureImporter)AssetImporter.GetAtPath(tex);
            if (importer == null) throw new System.Exception("Paint the textures first: python Tools/tavern_style.py \"" + StyleDir + "\"");
            if (importer.filterMode != FilterMode.Point || importer.npotScale != TextureImporterNPOTScale.None || importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.filterMode = FilterMode.Point; importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
            }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(like) { name = name }; AssetDatabase.CreateAsset(m, path); }
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(tex);
            foreach (var p in new[] { "_BaseMap", "_MainTex" }) if (m.HasProperty(p)) { m.SetTexture(p, t); m.SetTextureScale(p, new Vector2(tiling, tiling)); }
            m.color = Color.white;
            EditorUtility.SetDirty(m);
            return m;
        }
        var style = AssetDatabase.LoadAssetAtPath<DungeonRoomStyle>(StylePath);
        if (style == null) { style = ScriptableObject.CreateInstance<DungeonRoomStyle>(); AssetDatabase.CreateAsset(style, StylePath); }
        style.floor = church.floor;                                             // the Church's own pale flagstones, as drawn
        style.bottomWall = style.upperWall = Mat("Tavern wall", church.bottomWall, 1f);
        style.ceiling = Mat("Tavern ceiling", church.ceiling, 1f);
        style.trim = Mat("Tavern trim", church.trim, 1f);
        style.wood = church.wood; style.metal = church.metal;
        style.wallVariants = new DungeonSurfaceVariant[0]; style.floorVariants = new DungeonSurfaceVariant[0];
        style.wallDecor = new DungeonWeightedPrefab[0];
        EditorUtility.SetDirty(style);
        AssetDatabase.SaveAssets();
        string Density(Material m) { var t = m.GetTexture("_BaseMap"); var s = m.GetTextureScale("_BaseMap"); return $"{m.name} {t.width}x{t.height} -> {t.width * s.x:0} x {t.height * s.y:0} a tile"; }
        return string.Join("; ", new[] { style.floor, style.bottomWall, style.ceiling, style.trim }.Select(Density));
    }

    // ── Props ──

    // A pixel copy of a Synty prefab at 128 texels a unit, as a prefab in the tavern's props folder (made once; delete
    // the prefab to bake it again). scale is baked in: the copy's root carries it, and the texture is sized for it.
    public static GameObject Prop(string syntyPath, string name, float scale = 1f)
    {
        string path = $"{PropDir}/{name}.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;
        var synty = AssetDatabase.LoadAssetAtPath<GameObject>(syntyPath);
        if (synty == null) throw new System.Exception("No Synty prefab " + syntyPath);
        Directory.CreateDirectory(PixelDir);
        var copy = (GameObject)PrefabUtility.InstantiatePrefab(synty);
        PrefabUtility.UnpackPrefabInstance(copy, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        copy.name = name;
        copy.transform.localScale = Vector3.one * scale;
        try
        {
            foreach (var filter in copy.GetComponentsInChildren<MeshFilter>(true))
            {
                var r = filter.GetComponent<MeshRenderer>();
                if (filter.sharedMesh == null || r == null || r.sharedMaterial == null) continue;
                var mesh = filter.sharedMesh; var sourceMats = r.sharedMaterials;
                float s = filter.transform.lossyScale.x;
                string key = $"{mesh.name} x{s:0.00}";
                string dir = $"{PixelDir}/{key}";
                var bakedMesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{dir}/{key}.asset");
                var bakedMat = AssetDatabase.LoadAssetAtPath<Material>($"{dir}/{key}.mat");
                if (bakedMesh == null || bakedMat == null)
                {
                    float aim = Texels * s;
                    for (int pass = 0; pass < 4; pass++)
                    {
                        (bakedMesh, bakedMat) = PixelRestyle.Build(mesh, sourceMats, "Props", key, aim, dir);
                        if (bakedMesh == null) break;
                        filter.sharedMesh = bakedMesh; r.sharedMaterials = new[] { bakedMat };
                        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(bakedMat.GetTexture("_BaseMap")), ImportAssetOptions.ForceUpdate);
                        float now = PixelRestyle.TexelsPerUnit(r);
                        if (now <= 0f || Mathf.Abs(now / Texels - 1f) < .05f) break;
                        aim *= Texels / now;
                        filter.sharedMesh = mesh; r.sharedMaterials = sourceMats;
                    }
                }
                if (bakedMesh == null) continue;
                filter.sharedMesh = bakedMesh; r.sharedMaterials = new[] { bakedMat };
                // Colliders never point at a Synty mesh (a build can't read it for the NavMesh).
                foreach (var mc in filter.GetComponents<MeshCollider>()) mc.sharedMesh = bakedMesh;
            }
            return PrefabUtility.SaveAsPrefabAsset(copy, path);
        }
        finally { Object.DestroyImmediate(copy); }
    }

    static string Synty(string pack, string file)
    {
        var guid = AssetDatabase.FindAssets(file + " t:Prefab", new[] { "Assets/Synty/" + pack })
            .FirstOrDefault(g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)) == file);
        if (guid == null) throw new System.Exception($"No {file} in {pack}");
        return AssetDatabase.GUIDToAssetPath(guid);
    }

    // ── The layout ──

    const string DF = "PolygonDarkFantasy", FK = "PolygonFantasyKingdom", TN = "PolygonTown";

    public static string Dress()
    {
        var log = new List<string>();
        // Every piece the room uses, made (or found) first: a prefab under the tavern's props folder each.
        GameObject P(string pack, string file, string name, float scale = 1f) => Prop(Synty(pack, file), name, scale);
        var bar = P(DF, "SM_Prop_Counter_Bar_01", "Bar counter");
        var barStool = P(DF, "SM_Prop_Stool_Bar_01", "Bar stool");
        var wallShelf = P(FK, "SM_Prop_Shelf_04", "Wall shelf");
        var bottles = new[] { P(FK, "SM_Item_Bottle_01", "Bottle A"), P(FK, "SM_Item_Bottle_02", "Bottle C"), P(FK, "SM_Item_Bottle_03", "Bottle B"),
            P(FK, "SM_Item_Bottle_04", "Bottle D"), P(FK, "SM_Item_Jug_01", "Jug A"), P(FK, "SM_Item_Jug_03", "Jug B"), P(FK, "SM_Item_Jar_02", "Jar") };
        var logs = P(FK, "SM_Prop_Wood_Pile_03", "Hearth logs", .75f);
        var keg = P(DF, "SM_Prop_Keg_01", "Keg");
        var tap = P(FK, "SM_Prop_Barrel_Dispenser_Small_01", "Ale tap");
        var fireplace = P(FK, "SM_Bld_House_Fireplace_01", "Fireplace");
        var longTable = P(DF, "SM_Prop_Table_01", "Long table");
        var bench = P(FK, "SM_Prop_Bench_Seat_01", "Bench");
        var smallTable = P(DF, "SM_Prop_Table_Small_01", "Small table");
        var barrelTable = P(FK, "SM_Prop_Table_Barrel_01", "Barrel table");
        var chair = P(FK, "SM_Prop_Chair_Wood_01", "Chair");
        var stool = P(FK, "SM_Prop_Stool_02", "Tavern stool");
        var tankard = P(FK, "SM_Item_Mug_Tankard_01", "Tankard");
        var beerMug = P(DF, "SM_Prop_Mug_Beer_01", "Beer mug");
        var plate = P(DF, "SM_Prop_Plate_01", "Plate");
        var bowl = P(FK, "SM_Item_Food_Bowl_02", "Stew bowl");
        var pot = P(FK, "SM_Item_Pot_01", "Cooking pot");
        var bread = P(FK, "SM_Item_Bread_01", "Loaf");
        var cheese = P(FK, "SM_Item_Cheese_Wheel_01", "Cheese");
        var ham = P(FK, "SM_Prop_Ham_Hanging_01", "Hanging ham");
        var deer = P(FK, "SM_Prop_Animal_Head_Deer_01", "Deer head");
        var antlers = P(FK, "SM_Prop_Animal_Head_Antler_01", "Antlers");
        var boar = P(FK, "SM_Prop_Animal_Head_Boar_01", "Boar head");
        var banner = P(FK, "SM_Prop_Banner_01", "Banner");
        var shield = P(FK, "SM_Wep_Shield_03", "Wall shield");
        var lantern = P(DF, "SM_Prop_Lantern_01", "Wall lantern");
        var chandelier = P(FK, "SM_Prop_Chandelier_01", "Chandelier", .55f);
        var rug = P(FK, "SM_Prop_Rug_01", "Hearth rug");
        var runner = P(TN, "SM_Prop_Rug_03", "Floor rug");
        var runner2 = P(FK, "SM_Prop_TableCloth_04", "Table runner");
        var barrel = P(FK, "SM_Prop_Barrel_01", "Barrel");
        var barrels = P(FK, "SM_Prop_Barrel_Stack_02", "Barrel rack");
        var sacks = P("PolygonGeneric", "SM_Gen_Prop_Sack_Stack_01", "Sacks");
        var candle = P(DF, "SM_Prop_Candle_05", "Table candle");
        var bottle1 = P(FK, "SM_Item_Bottle_01", "Bottle A");
        var bottle2 = P(FK, "SM_Item_Bottle_03", "Bottle B");
        log.Add("props ready");

        var root = PrefabUtility.LoadPrefabContents(RoomPath);
        try
        {
            var t = root.transform;
            var keep = new HashSet<Transform> { t.Find("Merchant"), t.Find("Hearth") };
            foreach (Transform c in t.Cast<Transform>().ToArray()) if (!keep.Contains(c)) Object.DestroyImmediate(c.gameObject);
            var template = root.GetComponent<DungeonRoomTemplate>();
            template.style = AssetDatabase.LoadAssetAtPath<DungeonRoomStyle>(StylePath);
            template.replaceGeneratedTorches = true;   // its own lanterns light it; the floor's wall torches stay out

            Transform Group(string name) { var g = new GameObject(name).transform; g.SetParent(t, false); return g; }
            // piece: the generator may take it out (a pivot outside the room's cells, or, while blocking, on a walkway);
            // flat or fixed things (rugs, the fireplace, the chandelier) are never taken out.
            GameObject Put(GameObject prefab, Transform parent, float x, float y, float z, float turn = 0f, bool piece = true, bool blocking = true, bool collide = true)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                go.transform.localPosition = new Vector3(x, y, z); go.transform.localRotation = Quaternion.Euler(0, turn, 0);
                if (piece) (go.GetComponent<DungeonTemplatePiece>() ?? go.AddComponent<DungeonTemplatePiece>()).removeIfBlocking = blocking;
                if (!collide) foreach (var col in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
                return go;
            }
            void Glow(GameObject owner, Vector3 local, Color colour, float intensity, float range)
            {
                var l = new GameObject("Light").AddComponent<Light>();
                l.transform.SetParent(owner.transform, false); l.transform.localPosition = local;
                l.type = LightType.Point; l.color = colour; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
                l.gameObject.AddComponent<TorchFlicker>();
            }
            var warm = new Color(1f, .82f, .62f); var flame = new Color(1f, .68f, .38f);   // lamps a warm white, so the room is not all orange

            // The room is 14 m across (local x -7..7) and 10 m deep (z -5..5); the way in is the middle of z = -5, kept
            // clear with a path to the bar. Only what stands on the floor and can be walked into keeps a collider
            // (collide: false strips them): the player got caught under the chandelier's.

            // The bar along the back wall, the keeper behind it, his kegs and bottles against the wall at his back.
            var b = Group("Bar");
            Put(bar, b, -1.22f, 0, 1.8f); Put(bar, b, 1.22f, 0, 1.8f);   // a little overlap: no gap between the halves
            foreach (var x in new[] { -1.9f, -.65f, .65f, 1.9f }) Put(barStool, b, x, 0, .85f, Random.Range(0f, 90f), blocking: false);   // at the counter, where they belong even on the walkway
            Put(tap, b, 2.0f, 1.07f, 1.85f, 180, collide: false);
            Put(tankard, b, -1.5f, 1.07f, 1.55f, 200, collide: false); Put(beerMug, b, -.3f, 1.07f, 1.6f, 160, collide: false); Put(tankard, b, .55f, 1.07f, 1.5f, 120, collide: false);
            Put(bottle1, b, -2.2f, 1.07f, 1.9f, 30, collide: false); Put(bottle2, b, -2.0f, 1.07f, 1.75f, 70, collide: false);
            var barLamp = Put(lantern, b, 1.0f, 1.07f, 1.95f, 180, collide: false); Glow(barLamp, new Vector3(0, .35f, 0), warm, 2.2f, 5f);
            // A wall of open shelves behind him, lined with bottles, jugs and jars, two barrels under the middle ones.
            // (The wine rack it replaced kept flat, untextured sides and back however it was shaded.)
            Put(barrel, b, -.45f, 0, 4.55f, 30); Put(barrel, b, .45f, 0, 4.55f, 75);
            int n = 0;
            foreach (var (x, y) in new[] { (-2.15f, 1.45f), (-2.15f, 2.05f), (0f, 1.3f), (0f, 1.85f), (0f, 2.4f), (2.15f, 1.45f), (2.15f, 2.05f) })
                {
                    if (x == 0 && y < 1.5f) continue;   // the barrels stand there
                    Put(wallShelf, b, x, y, 4.97f, 180, blocking: false, collide: false);   // just inside the wall line: a pivot on it counts as in the wall
                    for (float bx = x - .65f; bx <= x + .66f; bx += .19f + (n % 3) * .04f)
                        Put(bottles[(n++ * 5 + (int)(y * 10)) % bottles.Length], b, bx, y, 4.8f, n * 47 % 360, blocking: false, collide: false);
                }
            Put(keg, b, -3.75f, 0, 4.2f, 180); Put(keg, b, 3.75f, 0, 4.2f, 180);
            Put(ham, b, -3.0f, 2.95f, 1.8f, 20, collide: false); Put(ham, b, -2.65f, 2.95f, 2.0f, 80, collide: false); Put(ham, b, 3.0f, 2.95f, 1.9f, 40, collide: false);
            var merchant = t.Find("Merchant");
            merchant.localPosition = new Vector3(0, 0, 3.2f);   // behind the bar, on floor wide enough to be walkable (the keeper is put on the nearest walkable floor)
            merchant.localRotation = Quaternion.Euler(0, 180, 0);

            // The fireplace on the left wall (the Hearth's model swapped; its rest, light, flames and Interaction kept),
            // a rug and seats round it, antlers over the mantel and shields either side.
            var hearth = t.Find("Hearth");
            foreach (Transform c in hearth.Cast<Transform>().ToArray())
                if (c.GetComponent<MeshFilter>() != null || c.name == "Fire pit" || c.name == "Stool" || c.name == "Fireplace" || c.name == "Hearth logs") Object.DestroyImmediate(c.gameObject);
            hearth.localPosition = new Vector3(-6.95f, 0, -.3f); hearth.localRotation = Quaternion.Euler(0, 90, 0);
            Put(fireplace, hearth, 0, 0, 0, 0, piece: false);
            Put(logs, hearth, 0, .2f, .4f, 0, piece: false, collide: false);
            // The fire on the logs: the hearth's flames spread across them, five, listed on the hearth.
            var th = hearth.GetComponent<TavernHearth>();
            var flames = hearth.GetComponentsInChildren<ParticleSystem>(true).Where(f => f.transform.parent == hearth).ToList();
            while (flames.Count < 5 && flames.Count > 0) { var extra = Object.Instantiate(flames[0].gameObject, hearth); extra.name = flames[0].name; flames.Add(extra.GetComponent<ParticleSystem>()); }
            var spots = new[] { (0f, .42f, 4.6f), (-.24f, .4f, 3.8f), (.24f, .38f, 3.8f), (-.1f, .3f, 3.2f), (.12f, .48f, 3f) };
            for (int i = 0; i < flames.Count && i < spots.Length; i++)
            {
                flames[i].transform.localPosition = new Vector3(spots[i].Item1, .38f, spots[i].Item2);
                flames[i].transform.localScale = Vector3.one * spots[i].Item3;
                var main = flames[i].main; main.scalingMode = ParticleSystemScalingMode.Hierarchy;   // the scale sizes the flames, not only where they start
            }
            var hso = new SerializedObject(th); var list = hso.FindProperty("flames");
            if (list != null) { list.arraySize = flames.Count; for (int i = 0; i < flames.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = flames[i]; hso.ApplyModifiedPropertiesWithoutUndo(); }
            var fire = hearth.GetComponentInChildren<Light>(true); if (fire != null) fire.transform.localPosition = new Vector3(0, .7f, .9f);
            var interaction = hearth.Find("Interaction"); if (interaction != null) interaction.localPosition = new Vector3(0, 0, 1.2f);
            var h = Group("Hearth side");
            Put(rug, h, -5.3f, .005f, -.3f, 90, blocking: false, collide: false);
            Put(chair, h, -5.0f, 0, -1.6f, 300); Put(chair, h, -5.0f, 0, 1.0f, 240); Put(stool, h, -4.4f, 0, -.3f);
            Put(antlers, h, -6.97f, 2.3f, -.3f, 90, collide: false);
            Put(shield, h, -6.95f, 1.95f, -2.3f, 90, collide: false); Put(shield, h, -6.95f, 1.95f, 1.7f, 90, collide: false);
            Put(pot, h, -6.45f, 0, -1.45f, 30, collide: false);

            // Tables along the right wall with benches either side, set with mugs, plates and food.
            var tb = Group("Tables");
            foreach (var z in new[] { -2.5f, 1.1f })
            {
                Put(longTable, tb, 5.45f, 0, z, 90);
                Put(runner2, tb, 5.45f, .785f, z, 0, blocking: false, collide: false);
                Put(bench, tb, 6.55f, 0, z, 90); Put(bench, tb, 4.3f, 0, z, 90);
                Put(tankard, tb, 5.1f, .78f, z - .8f, 30, collide: false); Put(beerMug, tb, 5.8f, .78f, z - .2f, 200, collide: false); Put(tankard, tb, 5.2f, .78f, z + .9f, 300, collide: false);
                Put(plate, tb, 5.6f, .78f, z + .35f, collide: false); Put(bowl, tb, 5.25f, .78f, z - .35f, 40, collide: false);
                Put(z < 0 ? bread : cheese, tb, 5.7f, .79f, z + .35f, 15, collide: false);
                var c = Put(candle, tb, 5.45f, .78f, z + 1.05f, collide: false); Glow(c, new Vector3(0, .32f, 0), flame, .9f, 2.5f);
            }
            // A small table with stools at the left wall's front, a barrel table left of the way in.
            Put(smallTable, tb, -5.6f, 0, -3.6f); Put(stool, tb, -4.7f, 0, -3.5f); Put(stool, tb, -5.6f, 0, -2.65f);
            Put(tankard, tb, -5.6f, .9f, -3.5f, 80, collide: false); Put(plate, tb, -5.4f, .9f, -3.75f, collide: false);
            Put(barrelTable, tb, -3.0f, 0, -3.9f); Put(stool, tb, -2.2f, 0, -4.0f); Put(stool, tb, -3.1f, 0, -3.05f);
            Put(beerMug, tb, -2.9f, .48f, -3.8f, 45, collide: false);

            // Stores in the corners: barrels at the back left, barrels and sacks at the back right and front right.
            var st = Group("Stores");
            Put(barrels, st, -6.5f, 0, 3.6f, 90);
            Put(barrel, st, 6.4f, 0, 4.4f); Put(sacks, st, 5.5f, 0, 4.5f, 20);
            Put(barrel, st, 6.4f, 0, -4.4f); Put(barrel, st, 5.65f, 0, -4.5f, 40);

            // On the walls: lanterns by the door and along the sides, banners, the deer and the boar, shields.
            var w = Group("Wall decor");
            foreach (var x in new[] { -1.6f, 1.6f }) { var l = Put(lantern, w, x, 2.0f, -4.95f, 0, collide: false); Glow(l, new Vector3(0, .25f, .25f), warm, 2.5f, 6f); }
            foreach (var z in new[] { -4.2f, 3.2f }) { var l = Put(lantern, w, 6.95f, 2.0f, z, 270, collide: false); Glow(l, new Vector3(0, .25f, .25f), warm, 2.5f, 6f); }
            foreach (var z in new[] { -3.6f, 3.0f }) { var l = Put(lantern, w, -6.95f, 2.0f, z, 90, collide: false); Glow(l, new Vector3(0, .25f, .25f), warm, 2.5f, 6f); }
            Put(banner, w, -3.0f, 2.95f, -4.95f, 0, collide: false); Put(banner, w, 3.0f, 2.95f, -4.95f, 0, collide: false);
            Put(deer, w, 6.95f, 1.9f, -.7f, 270, collide: false); Put(boar, w, -4.6f, 2.0f, 4.97f, 180, collide: false);
            Put(shield, w, 4.6f, 2.0f, 4.95f, 180, collide: false);

            // The floor rug between the door and the bar, the chandelier over it (no collider, high enough to pass under).
            var mid = Group("Middle");
            Put(runner, mid, 0, .005f, -1.1f, 0, blocking: false, collide: false);
            var ch = Put(chandelier, mid, 0, 3.0f, -1.1f, 0, false, collide: false); Glow(ch, new Vector3(0, -.7f, 0), warm, 4f, 9f);

            // Beams across the ceiling, in the style's trim, running front to back.
            var beams = Group("Ceiling beams");
            foreach (var x in new[] { -4.5f, -1.5f, 1.5f, 4.5f })
            {
                var go = new GameObject("Beam"); go.transform.SetParent(beams, false); go.transform.localPosition = new Vector3(x, 2.7f, 0);
                var k = go.AddComponent<DungeonKitBlock>(); k.size = new Vector3(.3f, .28f, 10f); k.fromCeiling = true; k.standsOnPosition = true;
                k.sides = DungeonKitBlock.Surface.Trim; k.top = DungeonKitBlock.Surface.Trim; k.solid = false;
            }
            PrefabUtility.SaveAsPrefabAsset(root, RoomPath);
            log.Add("room laid out");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return string.Join("; ", log);
    }
}

// ── The dishes' icons ──
// Each dish's icon rendered like the bag icons (ItemIconRenderer: the model straight on, point filtered, 88 px a cell)
// from a pixel copy of a bowl, through a short-lived ItemData; the PNG is kept in Levels/Shared/Props/Tavern/Icons.
public static class TavernDressIcons
{
    const string IconDir = "Assets/Game/Levels/Shared/Props/Tavern/Icons";
    const string DishDir = "Assets/Game/Levels/Resources/Tavern dishes";

    public static string Make(string dishName, GameObject model, Vector3 turn = default)
    {
        var dish = AssetDatabase.LoadAssetAtPath<TavernDish>($"{DishDir}/{dishName}.asset");
        if (dish == null || model == null) return dishName + ": no dish or model";
        Directory.CreateDirectory(IconDir);
        var item = ScriptableObject.CreateInstance<ItemData>();
        item.name = "Dish icon " + dishName; item.itemName = dishName; item.gridSize = new Vector2Int(1, 1);
        item.iconSource = new ItemIconSource { model = model, turn = turn };
        string temp = $"{IconDir}/{item.name}.asset";
        AssetDatabase.CreateAsset(item, temp);
        try
        {
            var error = ItemIconRenderer.Generate(item);
            if (error != null) return dishName + ": " + error;
            string made = ItemIconRenderer.PathFor(item), keep = $"{IconDir}/{dishName}.png";
            AssetDatabase.DeleteAsset(keep);
            var moved = AssetDatabase.MoveAsset(made, keep);
            if (!string.IsNullOrEmpty(moved)) return dishName + ": " + moved;
            dish.icon = AssetDatabase.LoadAssetAtPath<Sprite>(keep);
            EditorUtility.SetDirty(dish); AssetDatabase.SaveAssetIfDirty(dish);
            return dishName + ": " + keep;
        }
        finally { AssetDatabase.DeleteAsset(temp); }
    }
}

// ── Hand-made shading for the tavern's props ──
// The plain restyles (PixelRestyle.Build) came out smooth and blotchy next to the dungeon's props (the user, 2026-10-26).
// Each prop's texture is given the dungeon props' hand-made structure (Tools/prop_structure.py): faces as planks and
// bands, five shades a material, lit top edges and dark bottoms, wood grain, darker creases. Bake writes the G-buffers;
// Tools/tavern_structure.sh runs the painter; Apply reimports. The plain textures are kept in Temp/Tavern/plain.
public static class TavernStructure
{
    const string PixelDir = "Assets/Game/Levels/Shared/Props/Tavern/Pixel";

    public static string Bake(string only = null)
    {
        Directory.CreateDirectory("Temp/Tavern/gb"); Directory.CreateDirectory("Temp/Tavern/plain");
        var log = new System.Text.StringBuilder();
        foreach (var dir in Directory.GetDirectories(PixelDir))
        {
            string key = Path.GetFileName(dir);
            if (only != null && !only.Split('|').Any(o => key.StartsWith(o))) continue;
            string meshPath = $"{PixelDir}/{key}/{key}.asset", texPath = $"{PixelDir}/{key}/{key}.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath); var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (tex == null || mesh == null) { log.AppendLine(key + ": skipped"); continue; }
            string plain = $"Temp/Tavern/plain/{key}.png";
            if (!File.Exists(plain)) File.Copy(texPath, plain);
            string syntyName = key.LastIndexOf(" x") > 0 ? key.Substring(0, key.LastIndexOf(" x")) : key;
            log.AppendLine(key + "\t" + PixelRestyle.BakeGBuffer(meshPath, tex.width, tex.height, $"Temp/Tavern/gb/{key}.bin", "synty:" + syntyName));
        }
        File.WriteAllText("Temp/Tavern/gb/bake.txt", log.ToString());
        return log.ToString();
    }

    public static string Apply()
    {
        int n = 0;
        foreach (var dir in Directory.GetDirectories(PixelDir))
        {
            string key = Path.GetFileName(dir), made = $"Temp/Tavern/structured/{key}.png";
            if (!File.Exists(made)) continue;
            File.Copy(made, $"{PixelDir}/{key}/{key}.png", true);
            AssetDatabase.ImportAsset($"{PixelDir}/{key}/{key}.png", ImportAssetOptions.ForceUpdate); n++;
        }
        return n + " textures given structure";
    }
}
