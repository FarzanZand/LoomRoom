using UnityEngine;

// A kit piece (pillar, arch, rubble, gallery) that has a real model besides its blocks. When the level uses kit models
// (TableLevelData.kitModels, on by default) the model is kept and the blocks it replaces are removed; otherwise the
// model goes and the blocks are built of the room's stone as before. Pieces without a model are always blocks.
public class DungeonKitPiece : MonoBehaviour
{
    [Tooltip("The painted model (a child). Built in place of the Replaces blocks.")]
    public GameObject model;
    [Tooltip("Block groups the model stands in for (removed when the model is used).")]
    public GameObject[] replaces = new GameObject[0];
    [Tooltip("Stack the model up to the room's ceiling (a pillar in a 6 m room is two pillars high), in whole copies so its texels keep their size.")]
    public bool modelToCeiling;
    [Min(.1f), Tooltip("Height of one copy of the model, metres.")]
    public float modelHeight = 3f;

    public bool Replaces(Transform t)
    {
        if (replaces != null) foreach (var r in replaces) if (r != null && (t == r.transform || t.IsChildOf(r.transform))) return true;
        return false;
    }

    // Called by the generator before the kit blocks are built.
    public void Choose(bool useModel, float roomHeight)
    {
        if (!useModel || model == null)
        {
            if (model != null) DestroyImmediate(model);
            return;
        }
        if (replaces != null) foreach (var r in replaces) if (r != null) DestroyImmediate(r);
        replaces = new GameObject[0];
        if (modelToCeiling)
        {
            int copies = Mathf.Max(1, Mathf.RoundToInt(roomHeight / modelHeight));
            for (int i = 1; i < copies; i++)
            {
                var copy = Instantiate(model, model.transform.parent);
                copy.transform.localPosition = model.transform.localPosition + Vector3.up * modelHeight * i / Mathf.Max(.0001f, transform.lossyScale.y);
                copy.transform.localRotation = model.transform.localRotation; copy.transform.localScale = model.transform.localScale;
            }
        }
    }
}
