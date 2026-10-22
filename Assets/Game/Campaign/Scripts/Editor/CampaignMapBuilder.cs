using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Builds the realm's campaign map prefab (Assets/Game/Campaign/Map/Realm map.prefab) and its encounters, from an eval:
// CampaignMapBuilder.Build(). Each place becomes a CampaignPoint with its props as children, each road a CampaignPath,
// the rest of the props the map's Scatter. Running it again replaces the prefab and its generated assets; the
// encounter assets are kept (only missing ones are made), so their hand edits stay.
public static partial class CampaignMapBuilder
{
    const string Root = "Assets/Game/Campaign", MapDir = Root + "/Map", MatDir = MapDir + "/Materials", EncDir = Root + "/Encounters", Res = Root + "/Resources";
    public const string PrefabPath = MapDir + "/Realm map.prefab";

    // The places of the realm: id, name, kind, level, act, card line, picked line.
    static readonly (string id, string name, CampaignEncounterKind kind, string level, int act, string text, string picked)[] Places =
    {
        ("start", "Home", CampaignEncounterKind.Home, null, 1, "Where every road starts.", null),
        ("crypt", "The Crypt", CampaignEncounterKind.Level, "Assets/Game/Levels/Dungeon1/Dungeon1.asset", 1, "Three floors down, and something waiting at the bottom.", "The crypt. Mind the stairs."),
        ("forest", "The Old Wood", CampaignEncounterKind.Level, "Assets/Game/Levels/Forest1/Forest1.asset", 1, "A board laid out among the trees.", "Into the wood, then."),
        ("mill", "The Mill", CampaignEncounterKind.Placeholder, null, 1, "The miller has been quiet lately.", null),
        ("camp", "Soldiers' Camp", CampaignEncounterKind.Placeholder, null, 2, "Tents by the road, and no one on watch.", null),
        ("village", "Market Town", CampaignEncounterKind.Placeholder, null, 2, "Trade, gossip, and a church bell.", null),
        ("tower", "Hill Tower", CampaignEncounterKind.Level, "Assets/Game/Levels/Dungeon2/Dungeon2.asset", 2, "A light burns at the top every night. The way down starts under it.", "The tower. Whoever lit that lamp is still down there."),
        ("ruin", "Broken Keep", CampaignEncounterKind.Level, "Assets/Game/Levels/Dungeon3/Dungeon3.asset", 3, "Walls without a roof, and cellars without an end.", "The keep. Its cellars go deeper than its walls ever stood."),
        ("shrine", "Ember Shrine", CampaignEncounterKind.Placeholder, null, 3, "The braziers never go out.", null),
        ("peak", "The Black Fort", CampaignEncounterKind.Level, "Assets/Game/Levels/Dungeon4/Dungeon4.asset", 4, "The end of the road.", "The fort. This is the last of it."),
    };

    public static string Build()
    {
        foreach (var d in new[] { Root, MapDir, MatDir, EncDir, Res }) Directory.CreateDirectory(d);
        AssetDatabase.Refresh();
        tinted.Clear();
        var o = new GenOut();
        var built = Generate(o);
        try { return Convert(built, o); }
        finally { if (built != null) Object.DestroyImmediate(built); }
    }

