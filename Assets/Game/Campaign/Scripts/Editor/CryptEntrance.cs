using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Looking at the Crypt's entrance on the realm map (built by CryptEntrance.Build, the other part of this class). Run
// from an eval: Show() puts an unsaved copy of the realm map on the table where the game puts it, Capture(path, ...)
// renders it from any point in map units, Hide() takes it away again; Look(prefab, path) renders a pack model alone.
public static partial class CryptEntrance
{
    const string MapPath = "Assets/Game/Campaign/Map/Realm map.prefab";
    const string PreviewName = "Entrance preview map";
    static GameObject preview;

    public static GameObject Preview
    {
        get
        {
            if (preview == null) foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>()) if (go.name == PreviewName && go.scene.IsValid()) { preview = go; break; }
            return preview;
        }
    }

    public static string Show()
    {
        Hide();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapPath);
        var cm = Object.FindAnyObjectByType<CampaignManager>(FindObjectsInactive.Include);
        Vector3 at;
        if (cm != null && cm.mapSpot != null) at = cm.mapSpot.position;
        else
        {
            // As CampaignManager.SpawnMap: the top of the table's collider.
            var tm = Object.FindAnyObjectByType<TableManager>(FindObjectsInactive.Include);
            var col = tm != null ? tm.GetComponent<Collider>() : null;
            at = col != null ? new Vector3(col.bounds.center.x, col.bounds.max.y, col.bounds.center.z) : tm != null ? tm.transform.position : Vector3.zero;
        }
        preview = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        preview.name = PreviewName;
        preview.hideFlags = HideFlags.DontSave;
        preview.transform.position = at;
        foreach (var t in preview.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
        return "shown at " + at;
    }

    public static void Hide()
    {
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            if (go != null && go.name == PreviewName && go.scene.IsValid()) Object.DestroyImmediate(go);
        preview = null;
    }

    // The map's own frame: its middle on the table and a point in map units (x toward the Dungeon Master, z to the
    // player's left as they stand on the near side).
    public static Vector3 MapPoint(float x, float y, float z) => Preview != null ? Preview.transform.TransformPoint(new Vector3(x, y, z)) : new Vector3(x, y, z);

    // A model alone, far below the world under a light of its own, from the front and three quarters (two PNGs side by
    // side, w x h each): to see what a pack model is before using it.
    public static string Look(string prefabPath, string path, int w = 640, int h = 480)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) return "no " + prefabPath;
        var root = new GameObject("Look stage") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            root.transform.position = new Vector3(0, -2000, 0);
            var model = (GameObject)Object.Instantiate(prefab, root.transform);
            model.transform.localPosition = Vector3.zero;
            var b = new Bounds(); bool any = false;
            foreach (var r in model.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            var lightGo = new GameObject("Look light"); lightGo.transform.SetParent(root.transform);
            var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2.2f; light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(40, -30, 0);
            var camGo = new GameObject("Look camera"); camGo.transform.SetParent(root.transform);
            var cam = camGo.AddComponent<Camera>(); cam.fieldOfView = 30; cam.nearClipPlane = .05f; cam.farClipPlane = 500;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.35f, .38f, .42f);
            var rt = new RenderTexture(w, h, 24); cam.targetTexture = rt;
            var sheet = new Texture2D(w * 2, h, TextureFormat.RGB24, false);
            float dist = b.extents.magnitude / Mathf.Sin(15 * Mathf.Deg2Rad) * 1.05f;
            for (int i = 0; i < 2; i++)
            {
                var dir = Quaternion.Euler(18, i == 0 ? 180 : 215, 0) * Vector3.forward;
                camGo.transform.position = b.center - dir * dist;
                camGo.transform.LookAt(b.center);
                cam.Render();
                RenderTexture.active = rt;
                sheet.ReadPixels(new Rect(0, 0, w, h), i * w, 0);
            }
            sheet.Apply(); RenderTexture.active = null;
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet); cam.targetTexture = null; rt.Release();
            return $"{prefab.name} {b.size}";
        }
        finally { Object.DestroyImmediate(root); }
    }

    // The map clears scatter round its places when one moves in the editor, and brings back what isn't on a road: that
    // undoes the specks hidden by hand in the prefab. The preview takes the prefab's own scatter states again.
    static void SyncScatter()
    {
        if (Preview == null) return;
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(MapPath).transform.Find("Scatter");
        var mine = Preview.transform.Find("Scatter");
        if (asset == null || mine == null || asset.childCount != mine.childCount) return;
        for (int i = 0; i < asset.childCount; i++) mine.GetChild(i).gameObject.SetActive(asset.GetChild(i).gameObject.activeSelf);
    }

    // Camera at map point (cx, cy, cz) looking at (tx, ty, tz), field of view fov; 1920 x 1080 PNG.
    public static string Capture(string path, float cx, float cy, float cz, float tx, float ty, float tz, float fov = 50f, int w = 1920, int h = 1080)
    {
        SyncScatter();
        // Any other copy of the map on the table (someone's own in the scene) is hidden for the shot and shown again after.
        var others = new List<GameObject>();
        foreach (var m in Object.FindObjectsByType<CampaignMap>())
            if (m.gameObject != Preview && m.gameObject.activeSelf) { others.Add(m.gameObject); m.gameObject.SetActive(false); }
        var go = new GameObject("Entrance capture camera") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var cam = go.AddComponent<Camera>();
            var main = Camera.main;
            if (main != null) { cam.CopyFrom(main); cam.cullingMask = main.cullingMask; }
            var data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            cam.fieldOfView = fov; cam.nearClipPlane = .2f; cam.farClipPlane = 1500;
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            go.transform.position = MapPoint(cx, cy, cz);
            go.transform.LookAt(MapPoint(tx, ty, tz));
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = null; rt.Release();
        }
        finally
        {
            Object.DestroyImmediate(go);
            foreach (var o in others) if (o != null) o.SetActive(true);
        }
        return "captured " + path;
    }
}
