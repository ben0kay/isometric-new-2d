// Groups a biome's population recipes without implementing spawning.
// Shared spawners consume these definitions through BiomeDefinition.
using Godot;

[Tool, GlobalClass]
public partial class BiomeContent : Resource
{
    #region Vegetation
    [ExportGroup("Vegetation")]
    [Export] public BiomeVegetation Vegetation { get; set; } = new();
    #endregion

    #region Rocks
    [ExportGroup("Rocks")]
    [Export] public int RocksPerChunk { get; set; } = 8;

    [Export]
    public Godot.Collections.Array<BiomeSpecies> Rocks { get; set; } = new();

    [Export] public BiomePlacementSettings RocksPlacement { get; set; }
    #endregion

    #region Entities
    [ExportGroup("Entities")]
    [Export] public BiomeEnemies Enemies { get; set; }
    #endregion
}