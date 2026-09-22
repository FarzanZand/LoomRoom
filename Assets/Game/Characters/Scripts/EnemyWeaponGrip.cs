using UnityEngine;

[CreateAssetMenu(menuName = "Characters/Enemy weapon grip")]
public class EnemyWeaponGrip : ScriptableObject
{
    [Tooltip("Shared attachment pose relative to the humanoid right-hand bone.")]
    public Vector3 position = new Vector3(.078f, .03f, 0);
    public Vector3 rotation = new Vector3(0, 90, -90);
    [Min(.01f)] public float scale = 1;
}
