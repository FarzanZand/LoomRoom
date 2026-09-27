using TMPro;
using UnityEngine;

// While sneaking, a small word above the vitals: hidden, or seen by an enemy.
public class StealthIndicatorUI : MonoBehaviour
{
    [SerializeField] TMP_Text label;
    [SerializeField] string hiddenText = "HIDDEN", seenText = "SEEN";
    [SerializeField] Color hiddenColor = new(.5f, .56f, .62f), seenColor = new(.85f, .45f, .29f);

    void Update()
    {
        if (label == null) return;
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
        bool sneaking = player != null && player.kind == PlayerKind.Table && player.IsAlive && player.Motor != null && player.Motor.IsCrouching;
        label.enabled = sneaking;
        if (!sneaking) return;
        bool seen = false;
        foreach (var brain in EnemyBrain.Active)
            if (brain != null && brain.Perception != null && brain.Perception.Target == player && brain.Perception.TargetVisible) { seen = true; break; }
        label.text = seen ? seenText : hiddenText;
        label.color = seen ? seenColor : hiddenColor;
    }
}
