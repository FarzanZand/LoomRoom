// Mood pass (branch mood, 2026-10-24): the bedroom and the game table dressed toward the user's reference photo (a dark
// study: a heavy wooden war table with a misty relief map, candles on its corners, a caged lantern hanging over it, cool
// blue shadows and warm pools of light). Everything built here lives under Furnishings/Mood and is rebuilt on every
// run; pieces of the old dressing it replaces are switched off, not deleted. Props are the game's own pixel restyles,
// scaled so they measure the bedroom's 6.4 texels a unit; new boxes get box-projected UVs at that density.
if (UnityEngine.Application.isPlaying) return "stop Play first";
const string Bed = "World/Apartment/Main Bedroom";
var log = new System.Text.StringBuilder();
var fur = UnityEngine.GameObject.Find(Bed + "/Furnishings").transform;
var old = fur.Find("Mood"); if (old) UnityEngine.Object.DestroyImmediate(old.gameObject);
var root = new UnityEngine.GameObject("Mood").transform; root.SetParent(fur, false);
string dir = "Assets/Game/World/Art/Mood"; System.IO.Directory.CreateDirectory(dir + "/Meshes");
var newLightsEarly = new System.Collections.Generic.List<UnityEngine.Light>();
var quad = UnityEngine.Resources.GetBuiltinResource<UnityEngine.Mesh>("Quad.fbx");
UnityEngine.Material LoadMat(string p) => UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(p);
UnityEngine.Mesh LoadMesh(string p) => UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(p);

// a box with UVs projected in the world at the bedroom's density for its material's texture (texels across = width x tiling)
UnityEngine.GameObject Box(string name, UnityEngine.Transform parent, UnityEngine.Vector3 centre, UnityEngine.Vector3 size, UnityEngine.Material mat, bool grainAlongZ = false)
{
  var tex = mat.GetTexture("_BaseMap"); var til = mat.GetTextureScale("_BaseMap"); float k = 6.4f / (tex.width * til.x);
  string key = $"{name} {size.x:0.##}x{size.y:0.##}x{size.z:0.##} {mat.name}";
  string path = $"{dir}/Meshes/{key}.asset"; var m = LoadMesh(path);
  if (!m)
  {
    m = new UnityEngine.Mesh { name = key }; var h = size * .5f;
    var vs = new System.Collections.Generic.List<UnityEngine.Vector3>(); var ns = new System.Collections.Generic.List<UnityEngine.Vector3>(); var us = new System.Collections.Generic.List<UnityEngine.Vector2>(); var ts = new System.Collections.Generic.List<int>();
    void Face(UnityEngine.Vector3 n, UnityEngine.Vector3 u, UnityEngine.Vector3 v)
    {
      // four corners of the face whose normal is n, spanned by u and v (half extents along them)
      var c = UnityEngine.Vector3.Scale(n, h); var hu = UnityEngine.Vector3.Scale(u, h); var hv = UnityEngine.Vector3.Scale(v, h); int b = vs.Count;
      foreach (var (a, bb) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
      {
        var p = c + hu * a + hv * bb; vs.Add(p); ns.Add(n);
        // grain: the long board direction is u on top faces; world-metric UVs so every face has the same texel size
        var w = p; UnityEngine.Vector2 uv = UnityEngine.Mathf.Abs(n.y) > .5f ? (grainAlongZ ? new UnityEngine.Vector2(w.z, w.x) : new UnityEngine.Vector2(w.x, w.z)) : UnityEngine.Mathf.Abs(n.x) > .5f ? new UnityEngine.Vector2(w.z, w.y) : new UnityEngine.Vector2(w.x, w.y);
        us.Add(uv * k);
      }
      // winding so the face looks along n: Unity draws a triangle's front where cross(b - a, c - a) points
      var fn = UnityEngine.Vector3.Cross(vs[b + 1] - vs[b], vs[b + 2] - vs[b]);
      if (UnityEngine.Vector3.Dot(fn, n) > 0) { ts.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 }); } else { ts.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 }); }
    }
    Face(UnityEngine.Vector3.up, UnityEngine.Vector3.right, UnityEngine.Vector3.forward); Face(UnityEngine.Vector3.down, UnityEngine.Vector3.right, UnityEngine.Vector3.forward);
    Face(UnityEngine.Vector3.right, UnityEngine.Vector3.forward, UnityEngine.Vector3.up); Face(UnityEngine.Vector3.left, UnityEngine.Vector3.forward, UnityEngine.Vector3.up);
    Face(UnityEngine.Vector3.forward, UnityEngine.Vector3.right, UnityEngine.Vector3.up); Face(UnityEngine.Vector3.back, UnityEngine.Vector3.right, UnityEngine.Vector3.up);
    m.SetVertices(vs); m.SetNormals(ns); m.SetUVs(0, us); m.SetTriangles(ts, 0); m.RecalculateBounds(); m.RecalculateTangents();
    UnityEditor.AssetDatabase.CreateAsset(m, path);
  }
  var go = new UnityEngine.GameObject(name); go.transform.SetParent(parent, false); go.transform.position = centre;
  go.AddComponent<UnityEngine.MeshFilter>().sharedMesh = m; go.AddComponent<UnityEngine.MeshRenderer>().sharedMaterial = mat;
  return go;
}
// a restyled prop by its folder under Art/Synty Pixel, at the scale that measures 6.4
UnityEngine.GameObject Prop(string name, UnityEngine.Transform parent, string asset, UnityEngine.Vector3 pos, float yaw, float scale)
{
  var go = new UnityEngine.GameObject(name); go.transform.SetParent(parent, false); go.transform.position = pos; go.transform.rotation = UnityEngine.Quaternion.Euler(0, yaw, 0); go.transform.localScale = UnityEngine.Vector3.one * scale;
  go.AddComponent<UnityEngine.MeshFilter>().sharedMesh = LoadMesh($"Assets/Game/Art/Synty Pixel/{asset}.asset");
  var mr = go.AddComponent<UnityEngine.MeshRenderer>(); mr.sharedMaterial = LoadMat($"Assets/Game/Art/Synty Pixel/{asset}.mat");
  return go;
}
UnityEngine.Light PointLight(string name, UnityEngine.Transform parent, UnityEngine.Vector3 pos, UnityEngine.Color c, float intensity, float range, bool shadows = false)
{
  var go = new UnityEngine.GameObject(name); go.transform.SetParent(parent, false); go.transform.position = pos;
  var l = go.AddComponent<UnityEngine.Light>(); l.type = UnityEngine.LightType.Point; l.color = c; l.intensity = intensity; l.range = range; l.shadows = shadows ? UnityEngine.LightShadows.Soft : UnityEngine.LightShadows.None;
  return l;
}

