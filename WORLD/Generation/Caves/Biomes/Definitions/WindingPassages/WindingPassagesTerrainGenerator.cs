// Shapes repeated passage bends while preserving the shared room connections.
using Godot;

public sealed class WindingPassagesTerrainGenerator : CaveTerrainGenerator
{
    // =========================================================
    // Reuse shared chamber, boundary and floor sampling.
    public WindingPassagesTerrainGenerator(
        CaveBiomeDefinition definition, uint seed) : base(definition, seed)
    {
    }

    // =========================================================
    // Introduce several smooth bends with fixed endpoints.
    public override float PassageOffset(float t, uint edgeSeed)
    {
        float phase = (edgeSeed & 65535u) / 65535f * Mathf.Tau;

        return Mathf.Sin(Mathf.Pi * t) *
            Mathf.Sin(Mathf.Tau * t * 1.5f + phase) *
            Definition.WindingTiles;
    }
}