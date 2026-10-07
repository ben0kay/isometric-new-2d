// Defines a biome's basin probabilities, size, geometry, fill and water tint.
// Shared templates are duplicated before applying per-instance settings.
using Godot;
using System;

[Tool, GlobalClass]
public partial class BiomeBasinProfile : Resource
{
    #region Placement
    [ExportGroup("PLACEMENT")]
    [Export] public bool Enabled { get; set; }

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float SpawnProbability { get; set; } = 0.25f;

    [Export(PropertyHint.Range, "1,16,1")]
    public int PlacementAttempts { get; set; } = 8;

    [Export] public float MaximumHeightVariation { get; set; } = 2f;
    #endregion

    #region Shapes
    [ExportGroup("SHAPES")]
    [Export]
    public Godot.Collections.Array<WaterDefinition> Templates { get; set; }
        = new();

    [Export] public Vector2 SizeMultiplierRange { get; set; } = new(0.8f, 1.2f);
    [Export] public bool RandomRotation { get; set; } = true;
    #endregion

    #region Geometry
    [ExportGroup("GEOMETRY")]
    [Export] public float BasinDepth { get; set; } = 16f;
    [Export] public float ShoreWidthTiles { get; set; } = 3f;
    [Export] public float WaterSurfaceDrop { get; set; } = 4f;
    #endregion

    #region Fill And Appearance
    [ExportGroup("FILL AND APPEARANCE")]
    [Export] public Vector2 InitialFillRange { get; set; } = Vector2.One;
    [Export] public Color WaterTint { get; set; } = new(0.3f, 0.4f, 0.45f);
    #endregion

    #region Validation And Creation
    // =========================================================
    // Reject invalid profiles before starting world placement.
    public void Validate(string biomeId)
    {
        if (!Enabled) return;

        if (!float.IsFinite(SpawnProbability) ||
            SpawnProbability < 0f || SpawnProbability > 1f ||
            PlacementAttempts < 1 || PlacementAttempts > 16 ||
            !float.IsFinite(MaximumHeightVariation) ||
            MaximumHeightVariation < 0f ||
            !ValidRange(SizeMultiplierRange, 0.01f, 16f) ||
            !ValidRange(InitialFillRange, 0f, 1f) ||
            !float.IsFinite(BasinDepth) || BasinDepth <= 0f ||
            !float.IsFinite(ShoreWidthTiles) || ShoreWidthTiles <= 0f ||
            !float.IsFinite(WaterSurfaceDrop) ||
            WaterSurfaceDrop < MaximumHeightVariation ||
            WaterSurfaceDrop >= BasinDepth ||
            !float.IsFinite(WaterTint.R) ||
            !float.IsFinite(WaterTint.G) ||
            !float.IsFinite(WaterTint.B) ||
            Templates == null || Templates.Count == 0)
            throw new InvalidOperationException(
                $"Biome '{biomeId}' has invalid basin settings.");

        foreach (WaterDefinition template in Templates)
        {
            if (template == null)
                throw new InvalidOperationException(
                    $"Biome '{biomeId}' contains an empty basin template.");

            WaterDefinition smallest =
                CreateDefinition(template, SizeMultiplierRange.X, 0f);
            WaterDefinition largest =
                CreateDefinition(template, SizeMultiplierRange.Y, 0f);

            smallest.Validate();
            largest.Validate();
        }
    }

    // =========================================================
    // Create independent geometry without altering shared template resources.
    public WaterDefinition CreateDefinition(
        WaterDefinition template, float size, float rotation)
    {
        WaterDefinition definition =
            (WaterDefinition)template.Duplicate(false);

        definition.RadiusTiles = template.RadiusTiles * size;
        definition.RotationDegrees = rotation;
        definition.MaximumHeightVariation = MaximumHeightVariation;
        definition.BasinDepth = BasinDepth;
        definition.ShoreWidthTiles = ShoreWidthTiles;
        definition.WaterSurfaceDrop = WaterSurfaceDrop;
        definition.SurfaceTint = WaterTint;
        return definition;
    }

    // =========================================================
    // Validate an ordered finite range.
    private static bool ValidRange(Vector2 range, float minimum, float maximum)
    {
        return float.IsFinite(range.X) && float.IsFinite(range.Y) &&
            range.X >= minimum && range.Y <= maximum && range.X <= range.Y;
    }
    #endregion
}