// ── The table ─────────────────────────────────────────────────────────
var tm = TableManager.Instance; var top = tm.GetComponent<UnityEngine.BoxCollider>().bounds; var C = top.center; float hx = top.extents.x, hz = top.extents.z, ty = top.max.y;
var dressing = fur.Find("Game table dressing");
foreach (UnityEngine.Transform c in dressing) if (c.name.StartsWith("Table leg") || c.name == "Table apron" || c.name == "Table rim") c.gameObject.SetActive(false);
var woodSrc = LoadMat("Assets/Game/World/Art/Bedroom/Bedroom table wood.mat");
// the war table's own darker stain: the tabletop's planks (same texture and tiling, so the same texel size), darker
var wood = LoadMat($"{dir}/War table wood.mat"); if (!wood) { wood = new UnityEngine.Material(woodSrc); UnityEditor.AssetDatabase.CreateAsset(wood, $"{dir}/War table wood.mat"); }
wood.CopyPropertiesFromMaterial(woodSrc); wood.SetColor("_BaseColor", new UnityEngine.Color(.52f, .40f, .34f)); UnityEditor.EditorUtility.SetDirty(wood);
var table = new UnityEngine.GameObject("Table").transform; table.SetParent(root, false);
const float RimW = 6f, RimUp = 2.4f, RimDown = 4f, ApronH = 3.2f, LegW = 9f;
// the raised frame round the map (its inner face on the tabletop's edge, so nothing on the table is covered)
float rimY = ty + (RimUp - RimDown) * .5f, rimH = RimUp + RimDown;
Box("Frame east", table, new UnityEngine.Vector3(C.x + hx + RimW * .5f, rimY, C.z), new UnityEngine.Vector3(RimW, rimH, 2 * hz + 2 * RimW), wood, true);
Box("Frame west", table, new UnityEngine.Vector3(C.x - hx - RimW * .5f, rimY, C.z), new UnityEngine.Vector3(RimW, rimH, 2 * hz + 2 * RimW), wood, true);
Box("Frame north", table, new UnityEngine.Vector3(C.x, rimY, C.z + hz + RimW * .5f), new UnityEngine.Vector3(2 * hx, rimH, RimW), wood);
Box("Frame south", table, new UnityEngine.Vector3(C.x, rimY, C.z - hz - RimW * .5f), new UnityEngine.Vector3(2 * hx, rimH, RimW), wood);
// a thin moulding along the frame's top outer edge
float capY = ty + RimUp + .4f;
Box("Frame cap east", table, new UnityEngine.Vector3(C.x + hx + RimW * .5f, capY, C.z), new UnityEngine.Vector3(RimW + .8f, .8f, 2 * hz + 2 * RimW + .8f), wood, true);
Box("Frame cap west", table, new UnityEngine.Vector3(C.x - hx - RimW * .5f, capY, C.z), new UnityEngine.Vector3(RimW + .8f, .8f, 2 * hz + 2 * RimW + .8f), wood, true);
Box("Frame cap north", table, new UnityEngine.Vector3(C.x, capY, C.z + hz + RimW * .5f), new UnityEngine.Vector3(2 * hx + .8f, .8f, RimW + .8f), wood);
Box("Frame cap south", table, new UnityEngine.Vector3(C.x, capY, C.z - hz - RimW * .5f), new UnityEngine.Vector3(2 * hx + .8f, .8f, RimW + .8f), wood);
// the apron under the top, set in a little from the frame
float apronTop = ty - RimDown, apronY = apronTop - ApronH * .5f;
Box("Apron", table, new UnityEngine.Vector3(C.x, apronY, C.z), new UnityEngine.Vector3(2 * hx + RimW, ApronH, 2 * hz + RimW), wood, true);
// thick square legs at the corners, and stretchers between them near the floor
float legH = apronTop - ApronH;
foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
  Box("Leg", table, new UnityEngine.Vector3(C.x + sx * (hx - 1f), legH * .5f, C.z + sz * (hz - 1f)), new UnityEngine.Vector3(LegW, legH, LegW), wood);
