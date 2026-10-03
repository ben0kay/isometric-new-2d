// Displays FPS at a modest refresh rate, turning red below the target.
using Godot;

public partial class FpsLabel : Label
{
    #region Configuration
    [Export] public int TargetFps { get; set; } = 60;
    [Export] public double RefreshInterval { get; set; } = 0.25;
    [Export] public Color NormalColor { get; set; } = new("#b8e3ea");
    [Export] public Color LowColor { get; set; } = new("#ff6060");

    private double _refreshTimer;
    private bool _low;
    private int _lastFps = -1;
    #endregion

    #region Lifecycle
    // =========================================================
    // Set the initial appearance and display the first reading.
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeColorOverride("font_color", NormalColor);
        UpdateDisplay();
    }

    // =========================================================
    // Refresh periodically instead of changing label text every frame.
    public override void _Process(double delta)
    {
        _refreshTimer -= delta;
        if (_refreshTimer > 0.0) return;
        _refreshTimer = System.Math.Max(0.05, RefreshInterval);
        UpdateDisplay();
    }
    #endregion

    #region Display
    // =========================================================
    // Update the FPS text and change colour only when needed.
    private void UpdateDisplay()
    {
        int fps = (int)Engine.GetFramesPerSecond();
        if (fps != _lastFps)
        {
            Text = $"FPS: {fps}";
            _lastFps = fps;
        }

        bool low = fps < TargetFps;
        if (low == _low) return;
        _low = low;
        AddThemeColorOverride("font_color", low ? LowColor : NormalColor);
    }
    #endregion
}