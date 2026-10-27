// The Forest1 biome's props (Levels/Dungeon1/Forest/Props): pixel copies of the Synty Enchanted Forest models at the dungeon
// world's 128 texels a metre, one prefab each, no colliders (nothing in the woods is walked into but the banks).
//  - Solid models on the pack's colour atlas: PixelRestyle.Build (fresh UVs, the atlas colour per texel, crease AO, grain),
//    measured and baked again until within 5%. The atlas's glow sheet comes along as an emission map (glowing mushrooms).
//  - Trees and ferns: the trunk part (vertex colour blue 0, on the atlas) restyled as above; the leaf cards (blue 1) keep
//    their UVs and get the pack's leaf texture box-filtered down to 128 texels a metre with hard alpha, point-filtered.
//  - Earth-covered models (moss lumps): restyled with their colour read from the pack's triplanar textures.
// Safe to run again: everything is rebuilt. Writes Temp/Forest/props.txt. Run in edit mode.
if (UnityEngine.Application.isPlaying) return "playing";
const string D = "Assets/Game/Levels/Dungeon1/Forest/Props";
const string S = "Assets/Synty/PolygonNatureBiomes/PNB_Enchanted_Forest/";
const float TPU = 128f;
var sb = new System.Text.StringBuilder();
var leafCache = new System.Collections.Generic.Dictionary<string, UnityEngine.Material>();
void Folder(string path) { var parts = path.Split('/'); var cur = parts[0]; for (int i = 1; i < parts.Length; i++) { var next = cur + "/" + parts[i]; if (!UnityEditor.AssetDatabase.IsValidFolder(next)) UnityEditor.AssetDatabase.CreateFolder(cur, parts[i]); cur = next; } }
Folder(D);

UnityEngine.Texture2D Readable(UnityEngine.Texture t)
{
  var rt = UnityEngine.RenderTexture.GetTemporary(t.width, t.height, 0, UnityEngine.RenderTextureFormat.ARGB32, UnityEngine.RenderTextureReadWrite.sRGB);
  UnityEngine.Graphics.Blit(t, rt);
  var prev = UnityEngine.RenderTexture.active; UnityEngine.RenderTexture.active = rt;
  var tex = new UnityEngine.Texture2D(t.width, t.height, UnityEngine.TextureFormat.RGBA32, false);
  tex.ReadPixels(new UnityEngine.Rect(0, 0, t.width, t.height), 0, 0); tex.Apply();
  UnityEngine.RenderTexture.active = prev; UnityEngine.RenderTexture.ReleaseTemporary(rt);
  return tex;
}
UnityEngine.Texture Tex(UnityEngine.Material m, params string[] props) { foreach (var p in props) if (m.HasProperty(p) && m.GetTexture(p) != null) return m.GetTexture(p); return null; }

// A stand-in material PixelRestyle can read: the Synty shader's colour (and glow) sheet as _BaseMap (_EmissionMap).
UnityEngine.Material Readable_(UnityEngine.Material m)
{
  var copy = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Lit")) { name = m.name };
  copy.SetTexture("_BaseMap", Tex(m, "_Base_Texture", "_Albedo_Map", "_BaseMap", "_Trunk_Texture"));
  copy.SetColor("_BaseColor", UnityEngine.Color.white);
  bool glow = (m.HasProperty("_Enable_Emission_Texture") && m.GetFloat("_Enable_Emission_Texture") > .5f) || (m.HasProperty("_Enable_Emission") && m.GetFloat("_Enable_Emission") > .5f);
  var e = Tex(m, "_Emission_Texture", "_Emission_Map");
  if (glow && e != null) { copy.SetTexture("_EmissionMap", e); copy.SetColor("_EmissionColor", UnityEngine.Color.white); copy.EnableKeyword("_EMISSION"); }
  return copy;
}