foreach (var sx in new[] { -1f, 1f }) Box("Stretcher", table, new UnityEngine.Vector3(C.x + sx * (hx - 1f), 1.6f, C.z), new UnityEngine.Vector3(3f, 2f, 2 * hz - 4f), wood, true);
Box("Stretcher", table, new UnityEngine.Vector3(C.x, 1.6f, C.z), new UnityEngine.Vector3(2 * hx - 4f, 2f, 3f), wood);
log.Append($"table frame round {2 * hx:F1} x {2 * hz:F1} at top {ty:F2}; ");

// candles on the frame's corners: the dungeon's candle cluster (its own flames and light), its two full-size candles
// kept and drawn together to fit the frame
var candles = new UnityEngine.GameObject("Corner candles").transform; candles.SetParent(table, false);
var clusterPf = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Game/Levels/Shared/Props/Furnishing/Candle cluster.prefab");
foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
{
  var p = new UnityEngine.Vector3(C.x + sx * (hx + RimW * .5f), ty + RimUp + .8f, C.z + sz * (hz + RimW * .5f));
  var cl = (UnityEngine.GameObject)UnityEngine.Object.Instantiate(clusterPf, candles); cl.name = "Candles"; cl.transform.position = p; cl.transform.localScale = UnityEngine.Vector3.one * 20f;
  cl.transform.rotation = UnityEngine.Quaternion.Euler(0, sx * sz > 0 ? 30 : -60, 0);
  // cream pillars only (the cluster's red tapers in holders go), three of them stepped down in height
  var drop = new System.Collections.Generic.List<UnityEngine.GameObject>(); foreach (UnityEngine.Transform c in cl.transform) { var mf2 = c.GetComponent<UnityEngine.MeshFilter>(); if (mf2 && mf2.sharedMesh.name.Contains("Candle_03")) drop.Add(c.gameObject); }
  foreach (var d in drop) UnityEngine.Object.DestroyImmediate(d);   // gathered first: destroying while walking the children skips some
  var pillars = new System.Collections.Generic.List<UnityEngine.Transform>(); foreach (UnityEngine.Transform c in cl.transform) if (c.GetComponent<UnityEngine.MeshRenderer>()) pillars.Add(c);
  if (pillars.Count > 0) { var extra = UnityEngine.Object.Instantiate(pillars[0].gameObject, cl.transform); pillars.Add(extra.transform); }
  var spots = new[] { (new UnityEngine.Vector3(-.06f, 0, .03f), 1.15f), (new UnityEngine.Vector3(.07f, 0, -.04f), .85f), (new UnityEngine.Vector3(-.02f, 0, -.11f), .6f) };
  // full scale (so they keep the bedroom's texel size); the shorter ones stand sunk into the frame
  for (int k = 0; k < pillars.Count && k < 3; k++) { pillars[k].localPosition = spots[k].Item1 + UnityEngine.Vector3.up * (spots[k].Item2 - 1.15f) * .3f; pillars[k].localScale = UnityEngine.Vector3.one; }
  var cll = cl.GetComponentInChildren<UnityEngine.Light>(); if (cll) { cll.range = 30f; cll.intensity = 22f; cll.shadows = UnityEngine.LightShadows.None; }
}
// the lantern over the table in place of the opal shade (the night light and its glow stay where they hang)
var lamp = fur.Find("Night lamp"); lamp.GetComponent<UnityEngine.MeshRenderer>().enabled = false;
UnityEditor.AssetDatabase.ImportAsset($"{dir}/Globe lantern.fbx");
var lanternPf = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>($"{dir}/Globe lantern.fbx");
var lantern = (UnityEngine.GameObject)UnityEngine.Object.Instantiate(lanternPf, root); lantern.name = "Globe lantern";
const float LanternY = 55f; lantern.transform.position = new UnityEngine.Vector3(C.x, LanternY, C.z);
string gp = $"{dir}/Lantern glass Glow.mat"; var glass = LoadMat(gp);
if (!glass) { glass = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Lit")); UnityEditor.AssetDatabase.CreateAsset(glass, gp); }
glass.SetColor("_BaseColor", new UnityEngine.Color(.85f, .5f, .2f)); glass.EnableKeyword("_EMISSION"); glass.SetColor("_EmissionColor", new UnityEngine.Color(1.5f, .62f, .16f)); glass.SetFloat("_Smoothness", .2f);
glass.globalIlluminationFlags = UnityEngine.MaterialGlobalIlluminationFlags.RealtimeEmissive; UnityEditor.EditorUtility.SetDirty(glass);
foreach (var r in lantern.GetComponentsInChildren<UnityEngine.MeshRenderer>())
{
  r.sharedMaterial = r.name == "Brass" ? LoadMat("Assets/Game/World/Art/Trophy wall/Sconce brass 6.4.mat") : r.name == "Glass" ? glass : LoadMat("Assets/Game/World/Art/Trophy wall/Sconce flame.mat");
  r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // the lights hang inside it
}
var lb = lantern.GetComponentInChildren<UnityEngine.Renderer>().bounds; foreach (var r in lantern.GetComponentsInChildren<UnityEngine.Renderer>()) lb.Encapsulate(r.bounds);
float lanternTop = lb.max.y;
var susp = lamp.Find("Pendant suspension");
if (susp) { susp.GetComponent<UnityEngine.MeshRenderer>().enabled = false; Box("Lantern rod", root, new UnityEngine.Vector3(C.x, (73f + lanternTop) * .5f, C.z), new UnityEngine.Vector3(.4f, 73f - lanternTop, .4f), susp.GetComponent<UnityEngine.MeshRenderer>().sharedMaterial); }
// the night light (the spot onto the table) inside the globe, softer, and the glow that lights the ceiling round it stronger
var nl = lamp.Find("Night light"); if (nl) nl.position = new UnityEngine.Vector3(C.x, LanternY - 2f, C.z);
var glow = lamp.Find("Night lamp glow"); if (glow) { glow.position = new UnityEngine.Vector3(C.x, LanternY, C.z); var gl = glow.GetComponent<UnityEngine.Light>(); gl.range = 130f; }
var lmgr = UnityEngine.Object.FindAnyObjectByType<LightingManager>(); lmgr.nightLightIntensity = 6500f; UnityEditor.EditorUtility.SetDirty(lmgr);
log.Append($"globe lantern {lb.size.y:F1} tall, top {lanternTop:F1}; ");


// the mist over the map lives in the map prefab (Tools/mood_map_glows.eval.cs), so it comes and goes with the map
{ var oldMist = tm.transform.Find("Campaign map/Map mist"); if (oldMist) UnityEngine.Object.DestroyImmediate(oldMist.gameObject); var oldMist2 = root.Find("Map mist"); if (oldMist2) UnityEngine.Object.DestroyImmediate(oldMist2.gameObject); }
// the places' glows on the map are in the map prefab (Tools/mood_map_glows.eval.cs)



// ── The room ──────────────────────────────────────────────────────────
var roomPart = new UnityEngine.GameObject("Room").transform; roomPart.SetParent(root, false);
var newLights = new System.Collections.Generic.List<UnityEngine.Light>(newLightsEarly);
foreach (var l in root.GetComponentsInChildren<UnityEngine.Light>(true)) newLights.Add(l);   // the table's candles so far
// darker, cooler walls and ceiling: copies of the bedroom's plaster and ceiling (same textures, so the same texel size)
UnityEngine.Material Tinted(string src, string name, UnityEngine.Color c)
{
  var m = LoadMat($"{dir}/{name}.mat"); var sm = LoadMat(src);
  if (!m) { m = new UnityEngine.Material(sm); UnityEditor.AssetDatabase.CreateAsset(m, $"{dir}/{name}.mat"); }
  m.CopyPropertiesFromMaterial(sm); m.SetColor("_BaseColor", c); UnityEditor.EditorUtility.SetDirty(m); return m;
}
var plaster = Tinted("Assets/Game/World/Art/Bedroom/Bedroom plaster.mat", "Mood plaster", new UnityEngine.Color(.58f, .58f, .66f));
var ceiling = Tinted("Assets/Game/World/Art/Bedroom/Bedroom ceiling.mat", "Mood ceiling", new UnityEngine.Color(.42f, .42f, .5f));
foreach (var r in UnityEngine.GameObject.Find(Bed + "/Geometry").GetComponentsInChildren<UnityEngine.MeshRenderer>())
{
  var n = r.sharedMaterial ? r.sharedMaterial.name : "";
  if (n == "Bedroom plaster" || n == "Mood plaster") r.sharedMaterial = plaster; else if (n == "Bedroom ceiling" || n == "Mood ceiling") r.sharedMaterial = ceiling;
}
// dark ceiling beams running east to west (none over the lantern)
foreach (var z in new[] { 300f, 348f, 445f, 493f })
  Box("Ceiling beam", roomPart, new UnityEngine.Vector3(-370f, 71f, z), new UnityEngine.Vector3(203f, 4f, 4.5f), wood);
Box("Ceiling beam", roomPart, new UnityEngine.Vector3(-370f, 71.5f, 396.5f + 32f), new UnityEngine.Vector3(203f, 3f, 3f), wood);
Box("Ceiling beam", roomPart, new UnityEngine.Vector3(-370f, 71.5f, 396.5f - 32f), new UnityEngine.Vector3(203f, 3f, 3f), wood);

// the gothic window on the east wall, north of the painting, with moonlight coming in
var winWood = LoadMat($"{dir}/Window wood 6.4.mat"); if (!winWood) { winWood = new UnityEngine.Material(wood); UnityEditor.AssetDatabase.CreateAsset(winWood, $"{dir}/Window wood 6.4.mat"); }
winWood.CopyPropertiesFromMaterial(wood); var wtex = wood.GetTexture("_BaseMap"); winWood.SetTextureScale("_BaseMap", UnityEngine.Vector2.one * (6.4f / wtex.width)); UnityEditor.EditorUtility.SetDirty(winWood);
var night = LoadMat($"{dir}/Night glass Glow.mat"); if (!night) { night = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Lit")); UnityEditor.AssetDatabase.CreateAsset(night, $"{dir}/Night glass Glow.mat"); }
night.SetColor("_BaseColor", new UnityEngine.Color(.08f, .12f, .2f)); night.EnableKeyword("_EMISSION"); night.SetColor("_EmissionColor", new UnityEngine.Color(.3f, .45f, .85f)); night.SetFloat("_Smoothness", .7f); night.SetFloat("_Cull", 0); UnityEditor.EditorUtility.SetDirty(night);
UnityEditor.AssetDatabase.ImportAsset($"{dir}/Gothic window.fbx");
var winPf = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>($"{dir}/Gothic window.fbx");
const float WinZ = 471f, WallX = -268.2f;
var win = (UnityEngine.GameObject)UnityEngine.Object.Instantiate(winPf, roomPart); win.name = "Gothic window";
win.transform.position = new UnityEngine.Vector3(WallX, 10f, WinZ); win.transform.rotation = UnityEngine.Quaternion.Euler(0, -90, 0);   // z (into the room) turned to -x
foreach (var r in win.GetComponentsInChildren<UnityEngine.MeshRenderer>()) { r.sharedMaterial = r.name == "Glass" ? night : winWood; if (r.name == "Glass") r.enabled = false; }
// the night outside: a pixel picture (Tools/night_window.py) cut to the arch, glowing a little, just behind the frame
string ntp = $"{dir}/Night window.png"; UnityEditor.AssetDatabase.ImportAsset(ntp);
var nti = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(ntp);
if (nti.filterMode != UnityEngine.FilterMode.Point || nti.npotScale != UnityEditor.TextureImporterNPOTScale.None) { nti.filterMode = UnityEngine.FilterMode.Point; nti.npotScale = UnityEditor.TextureImporterNPOTScale.None; nti.mipmapEnabled = false; nti.alphaIsTransparency = true; nti.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed; nti.wrapMode = UnityEngine.TextureWrapMode.Clamp; nti.SaveAndReimport(); }
var ntex = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(ntp);
night.SetTexture("_BaseMap", ntex); night.SetColor("_BaseColor", UnityEngine.Color.white); night.SetTexture("_EmissionMap", ntex); night.SetColor("_EmissionColor", UnityEngine.Color.white * 1.1f);
night.SetFloat("_AlphaClip", 1); night.SetFloat("_Cutoff", .5f); night.EnableKeyword("_ALPHATEST_ON"); night.SetFloat("_Smoothness", 0f); UnityEditor.EditorUtility.SetDirty(night);
var nq = new UnityEngine.GameObject("Night outside"); nq.transform.SetParent(win.transform, false);
const float WW = 34f, WH = 56f, WFR = 2.2f; nq.transform.localPosition = new UnityEngine.Vector3(0, WH * .5f, .6f); nq.transform.localRotation = UnityEngine.Quaternion.Euler(0, 180, 0); nq.transform.localScale = new UnityEngine.Vector3(WW - 2 * WFR, WH - 2 * WFR, 1);
nq.AddComponent<UnityEngine.MeshFilter>().sharedMesh = quad; var nqr = nq.AddComponent<UnityEngine.MeshRenderer>(); nqr.sharedMaterial = night; nqr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
var moonGo = new UnityEngine.GameObject("Moonlight"); moonGo.transform.SetParent(win.transform, false); moonGo.transform.position = new UnityEngine.Vector3(WallX - 5f, 50f, WinZ);
moonGo.transform.LookAt(new UnityEngine.Vector3(-330f, 0f, 455f));
var moon = moonGo.AddComponent<UnityEngine.Light>(); moon.type = UnityEngine.LightType.Spot; moon.color = new UnityEngine.Color(.55f, .68f, 1f); moon.intensity = 6000f; moon.range = 170f; moon.spotAngle = 60f; moon.innerSpotAngle = 25f; moon.shadows = UnityEngine.LightShadows.Soft;
newLights.Add(moon);
var winGlow = PointLight("Window glow", win.transform, new UnityEngine.Vector3(WallX - 16f, 34f, WinZ), new UnityEngine.Color(.5f, .62f, 1f), 260f, 70f); newLights.Add(winGlow);
// the wall clock moves off the window's wall, onto the north wall over the wardrobe
var clock = fur.Find("Wall clock"); if (clock && clock.position.x > -275f) { clock.position = new UnityEngine.Vector3(-292f, 62f, 524.3f); clock.rotation = UnityEngine.Quaternion.Euler(0, -90, 0) * clock.rotation; }
// (no plants by the window: the painting and the wardrobe stand on either side of it)

// candle groups about the room, each with its own small warm light (as in the photo: on the desk, the shelves, the floor)
var roomCandles = new UnityEngine.GameObject("Room candles").transform; roomCandles.SetParent(roomPart, false);
foreach (var (pos, yaw) in new[] {
  (new UnityEngine.Vector3(-455f, 24.2f, 400f), 10f),     // on the west cabinet
  (new UnityEngine.Vector3(-414f, 17.2f, 516f), 40f),     // on the bedside table
  (new UnityEngine.Vector3(-330f, 24.4f, 284f), -20f),    // on the hobby table, by the sheet
  (new UnityEngine.Vector3(-420f, 24.4f, 284f), 60f),     // on the hobby table's other end
  (new UnityEngine.Vector3(-278f, 1f, 515f), 0f),         // on the floor under the window
  (new UnityEngine.Vector3(-466f, 1f, 470f), 80f) })      // on the floor in the north-west corner
{
  var cl = (UnityEngine.GameObject)UnityEngine.Object.Instantiate(clusterPf, roomCandles); cl.name = "Candles"; cl.transform.position = pos; cl.transform.localScale = UnityEngine.Vector3.one * 20f; cl.transform.rotation = UnityEngine.Quaternion.Euler(0, yaw, 0);
  var drop2 = new System.Collections.Generic.List<UnityEngine.GameObject>(); foreach (UnityEngine.Transform c in cl.transform) { var mf2 = c.GetComponent<UnityEngine.MeshFilter>(); if (mf2 && mf2.sharedMesh.name.Contains("Candle_03")) drop2.Add(c.gameObject); }
  foreach (var d in drop2) UnityEngine.Object.DestroyImmediate(d);
  foreach (UnityEngine.Transform c in cl.transform) if (c.GetComponent<UnityEngine.MeshRenderer>()) c.localScale = UnityEngine.Vector3.one;   // the bedroom's texel size
  var cll = cl.GetComponentInChildren<UnityEngine.Light>(); if (cll) { cll.range = 34f; cll.intensity = 26f; cll.shadows = UnityEngine.LightShadows.None; newLights.Add(cll); }
}
// a cool fill over the whole room: the shadows go blue-grey, as in the photo, while lamps and candles stay warm
var fill = PointLight("Cool night fill", roomPart, new UnityEngine.Vector3(-370f, 64f, 400f), new UnityEngine.Color(.42f, .52f, .85f), 120f, 230f); newLights.Add(fill);
// a big patterned rug under the table: the room's restyled rug, four strips side by side
var rugs = new UnityEngine.GameObject("Table rug").transform; rugs.SetParent(roomPart, false);
for (int k = 0; k < 4; k++) Prop("Rug", rugs, "Props/SM_Prop_Rug_02/SM_Prop_Rug_02 painted", new UnityEngine.Vector3(C.x - 40.5f + k * 27f, 1.05f, C.z), 0, 25.7f);
// (the bookcase that stood by the hobby table stays switched off: by the window it would stand in the memorial table)
var library = fur.Find("Reading Corner and Lighting/Campaign library"); var framed = fur.Find("Reading Corner and Lighting/Framed abstract - 1");
if (library && library.gameObject.activeSelf) { library.gameObject.SetActive(false); if (framed) framed.gameObject.SetActive(true); }
// dark wood panelling to dado height on the bare walls (the trophy wall has its own), and a rail along its top
var panels = new UnityEngine.GameObject("Wall panelling").transform; panels.SetParent(roomPart, false);
const float PanH = 22f, PanT = 2.2f;
void PanelX(float x0, float x1, float zFace, float dir)   // along a north or south wall; dir: +1 the room is at larger z
{ float zc = zFace + dir * PanT * .5f; Box("Panel", panels, new UnityEngine.Vector3((x0 + x1) * .5f, 1f + PanH * .5f, zc), new UnityEngine.Vector3(x1 - x0, PanH, PanT), wood);
  Box("Dado rail", panels, new UnityEngine.Vector3((x0 + x1) * .5f, 1f + PanH + .7f, zFace + dir * (PanT + .6f) * .5f), new UnityEngine.Vector3(x1 - x0, 1.4f, PanT + .6f), wood); }
void PanelZ(float z0, float z1, float xFace, float dir)   // along an east or west wall; dir: +1 the room is at larger x
{ float xc = xFace + dir * PanT * .5f; Box("Panel", panels, new UnityEngine.Vector3(xc, 1f + PanH * .5f, (z0 + z1) * .5f), new UnityEngine.Vector3(PanT, PanH, z1 - z0), wood, true);
  Box("Dado rail", panels, new UnityEngine.Vector3(xFace + dir * (PanT + .6f) * .5f, 1f + PanH + .7f, (z0 + z1) * .5f), new UnityEngine.Vector3(PanT + .6f, 1.4f, z1 - z0), wood, true); }
PanelX(-472f, -268f, 525f, -1f);                 // north wall
PanelZ(274f, 306f, -472f, 1f); PanelZ(334f, 525f, -472f, 1f);   // west wall, either side of the hall doorway
PanelZ(274f, 306f, -268f, -1f);                  // east wall south of its door (the painting and the window take the rest)

// a writing desk against the west wall with maps pinned over it, a lantern, candles and an open book (the photo's
// desk), and the chair drawn up to it
var desk = new UnityEngine.GameObject("Writing desk").transform; desk.SetParent(roomPart, false);
const float DeskZ = 440f, DeskTop = 16f, DeskW = 32f, DeskD = 13f; float deskX = -472f + PanT + DeskD * .5f;
Box("Desk top", desk, new UnityEngine.Vector3(deskX, DeskTop - .7f, DeskZ), new UnityEngine.Vector3(DeskD, 1.4f, DeskW), wood, true);
foreach (var (dx, dz) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) }) Box("Desk leg", desk, new UnityEngine.Vector3(deskX + dx * (DeskD * .5f - 1.2f), (DeskTop - 1.4f) * .5f, DeskZ + dz * (DeskW * .5f - 1.2f)), new UnityEngine.Vector3(2f, DeskTop - 1.4f, 2f), wood);
Box("Desk apron", desk, new UnityEngine.Vector3(deskX, DeskTop - 2.6f, DeskZ), new UnityEngine.Vector3(DeskD - 1f, 2.4f, DeskW - 1f), wood, true);
var deskLantern = Prop("Lantern", desk, "Props/SM_Prop_Lantern_01/SM_Prop_Lantern_01", new UnityEngine.Vector3(deskX - 1f, DeskTop, DeskZ - 11f), 20f, 17.7f);
var dl = PointLight("Lantern light", deskLantern.transform, deskLantern.transform.position + UnityEngine.Vector3.up * 6f, new UnityEngine.Color(1f, .64f, .32f), 40f, 40f); newLights.Add(dl);
Prop("Open book", desk, "Room/SM_Prop_BookOpen_01/SM_Prop_BookOpen_01 painted", new UnityEngine.Vector3(deskX + 1f, DeskTop, DeskZ + 1f), 75f, 19.94f);
Prop("Books", desk, "Room/SM_Prop_Book_Group_01/SM_Prop_Book_Group_01 painted", new UnityEngine.Vector3(deskX - 2f, DeskTop, DeskZ + 11f), 90f, 20.04f);
var dc = (UnityEngine.GameObject)UnityEngine.Object.Instantiate(clusterPf, desk); dc.name = "Candles"; dc.transform.position = new UnityEngine.Vector3(deskX + 2f, DeskTop, DeskZ + 6f); dc.transform.localScale = UnityEngine.Vector3.one * 20f;
{ var drop3 = new System.Collections.Generic.List<UnityEngine.GameObject>(); foreach (UnityEngine.Transform c in dc.transform) { var mf2 = c.GetComponent<UnityEngine.MeshFilter>(); if (mf2 && mf2.sharedMesh.name.Contains("Candle_03")) drop3.Add(c.gameObject); }
  foreach (var d in drop3) UnityEngine.Object.DestroyImmediate(d); foreach (UnityEngine.Transform c in dc.transform) if (c.GetComponent<UnityEngine.MeshRenderer>()) c.localScale = UnityEngine.Vector3.one;
  var dcl = dc.GetComponentInChildren<UnityEngine.Light>(); if (dcl) { dcl.range = 30f; dcl.intensity = 20f; dcl.shadows = UnityEngine.LightShadows.None; newLights.Add(dcl); } }
