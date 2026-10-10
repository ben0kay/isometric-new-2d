// Inspector-editable style, PNG slot and animation timing for the world loader.
// The optional image path is safe before the PNG exists in the project.
using Godot;
using System;

[Tool, GlobalClass]
public partial class LoadingScreenSettings : Resource
{
    #region Presentation
    [ExportGroup("Presentation")]
    [Export] public bool Enabled { get; set; } = true;
    [Export] public string Title { get; set; } = "FRACTURED HORIZONS";
    [Export] public string Subtitle { get; set; } = "INITIALIZING PLANETARY ENVIRONMENT";
    [Export] public bool ShowTechnicalDetails { get; set; } = true;
    [Export] public bool ShowPercentage { get; set; } = true;
    #endregion

    #region Backdrop
    [ExportGroup("Backdrop")]
    [Export(PropertyHint.File, "*.png")]
    public string BackdropPath { get; set; } =
        "res://UI/Loading/Artwork/LoadingBackdrop.png";
    [Export(PropertyHint.Range, "0,1,0.05")]
    public float BackdropDarken { get; set; } = 0.62f;
    [Export] public Color FallbackBackground { get; set; } = new("#0a1622");
    #endregion

    #region Palette
    [ExportGroup("Palette")]
    [Export] public Color Accent { get; set; } = new("#60ccda");
    [Export] public Color TextColor { get; set; } = new("#e9f6fa");
    [Export] public Color SecondaryText { get; set; } = new("#8faabb");
    [Export] public Color GridColor { get; set; } = new("#477388");
    #endregion

    #region Animation
    [ExportGroup("Animation")]
    [Export(PropertyHint.Range, "0.02,1,0.01")]
    public float StatusRefreshSeconds { get; set; } = 0.12f;
    [Export(PropertyHint.Range, "1,30,0.5")]
    public float ProgressCatchupSpeed { get; set; } = 6f;
    [Export(PropertyHint.Range, "0,2,0.05")]
    public float FadeOutSeconds { get; set; } = 0.45f;
    [Export(PropertyHint.Range, "0,4,0.1")]
    public float ScanSpeed { get; set; } = 0.85f;
    #endregion

    #region Validation
    // =========================================================
    // Invalid UI settings should not delay gameplay.
    public void Validate()
    {
        if (!float.IsFinite(BackdropDarken) || BackdropDarken < 0f || BackdropDarken > 1f ||
            !float.IsFinite(StatusRefreshSeconds) || StatusRefreshSeconds <= 0f ||
            !float.IsFinite(ProgressCatchupSpeed) || ProgressCatchupSpeed <= 0f ||
            !float.IsFinite(FadeOutSeconds) || FadeOutSeconds < 0f ||
            !float.IsFinite(ScanSpeed) || ScanSpeed < 0f)
            throw new InvalidOperationException("Invalid LoadingScreenSettings.");
    }
    #endregion
}
