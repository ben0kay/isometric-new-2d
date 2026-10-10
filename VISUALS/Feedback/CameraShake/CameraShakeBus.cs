// Event-only pipeline for camera feedback. Gameplay may issue requests
// without holding references to Camera2D or the rendering controller.
using Godot;
using System;

public enum CameraShakeScope { Target, Area, All }

// =========================================================
// Immutable event payload; one request can affect one camera or many.
public readonly struct CameraShakeRequest
{
    public CameraShakeScope Scope { get; }
    public Node2D Target { get; }
    public Vector2 Position { get; }
    public string WorldLayer { get; }
    public float Radius { get; }
    public float Strength { get; }
    public float Duration { get; }

    internal CameraShakeRequest(CameraShakeScope scope, Node2D target,
        Vector2 position, string worldLayer, float radius,
        float strength, float duration)
    {
        Scope = scope;
        Target = target;
        Position = position;
        WorldLayer = worldLayer;
        Radius = radius;
        Strength = strength;
        Duration = duration;
    }
}

// =========================================================
// Static event hub: no Autoload or scene dependency, no idle processing.
// With no subscribed cameras, requests safely have no visual effect.
public static class CameraShakeBus
{
    #region Events
    public static event Action<CameraShakeRequest> Requested;
    #endregion

    #region Sending
    // =========================================================
    // Bite, knockback or weapon recoil: only this actor's camera.
    public static void ShakeTarget(Node2D recipient, float strength, float duration)
    {
        if (!GodotObject.IsInstanceValid(recipient) || !Valid(strength, duration))
            return;

        Requested?.Invoke(new CameraShakeRequest(
            CameraShakeScope.Target, recipient, Vector2.Zero, null,
            0f, strength, duration));
    }

    // =========================================================
    // Explosion, stomp or local quake: auto-resolve event position and layer.
    public static void ShakeAt(Node2D source, float strength,
        float duration, float radius)
    {
        if (!GodotObject.IsInstanceValid(source)) return;
        ShakeAt(source.GlobalPosition, WorldLayerMember.For(source),
            strength, duration, radius);
    }

    // =========================================================
    // Position-only emitter: supply world layer explicitly (surface or cave).
    public static void ShakeAt(Vector2 position, string layer,
        float strength, float duration, float radius)
    {
        if (!Valid(strength, duration) || !float.IsFinite(radius) ||
            radius <= 0f || string.IsNullOrWhiteSpace(layer) ||
            !float.IsFinite(position.X) || !float.IsFinite(position.Y))
            return;

        Requested?.Invoke(new CameraShakeRequest(
            CameraShakeScope.Area, null, position, layer,
            radius, strength, duration));
    }

    // =========================================================
    // World-wide earthquakes or cinematic events: all active cameras.
    public static void ShakeAll(float strength, float duration)
    {
        if (!Valid(strength, duration)) return;

        Requested?.Invoke(new CameraShakeRequest(
            CameraShakeScope.All, null, Vector2.Zero, null,
            0f, strength, duration));
    }
    #endregion

    #region Validation
    // =========================================================
    // Strength is conventionally 0..1; receivers cap stronger combined requests.
    private static bool Valid(float strength, float duration) =>
        float.IsFinite(strength) && strength > 0f &&
        float.IsFinite(duration) && duration > 0f;
    #endregion
}
