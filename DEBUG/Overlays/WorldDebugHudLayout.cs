// Stacks world diagnostics at the top right, including the shared world clock.
// Refreshes text and layout four times per second without an F1 toggle.
using Godot;
using System;
using System.Collections.Generic;

public partial class WorldDebugHudLayout : Node
{
    #region Configuration
    [Export] public float Margin { get; set; } = 16f;
    [Export] public float LabelSpacing { get; set; } = 8f;
    #endregion

    #region State
    private readonly List<Label> _labels = new();
    private Label _timeLabel;
    private WorldClock _clock;
    private double _timer;
    #endregion

    #region Lifecycle
    // =========================================================
    // Add the clock to the existing HUD rather than creating another overlay.
    public override void _Ready()
    {
        Node hud = GetParent();
        _timeLabel = hud.GetNodeOrNull<Label>("WorldTime");

        if (_timeLabel == null)
        {
            _timeLabel = new Label
            {
                Name = "WorldTime",
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            hud.AddChild(_timeLabel);
        }

        _timeLabel.AddThemeFontSizeOverride("font_size", 16);
        _timeLabel.AddThemeColorOverride(
            "font_color", new Color("#dcebf3"));
        _timeLabel.AddThemeColorOverride(
            "font_shadow_color", new Color(0f, 0f, 0f, 0.8f));
        _timeLabel.AddThemeConstantOverride("shadow_offset_x", 1);
        _timeLabel.AddThemeConstantOverride("shadow_offset_y", 1);
    }

    // =========================================================
    // Update clock text and keep all diagnostics stacked without overlap.
    public override void _Process(double delta)
    {
        _timer -= delta;
        if (_timer > 0.0) return;
        _timer = 0.25;

        UpdateTime();

        _labels.Clear();
        Node hud = GetParent();
        AddLabel(hud.GetNodeOrNull<Label>("FPS"));
        AddLabel(hud.GetNodeOrNull<Label>("ChunkInfo"));
        AddLabel(_timeLabel);

        WorldLayerController layers = WorldLayerController.Find(this);
        CanvasLayer layerHud =
            layers?.GetNodeOrNull<CanvasLayer>("LayerHUD");

        if (layerHud != null)
            foreach (Node child in layerHud.GetChildren())
                if (child is Label label) AddLabel(label);

        float right = GetViewport().GetVisibleRect().Size.X - Margin;
        float top = Margin;

        foreach (Label label in _labels)
        {
            label.HorizontalAlignment = HorizontalAlignment.Right;
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
            label.SetAnchorsPreset(Control.LayoutPreset.TopLeft);

            Vector2 size = label.GetCombinedMinimumSize();
            label.Size = size;
            label.Position = new Vector2(right - size.X, top);
            top += size.Y + LabelSpacing;
        }
    }
    #endregion

    #region Clock
    // =========================================================
    // Display the existing simulation clock and next lighting transition.
    private void UpdateTime()
    {
        if (!GodotObject.IsInstanceValid(_clock))
            _clock = WorldEclipse.Find(this)
                ?.GetNodeOrNull<WorldClock>("WorldClock");

        if (!GodotObject.IsInstanceValid(_clock) ||
            _clock.CycleSeconds <= 0.0)
        {
            _timeLabel.Text = "World time: waiting...";
            return;
        }

        double cycleTime = _clock.ElapsedSeconds % _clock.CycleSeconds;
        double daylight = _clock.CycleSeconds *
            (1.0 - _clock.EclipseFraction);

        bool eclipse = _clock.EclipseFraction > 0f &&
            cycleTime > daylight;

        long cycle = (long)Math.Floor(
            _clock.ElapsedSeconds / _clock.CycleSeconds) + 1;

        string transition = _clock.EclipseFraction <= 0f
            ? "Eclipse disabled"
            : eclipse
                ? $"Daylight in {FormatTime(_clock.CycleSeconds - cycleTime)}"
                : $"Eclipse in {FormatTime(daylight - cycleTime)}";

        _timeLabel.Text =
            $"Cycle {cycle} · {(eclipse ? "ECLIPSE" : "DAYLIGHT")} · " +
            $"{FormatTime(cycleTime)}\n{transition}";
    }

    // =========================================================
    // Format elapsed minutes and seconds without rolling over at one hour.
    private static string FormatTime(double seconds)
    {
        long total = (long)Math.Ceiling(Math.Max(0.0, seconds));
        return $"{total / 60:00}:{total % 60:00}";
    }
    #endregion

    #region Labels
    // =========================================================
    // Include each valid diagnostic label once.
    private void AddLabel(Label label)
    {
        if (GodotObject.IsInstanceValid(label) && !_labels.Contains(label))
            _labels.Add(label);
    }
    #endregion
}