// Selects the terrain generator for narrow, winding underground passages.
using Godot;

[Tool, GlobalClass]
public partial class WindingPassagesBiome : CaveBiomeDefinition
{
    // =========================================================
    // Keep this biome's terrain implementation beside its definition.
    public override CaveTerrainGenerator CreateCaveTerrain(uint seed)
    {
        return new WindingPassagesTerrainGenerator(this, seed);
    }
}