    static string Convert(GameObject built, GenOut o)
    {
        // Shared materials first: the bases and the run marker read them from Resources.
        var roadMat = RoadMaterial();
        RunMarker();

        var mapGo = new GameObject("Realm map");
        mapGo.transform.position = o.C;
        var map = mapGo.AddComponent<CampaignMap>();
        map.halfX = o.HX; map.halfZ = o.HZ; map.gridX = o.W; map.gridZ = o.H; map.heights = o.heights;
        map.roadMaterial = roadMat;
        Looks(map);
        Transform Group(string name) { var g = new GameObject(name).transform; g.SetParent(mapGo.transform, false); return g; }
        var ground = Group("Ground"); var lights = Group("Lights");
        map.points = Group("Points"); map.paths = Group("Paths"); map.scatter = Group("Scatter");

        var children = new List<Transform>();
        foreach (Transform t in built.transform) children.Add(t);
        var index = new Dictionary<Transform, int>();
        for (int i = 0; i < children.Count; i++) index[children[i]] = i;

        // The places: one CampaignPoint each, with the props built round it.
        var points = new Dictionary<string, CampaignPoint>();
        foreach (var place in Places)
        {
            if (!o.pois.TryGetValue(place.id, out var uv)) continue;
            var go = new GameObject(place.name);
            go.transform.SetParent(map.points, false);
            go.transform.localPosition = new Vector3(uv.x * o.HX, 0, uv.y * o.HZ);
            var point = go.AddComponent<CampaignPoint>();
            point.radius = o.radii.TryGetValue(place.id, out var r) ? r : 2.4f;
            point.encounter = Encounter(place);
            points[place.id] = point;
        }
        foreach (var t in children)
        {
            if (t.name == "terrain") { t.SetParent(ground, true); map.terrain = t.GetComponent<MeshFilter>(); continue; }
            if (t.name == "cut edge" || t.name == "Cube") { t.SetParent(ground, true); continue; }
            if (t.name == "table light" || t.name == "fill light") { t.SetParent(lights, true); continue; }
            int i = index[t];
            CampaignPoint owner = null;
            // Crop rows round a town are the land's, not the place's.
            if (i >= o.poiStart && i < o.poiEnd && !t.name.Contains("Farm"))
            {
                float best = float.MaxValue;
                foreach (var p in points.Values)
                {
                    var d = t.position - p.transform.position; d.y = 0;
                    if (d.magnitude < p.radius + .6f && d.magnitude < best) { best = d.magnitude; owner = p; }
                }
            }
            if (owner != null)
            {
                bool light = t.GetComponent<Light>() != null;
                t.SetParent(owner.transform, true);
                if (light) { if (owner.glow == null) owner.glow = t.GetComponent<Light>(); t.localPosition = new Vector3(t.localPosition.x, 9f, t.localPosition.z); }
                else t.localPosition = new Vector3(t.localPosition.x, 0f, t.localPosition.z);
            }
            else t.SetParent(map.scatter, true);
        }
        // Points snap to the ground under them now that the map knows its heights.
        foreach (var p in points.Values) { p.enabled = false; p.enabled = true; }
        // The roads.
        foreach (var leg in o.legs)
        {
            if (!points.TryGetValue(leg.a, out var a) || !points.TryGetValue(leg.b, out var b)) continue;
            var go = new GameObject($"Road {a.name} - {b.name}");
            go.transform.SetParent(map.paths, false);
            var path = go.AddComponent<CampaignPath>();
            path.from = a; path.to = b; path.width = 1.1f; path.wobble = .3f;
            // The bend that puts the road's middle where the prototype had it.
            Vector3 pa = a.transform.localPosition, pb = b.transform.localPosition;
            var flat = pb - pa; flat.y = 0; float length = flat.magnitude;
            var side = new Vector3(-flat.z, 0, flat.x) / Mathf.Max(.01f, length);
            var midLocal = new Vector3(leg.mid.x * o.HX, 0, leg.mid.y * o.HZ);
            var straight = (pa + pb) * .5f; straight.y = 0;
            path.bend = Mathf.Clamp(Vector3.Dot(midLocal - straight, side) / Mathf.Max(.01f, length * .5f), -1f, 1f);
            path.Rebuild();
        }
        map.ClearScatterUnderRoads();

        var counts = $"{points.Count} places, {o.legs.Count} roads, {map.scatter.childCount} scatter";
        Persist(mapGo);
        // Saved over the old prefab, so its GUID (and every reference to it) stays.
        var prefab = PrefabUtility.SaveAsPrefabAsset(mapGo, PrefabPath);
        Object.DestroyImmediate(mapGo);
        AssetDatabase.SaveAssets();
        if (prefab == null) return "failed to save the prefab";
        // The models in the pixel look at the map's density (bakes are kept, so this is quick after the first time).
        return "built " + PrefabPath + ": " + counts + "; " + FitTiling() + PixelProps();
    }

