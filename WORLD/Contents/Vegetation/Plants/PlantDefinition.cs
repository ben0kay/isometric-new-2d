// Defines walkable plant artwork and placement requirements.
// BakedKind chooses an existing fallback; a Visual overrides that fallback.
using Godot;

public enum PlantArtwork { Frond, Shrub }

[Tool, GlobalClass]
public partial class PlantDefinition : WorldObjectDefinition
{
    #region Baked Fallback
    [ExportGroup("Baked Fallback")]
    [Export] public PlantArtwork BakedKind { get; set; }
    [Export] public Vector2 ContactShadowScale { get; set; } = new(0.8f, 0.7f);
    #endregion

    #region Placement
    [ExportGroup("Placement")]
    [Export] public Vector2 Spacing { get; set; } = new(88, 48);
    [Export] public float GroundClearance { get; set; } = 48f;
    #endregion
}