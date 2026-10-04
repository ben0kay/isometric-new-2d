// Stores generation results at an absolute logical tile position.
// Carries a stable biome ID instead of requiring an enum entry for every biome.
public readonly struct WorldSample
{
    #region Results
    public readonly float Height;
    public readonly bool Walkable;
    public readonly string BiomeId;
    public readonly float PlateauWeight;
    #endregion

    #region Construction
    // =========================================================
    // Package generation results without allocating a sample object.
    public WorldSample(
        float height, bool walkable, string biomeId, float plateauWeight)
    {
        Height = height;
        Walkable = walkable;
        BiomeId = biomeId;
        PlateauWeight = plateauWeight;
    }
    #endregion
}