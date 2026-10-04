// Defines the larger tree atlas layout and caches shared tree resources.
// Delegates rendering and capture to the same ArtworkBaker used by plants.
using Godot;
using System.Threading.Tasks;

public partial class TreeAtlas : Node2D
{
    #region Layout
    public const int CellSize = 512;
    private const int Columns = 4;
    private const int Rows =
        (CarbonTreeDrawing.VariantCount + Columns - 1) / Columns;
    public static readonly Vector2 Origin = new(-256, -460);
    #endregion

    #region Shared Resources
    public static ImageTexture Texture { get; private set; }
    public static ShaderMaterial WindMaterial { get; private set; }
    private static Task _bakeTask;
    #endregion

    #region Baking
    // =========================================================
    // Share one initial bake between all tree consumers.
    public static Task EnsureReady(Node host)
    {
        if (Texture != null) return Task.CompletedTask;
        return _bakeTask ??= BakeAsync(host);
    }

    // =========================================================
    // Locate one tree variant inside its padded atlas cell.
    public static Rect2 GetRegion(int variant)
    {
        variant = Mathf.Clamp(variant, 0, CarbonTreeDrawing.VariantCount - 1);
        return new Rect2(
            variant % Columns * CellSize,
            variant / Columns * CellSize, CellSize, CellSize);
    }

// =========================================================
// Load cached tree artwork or bake it, then prepare the shared canopy wind.
private static async Task BakeAsync(Node host)
{
    // Increase this after changing tree drawings, palettes or the baking shader.
    const int artworkRevision = 1;

    try
    {
        ShaderMaterial wind = ArtworkBaker.LoadMaterial(
            "res://VISUALS/Drawings/Vegetation/Trees/TreeWind.gdshader");

        int rows = (CarbonTreeDrawing.VariantCount + Columns - 1) / Columns;
        string key = $"trees-{artworkRevision}"
            + $"|variants={CarbonTreeDrawing.VariantCount}|origin={Origin}";

        Texture = await ArtworkBaker.LoadOrBake(
            host, "TreeBake", key,
            new Vector2I(Columns * CellSize, rows * CellSize),
            () => new TreeAtlas
            {
                Material = ArtworkBaker.LoadMaterial(
                    "res://VISUALS/Drawings/Vegetation/VegetationBake.gdshader")
            });
        WindMaterial = wind;
    }
    catch
    {
        _bakeTask = null;
        throw;
    }
}
    #endregion

    #region Drawing
    // =========================================================
    // Draw each tree variant into its independent padded cell.
    public override void _Draw()
    {
        for (int variant = 0; variant < CarbonTreeDrawing.VariantCount; variant++)
        {
            DrawSetTransform(GetRegion(variant).Position - Origin);
            CarbonTreeDrawing.Draw(this, variant);
        }
        DrawSetTransform(Vector2.Zero);
    }
    #endregion
}