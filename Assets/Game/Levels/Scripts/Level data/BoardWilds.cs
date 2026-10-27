using System;
using Sirenix.OdinInspector;
using UnityEngine;

// The wild ground round a board floor (DungeonBiome.wilds, built by DungeonGenerator.Wilds): every cell of the table
// within Depth of a room or path becomes a bank of earth standing above the floor beside it, its faces toward the floor
// a cliff of earth and roots, its top under trees and undergrowth. The rooms read as clearings and the passages as paths
// through a forest instead of tiles lying on a table. Ponds: pits cut into some rooms, which fill with the board's pit
// water (DungeonBiome.boardPitWater).
[Serializable]
public class BoardWilds
{
    [Tooltip("Fill the table round the rooms and paths with wild ground.")]
    public bool enabled;
    [ShowIf(nameof(enabled)), Tooltip("Walled floors only (the banks stand in for the walls there): keep the floor's ceilings over the rooms and paths, as in a cave. Off: open to the sky, as in a forest. Ponds are on board floors only.")]
    public bool roofed;
    [ShowIf(nameof(enabled)), Range(1, 10), Tooltip("Cells of wild ground out from the rooms and paths.")]
    public int depth = 5;
    [ShowIf(nameof(enabled)), MinMaxSlider(.4f, 8f, true), Tooltip("Height of a bank over the floor beside it (metres): the low end at its foot, rising toward the high end further from the floor.")]
    public Vector2 height = new(1.6f, 3.6f);
    [ShowIf(nameof(enabled)), Tooltip("The banks' tops (earth under leaves). Empty: the room style's floor.")]
    public Material top;
    [ShowIf(nameof(enabled)), Tooltip("The banks' faces (a cliff of earth). Empty: the room style's wall.")]
    public Material side;

    [ShowIf(nameof(enabled)), Title("On the banks", HorizontalLine = false), Tooltip("Trees standing on the banks, weighted.")]
    public DungeonWeightedPrefab[] trees = new DungeonWeightedPrefab[0];
    [ShowIf(nameof(enabled)), Range(0, 1), Tooltip("Chance of a tree on a bank cell (the cell at the floor's edge gets half).")]
    public float treeChance = .45f;
    [ShowIf(nameof(enabled)), MinMaxSlider(.2f, 3f, true), Tooltip("A tree's size against its prefab, picked at random.")]
    public Vector2 treeScale = new(.8f, 1.25f);
    [ShowIf(nameof(enabled)), Tooltip("Ferns, mushrooms, rocks and the like on the banks' tops.")]
    public DungeonWeightedPrefab[] undergrowth = new DungeonWeightedPrefab[0];
    [ShowIf(nameof(enabled)), Range(0, 3), Tooltip("Undergrowth pieces a bank cell gets on average.")]
    public float undergrowthPerCell = 1f;
    [ShowIf(nameof(enabled)), Tooltip("Set at the foot of a bank's face, on the floor's edge: roots, moss, ferns that break the line where bank meets floor.")]
    public DungeonWeightedPrefab[] foot = new DungeonWeightedPrefab[0];
    [ShowIf(nameof(enabled)), Range(0, 1), Tooltip("Chance of a foot piece on each cell edge where a bank meets the floor.")]
    public float footChance = .45f;
    // Serialized by integer: append only.
    public enum Edges { [InspectorName("Walls (straight earth)")] Walls = 0, [InspectorName("Formations (rock outcrops)")] Formations = 1 }
    [ShowIf(nameof(enabled)), LabelText("Bank edges"), Tooltip("Walls: the banks meet the floor in straight cut faces of earth. Formations: rock and earth outcrops are set into those faces (Faces below), breaking their straight line.")]
    public Edges edges = Edges.Formations;
    bool UsesFormations => enabled && edges == Edges.Formations;
    [ShowIf(nameof(UsesFormations)), Tooltip("Rock and earth formations set into a bank's face along the floor's edge, standing a little out of it: they break the bank's straight face.")]
    public DungeonWeightedPrefab[] faces = new DungeonWeightedPrefab[0];
    [ShowIf(nameof(UsesFormations)), Range(0, 1), Tooltip("Chance of a formation on each cell edge where a bank meets the floor.")]
    public float faceChance = .3f;
    [ShowIf(nameof(UsesFormations)), Range(0, 1.5f), Tooltip("How far (metres) a formation stands out of the bank's face over the floor.")]
    public float faceStandOut = .5f;
    [ShowIf(nameof(enabled)), Tooltip("Set on a bank's top right at its edge, hanging over the face: ferns and moss that break the bank's straight top line.")]
    public DungeonWeightedPrefab[] lip = new DungeonWeightedPrefab[0];
    [ShowIf(nameof(enabled)), Range(0, 1), Tooltip("Chance of a lip piece on each cell edge where a bank meets the floor.")]
    public float lipChance = .6f;
    [ShowIf(nameof(enabled)), Tooltip("Things that glow (mushrooms, crystals with a light under them) at the banks' edges: the forest's own lights.")]
    public DungeonWeightedPrefab[] glows = new DungeonWeightedPrefab[0];
    [ShowIf(nameof(enabled)), Range(2, 30), Tooltip("About one glow per this many cells of the floor's edge.")]
    public int glowEvery = 9;
    [ShowIf(nameof(enabled)), Tooltip("Hung flat on a bank's face: vines and ivy, their top just under the ceiling (roofed) or the bank's top. Only those no wider than a cell and lower than the wall are used.")]
    public DungeonWeightedPrefab[] hangings = new DungeonWeightedPrefab[0];
    [ShowIf(nameof(enabled)), Range(0, 1), Tooltip("Chance of a hanging on each cell edge where a bank meets the floor.")]
    public float hangChance = .25f;

    [ShowIf(nameof(enabled)), Title("Ponds", HorizontalLine = false), Range(0, 1), Tooltip("Chance a room (not the entrance or the exit) has a pond: a pit of a few cells away from its ways through, filled with the board's pit water.")]
    public float pondChance = .35f;
    [ShowIf(nameof(enabled)), MinMaxSlider(1, 5, true), Tooltip("A pond's width and depth in cells.")]
    public Vector2Int pondSize = new(2, 3);
    [ShowIf(nameof(enabled)), ColorUsage(false), Tooltip("A soft light just over each pond's water, so it glows. Its strength 0: no light.")]
    public Color pondGlow = new(.35f, .85f, 1f);
    [ShowIf(nameof(enabled)), Range(0, 10)] public float pondGlowStrength = 2.5f;
    [ShowIf(nameof(enabled)), Tooltip("Set round a pond's edge: reeds, stones, roots.")]
    public DungeonWeightedPrefab[] pondEdge = new DungeonWeightedPrefab[0];
}
