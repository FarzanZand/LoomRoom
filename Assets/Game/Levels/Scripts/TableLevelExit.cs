using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;

// The way back from a table level: the entry flight in reverse. The view rises out of the table player's eyes, up
// through the open ceiling and across the table into the room player's eyes (standing at the table), while the room's
// light and music come back; once it has landed, the floor sinks into the table. Presentation only, and every touched
// state is put back, also when interrupted.
public sealed class TableLevelExit : MonoBehaviour
{
    readonly List<(Renderer renderer, bool forcedOff)> roomRenderers = new();
    Camera output;
    CinemachineBrain brain;
    bool brainEnabled, captured, completed;
    public bool IsPlaying { get; private set; }

    // flying starts as the view leaves the floor (the loader brings the room's light and music back over the flight).
    public IEnumerator Play(DungeonGenerator dungeon, TableLevelRevealSettings settings, Action flying)
    {
        output = PlayerManager.HasInstance ? PlayerManager.Instance.OutputCamera : null;
        var room = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Room) : null;
        if (output == null || room == null || settings == null) yield break;
        IsPlaying = true;
        try
        {
            brain = output.GetComponent<CinemachineBrain>();
            brainEnabled = brain != null && brain.enabled;
            if (brain != null) brain.enabled = false;
            captured = true;
            var start = output.transform.position; var startRotation = output.transform.rotation;
            var startProjection = Matrix4x4.Perspective(output.fieldOfView, output.aspect, output.nearClipPlane, output.farClipPlane);
            output.projectionMatrix = startProjection;
            // The room player takes over under the flight; their body stays hidden until the view is in their head.
            PlayerManager.Instance.SwapToPlayerImmediately(PlayerKind.Room);
            foreach (var r in room.ActivationRoot.GetComponentsInChildren<Renderer>(true))
            { roomRenderers.Add((r, r.forceRenderingOff)); r.forceRenderingOff = true; }
            // Seen from the room again: the open tops (WorldManager.hideTopForGenerations) let the view rise straight out.
            if (dungeon != null) dungeon.ShowCeilingsFor(PlayerKind.Room);
            flying?.Invoke();
            var target = room.CameraRig.Camera;
            float seconds = Mathf.Max(.1f, settings.exitSeconds);
            var aboveStart = start + Vector3.up * settings.exitRise;
            for (float elapsed = 0f; elapsed < seconds; elapsed += Time.unscaledDeltaTime)
            {
                // The room camera's live pose (its rig may still settle), so Cinemachine takes over from the same view.
                target.InternalUpdateCameraState(Vector3.up, Time.unscaledDeltaTime);
                var live = target.State;
                var destination = live.GetFinalPosition();
                var projection = Matrix4x4.Perspective(live.Lens.FieldOfView, output.aspect, live.Lens.NearClipPlane, live.Lens.FarClipPlane);
                float t = Mathf.SmoothStep(0f, 1f, elapsed / seconds), u = 1f - t;
                // Straight up out of the floor first, then over to the room player (the entry's curve, run backward).
                var position = u * u * u * start + 3f * u * u * t * aboveStart + 3f * u * t * t * Vector3.Lerp(aboveStart, destination, .3f) + t * t * t * destination;
                output.transform.SetPositionAndRotation(position, Quaternion.Slerp(startRotation, live.GetFinalOrientation(), t));
                var matrix = new Matrix4x4();
                for (int i = 0; i < 16; i++) matrix[i] = Mathf.Lerp(startProjection[i], projection[i], t);
                output.projectionMatrix = matrix;
                yield return null;
            }
            target.InternalUpdateCameraState(Vector3.up, Time.unscaledDeltaTime);
            output.transform.SetPositionAndRotation(target.State.GetFinalPosition(), target.State.GetFinalOrientation());
            output.fieldOfView = target.State.Lens.FieldOfView;
            completed = true;
        }
        finally { Restore(); }
    }

    void Restore()
    {
        foreach (var entry in roomRenderers) if (entry.renderer != null) entry.renderer.forceRenderingOff = entry.forcedOff;
        roomRenderers.Clear();
        if (captured && output != null)
        {
            output.ResetProjectionMatrix();
            if (brain != null) { brain.enabled = brainEnabled; brain.ResetState(); }
        }
        // Cut short: the room player is the one in play either way.
        if (captured && !completed && PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind != PlayerKind.Room)
            PlayerManager.Instance.SwapToPlayerImmediately(PlayerKind.Room);
        captured = false; IsPlaying = false;
    }

    void OnDisable() => Restore();
}
