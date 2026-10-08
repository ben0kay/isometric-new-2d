// Stores optional rendering behaviour for imported vegetation images.
// Species share these settings without adding individual processing nodes.
using Godot;

[Tool, GlobalClass]
public partial class VegetationVisualSettings : Resource
{
    #region Lighting
    [ExportGroup("Lighting")]
    [Export] public bool LightingEnabled { get; set; } = true;
    [Export] public Texture2D NormalMap { get; set; }

    [Export(PropertyHint.Range, "0,2,0.05")]
    public float NormalStrength { get; set; } = 0.65f;

    [Export(PropertyHint.Range, "0,2,0.05")]
    public float Brightness { get; set; } = 0.8f;
    #endregion

    #region Movement
    [ExportGroup("Wind")]
    [Export] public bool WindEnabled { get; set; } = true;
    [Export] public float WindStrength { get; set; } = 4f;
    [Export] public float WindSpeed { get; set; } = 1.4f;

    [ExportGroup("Player Brushing")]
    [Export] public bool BrushingEnabled { get; set; } = true;
    [Export] public float BrushStrength { get; set; } = 9f;
    #endregion

    #region Visibility
    [ExportGroup("Visibility")]
    [Export] public bool FadeBehindPlayer { get; set; } = true;
    #endregion
}