using System.Collections.Generic;
using UnityEngine;

// Serialized by value: append only.
public enum DungeonRoomLight { Normal = 0, Dark = 1, Candlelit = 2, Moonlit = 3, Eerie = 4 }

// The light of one room, while the player is in it: a dark room pulls the light radius in until only the player's own
// light shows anything, a candlelit room warms the lamps, a moonlit room cools the shadows and hangs a shaft of moonlight
// through its skylight, an eerie room turns the light green. Added by the generator to a room whose template (or roll)
// asks for it; LightingManager (light radius) and ScreenManager (colour split) blend towards the room's look.
public class DungeonRoomMood : MonoBehaviour
{
    public DungeonRoomLight kind;
    [HideInInspector] public DungeonGenerator dungeon;
    [HideInInspector] public int room = -1;

    static readonly List<DungeonRoomMood> active = new();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { active.Clear(); current = null; weight = 0f; }
    void OnEnable() => active.Add(this);
    void OnDisable() => active.Remove(this);

    // ── The blend (read by LightingManager and ScreenManager) ─────────

    static DungeonRoomMood current;
    static float weight;
    const float BlendSeconds = 1.2f;

    // Once a frame from LightingManager: eases into the mood of the room the eye is in, and out of it again.
    public static void Tick(Vector3 eye, float deltaTime)
    {
        DungeonRoomMood inside = null;
        foreach (var m in active) if (m != null && m.Contains(eye)) { inside = m; break; }
        float step = deltaTime / BlendSeconds;
        if (inside == current) weight = Mathf.MoveTowards(weight, current != null ? 1f : 0f, step);
        else
        {
            weight = Mathf.MoveTowards(weight, 0f, step * 1.5f);
            if (weight <= 0f) current = inside;
        }
    }

    bool Contains(Vector3 p) => dungeon != null && dungeon.Layout != null && room >= 0 && dungeon.Layout.RoomAt(dungeon.CellOf(p)) == room;

    // Light radius: (radius metres, edge metres, share of ambient left beyond).
    public static void LightRadius(ref float radius, ref float edge, ref float floor)
    {
        if (current == null || weight <= 0f) return;
        switch (current.kind)
        {
            case DungeonRoomLight.Dark: Blend(ref radius, 1.4f); Blend(ref edge, 1.6f); Blend(ref floor, 0f); break;
            case DungeonRoomLight.Moonlit: Blend(ref radius, radius * 1.35f); break;
        }
    }

    // Colour split tints (ScreenManager): ambient and shadows, lamps and torches.
    public static void Tints(ref Color shadow, ref Color lamp)
    {
        if (current == null || weight <= 0f) return;
        switch (current.kind)
        {
            case DungeonRoomLight.Candlelit: lamp = Color.Lerp(lamp, new Color(1.16f, .94f, .7f), weight); break;
            case DungeonRoomLight.Moonlit: shadow = Color.Lerp(shadow, new Color(.62f, .78f, 1.2f), weight); lamp = Color.Lerp(lamp, new Color(.86f, .95f, 1.12f), weight); break;
            case DungeonRoomLight.Eerie: shadow = Color.Lerp(shadow, new Color(.58f, 1.08f, .62f), weight); lamp = Color.Lerp(lamp, new Color(.78f, 1.16f, .7f), weight); break;
        }
    }

    static void Blend(ref float value, float target) => value = Mathf.Lerp(value, target, weight);

    // ── Building ─────────────────────────────────────────────────────

    public static readonly Color EerieColor = new(.45f, 1f, .35f);

    // Called by the generator once the room is built (and its torches are up). A moonlit room carries its own moonlight
    // (a child the chunk kit authors); an eerie room turns every light and flame in it green.
    public void Build(DungeonGenerator generator, int index)
    {
        dungeon = generator; room = index;
        if (kind == DungeonRoomLight.Eerie) Recolour(EerieColor);
    }

    // Every light and flame in the room takes the colour (torches, braziers, candles placed by the room).
    void Recolour(Color colour)
    {
        if (dungeon == null || dungeon.Layout == null) return;
        foreach (var l in dungeon.GetComponentsInChildren<Light>(true))
            if (dungeon.Layout.RoomAt(dungeon.CellOf(l.transform.position)) == room) DungeonWallLight.Apply(l.gameObject, colour);
        foreach (var ps in dungeon.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (dungeon.Layout.RoomAt(dungeon.CellOf(ps.transform.position)) != room) continue;
            var main = ps.main;
            var c = main.startColor.color;
            main.startColor = new Color(colour.r, colour.g, colour.b, c.a);
        }
    }
}
