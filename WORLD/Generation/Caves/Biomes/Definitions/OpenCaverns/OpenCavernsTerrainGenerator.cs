// Shapes broad cavern chambers with gently bending connecting passages.
using Godot;

public sealed class OpenCavernsTerrainGenerator : CaveTerrainGenerator
{
    // =========================================================
    // Reuse shared organic chamber and floor sampling.
    public OpenCavernsTerrainGenerator(
        CaveBiomeDefinition definition, uint seed) : base(definition, seed)
    {
    }

    // =========================================================
    // Give broad passages one gentle bend rather than repeated tight turns.
    public override float PassageOffset(float t, uint edgeSeed)
    {
        float direction = (edgeSeed & 1u) == 0 ? -1f : 1f;
        return Mathf.Sin(Mathf.Pi * t) *
            Definition.WindingTiles * direction;
    }
}