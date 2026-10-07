// Defines whether a surface biome permits cave holes.
// Biomes can inherit the shared profile or provide their own override.
using Godot;
using System;

[Tool, GlobalClass]
public partial class CaveHoleProfile : Resource
{
    #region Configuration
    [ExportGroup("Placement")]
    [Export] public bool Enabled { get; set; } = true;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float Chance { get; set; } = 1f;

    [ExportGroup("Surface Clearance")]
    [Export] public float ClearRadius { get; set; } = 100f;
    [Export] public float MaximumHeightVariation { get; set; } = 32f;
    #endregion

    #region Validation
    // =========================================================
    // Reject invalid placement settings before sampling terrain.
    public void Validate()
    {
        if (!float.IsFinite(Chance) ||
            Chance < 0f || Chance > 1f ||
            !float.IsFinite(ClearRadius) ||
            ClearRadius < 32f ||
            !float.IsFinite(MaximumHeightVariation) ||
            MaximumHeightVariation < 0f)
        {
            throw new InvalidOperationException(
                "Invalid cave-hole profile. Check chance, clearance and height variation.");
        }
    }
    #endregion
}