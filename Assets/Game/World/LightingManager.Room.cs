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

    [Tooltip("The lamp over the table: lit while a level keeps the room dark (Room Lighting: Dark), on the room's rendering layer only so the miniature keeps its own darkness.")]
    public Light tableLamp;

    // Rendering layer added to room renderers; the sun lights only this layer during darkness.
    public const uint RoomLayer = 8;

    float lampBase = -1f, appliedRoomDark = -1f;
    bool lampDriven;
    // How much of the held room light is taken away now (the level's Room Lighting).
    float RoomDark => moodActive && roomHeld ? Mathf.Clamp01(displayedMood.roomDark) : 0f;

    bool roomHeld;
    SphericalHarmonicsL2 heldProbe;
    float heldSun;
    Color heldSunColor, heldSky;
    float heldExposure;
    uint heldSunLayers;
    readonly List<(Renderer renderer, LightProbeUsage usage)> roomRenderers = new();
    // Room renderers that had no property block of their own: theirs is cleared again on release, or the
    // held light probe in it would keep lighting them.
    readonly HashSet<Renderer> blockless = new();
    MaterialPropertyBlock roomBlock;

    bool RoomSeparated => Application.isPlaying && roomRoots != null && roomRoots.Length > 0;

    // Room renderers carry the Room rendering layer for good: the pixel look reads it to give the room
    // its own texel scale, and the held sun lights only that layer.
    void TagRoom()
    {
        if (!RoomSeparated) return;
        foreach (var root in roomRoots)
        {
            if (root == null) continue;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r != null && !IsNotRoom(r.transform)) r.renderingLayerMask |= RoomLayer;
        }
    }

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
        roomRenderers.Clear(); blockless.Clear();
        foreach (var root in roomRoots)
        {
            if (root == null) continue;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || IsNotRoom(r.transform)) continue;
                roomRenderers.Add((r, r.lightProbeUsage));
                if (!r.HasPropertyBlock()) blockless.Add(r);
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

    // The held room light scaled down by the level's Room Lighting, and the table lamp for a dark room.
    void ApplyRoomDark()
    {
        float d = RoomDark;
        if (roomHeld && Mathf.Abs(d - appliedRoomDark) > .001f)
        {
            appliedRoomDark = d;
            var probe = new[] { heldProbe * (1f - d) };
            foreach (var (r, _) in roomRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(roomBlock);
                roomBlock.CopySHCoefficientArraysFrom(probe);
                r.SetPropertyBlock(roomBlock);
            }
        }
        if (tableLamp == null) return;
        if (lampBase < 0f) lampBase = tableLamp.intensity;
        float lamp = Mathf.InverseLerp(.7f, 1f, d);
        if (lamp > 0f)
        {
            lampDriven = true;
            tableLamp.enabled = true;
            tableLamp.intensity = lampBase * lamp;
            FirstPersonLighting.SetLayers(tableLamp, RoomLayer);
        }
        else if (lampDriven)
        {
            lampDriven = false;
            tableLamp.enabled = false;
            tableLamp.intensity = lampBase;
            FirstPersonLighting.SetLayers(tableLamp, uint.MaxValue);
        }
    }

    void ReleaseRoom()
    {
        roomHeld = false;
        appliedRoomDark = -1f;
        foreach (var (r, usage) in roomRenderers)
        {
            if (r == null) continue;
            r.lightProbeUsage = usage;
            if (blockless.Contains(r)) r.SetPropertyBlock(null);
        }
        roomRenderers.Clear(); blockless.Clear();
        var sun = Sun();
        if (sun != null) FirstPersonLighting.SetLayers(sun, heldSunLayers);
    }

    // While held: the sky stays the room's.
    bool HeldSky(out Color sky, out float exposure)
    {
        sky = heldSky; exposure = heldExposure * (1f - RoomDark);
        return roomHeld;
    }

    // While held: the sun keeps the room's strength and colour, on the room's layer only.
    bool HeldSun(Light sun)
    {
        if (!roomHeld || sun == null) return false;
        sun.intensity = heldSun * (1f - RoomDark);
        sun.color = heldSunColor;
        FirstPersonLighting.SetLayers(sun, RoomLayer);
        return true;
    }
}
