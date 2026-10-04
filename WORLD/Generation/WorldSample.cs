// Stores generation results at an absolute logical tile position.
// Height is visual elevation; walkability describes the logical ground plane.
public readonly struct WorldSample
{
    #region Results
    public readonly float Height;
    public readonly bool Walkable;
    public readonly BiomeType Biome;
    public readonly float PlateauWeight;
    #endregion

    #region Construction
    // =========================================================
    // Package terrain and biome results without heap allocations.
    public WorldSample(
        float height, bool walkable, BiomeType biome, float plateauWeight)
    {
        Height = height;
        Walkable = walkable;
        Biome = biome;
        PlateauWeight = plateauWeight;
    }
    #endregion
}