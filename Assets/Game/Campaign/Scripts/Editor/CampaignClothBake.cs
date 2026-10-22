using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// The first reveal's cloth drop, baked in Blender, run from an eval: CampaignClothBake.Run(). It writes the map's
// heights for Tools/campaign_cloth_drop.py, runs Blender headless (the cloth falls onto the bare table, the land rises
// under it), and puts the result in Campaign/Map/Cloth drop.bytes (with the .blend beside it to open and tweak), set on
// the map prefab (CampaignMap.clothDropBake). Run it again after the map's heights change.
public static class CampaignClothBake
{
    const string Blender = @"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe";
    const string Out = "Assets/Game/Campaign/Map/Cloth drop.bytes";

    public static string Run()
    {
        var map = AssetDatabase.LoadAssetAtPath<CampaignMap>(CampaignMapBuilder.PrefabPath);
        string temp = Path.GetFullPath("Temp/Campaign cloth");
        Directory.CreateDirectory(temp);
        string heights = Path.Combine(temp, "heights.json");
        var sb = new StringBuilder();
        sb.Append($"{{\"gridX\":{map.gridX},\"gridZ\":{map.gridZ},\"halfX\":{map.halfX.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"halfZ\":{map.halfZ.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"heights\":[");
        for (int i = 0; i < map.heights.Length; i++) { if (i > 0) sb.Append(','); sb.Append(map.heights[i].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)); }
        sb.Append("]}");
        File.WriteAllText(heights, sb.ToString());
        string script = Path.GetFullPath("Tools/campaign_cloth_drop.py");
        string output = Path.GetFullPath(Out), blend = Path.GetFullPath("Tools/Blender/Campaign cloth drop.blend");
        Directory.CreateDirectory(Path.GetDirectoryName(blend));
        if (File.Exists(output)) File.Delete(output);   // a failed run must not pass on the old bake
        var p = new System.Diagnostics.Process();
        p.StartInfo.FileName = Blender;
        p.StartInfo.Arguments = $"--background --python \"{script}\" -- \"{heights}\" \"{output}\" \"{blend}\"";
        p.StartInfo.UseShellExecute = false; p.StartInfo.RedirectStandardOutput = true; p.StartInfo.RedirectStandardError = true;
        p.StartInfo.CreateNoWindow = true;
        p.Start();
        var errors = p.StandardError.ReadToEndAsync();   // both streams read at once, so neither can fill and stall Blender
        string log = p.StandardOutput.ReadToEnd() + errors.Result;
        p.WaitForExit();
        File.WriteAllText(Path.Combine(temp, "blender.log"), log);
        if (!File.Exists(output)) return "Blender wrote nothing; see Temp/Campaign cloth/blender.log";
        AssetDatabase.ImportAsset(Out);
        var root = PrefabUtility.LoadPrefabContents(CampaignMapBuilder.PrefabPath);
        try
        {
            root.GetComponent<CampaignMap>().clothDropBake = AssetDatabase.LoadAssetAtPath<TextAsset>(Out);
            PrefabUtility.SaveAsPrefabAsset(root, CampaignMapBuilder.PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return "baked " + Out + " (" + new FileInfo(output).Length / 1024 + " KB)";
    }
}
