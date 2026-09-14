using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewPlayerData", menuName = "Game/Player Data")]
public class PlayerData : ScriptableObject
{
    [Header("Identity")]
    public Faction faction = Faction.Player;

    [Header("Stats")]
    public List<StatEntry> stats;

    [Header("Movement — Gameplay Critical")]
    public float walkSpeed        = 3f;
    public float sprintSpeed      = 6f;
    public float crouchSpeed      = 1.75f;
    public float jumpForce        = 8f;
    public float gravityMultiplier = 2.5f;

    [Header("Starting Inventory")]
    public List<ItemData> startingItems;
}
