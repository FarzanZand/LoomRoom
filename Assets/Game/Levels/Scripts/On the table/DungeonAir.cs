using UnityEngine;

// Dust in the dungeon's air: slow motes drifting round the table player, on every floor. They are one particle system
// (Levels/Resources/Dungeon air, made by Tools/dungeon_air.eval.cs) that stays on the player's eye and seeds a box of
// air round it in world space, so walking leaves them where they hang. A mote shows only the light that reaches it
// (LoomRoom/Air Mote): they glint in a torch's light and round the player's own, and a dark room has none. Added to
// each floor by DungeonGenerator beside its ambience; it is its own root object, not the floor's child, so the floor's
// what-is-seen culling (DungeonVisibility) never takes it for a prop of one cell. Only while the table player's eye is
// in the dungeon: from the room's view of the table, in the flights and the reveal it is switched off.
public class DungeonAir : MonoBehaviour
{
    const string PrefabName = "Dungeon air";
    [Tooltip("Metres over the player's feet the box of air is centred on.")]
    public float height = 1.5f;
    [Range(0f, 1f), Tooltip("The share of the motes left while a boss stage's fight is on: its lights are strong and reach far, and at full count every mote for metres round was lit, a field of specks against the dark room.")]
    public float stageShare = .3f;

    ParticleSystem motes;
    ParticleSystemRenderer drawn;
    float fullRate = -1f;

    public void Initialize()
    {
        var prefab = Resources.Load<GameObject>(PrefabName);
        if (prefab == null) return;
        var go = Instantiate(prefab);
        go.name = PrefabName;
        motes = go.GetComponent<ParticleSystem>();
        drawn = go.GetComponent<ParticleSystemRenderer>();
        // Lit as the floor is: by the dungeon's lights only, never the bedroom's lamps over the table.
        if (drawn != null) { drawn.renderingLayerMask = FirstPersonLighting.WorldLayer; drawn.enabled = false; }
        if (motes != null) motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    void LateUpdate()
    {
        if (motes == null || drawn == null) return;
        var player = PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table ? PlayerManager.Instance.Active : null;
        bool on = player != null && player.IsAlive && (!GameManager.HasInstance || GameManager.Instance.State != GameState.Cutscene);
        if (drawn.enabled != on)
        {
            drawn.enabled = on;
            if (on) { motes.Clear(); motes.Play(); } else motes.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        if (!on) return;
        motes.transform.position = player.transform.position + Vector3.up * height;
        var emission = motes.emission;
        if (fullRate < 0f) fullRate = emission.rateOverTimeMultiplier;
        var stage = BossStageEncounter.Current;
        emission.rateOverTimeMultiplier = fullRate * (stage != null && stage.Busy ? stageShare : 1f);
    }

    void OnDisable() { if (drawn != null) drawn.enabled = false; }
    void OnDestroy() { if (motes != null) Destroy(motes.gameObject); }
}
