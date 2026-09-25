using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// What the old beat-timed table reveal switched on. The reveal no longer plays;
// TableLevelLoader reads these steps to know which town objects belong to the table
// and where the moving props end up.
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
