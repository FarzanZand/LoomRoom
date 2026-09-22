using TMPro;
using UnityEngine;
using UnityEngine.UI;

// All artwork, sizes and typography are authored in the UI prefab.
public class DungeonEnemyBar : MonoBehaviour
{
    public Image healthFill;
    public Image recentDamageFill;
    public TMP_Text enemyName;
    public RectTransform Rect => (RectTransform)transform;
}