    static CampaignEncounter Encounter((string id, string name, CampaignEncounterKind kind, string level, int act, string text, string picked) p)
    {
        // One folder per act.
        Directory.CreateDirectory($"{EncDir}/Act {p.act}");
        string path = $"{EncDir}/Act {p.act}/{p.name}.asset";
        var e = AssetDatabase.LoadAssetAtPath<CampaignEncounter>(path);
        if (e != null) return e;
        AssetDatabase.Refresh();
        e = ScriptableObject.CreateInstance<CampaignEncounter>();
        e.id = p.id; e.displayName = p.name; e.kind = p.kind; e.act = p.act; e.description = p.text;
        if (p.level != null) e.level = AssetDatabase.LoadAssetAtPath<TableLevelData>(p.level);
        if (p.picked != null) e.pickedLine = new DungeonMaster.Line { text = p.picked, inPerson = true };
        AssetDatabase.CreateAsset(e, path);
        return e;
    }

    // ── Assets ──

    static Material SaveMat(string path, Color c, float smooth)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth);
        EditorUtility.SetDirty(m);
        return m;
    }

    // The bases' earth and stone, and the cloths over closed acts, all pixel textures at the ground's texel
    // size (4 a local unit). Safe to run again on the map prefab: CampaignMapBuilder.Looks(map).
    public static void Looks(CampaignMap map)
    {
        uint Hash(int x, int y, int k) { unchecked { uint h = (uint)(x * 374761393 + y * 668265263 + k * 2147483647); h = (h ^ (h >> 13)) * 1274126177; return h ^ (h >> 16); } }
        float R(int x, int y, int k) => (Hash(x, y, k) & 0xffff) / 65535f;
        // Tileable value noise.
        float Noise(int x, int y, int cell, int size, int k)
        {
            int cells = size / cell;
            float fx = x / (float)cell, fy = y / (float)cell;
            int x0 = (int)fx, y0 = (int)fy; float tx = fx - x0, ty = fy - y0;
            float C(int cx, int cy) => R((cx % cells + cells) % cells, (cy % cells + cells) % cells, k);
            return Mathf.Lerp(Mathf.Lerp(C(x0, y0), C(x0 + 1, y0), tx), Mathf.Lerp(C(x0, y0 + 1), C(x0 + 1, y0 + 1), tx), ty);
        }

        // Base top: packed earth with patches of grass, pebbles and dark crumbs, painted texel by texel at the map's
        // density (no upscaled design: the ground beside it has detail in every texel too).
        int E = Mathf.RoundToInt(8f * MapTexelsPerUnit);   // the earth tile spans 8 units
        var earth = PixelTexture(Res + "/Campaign base top.png", E, E, (x, y) =>
        {
            float grass = Noise(x, y, E / 4, E, 1) * .6f + Noise(x, y, E / 8, E, 2) * .4f;
            var c = Color.Lerp(new Color(.42f, .33f, .22f), new Color(.31f, .24f, .16f), Noise(x, y, E / 16 > 0 ? E / 16 : 1, E, 3));
            if (grass > .56f) c = Color.Lerp(new Color(.33f, .42f, .2f), new Color(.22f, .31f, .14f), R(x, y, 4));
            else if (grass > .52f) c = R(x, y, 12) > .5f ? new Color(.3f, .37f, .18f) : c;
            float r = R(x, y, 5);
            if (r > .95f) c = Color.Lerp(c, new Color(.62f, .58f, .5f), .7f);
            else if (r < .07f) c *= .7f;
            return c * (.9f + R(x, y, 6) * .18f);
        }, MapTexelsPerUnit);
        // Base sides: courses of stone a texel apart, dark seams between them, cracks and moss.
        int K = Mathf.RoundToInt(4f * MapTexelsPerUnit);   // the stone tile spans 4 units
        var stone = PixelTexture(Res + "/Campaign base rim.png", K, K, (x, y) =>
        {
            int course = Mathf.Max(3, K / 6);
            int shift = (int)(Noise(x, 0, Mathf.Max(1, K / 4), K, 7) * 2);
            int band = (y + shift) / course;
            var c = Color.Lerp(new Color(.44f, .41f, .37f), new Color(.31f, .29f, .27f), R(band, (x + band * 7) / Mathf.Max(2, course * 2), 8));
            c *= .9f + R(x, y, 9) * .18f;
            if ((y + shift) % course == 0) c *= .55f;
            if (R(x, y, 10) > .94f) c *= .65f;
            if (R(x, y, 11) > .93f) c = Color.Lerp(c, new Color(.3f, .38f, .2f), .55f);
            return c;
        }, MapTexelsPerUnit);
        LookMaterial(Res + "/Campaign base top.mat", earth, false);
        LookMaterial(Res + "/Campaign base rim.mat", stone, false);

        // The cloths over closed acts, one per CampaignClothStyle; 32 texels across 8 local units.
        Color Weave(int x, int y, Color c, int k) => c * (((x + y) & 1) == 0 ? .96f : 1.04f) * (.93f + R(x, y, k) * .1f);
        var looks = new List<CampaignMap.ClothLook>();
        // size 64 repeats every 16 local units instead of 8 (the material is tiled at half), at the same texel size.
        void Cloth(CampaignClothStyle style, System.Func<int, int, Color> paint, int size = 32)
        {
            var tex = PixelTexture($"{MatDir}/Cloth {style}.png", size, size, paint);
            var m = LookMaterial($"{MatDir}/Cloth {style}.mat", tex, true);
            m.SetTextureScale("_BaseMap", Vector2.one * (32f / size));
            looks.Add(new CampaignMap.ClothLook { style = style, material = m });
        }
        // Plaid: a woollen tartan, deep red with green and gold lines, its weave in alternate texels.
        Cloth(CampaignClothStyle.Plaid, (x, y) =>
        {
            Color Stripe(int i) => (i % 16) switch
            {
                >= 4 and < 8 => new Color(.16f, .26f, .2f),
                10 => new Color(.78f, .62f, .3f),
                13 or 14 => new Color(.13f, .12f, .16f),
                _ => new Color(.5f, .14f, .12f),
            };
            var warp = Stripe(x); var weft = Stripe(y);
            var c = ((x + y) & 1) == 0 ? Color.Lerp(warp, weft, .35f) : Color.Lerp(weft, warp, .35f);
            return c * (.9f + R(x, y, 12) * .14f);
        });
        // Quilt: four patches (dotted blue, striped mustard, checked sage, sprigged red), dark seams and cream stitches.
        Cloth(CampaignClothStyle.Quilt, (x, y) =>
        {
            int px = x / 16, py = y / 16, lx = x % 16, ly = y % 16;
            Color c;
            if (px == 0 && py == 0) c = (lx % 4 == 2 && ly % 4 == 2) || (lx % 4 == 0 && ly % 4 == 0) ? new Color(.85f, .82f, .72f) : new Color(.27f, .36f, .5f);
            else if (px == 1 && py == 0) c = (lx + ly) % 5 == 0 ? new Color(.45f, .3f, .17f) : new Color(.72f, .56f, .24f);
            else if (px == 0) c = ((lx / 2 + ly / 2) & 1) == 0 ? new Color(.42f, .5f, .36f) : new Color(.33f, .41f, .29f);
            else
            {
                int fx = lx % 8, fy = ly % 8;
                bool sprig = (fx == 4 && fy >= 3 && fy <= 5) || (fy == 4 && fx >= 3 && fx <= 5);
                c = sprig ? (fx == 4 && fy == 4 ? new Color(.86f, .74f, .36f) : new Color(.86f, .78f, .68f)) : new Color(.58f, .2f, .17f);
            }
            if (lx == 0 || ly == 0) c = Color.Lerp(c, new Color(.12f, .1f, .1f), .55f);
            else if ((lx == 1 && ly % 2 == 0) || (ly == 1 && lx % 2 == 0)) c = new Color(.88f, .84f, .74f);
            return Weave(x, y, c, 15);
        });
        // Embroidered: teal velvet with a sheen, a lattice of gold thread and a red bead in each diamond.
        Cloth(CampaignClothStyle.Embroidered, (x, y) =>
        {
            var c = Color.Lerp(new Color(.06f, .22f, .25f), new Color(.1f, .33f, .35f), Noise(x, y, 8, 32, 16));
            bool line = (x + y) % 16 == 0 || ((x - y) % 16 + 16) % 16 == 0;
            int cx = x % 16, cy = y % 16;
            bool knot = (cx == 0 || cx == 15 || cx == 1) && (cy == 0 || cy == 15 || cy == 1);
            if (knot) c = new Color(.92f, .76f, .38f);
            else if (line) c = ((x / 1) & 1) == 0 ? new Color(.82f, .64f, .3f) : new Color(.6f, .45f, .2f);
            else if (cx == 8 && cy == 8) c = new Color(.66f, .14f, .14f);
            else if ((cx == 8 && (cy == 7 || cy == 9)) || (cy == 8 && (cx == 7 || cx == 9))) c = new Color(.42f, .1f, .12f);
            return c * (.94f + R(x, y, 17) * .08f);
        });
        // Burlap: coarse sackcloth, its weave in pairs of texels, thick slubs, dark specks and a faded stamped band.
        Cloth(CampaignClothStyle.Burlap, (x, y) =>
        {
            var c = ((x / 2 + y / 2) & 1) == 0 ? new Color(.58f, .47f, .31f) : new Color(.5f, .4f, .26f);
            if (R(x / 4, y, 18) > .86f) c = new Color(.64f, .53f, .36f);
            if (R(x, y, 19) > .95f) c *= .62f;
            if (y == 11 || y == 14) c = Color.Lerp(c, new Color(.35f, .2f, .14f), .45f);
            return c * (.93f + R(x, y, 20) * .1f);
        });
        // Starcloth: night-blue cloth sewn with gold and pale stars, a few of them four-pointed, and a crescent moon.
        Cloth(CampaignClothStyle.Starcloth, (x, y) =>
        {
            var c = Color.Lerp(new Color(.05f, .06f, .14f), new Color(.09f, .1f, .22f), Noise(x, y, 8, 32, 21));
            foreach (var (sx, sy) in new[] { (6, 9), (23, 25), (26, 5) })
            {
                int dx = Mathf.Abs(x - sx), dy = Mathf.Abs(y - sy);
                if (dx + dy == 0) return new Color(1f, .92f, .6f);
                if ((dx == 0 && dy <= 2) || (dy == 0 && dx <= 2)) return new Color(.85f, .68f, .32f);
            }
            float mx = x - 14.5f, my = y - 20.5f, ox = x - 16f, oy = y - 21.5f;
            if (mx * mx + my * my <= 9f && ox * ox + oy * oy > 7f) return new Color(.9f, .78f, .42f);
            float r = R(x, y, 22);
            if (r > .975f) c = new Color(.95f, .9f, .72f);
            else if (r > .955f) c = Color.Lerp(c, new Color(.5f, .52f, .7f), .6f);
            return Weave(x, y, c, 23);
        });
        // Fog: grey cloth in soft drifting patches, darker where it gathers, with only a faint weave.
        Cloth(CampaignClothStyle.Fog, (x, y) =>
        {
            float n = Noise(x, y, 32, 64, 24) * .45f + Noise(x, y, 16, 64, 25) * .3f + Noise(x, y, 8, 64, 26) * .17f + Noise(x, y, 4, 64, 28) * .08f;
            var c = Color.Lerp(new Color(.19f, .2f, .24f), new Color(.47f, .48f, .52f), Mathf.SmoothStep(0f, 1f, n));
            return c * (((x + y) & 1) == 0 ? .985f : 1.015f) * (.97f + R(x, y, 27) * .05f);
        }, 64);
        map.cloths = looks;
        // The first reveal's simulated cloth drop, when it has been baked (CampaignClothBake.Run).
        map.clothDropBake = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Campaign/Map/Cloth drop.bytes");
        EditorUtility.SetDirty(map);
        AssetDatabase.SaveAssets();
    }

    // The looks are designed at 4 texels a local unit (w x h) and written at the map's density (MapTexelsPerUnit): the
    // design is sampled per texel of the larger texture and each texel gets a little grain of its own, so the patterns
    // keep their size and the pixels match the ground's. scale: the design's own density, when it isn't 4.
    static Texture2D PixelTexture(string path, int w, int h, System.Func<int, int, Color> paint, float designTexels = 4f)
    {
        float up = MapTexelsPerUnit / designTexels;
        int W = Mathf.Max(1, Mathf.RoundToInt(w * up)), H = Mathf.Max(1, Mathf.RoundToInt(h * up));
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var c = paint(Mathf.Min(w - 1, (int)(x * w / (float)W)), Mathf.Min(h - 1, (int)(y * h / (float)H)));
                uint hsh = unchecked((uint)(x * 73856093) ^ (uint)(y * 19349663) ^ 0x9e3779b9u); hsh ^= hsh >> 13; hsh *= 1274126177u; hsh ^= hsh >> 16;
                c *= .96f + (hsh & 0xff) / 255f * .08f;
                c.a = 1; tex.SetPixel(x, y, c);
            }
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.filterMode = FilterMode.Point; importer.textureCompression = TextureImporterCompression.Uncompressed; importer.mipmapEnabled = false; importer.wrapMode = TextureWrapMode.Repeat;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material LookMaterial(string path, Texture2D tex, bool twoSided)
    {
        var m = SaveMat(path, Color.white, .05f);
        m.SetTexture("_BaseMap", tex);
        m.SetTextureScale("_BaseMap", Vector2.one);   // FitTiling sets it from a measurement after
        m.SetFloat("_Cull", twoSided ? 0f : 2f);
        m.doubleSidedGI = twoSided;
        EditorUtility.SetDirty(m);
        return m;
    }

    // A worn dirt road: darker edges, a lighter middle, a few stones; point-filtered like the ground.
    static Material RoadMaterial()
    {
        // 16 x 8 texels over 2.2 x 1.1 local units of road.
        var texPath = MatDir + "/Road.png";
        PixelTexture(texPath, 16, 8, (x, y) =>
        {
            float edge = Mathf.Abs(y - 3.5f) / 3.5f;
            var c = Color.Lerp(new Color(.56f, .46f, .31f), new Color(.36f, .28f, .19f), Mathf.Pow(edge, 3));
            if (((x * 7 + y * 13) % 11) == 0) c *= .85f;
            if (((x * 5 + y * 3) % 17) == 0) c = Color.Lerp(c, new Color(.66f, .6f, .5f), .6f);
            return c;
        }, 16f / 2.2f);
        var m = SaveMat(MatDir + "/Road.mat", Color.white, .05f);
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
        return m;
    }

    // The flag that bobs over the place where a saved run waits.
    static void RunMarker()
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Synty/PolygonDarkFantasy/Prefabs/Props/SM_Prop_Flag_Hero_01.prefab");
        if (src == null) return;
        var go = new GameObject("Campaign run marker");
        var flag = (GameObject)PrefabUtility.InstantiatePrefab(src, go.transform);
        flag.transform.localScale = Vector3.one * 1.6f;
        foreach (var c in flag.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        var light = new GameObject("Glow").AddComponent<Light>();
        light.transform.SetParent(go.transform, false); light.transform.localPosition = Vector3.up * 3f;
        light.type = LightType.Point; light.color = new Color(1f, .8f, .4f); light.range = 12f; light.intensity = 120f;
        PrefabUtility.SaveAsPrefabAsset(go, Res + "/Campaign run marker.prefab");
        Object.DestroyImmediate(go);
    }

    static readonly Dictionary<(Material, Color32), Material> tinted = new();

    // A tinted copy of each of the prop's materials (saved with the map), in place of a property block, which a prefab
    // cannot keep. Tints are rounded so similar ones share a material.
    static void Tint(GameObject g, Color tint)
    {
        var key = (Color32)new Color(Mathf.Round(tint.r * 12) / 12, Mathf.Round(tint.g * 12) / 12, Mathf.Round(tint.b * 12) / 12, 1);
        foreach (var r in g.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                if (!tinted.TryGetValue((m, key), out var t))
                {
                    t = new Material(m) { name = $"{m.name} tint {key.r:X2}{key.g:X2}{key.b:X2}" };
                    var baseColor = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
                    if (t.HasProperty("_BaseColor")) t.SetColor("_BaseColor", baseColor * (Color)key);
                    if (t.HasProperty("_Color")) t.SetColor("_Color", baseColor * (Color)key);
                    tinted[(m, key)] = t;
                }
                mats[i] = t;
            }
            r.sharedMaterials = mats;
        }
    }

    // Saves every generated mesh, texture and material under the map as assets, so the prefab keeps them.
    static void Persist(GameObject root)
    {
        int n = 0;
        string Unique(string name, string ext) => AssetDatabase.GenerateUniqueAssetPath($"{MapDir}/Generated/{San(name)}.{ext}");
        Directory.CreateDirectory(MapDir + "/Generated");
        foreach (var old in AssetDatabase.FindAssets("", new[] { MapDir + "/Generated" })) AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(old));
        var savedMats = new Dictionary<Material, Material>();
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.GetComponent<CampaignPath>() != null) continue;   // roads rebuild themselves
            if (mf.name == "Base" && mf.GetComponentInParent<CampaignPoint>() != null) continue;   // so do the places' bases
            var mesh = mf.sharedMesh;
            if (mesh != null && !AssetDatabase.Contains(mesh)) { AssetDatabase.CreateAsset(mesh, Unique(mf.name + " mesh", "asset")); n++; }
        }
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || AssetDatabase.Contains(m)) continue;
                if (!savedMats.TryGetValue(m, out var saved))
                {
                    var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") as Texture2D : null;
                    if (tex != null && !AssetDatabase.Contains(tex))
                    {
                        string tp = Unique(r.name + " texture", "png");
                        File.WriteAllBytes(tp, tex.EncodeToPNG());
                        AssetDatabase.ImportAsset(tp);
                        var imp = (TextureImporter)AssetImporter.GetAtPath(tp);
                        imp.filterMode = tex.filterMode; imp.textureCompression = TextureImporterCompression.Uncompressed; imp.mipmapEnabled = false;
                        imp.wrapMode = tex.wrapMode; imp.npotScale = TextureImporterNPOTScale.None; imp.maxTextureSize = 2048;
                        imp.SaveAndReimport();
                        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tp));
                    }
                    AssetDatabase.CreateAsset(m, Unique(m.name.Length > 0 ? m.name : r.name + " material", "mat"));
                    savedMats[m] = saved = m; n++;
                }
                mats[i] = saved;
            }
            r.sharedMaterials = mats;
        }
    }

    static string San(string s) { foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return s.Trim(); }
}
