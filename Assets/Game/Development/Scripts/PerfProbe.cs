using System.Collections;
using System.IO;
using UnityEngine;

// Play mode measuring helper, started from an eval: PerfProbe.Run("label", 120) averages frame time over that many
// frames and counts what is on the floor (blood stains, rigidbodies awake and asleep, renderers), plus the editor's
// draw calls and SetPass calls, and appends a line to Temp/PerfProbe.txt.
public class PerfProbe : MonoBehaviour
{
    public static void Run(string label, int frames = 120) =>
        new GameObject("Perf probe").AddComponent<PerfProbe>().StartCoroutine(Measure(label, frames));

    static IEnumerator Measure(string label, int frames)
    {
        float sum = 0f, worst = 0f; int batches = 0, setPass = 0;
        for (int i = 0; i < frames; i++)
        {
            yield return null;
            float dt = Time.unscaledDeltaTime * 1000f; sum += dt; worst = Mathf.Max(worst, dt);
#if UNITY_EDITOR
            batches += UnityEditor.UnityStats.drawCalls; setPass += UnityEditor.UnityStats.setPassCalls;
#endif
        }
        int stains = 0, bodies = 0, awake = 0, renderers = 0;
        foreach (var f in FindObjectsByType<MeshFilter>()) if (f.name == "Blood stain") stains++;
        foreach (var r in FindObjectsByType<Rigidbody>()) { bodies++; if (!r.isKinematic && !r.IsSleeping()) awake++; }
        renderers = FindObjectsByType<Renderer>().Length;
        string line = $"{label}: frame {sum / frames:0.00} ms avg, {worst:0.0} worst; draw calls {batches / frames}, setpass {setPass / frames}; " +
                      $"stains {stains}, rigidbodies {bodies} ({awake} awake), renderers {renderers}; mem {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576} MB";
        File.AppendAllText(Path.Combine(Application.dataPath, "..", "Temp", "PerfProbe.txt"), line + "\n");
        Debug.Log("[PerfProbe] " + line);
        Destroy(FindAnyObjectByType<PerfProbe>()?.gameObject);
    }
}
