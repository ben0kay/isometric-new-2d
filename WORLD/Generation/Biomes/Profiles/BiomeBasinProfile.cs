// Defines biome-owned basin placement, geometry and fill.
// Optional elevation influence reduces frequency and size above lowlands.
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

    #region Elevation
    [ExportGroup("ELEVATION")]
    [Export] public bool ElevationInfluenceEnabled { get; set; }

    // X: height where reduction starts. Y: height where it reaches its limit.
    [Export] public Vector2 ElevationFadeRange { get; set; } = new(0f, 256f);

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float HighElevationProbabilityMultiplier { get; set; } = 0.1f;

    [Export(PropertyHint.Range, "0.1,1,0.01")]
    public float HighElevationSizeMultiplier { get; set; } = 0.4f;
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

    #region Fill
    [ExportGroup("FILL")]
    [Export] public Vector2 InitialFillRange { get; set; } = Vector2.One;
    #endregion

    #region Elevation Queries
    // =========================================================
    // Return frequency and footprint multipliers at the sampled surface height.
    public void SampleElevation(
        float height, out float probability, out float size)
    {
        probability = size = 1f;
        if (!ElevationInfluenceEnabled) return;

        float t = Mathf.Clamp(
            (height - ElevationFadeRange.X) /
            (ElevationFadeRange.Y - ElevationFadeRange.X), 0f, 1f);
        t = t * t * (3f - 2f * t);

        probability = Mathf.Lerp(
            1f, HighElevationProbabilityMultiplier, t);
        size = Mathf.Lerp(1f, HighElevationSizeMultiplier, t);
    }
    #endregion

    #region Validation And Creation
    // =========================================================
    // Reject invalid settings and validate the smallest and largest geometry.
    public void Validate(string biomeId)
    {
        if (!Enabled) return;

        bool invalidElevation = ElevationInfluenceEnabled &&
            (!float.IsFinite(ElevationFadeRange.X) ||
             !float.IsFinite(ElevationFadeRange.Y) ||
             ElevationFadeRange.Y <= ElevationFadeRange.X ||
             !float.IsFinite(HighElevationProbabilityMultiplier) ||
             HighElevationProbabilityMultiplier < 0f ||
             HighElevationProbabilityMultiplier > 1f ||
             !float.IsFinite(HighElevationSizeMultiplier) ||
             HighElevationSizeMultiplier < 0.1f ||
             HighElevationSizeMultiplier > 1f);

        if (invalidElevation ||
            !float.IsFinite(SpawnProbability) ||
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
            Templates == null || Templates.Count == 0)
            throw new InvalidOperationException(
                $"Biome '{biomeId}' has invalid basin settings.");

        float smallestElevationSize = ElevationInfluenceEnabled
            ? HighElevationSizeMultiplier : 1f;

        foreach (WaterDefinition template in Templates)
        {
            if (template == null)
                throw new InvalidOperationException(
                    $"Biome '{biomeId}' contains an empty basin template.");

            CreateDefinition(
                template, SizeMultiplierRange.X, 0f,
                smallestElevationSize).Validate();

            CreateDefinition(
                template, SizeMultiplierRange.Y, 0f).Validate();
        }
    }

    // =========================================================
    // Duplicate templates and shrink banks alongside elevation-reduced footprints.
    public WaterDefinition CreateDefinition(
        WaterDefinition template, float size, float rotation,
        float elevationSize = 1f)
    {
        WaterDefinition definition =
            (WaterDefinition)template.Duplicate(false);

        definition.RadiusTiles = template.RadiusTiles * size * elevationSize;
        definition.RotationDegrees = rotation;
        definition.MaximumHeightVariation = MaximumHeightVariation;
        definition.BasinDepth = BasinDepth;
        definition.ShoreWidthTiles = ShoreWidthTiles * elevationSize;
        definition.WaterSurfaceDrop = WaterSurfaceDrop;
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
