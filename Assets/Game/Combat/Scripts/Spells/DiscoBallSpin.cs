using UnityEngine;

// A disco ball (the Disco Ball spell's projectile and the hand that throws it): it turns, and its light walks round the
// party colours, so the walls it passes catch spots of pink, cyan and gold.
public class DiscoBallSpin : MonoBehaviour
{
    [Tooltip("Degrees a second it turns about its own up.")] public float spin = 220f;
    [Tooltip("The light whose colour walks round the colours (optional).")] public Light sparkle;
    public Color[] colours = { new(1f, .35f, .8f), new(.3f, .9f, 1f), new(1f, .8f, .3f), new(.65f, .45f, 1f) };
    [Tooltip("Colours a second.")] public float colourSpeed = 3f;

    float offset;

    void Awake() => offset = Random.value * 10f;

    void Update()
    {
        transform.Rotate(0f, spin * Time.deltaTime, 0f, Space.Self);
        if (sparkle == null || colours == null || colours.Length == 0) return;
        float t = (Time.time + offset) * colourSpeed;
        int i = Mathf.FloorToInt(t) % colours.Length;
        sparkle.color = Color.Lerp(colours[i], colours[(i + 1) % colours.Length], t - Mathf.Floor(t));
    }
}