UnityEngine.Mesh Subset(UnityEngine.Mesh src, System.Func<UnityEngine.Color, bool> keep, string name)
{
  var cols = src.colors; var tri = src.triangles; var list = new System.Collections.Generic.List<int>();
  for (int i = 0; i < tri.Length; i += 3)
  {
    bool k = cols.Length == 0 ? keep(UnityEngine.Color.white) : keep((cols[tri[i]] + cols[tri[i + 1]] + cols[tri[i + 2]]) / 3f);
    if (k) { list.Add(tri[i]); list.Add(tri[i + 1]); list.Add(tri[i + 2]); }
  }
  if (list.Count == 0) return null;
  // Only the vertices used, so the unwrap and the AO probe see this part alone.
  var map = new System.Collections.Generic.Dictionary<int, int>(); var v = src.vertices; var n = src.normals; var uv = src.uv; var cc = cols;
  var nv = new System.Collections.Generic.List<UnityEngine.Vector3>(); var nn = new System.Collections.Generic.List<UnityEngine.Vector3>(); var nu = new System.Collections.Generic.List<UnityEngine.Vector2>(); var nc = new System.Collections.Generic.List<UnityEngine.Color>();
  var nt = new int[list.Count];
  for (int i = 0; i < list.Count; i++)
  {
    int o = list[i];
    if (!map.TryGetValue(o, out int idx)) { idx = nv.Count; map[o] = idx; nv.Add(v[o]); if (n.Length > 0) nn.Add(n[o]); if (uv.Length > 0) nu.Add(uv[o]); if (cc.Length > 0) nc.Add(cc[o]); }
    nt[i] = idx;
  }
  var m = new UnityEngine.Mesh { name = name, indexFormat = nv.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
  m.SetVertices(nv); if (nn.Count > 0) m.SetNormals(nn); if (nu.Count > 0) m.SetUVs(0, nu); if (nc.Count > 0) m.SetColors(nc);
  m.SetTriangles(nt, 0); m.RecalculateBounds(); if (nn.Count == 0) m.RecalculateNormals();
  return m;
}

// Restyled at TPU texels a world metre for a model placed at `scale`: baked, measured on a probe, baked again.
(UnityEngine.Mesh, UnityEngine.Material) Restyle(UnityEngine.Mesh mesh, UnityEngine.Material[] mats, string name, string dir, float scale)
{
  var probe = new UnityEngine.GameObject("Texel probe") { hideFlags = UnityEngine.HideFlags.HideAndDontSave };
  probe.transform.localScale = UnityEngine.Vector3.one * scale;
  var pf = probe.AddComponent<UnityEngine.MeshFilter>(); var pr = probe.AddComponent<UnityEngine.MeshRenderer>();
  float aim = TPU * scale, measured = 0; UnityEngine.Mesh outMesh = null; UnityEngine.Material outMat = null;
  for (int pass = 0; pass < 4; pass++)
  {
    // Old copies go first: a restyle written over an existing mesh asset keeps stale vertices.
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

// A leaf card material: the pack's leaf texture box-filtered to TPU texels a metre on these cards at this scale, its
// alpha made hard (a leaf is there or not), point-filtered, both sides drawn.
UnityEngine.Material Leaves(UnityEngine.Mesh cards, UnityEngine.Texture leafTex, float scale, string name, string dir, float cutoff)
{
  var v = cards.vertices; var uv = cards.uv; var t = cards.triangles;
  double world = 0, uvA = 0;
  for (int i = 0; i < t.Length; i += 3)
  {
    world += UnityEngine.Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).magnitude * .5 * scale * scale;
    var a = uv[t[i]]; var b = uv[t[i + 1]]; var c = uv[t[i + 2]];
    uvA += System.Math.Abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y)) * .5;
  }
  float density = (float)System.Math.Sqrt(uvA * leafTex.width * leafTex.height / System.Math.Max(world, 1e-6));
  int size = UnityEngine.Mathf.Clamp(UnityEngine.Mathf.RoundToInt(leafTex.width * TPU / density), 8, leafTex.width);
  string key = $"{leafTex.name}@{size}";
  if (leafCache.TryGetValue(key, out var cached)) { sb.Append($"  {name}: cards {density:F0} texels a metre, shared {key}\n"); return cached; }
  var src = Readable(leafTex); var px = src.GetPixels(); int W = src.width;
  var outPx = new UnityEngine.Color[size * size];
  float step = (float)W / size;
  var rng = new System.Random(leafTex.name.GetHashCode());
  for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
  {
    int x0 = (int)(x * step), x1 = UnityEngine.Mathf.Max(x0 + 1, (int)((x + 1) * step)), y0 = (int)(y * step), y1 = UnityEngine.Mathf.Max(y0 + 1, (int)((y + 1) * step));
    float r = 0, g = 0, b = 0, a = 0; int n = 0;
    for (int yy = y0; yy < y1; yy++) for (int xx = x0; xx < x1; xx++) { var c = px[yy * W + xx]; r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a; n++; }
    var col = a > 0 ? new UnityEngine.Color(r / a, g / a, b / a, 1) : new UnityEngine.Color(0, 0, 0, 0);
    // Muted a little and varied texel to texel, as the house style asks.
    float jitter = 1f + ((float)rng.NextDouble() - .5f) * .12f;
    // The teal leaves are pure cyan in the pack and read as glaring blue under the forest's blue light: muted harder.
    float grey = col.grayscale; col = UnityEngine.Color.Lerp(col, new UnityEngine.Color(grey, grey * 1.08f, grey * 1.02f), name.Contains("teal") ? .5f : .12f) * jitter;
    col.a = a / UnityEngine.Mathf.Max(1, n) >= cutoff ? 1f : 0f;
    outPx[y * size + x] = col;
  }
  UnityEngine.Object.DestroyImmediate(src);
  // Bleed the leaf colour into the cut-away texels so mipmaps never fringe in black.
  for (int pass = 0; pass < 3; pass++)
  {
    var copy = (UnityEngine.Color[])outPx.Clone();
    for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
    {
      if (outPx[y * size + x].a > 0 || outPx[y * size + x].r + outPx[y * size + x].g + outPx[y * size + x].b > 0) continue;
      UnityEngine.Color s = UnityEngine.Color.clear; int k = 0;
      for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) { int xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= size || yy >= size) continue; var c = outPx[yy * size + xx]; if (c.r + c.g + c.b > 0) { s += c; k++; } }
      if (k > 0) { s /= k; s.a = 0; copy[y * size + x] = s; }
    }
    outPx = copy;
  }
  var tex = new UnityEngine.Texture2D(size, size, UnityEngine.TextureFormat.RGBA32, false); tex.SetPixels(outPx); tex.Apply();
  string png = $"{dir}/{name} leaves.png";
  System.IO.File.WriteAllBytes(png, tex.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(tex);
  UnityEditor.AssetDatabase.ImportAsset(png, UnityEditor.ImportAssetOptions.ForceUpdate);
  var imp = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(png);
  imp.textureType = UnityEditor.TextureImporterType.Default; imp.filterMode = UnityEngine.FilterMode.Point; imp.npotScale = UnityEditor.TextureImporterNPOTScale.None;
  imp.alphaIsTransparency = true; imp.mipmapEnabled = true; imp.mipMapsPreserveCoverage = true; imp.alphaTestReferenceValue = .5f;
  imp.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed; imp.wrapMode = UnityEngine.TextureWrapMode.Clamp;
  imp.SaveAndReimport();
  var matPath = $"{dir}/{name} leaves.mat";
  var mat = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Lit"));
  mat.SetTexture("_BaseMap", UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(png));
  mat.SetColor("_BaseColor", UnityEngine.Color.white);
  mat.SetFloat("_AlphaClip", 1f); mat.SetFloat("_Cutoff", .5f); mat.EnableKeyword("_ALPHATEST_ON");
  mat.SetFloat("_Cull", 0f); mat.SetFloat("_Smoothness", 0f); mat.SetFloat("_SpecularHighlights", 0f); mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
  mat.renderQueue = 2450; mat.enableInstancing = true;
  UnityEditor.AssetDatabase.DeleteAsset(matPath); UnityEditor.AssetDatabase.CreateAsset(mat, matPath);
  leafCache[key] = mat;
  sb.Append($"  {name}: cards {density:F0} texels a metre -> {size} px\n");
  return mat;
}

