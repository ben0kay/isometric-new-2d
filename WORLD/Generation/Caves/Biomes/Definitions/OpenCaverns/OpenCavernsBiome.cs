// Selects the terrain generator for broad, organic underground chambers.
using Godot;

[Tool, GlobalClass]
public partial class OpenCavernsBiome : CaveBiomeDefinition
{
    // =========================================================
    // Keep this biome's terrain implementation beside its definition.
    public override CaveTerrainGenerator CreateCaveTerrain(uint seed)
    {
        return new OpenCavernsTerrainGenerator(this, seed);
    }
}