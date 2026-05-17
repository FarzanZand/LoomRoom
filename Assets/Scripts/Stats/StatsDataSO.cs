using System.Collections.Generic;
using UnityEngine;

// Define base stat values here and share across enemy/character types.
// StatsComponent reads from this SO first, then applies per-instance overrides.
[CreateAssetMenu(fileName = "NewStatsData", menuName = "Game/Stats Data")]
public class StatsDataSO : ScriptableObject
{
    public List<StatEntry> stats;
}
