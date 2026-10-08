// Defines optional surface information for any world artwork.
// Normal maps replace the shared rounded approximation when provided.
using Godot;

[Tool, GlobalClass]
public partial class VisualLightingSettings : Resource
{
    #region Surface
    [ExportGroup("Surface Lighting")]
    [Export] public bool Enabled { get; set; } = true;
    [Export] public Texture2D NormalMap { get; set; }

    [Export(PropertyHint.Range, "0,3,0.05")]
    public float NormalStrength { get; set; } = 1f;

    [Export(PropertyHint.Range, "0,3,0.05")]
    public float ShapeStrength { get; set; } = 1f;

    [Export(PropertyHint.Range, "0,3,0.05")]
    public float Brightness { get; set; } = 1f;
    #endregion

    #region Drawing Bounds
    [ExportGroup("Optional Drawing Bounds")]
    // Leave empty for ordinary sprites, whose bounds are detected automatically.
    [Export] public Rect2 DrawingBounds { get; set; }
    #endregion
}