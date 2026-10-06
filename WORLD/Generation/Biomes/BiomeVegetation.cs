// Stores biome vegetation species and base frequencies.
// Empty placement resources inherit from CONFIG/Biomes/BiomeDefaults.tres.
using Godot;

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
    #endregion

    #region Trees
    [ExportGroup("Trees")]
    [Export] public int TreesPerChunk { get; set; } = 3;

    [Export] public Godot.Collections.Array<BiomeSpecies> Trees { get; set; }
        = new();

    [Export] public BiomePlacementSettings TreesPlacement { get; set; }
    #endregion

    #region Grass
    [ExportGroup("Grass")]
    [Export] public int GrassPatches { get; set; } = 6;
    [Export] public int GrassTuftsPerPatch { get; set; } = 10;

    [Export] public Godot.Collections.Array<BiomeSpecies> Grass { get; set; }
        = new();

    [Export] public BiomePlacementSettings GrassPlacement { get; set; }
    #endregion

    #region Validation
    // =========================================================
    // Validate resolved placement defaults and the biome's species lists.
    public void Validate(string biomeId)
    {
        BiomeDefaults defaults = BiomeDefaults.GetShared();

        (PlantsPlacement ?? defaults.Plants).Validate($"{biomeId}/Plants");
        (TreesPlacement ?? defaults.Trees).Validate($"{biomeId}/Trees");
        (GrassPlacement ?? defaults.Grass).Validate($"{biomeId}/Grass");

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