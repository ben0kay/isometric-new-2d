// Represents liquid occupying a permanent basin.
// Shares fill levels and immersion queries independently from liquid artwork.
using Godot;

public partial class LiquidBody : SurfacePatch
{
    #region State
    public WaterBasinWorld.Basin Basin { get; set; }
    public LiquidDefinition Liquid => Basin.Definition.Liquid;
    private float _shoreInset;
    #endregion

    #region Lifecycle
    // =========================================================
    // Register this liquid after its artwork has been constructed.
    public override void _Ready()
    {
        AddToGroup("world_liquids");
        SetFill(Basin.Fill);
        World.Register(this);
        SetProcess(false);
    }
    #endregion

    #region Fill And Immersion
    // =========================================================
    // Change liquid level without changing the permanent basin terrain.
    public void SetFill(float fill)
    {
        Basin.Fill = Mathf.Clamp(fill, 0f, 1f);
        SurfaceHeight = Basin.WaterHeight;
        _shoreInset = Basin.ShoreInsetNormalized();

        GlobalPosition = World.TileToWorld(TileCentre) +
            Vector2.Up * SurfaceHeight;
        Visible = Basin.Fill > 0.0001f;
        UpdateFillArtwork();
    }

    // =========================================================
    // Let artwork subclasses update their own rendering parameters.
    protected virtual void UpdateFillArtwork() { }

    // =========================================================
    // Measure immersion against the rendered terrain floor.
    public float GetDepth(Vector2 tile, float floorHeight)
    {
        if (Basin.Fill <= 0.0001f ||
            Basin.InwardDistance(tile) <= 0f) return 0f;

        return Mathf.Max(0f, Basin.WaterHeight - floorHeight);
    }

    // =========================================================
    // Expose an approximate depth-based influence for existing surface callers.
    public override float GetInfluence(Vector2 tile)
    {
        if (Basin.Fill <= 0.0001f) return 0f;

        return Mathf.Clamp(
            (Basin.DepthAt(tile) - Basin.WaterDrop) /
            Liquid.FullResistanceDepthPixels, 0f, 1f);
    }

    // =========================================================
    // Follow the current liquid contour instead of the outer basin boundary.
    public override Vector2[] GetDebugOutline()
    {
        if (Basin.Fill <= 0.0001f)
            return System.Array.Empty<Vector2>();

        const int segments = 48;
        Vector2[] points = new Vector2[segments];

        for (int i = 0; i < segments; i++)
        {
            float angle = Mathf.Tau * i / segments;
            float radius = Mathf.Max(
                0f, SurfaceGeometry.Edge(angle, Phase) - _shoreInset);
            Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));
            points[i] = ToGlobal(LocalPoint(direction * radius));
        }
        return points;
    }
    #endregion
}