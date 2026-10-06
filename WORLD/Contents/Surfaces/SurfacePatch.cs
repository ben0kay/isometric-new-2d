// Represents a placed surface independently from its water/snow/quicksand artwork.
using Godot;

public partial class SurfacePatch : Node2D
{
    #region State
    public SurfaceDefinition Definition { get; set; }
    public Vector2 TileCentre { get; set; }
    public Vector2 TileSize { get; set; }
    public float Phase { get; set; }
    public float SurfaceHeight { get; set; }
    public SurfaceWorld World { get; set; }
    public float RotationRadians =>
        Mathf.DegToRad(Definition.RotationDegrees);
    #endregion

    #region Queries
    // =========================================================
    // Evaluate the same elliptical footprint used by this surface's shader.
public virtual float GetInfluence(Vector2 tile)
    {
        return SurfaceGeometry.Influence(tile - TileCentre,
            Definition.RadiusTiles, RotationRadians, Phase);
    }

    // =========================================================
    // Convert normalized shape coordinates into this flat isometric surface.
    public Vector2 LocalPoint(Vector2 normalized)
    {
        Vector2 tile = new(
            normalized.X * Definition.RadiusTiles.X,
            normalized.Y * Definition.RadiusTiles.Y);
        return IsoGrid.TileToWorld(tile.Rotated(RotationRadians), TileSize);
    }

    // =========================================================
    // Expose unique boundary vertices for the collision debug overlay.
    public virtual Vector2[] GetDebugOutline()
    {
        const int segments = 48;
        Vector2[] points = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float angle = Mathf.Tau * i / segments;
            Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));
            points[i] = ToGlobal(LocalPoint(
                direction * SurfaceGeometry.Edge(angle, Phase)));
        }
        return points;
    }
    #endregion

    #region Lifecycle
    // =========================================================
    // Remove departing patches from gameplay queries.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(World)) World.Unregister(this);
    }
    #endregion
}