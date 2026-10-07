// Samples the test cave's descending tunnel and level chamber.
// Interpolation matches the floor's tile-corner geometry.
using Godot;

public partial class CaveTerrainElevation : Node
{
    #region State
    public CaveWorld World { get; set; }
    #endregion

    #region Sampling
    // =========================================================
    // Return the rendered floor height beneath a logical world position.
    public float SampleWorldHeight(Vector2 point)
    {
        float x = World.WorldToTile(point).X;
        float centre = Mathf.Floor(x + 0.5f);
        float fraction = x - centre + 0.5f;
        return Mathf.Lerp(
            RampHeight(centre - 0.5f),
            RampHeight(centre + 0.5f), fraction);
    }

    // =========================================================
    // Ease from entrance elevation to the underground chamber.
    private float RampHeight(float x)
    {
        float t = Mathf.Clamp(x / World.TunnelLengthTiles, 0f, 1f);
        t = t * t * (3f - 2f * t);
        return World.RimHeight - World.DepthPixels * t;
    }
    #endregion
}