using UnityEngine;

public interface IEquippable
{
    string ItemName { get; }
    GameObject EquippedPrefab { get; }
}
