// A reusable, event-driven Camera2D shake controller.
// Attach CameraShake.tscn under a Camera2D. Camera position, smoothing,
// and game systems remain untouched; only Camera2D.Offset is animated.
using Godot;
using System;
using System.Collections.Generic;

public partial class CameraShakeController : Node
{
    #region Inspector
    [ExportGroup("Settings")]
    [Export] public CameraShakeSettings Settings { get; set; }

    [ExportGroup("Debug (Disable After Testing)")]
    [Export] public bool TestShakeOnReady { get; set; }
    [Export(PropertyHint.Range, "0.05,1,0.05")]
    public float TestStrength { get; set; } = 0.6f;
    [Export(PropertyHint.Range, "0.1,5,0.1")]
    public float TestDuration { get; set; } = 0.5f;
    #endregion

    #region Runtime
    private sealed class Impulse
    {
        public float Strength, Duration, Age;
    }

    private readonly List<Impulse> _impulses = new();
    private Camera2D _camera;
    private Node2D _actor;
    private Vector2 _restOffset;
    private float _phase;
    private bool _bound, _runtimeEnabled = true;
    private float _runtimeStrength = 1f;
    #endregion

    #region Lifecycle
    // =========================================================
    // Subscribe while active; otherwise do no work at all.
    public override void _Ready()
    {
        SetProcess(false);
        SetPhysicsProcess(false);
        _camera = GetParent() as Camera2D;
        _actor = _camera?.GetParent() as Node2D;

        if (_camera == null || _actor == null || Settings == null)
        {
            GD.PushError("CameraShake needs a Camera2D parent, actor and Settings resource.");
            return;
        }

        try { Settings.Validate(); }
        catch (Exception error)
        {
            GD.PushError($"CameraShake disabled: {error.Message}");
            return;
        }

        _restOffset = _camera.Offset;
        CameraShakeBus.Requested += OnRequest;
        _bound = true;

        // Test directly because child _Ready runs before Camera2D itself is ready.
        if (TestShakeOnReady && float.IsFinite(TestStrength) &&
            float.IsFinite(TestDuration) && TestStrength > 0f && TestDuration > 0f)
            AddImpulse(TestStrength, TestDuration);
    }

    // =========================================================
    // Unsubscribe so streamed-out players and deleted scenes are never retained.
    public override void _ExitTree()
    {
        if (_bound)
            CameraShakeBus.Requested -= OnRequest;
        StopShake();
        _bound = false;
    }
    #endregion

    #region Requests
    // =========================================================
    // Target, radius and world layer filtering happen before starting an impulse.
    private void OnRequest(CameraShakeRequest request)
    {
        if (!_bound || !_runtimeEnabled || !Settings.Enabled ||
            _runtimeStrength <= 0f || Settings.MaximumOffsetPixels <= 0f ||
            !GodotObject.IsInstanceValid(_camera) ||
            !GodotObject.IsInstanceValid(_actor) || !_camera.IsCurrent())
            return;

        float strength = request.Strength;
        switch (request.Scope)
        {
            case CameraShakeScope.Target:
                if (request.Target != _actor && request.Target != _camera)
                    return;
                break;

            case CameraShakeScope.Area:
                if (WorldLayerMember.For(_actor) != request.WorldLayer)
                    return;
                float distance = _actor.GlobalPosition.DistanceTo(request.Position);
                if (distance >= request.Radius) return;
                float closeness = 1f - distance / request.Radius;
                strength *= Mathf.Pow(closeness, Settings.DistanceFalloffPower);
                break;

            case CameraShakeScope.All:
                break;

            default:
                return;
        }

        AddImpulse(strength, request.Duration);
    }

    // =========================================================
    // Limit the number of overlapping requests and keep processing event-only.
    private void AddImpulse(float strength, float duration)
    {
        if (!Settings.Enabled || !_runtimeEnabled || _runtimeStrength <= 0f ||
            Settings.MaximumOffsetPixels <= 0f || strength <= 0f || duration <= 0f)
            return;

        if (_impulses.Count >= Settings.MaximumConcurrentRequests)
            _impulses.RemoveAt(0);

        _impulses.Add(new Impulse
        {
            Strength = strength,
            Duration = duration,
            Age = 0f
        });
        SetProcess(true);
    }

    // =========================================================
    // Future Options menu: globally enable/disable this player's controller.
    public void SetEnabled(bool enabled)
    {
        _runtimeEnabled = enabled;
        if (!enabled) StopShake();
    }

    // =========================================================
    // Future Options menu: 0 = off, 0.5 = half intensity, 1 = default.
    public void SetStrength(float factor)
    {
        _runtimeStrength = float.IsFinite(factor) ?
            Mathf.Clamp(factor, 0f, 1f) : 1f;
        if (_runtimeStrength <= 0f) StopShake();
    }
    #endregion

    #region Motion
    // =========================================================
    // Blend active impulses, cap strength and animate only Camera2D.Offset.
    public override void _Process(double delta)
    {
        if (!_bound || !Settings.Enabled || !_runtimeEnabled ||
            _runtimeStrength <= 0f || Settings.MaximumOffsetPixels <= 0f ||
            !GodotObject.IsInstanceValid(_camera))
        {
            StopShake();
            return;
        }

        float power = 0f;
        for (int i = _impulses.Count - 1; i >= 0; i--)
        {
            Impulse impulse = _impulses[i];
            impulse.Age += (float)delta;
            if (impulse.Age >= impulse.Duration)
            {
                _impulses.RemoveAt(i);
                continue;
            }

            float fadeIn = Settings.FadeInSeconds > 0f
                ? Mathf.Clamp(impulse.Age / Settings.FadeInSeconds, 0f, 1f) : 1f;
            float fadeOutDuration = impulse.Duration * Settings.FadeOutFraction;
            float fadeOut = Mathf.Clamp((impulse.Duration - impulse.Age)
                / fadeOutDuration, 0f, 1f);
            float weighted = impulse.Strength * fadeIn * fadeOut;
            power += weighted * weighted;
        }

        if (_impulses.Count == 0)
        {
            StopShake();
            return;
        }

        float intensity = Mathf.Min(Settings.MaximumStrength,
            Mathf.Sqrt(power) * Settings.StrengthMultiplier * _runtimeStrength);
        _phase += (float)delta * Settings.ShakeFrequencyHz * Mathf.Tau;

        // Layered sine waves avoid jarring, frame-to-frame random jumps.
        Vector2 motion = new(
            Mathf.Sin(_phase * 1.19f) * 0.74f +
                Mathf.Sin(_phase * 2.17f + 1.4f) * 0.26f,
            Mathf.Cos(_phase * 1.07f + 0.6f) * 0.74f +
                Mathf.Sin(_phase * 2.31f + 0.9f) * 0.26f);

        _camera.Offset = _restOffset + motion.LimitLength(1f) *
            (intensity * Settings.MaximumOffsetPixels);
    }

    // =========================================================
    // Preserve exactly the original offset when effects end or are disabled.
    private void StopShake()
    {
        _impulses.Clear();
        SetProcess(false);
        if (_bound && GodotObject.IsInstanceValid(_camera))
            _camera.Offset = _restOffset;
    }
    #endregion
}
