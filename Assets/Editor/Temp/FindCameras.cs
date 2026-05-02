using UnityEngine;
using UnityEditor;

public class FindCameras
{
    public static string Execute()
    {
        string result = "";
        var allGOs = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude);
        foreach (var go in allGOs)
        {
            var name = go.name.ToLower();
            if (name.Contains("camera") || name.Contains("righthand") || name.Contains("hand"))
            {
                var path = AnimationUtility.CalculateTransformPath(go.transform, null);
                result += go.name + " | " + path + "\n";
            }
        }

        // Also check ItemHolder
        var holders = Object.FindObjectsByType<ItemHolder>(FindObjectsInactive.Exclude);
        result += "\nItemHolder count: " + holders.Length + "\n";
        foreach (var h in holders)
            result += "  " + AnimationUtility.CalculateTransformPath(h.transform, null) + "\n";

        return result;
    }
}
