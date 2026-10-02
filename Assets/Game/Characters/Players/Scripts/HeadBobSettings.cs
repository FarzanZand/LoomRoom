using UnityEngine;

// Head bob tuning, edited on WorldManager and read by HeadBob on each player's camera. Distances are
// in metres at the Table player's scale; the Room player (scaled up) gets the same look automatically.
[System.Serializable]
public class HeadBobSettings
{
    [System.Serializable]
    public class Gait
    {
        [Min(0), Tooltip("Footsteps per second. One step = one dip; two steps = one sway left and right.")]
        public float stepsPerSecond = 1.8f;
        [Min(0), Tooltip("How far the view dips on each step (metres).")]
        public float vertical = .035f;
        [Min(0), Tooltip("How far the view sways sideways over a pair of steps (metres).")]
        public float sideways = .025f;
        [Min(0), Tooltip("Camera roll at the side of each sway (degrees).")]
        public float roll = .4f;
    }

    public bool enabled = true;
    [Range(0, 3), Tooltip("Scales every bob distance and the roll. 0 = off.")]
    public float strength = 1f;
    [Range(.25f, 3), Tooltip("Scales every gait's steps per second.")]
    public float tempo = 1f;
    [Min(.1f), Tooltip("How fast the bob fades in when you start moving, out when you stop, and between gaits.")]
    public float blendSpeed = 8f;

    public Gait walk = new() { stepsPerSecond = 1.8f, vertical = .035f, sideways = .025f, roll = .4f };
    public Gait run = new() { stepsPerSecond = 2.6f, vertical = .065f, sideways = .04f, roll = .9f };
    [Tooltip("Crouch-walking (sneaking).")]
    public Gait crouch = new() { stepsPerSecond = 1.3f, vertical = .02f, sideways = .02f, roll = .25f };
}
