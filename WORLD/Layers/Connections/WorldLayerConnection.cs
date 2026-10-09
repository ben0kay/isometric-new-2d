// One bidirectional connection with a descending ramp owned by its lower layer.
// Endpoints share logical coordinates; elevation, drawing and collision use one route.
using Godot;
using System;

public sealed class WorldLayerConnection
{
    #region Data
    public readonly string Id, UpperLayer, LowerLayer;
    public readonly Vector2 MouthTile, Direction, UpperPosition, UpperAnchor;
    public readonly Vector2I AnchorCell;
    public readonly float RimHeight, TunnelLength;
    public float ClearRadius { get; set; } = 100f;
    public CaveEntrance Marker { get; set; }
    public Rect2 SampleArea { get; internal set; }
    public Rect2 RampArea => new Rect2(TileAt(-2f), Vector2.Zero)
        .Expand(TileAt(TunnelLength + 2f)).Grow(4f);
    #endregion

    #region Construction
    // =========================================================
    // Require distinct layer IDs and a cardinal direction for tile-edge openings.
    public WorldLayerConnection(string id, string upperLayer, string lowerLayer,
        Vector2 mouthTile, Vector2 direction, Vector2 upperPosition,
        float rimHeight, float tunnelLength, Vector2I anchorCell,
        Vector2 upperAnchor = default)
    {
        WorldLayerId.Validate(upperLayer);
        WorldLayerId.Validate(lowerLayer);
        if (string.IsNullOrWhiteSpace(id) || upperLayer == lowerLayer ||
            !float.IsFinite(rimHeight) || !float.IsFinite(tunnelLength) ||
            tunnelLength < 6f || !direction.IsFinite() ||
            (direction != Vector2.Up && direction != Vector2.Down &&
                direction != Vector2.Left && direction != Vector2.Right))
            throw new InvalidOperationException("Invalid layer connection.");
        Id = id; UpperLayer = upperLayer; LowerLayer = lowerLayer;
        MouthTile = mouthTile; Direction = direction; UpperPosition = upperPosition;
        RimHeight = rimHeight; TunnelLength = tunnelLength;
        AnchorCell = anchorCell; UpperAnchor = upperAnchor;
    }

    // =========================================================
    // Apply the two global controls once when constructing generation metadata.
    public static float LengthFor(GlobalConfig config, float baseLength)
    {
        float length = config.EntranceLengthMultiplier;
        float slope = config.EntranceSlopeMultiplier;
        float result = baseLength * length / slope;
        if (!float.IsFinite(length) || length <= 0f ||
            !float.IsFinite(slope) || slope <= 0f ||
            !float.IsFinite(result) || result < 6f || result > 512f)
            throw new InvalidOperationException(
                "Entrance multipliers must produce a tunnel between 6 and 512 tiles.");
        return result;
    }
    #endregion

    #region Coordinates
    // =========================================================
    // Resolve the other endpoint without assuming that an exit means surface.
    public string Other(string layer)
    {
        if (layer == UpperLayer) return LowerLayer;
        if (layer == LowerLayer) return UpperLayer;
        throw new InvalidOperationException($"'{layer}' does not join '{Id}'.");
    }

    // =========================================================
    // Return along/across coordinates on the common logical tile plane.
    public Vector2 Coordinates(Vector2 tile)
    {
        Vector2 offset = tile - MouthTile;
        return new Vector2(offset.Dot(Direction),
            offset.Dot(new Vector2(-Direction.Y, Direction.X)));
    }

    // =========================================================
    // Convert route coordinates into the common logical tile plane.
    public Vector2 TileAt(float along, float across = 0f)
    {
        return MouthTile + Direction * along +
            new Vector2(-Direction.Y, Direction.X) * across;
    }

    // =========================================================
    // Use a shared mouth apron for upward landings.
    public Vector2 OutsidePosition(Vector2 tileSize)
    {
        return UpperPosition - IsoGrid.TileToWorld(Direction * 0.9f, tileSize);
    }
    #endregion
}
