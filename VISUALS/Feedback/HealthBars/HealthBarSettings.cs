// Inspector-editable shared world health bar settings; species may override the default.
using Godot;
using System;

[Tool, GlobalClass]
public partial class HealthBarSettings : Resource
{
    #region Visibility
    [ExportGroup("Visibility")]
    [Export] public bool Enabled { get; set; } = true;
    [Export] public bool ShowAtFullHealth { get; set; }
    #endregion

    #region Sizing
    [ExportGroup("Sizing")]
    [Export(PropertyHint.Range, "16,300,1,or_greater")] public float MinimumWidth { get; set; } = 36f;
    [Export(PropertyHint.Range, "16,300,1,or_greater")] public float MaximumWidth { get; set; } = 96f;
    [Export(PropertyHint.Range, "1,100000,1,or_greater")] public int HealthAtMinimumWidth { get; set; } = 50;
    [Export(PropertyHint.Range, "2,100000,1,or_greater")] public int HealthAtMaximumWidth { get; set; } = 2000;
    [Export(PropertyHint.Range, "2,16,1")] public float BarHeight { get; set; } = 5f;
    [Export(PropertyHint.Range, "0,80,1,or_greater")] public float VerticalGap { get; set; } = 10f;
    // Positive values move the bar down; negative values move it up.
    [Export(PropertyHint.Range, "-120,120,1")]
    public float VerticalOffset { get; set; } = 0f;
    #endregion

    #region Color
    [ExportGroup("Color")]
    [Export] public Color Background { get; set; } = new("#101c29");
    [Export] public Color Border { get; set; } = new("#688da2");
    [Export] public Color Healthy { get; set; } = new("#4de0bb");
    [Export] public Color Warning { get; set; } = new("#eeab62");
    [Export] public Color Critical { get; set; } = new("#ff6666");
    [Export(PropertyHint.Range, "0,1,0.01")] public float WarningThreshold { get; set; } = 0.5f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float CriticalThreshold { get; set; } = 0.25f;
    #endregion

    #region Animation
    [ExportGroup("Animation")]
    [Export] public bool SmoothChanges { get; set; } = true;
    [Export(PropertyHint.Range, "0,1,0.01")] public float AnimationSeconds { get; set; } = 0.15f;
    #endregion

    #region Validation
    // =========================================================
    // Catch invalid tuning before drawing; feedback failures must not break combat.
    public void Validate()
    {
        if (!float.IsFinite(MinimumWidth) || MinimumWidth < 1f ||
            !float.IsFinite(MaximumWidth) || MaximumWidth < MinimumWidth ||
            HealthAtMinimumWidth < 1 || HealthAtMaximumWidth <= HealthAtMinimumWidth ||
            !float.IsFinite(BarHeight) || BarHeight < 1f ||
            !float.IsFinite(VerticalGap) || VerticalGap < 0f ||
            !float.IsFinite(VerticalOffset) ||
            !float.IsFinite(WarningThreshold) || WarningThreshold < 0f || WarningThreshold > 1f ||
            !float.IsFinite(CriticalThreshold) || CriticalThreshold < 0f ||
            CriticalThreshold > WarningThreshold ||
            !float.IsFinite(AnimationSeconds) || AnimationSeconds < 0f)
            throw new InvalidOperationException("Invalid HealthBarSettings resource.");
    }
    #endregion
}
