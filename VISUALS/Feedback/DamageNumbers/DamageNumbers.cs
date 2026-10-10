// A single reusable floating number per Health-owning actor.
// Rapid hits update the same number; no per-hit nodes and no idle processing.
using Godot;
using System;

public partial class DamageNumbers : Node2D
{
    #region State
    private Health _health;
    private DamageNumberSettings _settings;
    private Label _label;
    private Vector2 _labelOrigin;
    private long _total;
    private float _elapsed, _sinceHit;
    private bool _active, _runtimeEnabled = true;
    #endregion

    #region Lifecycle
    // =========================================================
    // Stay completely idle until a real HP-loss signal arrives.
    public override void _Ready()
    {
        SetProcess(false);
        SetPhysicsProcess(false);
        Hide();
    }

    // =========================================================
    // Listen to resolved HP loss, not an attack's requested damage amount.
    public void Bind(Health health, DamageNumberSettings settings)
    {
        Unbind();
        _health = health;
        _settings = settings;
        _runtimeEnabled = true;
        health.DamageApplied += OnDamageApplied;
        health.Died += OnDeath;
        ResetDisplay();
    }

    // =========================================================
    // Release event subscriptions when an entity unloads, dies or is removed.
    public override void _ExitTree() => Unbind();

    private void Unbind()
    {
        if (GodotObject.IsInstanceValid(_health))
        {
            _health.DamageApplied -= OnDamageApplied;
            _health.Died -= OnDeath;
        }
        _health = null;
        ResetDisplay();
    }
    #endregion

    #region Accumulation
    // =========================================================
    // Hits inside the merge window update one number; later hits replace it.
    private void OnDamageApplied(int actualLoss)
    {
        if (!_runtimeEnabled || !_settings.Enabled || actualLoss <= 0) return;

        bool merge = _active && _sinceHit <= _settings.MergeWindowSeconds;
        _total = merge ? Math.Min(long.MaxValue - actualLoss, _total) + actualLoss
            : actualLoss;

        EnsureLabel();
        _label.Text = "-" + _total.ToString();
        _elapsed = 0f;
        _sinceHit = 0f;
        _active = true;
        Show();
        UpdatePresentation();
        SetProcess(true);
    }

    // =========================================================
    // End the previous display immediately when the owner dies.
    private void OnDeath() => ResetDisplay();

    // =========================================================
    // Options-menu-ready runtime gate; .tres Enabled remains the master toggle.
    public void SetEnabled(bool enabled)
    {
        _runtimeEnabled = enabled;
        if (!enabled) ResetDisplay();
    }

    private void ResetDisplay()
    {
        _active = false;
        _total = 0;
        _elapsed = 0f;
        _sinceHit = 0f;
        SetProcess(false);
        Hide();
    }
    #endregion

    #region Presentation
    // =========================================================
    // Create the single Label only when the actor first takes damage.
    private void EnsureLabel()
    {
        if (GodotObject.IsInstanceValid(_label)) return;

        float width = Math.Max(160f, _settings.FontSize * 13f);
        float height = Math.Max(32f, _settings.FontSize * 2f);
        _labelOrigin = new Vector2(-width * 0.5f, -height * 0.5f);
        _label = new Label
        {
            Name = "DamageValue",
            Position = _labelOrigin,
            Size = new Vector2(width, height),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _label.PivotOffset = _label.Size * 0.5f;
        _label.AddThemeFontSizeOverride("font_size", _settings.FontSize);
        _label.AddThemeColorOverride("font_color", _settings.TextColor);
        _label.AddThemeColorOverride("font_outline_color", _settings.OutlineColor);
        _label.AddThemeConstantOverride("outline_size", _settings.OutlineSize);
        AddChild(_label);
    }

    // =========================================================
    // Process only while floating and fading; then return to idle.
    public override void _Process(double delta)
    {
        _elapsed += (float)delta;
        _sinceHit += (float)delta;

        if (_elapsed >= _settings.DisplaySeconds)
        {
            ResetDisplay();
            return;
        }
        UpdatePresentation();
    }

    // =========================================================
    // A quick size pulse, gentle upward float and delayed fade.
    private void UpdatePresentation()
    {
        float progress = Mathf.Clamp(_elapsed / _settings.DisplaySeconds, 0f, 1f);
        float fadeStart = _settings.FadeStartFraction;
        float alpha = progress <= fadeStart
            ? 1f : Mathf.Clamp((1f - progress) / (1f - fadeStart), 0f, 1f);

        float pop = 1f;
        if (_settings.PopEnabled && _elapsed < _settings.PopSeconds)
        {
            float t = _elapsed / _settings.PopSeconds;
            pop = Mathf.Lerp(_settings.PopScale, 1f, t);
        }

        _label.Position = _labelOrigin + Vector2.Up * (_settings.FloatDistance * progress);
        _label.Scale = new Vector2(pop, pop);
        _label.Modulate = new Color(1f, 1f, 1f, alpha);
    }
    #endregion
}
