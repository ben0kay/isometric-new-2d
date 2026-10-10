// Lightweight sci-fi health bar for any Health component.
// Only the brief fill transition processes frames; static and full-health bars do not.
using Godot;
using System;

public partial class HealthBar : Node2D
{
    #region State
    private Health _health;
    private HealthBarSettings _settings;
    private float _width, _targetFill, _displayFill, _startFill, _elapsed;
    #endregion

    #region Lifecycle
    // =========================================================
    // Disable idle work; health signals control the bar.
    public override void _Ready()
    {
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Use current health so a restored injured actor immediately has a bar.
    public void Bind(Health health, HealthBarSettings settings)
    {
        Unbind();
        _health = health;
        _settings = settings;
        _width = WidthFor(health.MaxHealth, settings);
        _targetFill = Mathf.Clamp(health.Current / (float)Math.Max(1, health.MaxHealth), 0f, 1f);
        _displayFill = _targetFill;
        Visible = ShouldShow(health.Current, health.MaxHealth);
        health.Changed += OnChanged;
        health.Died += OnDeath;
        QueueRedraw();
    }

    // =========================================================
    // Avoid subscriptions retaining streamed-out actors.
    public override void _ExitTree() => Unbind();

    private void Unbind()
    {
        SetProcess(false);
        if (GodotObject.IsInstanceValid(_health))
        {
            _health.Changed -= OnChanged;
            _health.Died -= OnDeath;
        }
        _health = null;
    }
    #endregion

    #region Signals And Animation
    // =========================================================
    // Recalculate width on maximum-health changes and animate only when visible.
    private void OnChanged(int current, int maximum)
    {
        _width = WidthFor(maximum, _settings);
        _targetFill = Mathf.Clamp(current / (float)Math.Max(1, maximum), 0f, 1f);
        Visible = ShouldShow(current, maximum);
        if (!Visible || !_settings.SmoothChanges || _settings.AnimationSeconds <= 0f)
        {
            _displayFill = _targetFill;
            SetProcess(false);
        }
        else
        {
            _startFill = _displayFill;
            _elapsed = 0f;
            SetProcess(Mathf.Abs(_startFill - _targetFill) > 0.0001f);
        }
        QueueRedraw();
    }

    // =========================================================
    // Stop showing a dead actor's health during its removal animation.
    private void OnDeath()
    {
        Hide();
        SetProcess(false);
    }

    // =========================================================
    // Stop processing at the end of the short damage/healing fill transition.
    public override void _Process(double delta)
    {
        _elapsed += (float)delta;
        float t = Mathf.Clamp(_elapsed / _settings.AnimationSeconds, 0f, 1f);
        _displayFill = Mathf.Lerp(_startFill, _targetFill, t);
        QueueRedraw();
        if (t >= 1f) SetProcess(false);
    }

    private bool ShouldShow(int current, int maximum) =>
        _settings.Enabled && current > 0 && (_settings.ShowAtFullHealth || current < maximum);
    #endregion

    #region Draw
    // =========================================================
    // Logarithmic mapping gives high-HP bosses wider bars without growing indefinitely.
    private static float WidthFor(int hp, HealthBarSettings s)
    {
        double lo = Math.Log(s.HealthAtMinimumWidth);
        double range = Math.Log(s.HealthAtMaximumWidth) - lo;
        float t = (float)Math.Clamp((Math.Log(Math.Max(1, hp)) - lo) / range, 0.0, 1.0);
        return Mathf.Lerp(s.MinimumWidth, s.MaximumWidth, t);
    }

    // =========================================================
    // Draw the fill, thin illuminated edge and angular end caps in one canvas node.
    public override void _Draw()
    {
        if (!Visible || _settings == null) return;
        float half = _width * 0.5f, top = -_settings.BarHeight * 0.5f;
        Rect2 track = new(-half, top, _width, _settings.BarHeight);
        DrawRect(track, _settings.Background);
        float fillWidth = Mathf.Clamp(_displayFill, 0f, 1f) * _width;
        Color fill = _targetFill <= _settings.CriticalThreshold ? _settings.Critical
            : _targetFill <= _settings.WarningThreshold ? _settings.Warning : _settings.Healthy;

        if (fillWidth > 0f)
        {
            DrawRect(new Rect2(-half, top, fillWidth, _settings.BarHeight), fill);
            DrawLine(new Vector2(-half, top + 1f),
                new Vector2(-half + fillWidth, top + 1f),
                new Color(1f, 1f, 1f, 0.28f));
        }
        DrawRect(track, _settings.Border, false, 1f);
        DrawLine(new Vector2(-half - 3f, top - 2f),
            new Vector2(-half - 3f, top + _settings.BarHeight + 2f), _settings.Border);
        DrawLine(new Vector2(half + 3f, top - 2f),
            new Vector2(half + 3f, top + _settings.BarHeight + 2f), _settings.Border);
    }
    #endregion
}
