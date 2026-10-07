// Samples the same generated cave height used by floor meshes.
// Artwork and aiming stay aligned with the entrance descent.
using Godot;

public partial class CaveTerrainElevation : Node
{
    #region State
    public CaveWorld World { get; set; }
    #endregion

    #region Sampling
    // =========================================================
    // Match floor interpolation beneath a logical world position.
    public float SampleWorldHeight(Vector2 point)
    {
        return World.Generator.HeightAt(
            World.WorldToTile(point), World.RimHeight);
    }
    #endregion
}