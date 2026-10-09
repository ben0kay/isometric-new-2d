// Optional biome-owned colours, independent of terrain and population settings.
// Unassigned profiles and disabled sections retain the artwork's normal colours.
using Godot;
using System;

[Tool, GlobalClass]
public partial class BiomeColourProfile : Resource
{
    public static readonly Color DefaultGroundGrassShade = new("#16372d");
    public static readonly Color DefaultGroundGrassMain = new("#285449");

    [ExportGroup("Colours")]
    [ExportSubgroup("Ground Grass")]
    [Export] public bool TintGroundGrass { get; set; }
    [Export] public Color GroundGrassShade { get; set; } = DefaultGroundGrassShade;
    [Export] public Color GroundGrassMain { get; set; } = DefaultGroundGrassMain;

    [ExportSubgroup("Grass")]
    [Export] public bool TintGrass { get; set; }
    [Export] public Color GrassTint { get; set; } = new("#527b68");
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float GrassTintStrength { get; set; } = 1f;

    [ExportSubgroup("Water")]
    [Export] public bool TintWater { get; set; }
    [Export] public Color WaterTint { get; set; } = new(0.3f, 0.4f, 0.45f, 1f);

    // Add plant and tree subsections here when their renderers support tinting.

    // =========================================================
    // Validate once with the biome, before generation starts sampling colours.
    public void Validate(string biomeId)
    {
        if (TintGroundGrass)
        {
            ValidateColour(GroundGrassShade, biomeId, nameof(GroundGrassShade));
            ValidateColour(GroundGrassMain, biomeId, nameof(GroundGrassMain));
        }

        if (TintGrass)
        {
            ValidateColour(GrassTint, biomeId, nameof(GrassTint));
            if (!float.IsFinite(GrassTintStrength) ||
                GrassTintStrength < 0f || GrassTintStrength > 1f)
                throw new InvalidOperationException(
                    $"Biome '{biomeId}': GrassTintStrength must be between zero and one.");
        }

        if (TintWater) ValidateColour(WaterTint, biomeId, nameof(WaterTint));
    }

    // =========================================================
    // Tint alpha is ignored; surface transparency remains a rendering setting.
    private static void ValidateColour(Color colour, string biomeId, string label)
    {
        if (!float.IsFinite(colour.R) || !float.IsFinite(colour.G) ||
            !float.IsFinite(colour.B) ||
            colour.R < 0f || colour.R > 1f ||
            colour.G < 0f || colour.G > 1f ||
            colour.B < 0f || colour.B > 1f)
            throw new InvalidOperationException(
                $"Biome '{biomeId}': {label} requires RGB values between zero and one.");
    }
}
