// Stores biome vegetation species, base counts and placement recipes.
// Shared generators execute these recipes for every biome.
using Godot;
using System;

[Tool, GlobalClass]
public partial class BiomeVegetation : Resource
{
    #region Plants
    [ExportGroup("Plants")]
    [Export] public int PlantPatches { get; set; } = 3;
    [Export] public int PlantsPerPatch { get; set; } = 4;
    [Export] public Godot.Collections.Array<BiomeSpecies> Plants { get; set; }
        = new();

    [Export] public BiomePlacementSettings PlantsPlacement { get; set; }
        = new()
        {
            Distribution = BiomeDistribution.Clumps,
            ClumpsPerChunk = 3,
            ClumpRadiusTiles = 1.8f
        };
    #endregion

    #region Trees
    [ExportGroup("Trees")]
    [Export] public int TreesPerChunk { get; set; } = 3;
    [Export] public Godot.Collections.Array<BiomeSpecies> Trees { get; set; }
        = new();

    [Export] public BiomePlacementSettings TreesPlacement { get; set; }
        = new();
    #endregion

    #region Grass
    [ExportGroup("Grass")]
    [Export] public int GrassPatches { get; set; } = 6;
    [Export] public int GrassTuftsPerPatch { get; set; } = 10;
    [Export] public Godot.Collections.Array<BiomeSpecies> Grass { get; set; }
        = new();

    [Export] public BiomePlacementSettings GrassPlacement { get; set; }
        = new()
        {
            Distribution = BiomeDistribution.Clumps,
            ClumpsPerChunk = 6,
            ClumpRadiusTiles = 1.3f
        };
    #endregion

    #region Validation
    // =========================================================
    // Validate species and independent placement recipes.
    public void Validate(string biomeId)
    {
        if (PlantsPlacement == null || TreesPlacement == null ||
            GrassPlacement == null)
            throw new InvalidOperationException(
                $"Biome '{biomeId}' requires all vegetation placement settings.");

        PlantsPlacement.Validate($"{biomeId}/Plants");
        TreesPlacement.Validate($"{biomeId}/Trees");
        GrassPlacement.Validate($"{biomeId}/Grass");

        BiomeSpecies.Validate<PlantDefinition>(
            Plants, $"{biomeId}/Plants",
            PlantPatches > 0 && PlantsPerPatch > 0);

        BiomeSpecies.Validate<TreeDefinition>(
            Trees, $"{biomeId}/Trees", TreesPerChunk > 0);

        BiomeSpecies.Validate<GrassDefinition>(
            Grass, $"{biomeId}/Grass",
            GrassPatches > 0 && GrassTuftsPerPatch > 0);
    }
    #endregion
}