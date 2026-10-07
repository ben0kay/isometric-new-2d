// Describes one surface hole connected to the shared cave network.
// Its surface clearance is reserved independently from rendered artwork.
using Godot;

public sealed class CaveHole
{
    #region Data
    public readonly string Id;
    public readonly Vector2 MouthTile, Direction;
    public readonly Vector2 SurfacePosition;
    public readonly Vector2I AnchorCell;
    public readonly float RimHeight, TunnelLength;

    public float SurfaceClearRadius { get; set; } = 100f;
    public CaveEntrance Marker { get; set; }
    #endregion

    #region Construction
    // =========================================================
    // Store one cardinal entrance direction and its surface/cave pairing.
    public CaveHole(
        string id, Vector2 mouthTile, Vector2 direction,
        Vector2 surfacePosition, float rimHeight,
        float tunnelLength, Vector2I anchorCell)
    {
        Id = id;
        MouthTile = mouthTile;
        Direction = direction;
        SurfacePosition = surfacePosition;
        RimHeight = rimHeight;
        TunnelLength = tunnelLength;
        AnchorCell = anchorCell;
    }
    #endregion

    #region Coordinates
    // =========================================================
    // Return distance along the tunnel and distance across it.
    public Vector2 Coordinates(Vector2 caveTile)
    {
        Vector2 offset = caveTile - MouthTile;
        Vector2 across = new(-Direction.Y, Direction.X);
        return new Vector2(offset.Dot(Direction), offset.Dot(across));
    }

    // =========================================================
    // Convert entrance-relative coordinates into shared cave coordinates.
    public Vector2 TileAt(float along, float across = 0f)
    {
        return MouthTile + Direction * along +
            new Vector2(-Direction.Y, Direction.X) * across;
    }

    // =========================================================
    // Return a surface position just outside the tunnel mouth.
    public Vector2 OutsidePosition(Vector2 tileSize)
    {
        return SurfacePosition -
            IsoGrid.TileToWorld(Direction * 0.9f, tileSize);
    }
    #endregion
}