// a board of pinned maps over the desk
float boardX = -472f + PanT * .5f + .3f;
Box("Map board", desk, new UnityEngine.Vector3(-472f + .6f, 38f, DeskZ), new UnityEngine.Vector3(1.2f, 22f, 38f), wood, true);
foreach (var (y, z, rot) in new[] { (42f, DeskZ - 9f, 2f), (35f, DeskZ + 8f, -4f), (44f, DeskZ + 10f, 6f), (33f, DeskZ - 10f, -3f) })
{ var mp = Prop("Pinned map", desk, "Room/SM_Prop_Map_01/SM_Prop_Map_01", new UnityEngine.Vector3(-472f + 1.35f, y, z), 0, 14.17f);
  mp.transform.rotation = UnityEngine.Quaternion.Euler(0, 0, -90) * UnityEngine.Quaternion.Euler(0, 90 + rot, 0); mp.transform.localScale = UnityEngine.Vector3.one * (z > DeskZ ? 11f : 14.17f); }
var chair = fur.Find("SM_Prop_Chair_Wood_03"); if (chair) { chair.position = new UnityEngine.Vector3(deskX + 11f, chair.position.y, DeskZ - 2f); chair.rotation = UnityEngine.Quaternion.Euler(0, -90, 0); }
// the standing reading lamp by the bed goes (the photo has no floor lamps; the bookcase stands there now)
var readLamp = fur.Find("Reading Corner and Lighting/Hobby reading lamp"); if (readLamp) readLamp.gameObject.SetActive(false);
// a bookcase against the north wall between the bedside table and the memorial table
var shelf = Prop("Bookcase", roomPart, "Props/SM_Prop_Bookshelf_01/SM_Prop_Bookshelf_01 painted", UnityEngine.Vector3.zero, 180f, 16f);
{ var sb = shelf.GetComponent<UnityEngine.Renderer>().bounds; shelf.transform.position += new UnityEngine.Vector3(-385.5f - sb.center.x, 1f - sb.min.y, 525f - PanT - .2f - sb.max.z); }
// books on its shelves (boards at .22, .76, 1.26, 1.76 and 2.26 of the mesh's height units, x16) and a candle on one
{ var sb = shelf.GetComponent<UnityEngine.Renderer>().bounds; var rngB = new System.Random(3); float baseY = sb.min.y;
  foreach (var (board, k) in new[] { (.22f, 0), (.76f, 1), (1.26f, 2), (1.76f, 3) })
  {
    float y = baseY + board * 16f + .05f;
    foreach (var side in new[] { -1f, 1f })
    {
      if (k == 3 && side > 0) continue;   // the candle stands there
      float x = sb.center.x + side * (3.6f + (float)rngB.NextDouble() * 1.4f);
      Prop("Books", shelf.transform.parent, "Room/SM_Prop_Book_Group_01/SM_Prop_Book_Group_01 painted", new UnityEngine.Vector3(x, y, sb.center.z + .4f), 180f + (float)rngB.NextDouble() * 10f - 5f, 20.04f);
    }
  }
  var sc = (UnityEngine.GameObject)UnityEngine.Object.Instantiate(clusterPf, shelf.transform.parent); sc.name = "Shelf candles"; sc.transform.position = new UnityEngine.Vector3(sb.center.x + 4.5f, baseY + 1.76f * 16f + .05f, sb.center.z); sc.transform.localScale = UnityEngine.Vector3.one * 20f;
  var drop4 = new System.Collections.Generic.List<UnityEngine.GameObject>(); foreach (UnityEngine.Transform c in sc.transform) { var mf2 = c.GetComponent<UnityEngine.MeshFilter>(); if (mf2 && mf2.sharedMesh.name.Contains("Candle_03")) drop4.Add(c.gameObject); }
  foreach (var d in drop4) UnityEngine.Object.DestroyImmediate(d); foreach (UnityEngine.Transform c in sc.transform) if (c.GetComponent<UnityEngine.MeshRenderer>()) c.localScale = UnityEngine.Vector3.one;
  var scl = sc.GetComponentInChildren<UnityEngine.Light>(); if (scl) { scl.range = 30f; scl.intensity = 22f; scl.shadows = UnityEngine.LightShadows.None; newLights.Add(scl); } }