// The pack's prefab of that name, wherever it sits under Prefabs.
UnityEngine.GameObject Source(string n) { foreach (var g in UnityEditor.AssetDatabase.FindAssets(n + " t:Prefab", new[] { S + "Prefabs" })) { var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g); if (System.IO.Path.GetFileNameWithoutExtension(p) == n) return UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(p); } return null; }
UnityEngine.Mesh FirstMesh(UnityEngine.GameObject prefab, out UnityEngine.Material[] mats)
{
  var lod = prefab.GetComponent<UnityEngine.LODGroup>();
  UnityEngine.Renderer r = null;
  if (lod != null && lod.GetLODs().Length > 0) foreach (var x in lod.GetLODs()[0].renderers) if (x != null && x.GetComponent<UnityEngine.MeshFilter>() != null) { r = x; break; }
  if (r == null) r = prefab.GetComponentInChildren<UnityEngine.MeshRenderer>();
  mats = r.sharedMaterials;
  var mesh = UnityEngine.Object.Instantiate(r.GetComponent<UnityEngine.MeshFilter>().sharedMesh);
  // Baked into the prefab's own pose (Synty roots are rotated and scaled on their child).
  var m = prefab.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
  var v = mesh.vertices; var n = mesh.normals;
  for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]);
  for (int i = 0; i < n.Length; i++) n[i] = m.MultiplyVector(n[i]).normalized;
  mesh.vertices = v; if (n.Length > 0) mesh.normals = n; mesh.RecalculateBounds();
  return mesh;
}

