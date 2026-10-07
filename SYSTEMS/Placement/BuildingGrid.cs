// Shares terrain-aligned building coordinates and footprint geometry.
// Cell coordinates refer to tile centres, including negative coordinates.
using Godot;

public sealed class BuildingGrid
{
    #region State
    public Vector2 TileSize { get; }
    private readonly Node2D _ground;
    #endregion

    #region Coordinates
    // =========================================================
    // Bind the existing terrain origin and tile dimensions.
    public BuildingGrid(Node2D ground, Vector2 tileSize)
    {
        _ground = ground;
        TileSize = tileSize;
    }

    // =========================================================
    // Convert a logical ground position into continuous grid coordinates.
    public Vector2 ToTile(Vector2 world)
    {
        return IsoGrid.WorldToTile(_ground.ToLocal(world), TileSize);
    }

    // =========================================================
    // Convert continuous grid coordinates into logical ground positions.
    public Vector2 ToWorld(Vector2 tile)
    {
        return _ground.ToGlobal(IsoGrid.TileToWorld(tile, TileSize));
    }

    // =========================================================
    // Snap to the nearest tile centre.
    public Vector2I CellAt(Vector2 world)
    {
        Vector2 tile = ToTile(world);
        return new Vector2I(
            Mathf.FloorToInt(tile.X + 0.5f),
            Mathf.FloorToInt(tile.Y + 0.5f));
    }

    // =========================================================
    // Find the centre of a footprint anchored at its first occupied cell.
    public Vector2 Centre(Vector2I anchor, Vector2I cells)
    {
        return ToWorld(new Vector2(
            anchor.X + (cells.X - 1) * 0.5f,
            anchor.Y + (cells.Y - 1) * 0.5f));
    }

    // =========================================================
    // Produce the four logical corners of an isometric footprint.
    public Vector2[] Corners(Vector2I anchor, Vector2I cells)
    {
        Vector2 start = new(anchor.X - 0.5f, anchor.Y - 0.5f);
        return new[]
        {
            ToWorld(start),
            ToWorld(start + new Vector2(cells.X, 0f)),
            ToWorld(start + new Vector2(cells.X, cells.Y)),
            ToWorld(start + new Vector2(0f, cells.Y))
        };
    }
    #endregion
}