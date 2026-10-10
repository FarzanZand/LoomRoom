using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// The town on the table, as a list: each step names objects that belong to the town (shown and
// hidden with it) and an ObjectController that is placed at its move target when the town is set up.
// Nothing plays here; TableLevelLoader reads the list (through TableManager.tableIntroController).
public class TableIntroController : MonoBehaviour
{
    [Serializable]
    public class BeatStep
    {
        [Tooltip("Name shown in this step's collapsed header.")]
        public string label;

        public string DisplayLabel
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(label)) return label;
                if (activate != null)
                    foreach (var go in activate)
                        if (go != null) return "Reveal " + go.name;
                if (startMove != null) return "Move " + startMove.name;
                return "Empty step";
            }
        }

        [Tooltip("Objects that belong to the town and are shown with it.")]
        public GameObject[] activate;
        [Tooltip("ObjectController placed at its move target when the town is set up.")]
        public ObjectController startMove;
    }

    [ListDrawerSettings(ShowFoldout = true, NumberOfItemsPerPage = 20,
        ListElementLabelName = "DisplayLabel", DefaultExpandedState = false, ShowIndexLabels = true)]
    public List<BeatStep> steps = new();
}
