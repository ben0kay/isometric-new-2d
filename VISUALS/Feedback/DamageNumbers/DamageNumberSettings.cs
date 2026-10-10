// Inspector-editable configuration for floating, merging world damage numbers.
// A separate runtime visibility switch will let a future Options menu hide them.
using Godot;
using System;

[Tool, GlobalClass]
public partial class DamageNumberSettings : Resource
{
    #region Visibility
    [ExportGroup("Visibility")]
    [Export] public bool Enabled { get; set; } = true;
    #endregion

    #region Typography
    [ExportGroup("Typography")]
    [Export(PropertyHint.Range, "8,48,1")]
    public int FontSize { get; set; } = 17;
    [Export] public Color TextColor { get; set; } = new("#ff8f83");
    [Export] public Color OutlineColor { get; set; } = new("#101925");
    [Export(PropertyHint.Range, "0,8,1")]
    public int OutlineSize { get; set; } = 2;
    #endregion

    #region Placement And Lifetime
    [ExportGroup("Placement And Lifetime")]
    [Export(PropertyHint.Range, "0,120,1,or_greater")]
    public float VerticalGap { get; set; } = 38f;
    [Export(PropertyHint.Range, "0,160,1,or_greater")]
    public float FloatDistance { get; set; } = 25f;
    [Export(PropertyHint.Range, "0.1,5,0.05,or_greater")]
    public float DisplaySeconds { get; set; } = 0.9f;
    [Export(PropertyHint.Range, "0,5,0.05,or_greater")]
    public float MergeWindowSeconds { get; set; } = 0.35f;
    [Export(PropertyHint.Range, "0,1,0.05")]
    public float FadeStartFraction { get; set; } = 0.55f;
    #endregion

    #region Pop Effect
    [ExportGroup("Pop Effect")]
    [Export] public bool PopEnabled { get; set; } = true;
    [Export(PropertyHint.Range, "1,2,0.05")]
    public float PopScale { get; set; } = 1.2f;
    [Export(PropertyHint.Range, "0.01,1,0.01")]
    public float PopSeconds { get; set; } = 0.12f;
    #endregion

    #region Validation
    // =========================================================
    // Reject invalid settings before an actor subscribes to damage events.
    public void Validate()
    {
        if (FontSize < 8 || FontSize > 128 ||
            OutlineSize < 0 || OutlineSize > 16 ||
            !float.IsFinite(VerticalGap) || VerticalGap < 0f ||
            !float.IsFinite(FloatDistance) || FloatDistance < 0f ||
            !float.IsFinite(DisplaySeconds) || DisplaySeconds <= 0f ||
            !float.IsFinite(MergeWindowSeconds) || MergeWindowSeconds < 0f ||
            !float.IsFinite(FadeStartFraction) ||
            FadeStartFraction < 0f || FadeStartFraction >= 1f ||
            !float.IsFinite(PopScale) || PopScale < 1f ||
            !float.IsFinite(PopSeconds) || PopSeconds <= 0f)
            throw new InvalidOperationException("Invalid damage number settings.");
    }
    #endregion
}
