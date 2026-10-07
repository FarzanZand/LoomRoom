using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;

// The room keeps its own light while a dungeon floor is dark. The dungeon is a miniature on the table in
// the same scene, so its darkness (ambient and sun held down) would otherwise black out the bedroom seen
// from the table. When darkness begins, the room's ambient, sun and sky are held as they were: room
// renderers (under Room Roots) get that ambient through their own light probe, the sun keeps its strength
// but lights only the room's rendering layer, and the sky (only seen through the room's skylight) stays.
// The global ambient is the dungeon's. Released when the darkness ends.
public sealed partial class LightingManager
{
    [Title("Room during dungeon darkness")]
    [Tooltip("Renderers under these keep the room's own light while a dungeon floor is dark.")]
    public Transform[] roomRoots = Array.Empty<Transform>();
    [Tooltip("Under a room root but not part of the room (the table's own levels).")]
    public Transform[] notRoom = Array.Empty<Transform>();

    // Rendering layer added to room renderers; the sun lights only this layer during darkness.
    public const uint RoomLayer = 8;

    bool roomHeld;
    SphericalHarmonicsL2 heldProbe;
    float heldSun;
    Color heldSunColor, heldSky;
    float heldExposure;
    uint heldSunLayers;
    readonly List<(Renderer renderer, LightProbeUsage usage)> roomRenderers = new();
    MaterialPropertyBlock roomBlock;

    bool RoomSeparated => Application.isPlaying && roomRoots != null && roomRoots.Length > 0;

    Light Sun()
    {
        if (sceneSources != null) foreach (var s in sceneSources) if (IsDirectional(s)) return s.light;
        return null;
    }

    // Before the darkened values are written: start holding the room as it looks now, or let it go.
    void HoldRoom(float dark)
    {
        if (!RoomSeparated) return;
        if (dark > 0f && !roomHeld) BeginHold();
        else if (dark <= 0f && roomHeld) ReleaseRoom();
    }

    void BeginHold()
    {
        roomHeld = true;
        heldProbe = RenderSettings.ambientProbe;
        var sun = Sun();
        heldSun = sun != null ? sun.intensity : 0f;
        heldSunColor = sun != null ? sun.color : Color.white;
        heldSunLayers = sun != null ? (uint)sun.renderingLayerMask : uint.MaxValue;
        var sky = moodSkybox != null ? moodSkybox : baseSkybox;
        string tint = SkyTintProperty(sky);
        heldSky = tint != null ? sky.GetColor(tint) : Color.white;
        heldExposure = sky != null && sky.HasProperty("_Exposure") ? sky.GetFloat("_Exposure") : 1f;

        roomBlock ??= new MaterialPropertyBlock();
        var probe = new[] { heldProbe };
        roomRenderers.Clear();
        foreach (var root in roomRoots)
        {
            if (root == null) continue;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || IsNotRoom(r.transform)) continue;
                roomRenderers.Add((r, r.lightProbeUsage));
                r.renderingLayerMask |= RoomLayer;
                r.lightProbeUsage = LightProbeUsage.CustomProvided;
                r.GetPropertyBlock(roomBlock);
                roomBlock.CopySHCoefficientArraysFrom(probe);
                r.SetPropertyBlock(roomBlock);
            }
        }
    }

    bool IsNotRoom(Transform t)
    {
        if (notRoom != null) foreach (var n in notRoom) if (n != null && t.IsChildOf(n)) return true;
        return false;
    }

    void ReleaseRoom()
    {
        roomHeld = false;
        foreach (var (r, usage) in roomRenderers) if (r != null) r.lightProbeUsage = usage;
        roomRenderers.Clear();
        var sun = Sun();
        if (sun != null) FirstPersonLighting.SetLayers(sun, heldSunLayers);
    }

    // While held: the sky stays the room's.
    bool HeldSky(out Color sky, out float exposure)
    {
        sky = heldSky; exposure = heldExposure;
        return roomHeld;
    }

    // While held: the sun keeps the room's strength and colour, on the room's layer only.
    bool HeldSun(Light sun)
    {
        if (!roomHeld || sun == null) return false;
        sun.intensity = heldSun;
        sun.color = heldSunColor;
        FirstPersonLighting.SetLayers(sun, RoomLayer);
        return true;
    }
}