// the room's own lamps much dimmer: the light comes from the lantern, the candles and the window
var lm = UnityEngine.Object.FindAnyObjectByType<LightingManager>(); var bedT = UnityEngine.GameObject.Find(Bed).transform;
var dim = new System.Collections.Generic.Dictionary<string, float> { { "Hobby reading pool", 110f }, { "Bedside amber pool", 55f }, { "Bed warm fill", 35f }, { "Bedroom daylight", 0f }, { "Reading amber pool", 60f }, { "Fill -40", 160f }, { "Fill 40", 160f }, { "Night lamp glow", 900f } };
var list = new System.Collections.Generic.List<LightingManager.Source>();
foreach (var src in lm.sceneSources)
{
  if (src == null || src.light == null) continue;
  if (src.light.transform.IsChildOf(root) || src.light.name == "Mood place glow") continue;   // ours are added again below
  if (src.light.transform.IsChildOf(bedT) && dim.TryGetValue(src.light.name, out var v)) src.baseIntensity = v;
  list.Add(src);
}
foreach (var l in newLights) list.Add(new LightingManager.Source { light = l, baseIntensity = l.intensity, baseColor = l.color });
lm.sceneSources = list.ToArray(); lm.roomAmbient = .65f; UnityEditor.EditorUtility.SetDirty(lm);
log.Append($"room: walls, beams, window, {newLights.Count} lights registered; ");
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(fur.gameObject.scene); UnityEditor.AssetDatabase.SaveAssets();
return log.ToString();
