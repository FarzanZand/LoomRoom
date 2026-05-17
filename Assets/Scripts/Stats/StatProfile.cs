using System.Collections.Generic;
using UnityEngine;

// Define base stat values here and share them across enemy or character types.
// Assign to StatsComponent directly, or let a character data asset (EnemyData,
// PlayerData) push the profile into StatsComponent at runtime.
[CreateAssetMenu(fileName = "NewStatProfile", menuName = "Game/Stat Profile")]
public class StatProfile : ScriptableObject
{
    public List<StatEntry> stats;
}
