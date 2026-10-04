// Stores a biome's vegetation densities and weighted species lists.
// Species size, artwork and footprint settings live in their own definition files.
using Godot;

[Tool, GlobalClass]
public partial class BiomeVegetation : Resource
{
    #region Plants
    [ExportGroup("Plants")]
    [Export] public int PlantPatches { get; set; } = 3;
    [Export] public int PlantsPerPatch { get; set; } = 4;
    [Export] public Godot.Collections.Array<BiomeSpecies> Plants { get; set; } = new();
    #endregion

    #region Trees
    [ExportGroup("Trees")]
    [Export] public int TreesPerChunk { get; set; } = 3;
    [Export] public Godot.Collections.Array<BiomeSpecies> Trees { get; set; } = new();
    #endregion

    #region Grass
    [ExportGroup("Grass")]
    [Export] public int GrassPatches { get; set; } = 6;
    [Export] public int GrassTuftsPerPatch { get; set; } = 10;
    [Export] public Godot.Collections.Array<BiomeSpecies> Grass { get; set; } = new();
    #endregion

    #region Validation
    // =========================================================
    // Validate only the families this biome is configured to generate.
    public void Validate(string biomeId)
    {
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