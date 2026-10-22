using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// The campaign map lying on the table: the terrain (a height grid and its painted ground), the scattered trees and
// rocks, the places (CampaignPoint children of Points) and the roads between them (CampaignPath children of Paths).
// Everything is edited in its prefab: move a place and its props, base and roads follow; add a place by duplicating
// one and giving it an encounter; join two places with a new path. Which act a patch of ground belongs to is the act
// of its nearest place, so the fog over closed acts follows the places wherever they are put.
[ExecuteAlways]
public partial class CampaignMap : MonoBehaviour
{
    [Title("Terrain (made by the map builder)")]
    public MeshFilter terrain;
    [Tooltip("Cells across (x) and along (z) the height grid.")] public int gridX = 1, gridZ = 1;
    [Tooltip("Half the map's size in local units, across (x) and along (z).")] public float halfX = 39, halfZ = 53;
    [HideInInspector] public float[] heights = new float[0];

    [Title("Parts")]
    public Transform scatter, points, paths;
    [Tooltip("What roads are drawn with.")] public Material roadMaterial;

    // ── Ground ──

    // Height of the ground at a local position (x, z).
    public float HeightAt(float x, float z)
    {
        if (heights == null || heights.Length < (gridX + 1) * (gridZ + 1)) return 0f;
        float fx = Mathf.Clamp01((x / halfX + 1f) * .5f) * gridX, fz = Mathf.Clamp01((z / halfZ + 1f) * .5f) * gridZ;
        int ix = Mathf.Min(gridX - 1, (int)fx), iz = Mathf.Min(gridZ - 1, (int)fz);
        float tx = fx - ix, tz = fz - iz;
        float a = heights[iz * (gridX + 1) + ix], b = heights[iz * (gridX + 1) + ix + 1];
        float c = heights[(iz + 1) * (gridX + 1) + ix], d = heights[(iz + 1) * (gridX + 1) + ix + 1];
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
    }

    public Vector3 OnGround(Vector3 local, float lift = 0f) => new(local.x, HeightAt(local.x, local.z) + lift, local.z);

    // ── Places and roads ──

    readonly List<CampaignPoint> pointList = new();
    readonly List<CampaignPath> pathList = new();
    public IReadOnlyList<CampaignPoint> Points { get { Collect(); return pointList; } }
    public IReadOnlyList<CampaignPath> Paths { get { Collect(); return pathList; } }

    bool collected;
    void Collect()
    {
        // At run time the places and roads never change: collected once. In the editor, every time (they are edited).
        if (Application.isPlaying && collected) return;
        collected = true;
        pointList.Clear(); pathList.Clear();
        if (points != null) points.GetComponentsInChildren(true, pointList);
        if (paths != null) paths.GetComponentsInChildren(true, pathList);
    }

    public CampaignPoint PointFor(CampaignEncounter encounter)
    {
        foreach (var p in Points) if (p.encounter == encounter) return p;
        return null;
    }

    // The act of the nearest place (with an encounter): the land round a place belongs to its act.
    public int ActAt(Vector3 local)
    {
        int act = 1; float best = float.MaxValue;
        foreach (var p in Points)
        {
            if (p.encounter == null) continue;
            var d = p.transform.localPosition - local; d.y = 0;
            float m = d.sqrMagnitude;
            if (m < best) { best = m; act = p.encounter.act; }
        }
        return act;
    }

    // The roads from one place to another, as places to walk through (the start excluded, the end included).
    public List<Vector3> Route(CampaignPoint from, CampaignPoint to, int openAct)
    {
        var route = new List<Vector3>();
        if (from == null || to == null || from == to) return route;
        // Breadth first over the open roads (none to a place still shut, which isn't on the map).
        var previous = new Dictionary<CampaignPoint, CampaignPath>();
        var queue = new Queue<CampaignPoint>();
        queue.Enqueue(from); previous[from] = null;
        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            if (at == to) break;
            foreach (var path in Paths)
            {
                if (!path.Open(openAct) || Hidden(path)) continue;
                var next = path.from == at ? path.to : path.to == at ? path.from : null;
                if (next == null || previous.ContainsKey(next)) continue;
                previous[next] = path;
                queue.Enqueue(next);
            }
        }
        if (!previous.ContainsKey(to)) { route.Add(to.transform.localPosition); return route; }
        var legs = new List<(CampaignPath path, CampaignPoint end)>();
        for (var p = to; previous[p] != null; p = previous[p].from == p ? previous[p].to : previous[p].from) legs.Add((previous[p], p));
        legs.Reverse();
        foreach (var (path, end) in legs)
        {
            var pts = path.LocalPoints;
            if (path.from == end) for (int i = pts.Count - 1; i >= 0; i--) route.Add(pts[i]);
            else route.AddRange(pts);
        }
        route.Add(to.transform.localPosition);
        return route;
    }

    // ── Editor: roads clear the scatter under them ──

    [System.NonSerialized] double clearAt = -1;
    // Asked for by places and roads as they are edited: done once the editing has paused (a slider dragged would
    // otherwise test every scatter piece against every road each frame).
    public void ScatterNeedsClearing()
    {
#if UNITY_EDITOR
        if (Application.isPlaying) return;
        if (clearAt < 0) UnityEditor.EditorApplication.update += ClearWhenIdle;
        clearAt = UnityEditor.EditorApplication.timeSinceStartup + .4;
#endif
    }

#if UNITY_EDITOR
    void ClearWhenIdle()
    {
        if (this == null) { UnityEditor.EditorApplication.update -= ClearWhenIdle; return; }
        if (UnityEditor.EditorApplication.timeSinceStartup < clearAt) return;
        UnityEditor.EditorApplication.update -= ClearWhenIdle;
        clearAt = -1;
        if (ClearScatterUnderRoads() > 0 && gameObject.scene.IsValid())
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);   // the prefab stage saves it
    }
#endif

    // Scatter standing on a road is switched off; scatter no longer on one comes back.
    public int ClearScatterUnderRoads()
    {
        if (scatter == null) return 0;
        int changed = 0;
        Collect();
        foreach (Transform child in scatter)
        {
            var p = child.localPosition;
            bool onRoad = false;
            foreach (var path in pathList) if (path.isActiveAndEnabled && path.DistanceTo(p) < path.width * .8f) { onRoad = true; break; }
            foreach (var point in pointList) { var d = point.transform.localPosition - p; d.y = 0; if (d.magnitude < point.radius + .4f) { onRoad = true; break; } }
            if (child.gameObject.activeSelf == onRoad) { child.gameObject.SetActive(!onRoad); changed++; }
        }
        return changed;
    }
}