void Save(UnityEngine.GameObject root, string name, string dir, float scale)
{
  root.transform.localScale = UnityEngine.Vector3.one * scale;
  foreach (var c in root.GetComponentsInChildren<UnityEngine.Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
  UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, $"{dir}/{name}.prefab");
  UnityEngine.Object.DestroyImmediate(root);
}
UnityEngine.GameObject Part(UnityEngine.Transform parent, string name, UnityEngine.Mesh mesh, UnityEngine.Material mat)
{
  var go = new UnityEngine.GameObject(name); go.transform.SetParent(parent, false);
  go.AddComponent<UnityEngine.MeshFilter>().sharedMesh = mesh; var r = go.AddComponent<UnityEngine.MeshRenderer>(); r.sharedMaterial = mat;
  return go;
}

// A tree or fern: trunk restyled, leaves as pixel cards in one or more leaf colours (one prefab per colour).
void Plant(string source, string name, float scale, (string suffix, string leafMat)[] colours)
{
  var dir = $"{D}/{name}"; Folder(dir);
  var prefab = Source(source);
  var mesh = FirstMesh(prefab, out var mats);
  var trunk = Subset(mesh, c => c.b < .5f, name + " trunk");
  var cards = Subset(mesh, c => c.b >= .5f, name + " leaves");
  UnityEngine.Mesh trunkMesh = null; UnityEngine.Material trunkMat = null;
  if (trunk != null) (trunkMesh, trunkMat) = Restyle(trunk, new[] { Readable_(mats[0]) }, name + " trunk", dir, scale);
  UnityEngine.Mesh cardMesh = null;
  if (cards != null)
  {
    UnityEditor.AssetDatabase.DeleteAsset($"{dir}/{name} cards.asset");
    UnityEditor.AssetDatabase.CreateAsset(cards, $"{dir}/{name} cards.asset"); PixelRestyle.MarkReadable(cards); cardMesh = cards;
  }
  foreach (var (suffix, leafMat) in colours)
  {
    var root = new UnityEngine.GameObject(name + suffix);
    if (trunkMesh != null) Part(root.transform, "Trunk", trunkMesh, trunkMat);
    if (cardMesh != null)
    {
      var srcMat = leafMat != null ? UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>($"{S}Materials/{leafMat}.mat") : mats[0];
      var lm = Leaves(cardMesh, srcMat.GetTexture("_Leaf_Texture"), scale, name + suffix, dir, srcMat.HasProperty("_Alpha_Clip_Threshold") ? UnityEngine.Mathf.Max(.25f, srcMat.GetFloat("_Alpha_Clip_Threshold")) : .5f);
      var leaves = Part(root.transform, "Leaves", cardMesh, lm);
      leaves.GetComponent<UnityEngine.MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
    }
    Save(root, name + suffix, dir, scale);
    sb.Append($"{name}{suffix} done\n");
  }
}

// A solid model on the pack's atlas.
void Solid(string source, string name, float scale)
{
  var dir = $"{D}/{name}"; Folder(dir);
  var prefab = Source(source);
  var mesh = FirstMesh(prefab, out var mats);
  var readable = new UnityEngine.Material[mats.Length]; for (int i = 0; i < mats.Length; i++) readable[i] = Readable_(mats[i]);
  var (m, mat) = Restyle(mesh, readable, name, dir, scale);
  var root = new UnityEngine.GameObject(name); Part(root.transform, "Model", m, mat);
  Save(root, name, dir, scale);
  sb.Append($"{name} done\n");
}

// Earth and moss: the triplanar textures read by position and facing.
void Triplanar(string source, string name, float scale)
{
  var dir = $"{D}/{name}"; Folder(dir);
  var prefab = Source(source);
  var mesh = FirstMesh(prefab, out var mats);
  var m0 = mats[0];
  var top = Readable(m0.GetTexture("_Triplanar_Texture_Top")); var side = Readable(m0.GetTexture("_Triplanar_Texture_Side"));
  float tiling = m0.HasProperty("_Tiling") ? m0.GetFloat("_Tiling") : .5f;
  UnityEngine.Color Read(UnityEngine.Texture2D t, float u, float v) => t.GetPixelBilinear(u - UnityEngine.Mathf.Floor(u), v - UnityEngine.Mathf.Floor(v));
  var f = typeof(PixelRestyle).GetField("PaintSource", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
  System.Func<UnityEngine.Vector3, UnityEngine.Vector3, UnityEngine.Color> paint = (p, n) =>
  {
    var w = new UnityEngine.Vector3(UnityEngine.Mathf.Abs(n.x), UnityEngine.Mathf.Abs(n.y), UnityEngine.Mathf.Abs(n.z)); w = w / (w.x + w.y + w.z);
    var q = p * scale * tiling;
    var c = Read(side, q.z, q.y) * w.x + (n.y > 0 ? Read(top, q.x, q.z) : Read(side, q.x, q.z)) * w.y + Read(side, q.x, q.y) * w.z;
    c.a = 1; return c;
  };
  f.SetValue(null, paint);
  try
  {
    var (m, mat) = Restyle(mesh, new[] { Readable_(m0) }, name, dir, scale);
    var root = new UnityEngine.GameObject(name); Part(root.transform, "Model", m, mat);
    Save(root, name, dir, scale);
  }
  finally { f.SetValue(null, null); UnityEngine.Object.DestroyImmediate(top); UnityEngine.Object.DestroyImmediate(side); }
  sb.Append($"{name} done\n");
}

var only = "";   // names to rebuild alone (comma separated); empty builds all
bool Want(string n) => string.IsNullOrEmpty(only) || System.Array.IndexOf(only.Split(','), n) >= 0;
var colours4 = new[] { (" green", "Plants/EnchantedTree_Mat_01a"), (" gold", "Plants/EnchantedTree_Mat_01b"), (" red", "Plants/EnchantedTree_Mat_01c"), (" teal", "Plants/EnchantedTree_Mat_01d") };
try
{
  if (Want("Tree small 1")) Plant("SM_Env_Tree_Small_01", "Tree small 1", 1f, colours4);
  if (Want("Tree small 2")) Plant("SM_Env_Tree_Small_02", "Tree small 2", 1f, colours4);
  if (Want("Tree medium 1")) Plant("SM_Env_Tree_Medium_01", "Tree medium 1", .62f, colours4);
  if (Want("Tree medium 2")) Plant("SM_Env_Tree_Medium_02", "Tree medium 2", .62f, colours4);
  if (Want("Fern tree 1")) Plant("SM_Env_Fern_Tree_01", "Fern tree 1", .9f, new[] { ("", (string)null) });
  if (Want("Fern tree 2")) Plant("SM_Env_Fern_Tree_02", "Fern tree 2", .9f, new[] { ("", (string)null) });
  if (Want("Fern 1")) Plant("SM_Env_Fern_01", "Fern 1", .8f, new[] { ("", (string)null) });
  if (Want("Fern 2")) Plant("SM_Env_Fern_02", "Fern 2", .8f, new[] { ("", (string)null) });
  if (Want("Fern 3")) Plant("SM_Env_Fern_03", "Fern 3", .8f, new[] { ("", (string)null) });
  foreach (var i in new[] { 5, 6, 7, 8, 9, 10 }) if (Want($"Undergrowth {i}")) Plant($"SM_Env_Undergrowth_Fern_{i:00}", $"Undergrowth {i}", 1.4f, new[] { ("", (string)null) });
  foreach (var i in new[] { 1, 2, 3 }) if (Want($"Fallen leaves {i}")) Plant($"SM_Env_Leaves_{i:00}", $"Fallen leaves {i}", 1f, new[] { ("", (string)null) });
  foreach (var i in new[] { 1, 2, 3, 4, 5, 6 }) if (Want($"Mushrooms {i}")) Solid($"SM_Env_Mushroom_Small_Group_{i:00}", $"Mushrooms {i}", 1f);
  foreach (var i in new[] { 1, 2, 4, 5 }) if (Want($"Giant mushroom {i}")) Solid($"SM_Env_Mushroom_{i:00}", $"Giant mushroom {i}", .45f);
  foreach (var i in new[] { 1, 3, 4 }) if (Want($"Log {i}")) Solid($"SM_Env_Log_{i:00}", $"Log {i}", i == 1 ? .4f : .6f);
  foreach (var i in new[] { 1, 2, 3, 4, 5, 6 }) if (Want($"Roots {i}")) Solid($"SM_Env_Roots_{i:00}", $"Roots {i}", .7f);
  foreach (var i in new[] { 1, 2, 3 }) if (Want($"Root cluster {i}")) Solid($"SM_Env_Roots_Cluster_{i:00}", $"Root cluster {i}", .7f);
  foreach (var i in new[] { 1, 2 }) if (Want($"Low roots {i}")) Solid($"SM_Env_Roots_Low_{i:00}", $"Low roots {i}", .8f);
  foreach (var i in new[] { 5, 3 }) if (Want($"Pebbles {i}")) Solid($"SM_Env_Rock_Pebbles_{i:00}", $"Pebbles {i}", 1f);
  foreach (var i in new[] { 2, 3 }) if (Want($"Ruin {i}")) Solid($"SM_Prop_Ruins_{i:00}", $"Ruin {i}", .45f);
  foreach (var i in new[] { 2, 4 }) if (Want($"Branch {i}")) Solid($"SM_Env_Branch_{i:00}", $"Branch {i}", 1f);
  if (Want("Lantern stand")) Solid("SM_Prop_Lantern_Stand_01", "Lantern stand", 1f);
  foreach (var i in new[] { 1, 2, 3, 4 }) if (Want($"Moss {i}")) Triplanar($"SM_Env_Moss_Lumps_{i:00}", $"Moss {i}", .7f);
  // Earth and rock formations about 3 m high: set into the banks' faces and on mounds in the clearings.
  foreach (var i in new[] { 1, 2, 5, 9, 12 }) if (Want($"Formation {i}")) Triplanar($"SM_Env_Dirt_Cliff_{i:00}", $"Formation {i}", .28f);
}
catch (System.Exception e) { sb.Append("ERR " + e + "\n"); }
UnityEditor.AssetDatabase.SaveAssets();
System.IO.Directory.CreateDirectory("Temp/Forest");
System.IO.File.WriteAllText("Temp/Forest/props.txt", sb.ToString());
return "ok";
