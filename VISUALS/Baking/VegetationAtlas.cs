// Defines the plant atlas layout and caches its shared artwork and wind material.
// Delegates rendering and capture to ArtworkBaker.
using Godot;
using System.Threading.Tasks;

public partial class VegetationAtlas : Node2D
{
    #region Layout
    public const int VariantsPerKind =
        VegetationPalette.ShapeCount * VegetationPalette.PaletteCount;
    public const int CellSize = 256;
    private const int Columns = 4;
    private const int RowsPerKind = (VariantsPerKind + Columns - 1) / Columns;
    public static readonly Vector2 Origin = new(-128, -220);
    #endregion

    #region Shared Resources
    public static ImageTexture Texture { get; private set; }
    public static ShaderMaterial WindMaterial { get; private set; }
    private static Task _bakeTask;
    #endregion

    #region Baking
    // =========================================================
    // Share one initial bake between all plant consumers.
    public static Task EnsureReady(Node host)
    {
        if (Texture != null) return Task.CompletedTask;
        return _bakeTask ??= BakeAsync(host);
    }

    // =========================================================
    // Locate one shape-and-palette cell within the requested plant family.
    public static Rect2 GetRegion(bool shrub, int variant)
    {
        variant = Mathf.Clamp(variant, 0, VariantsPerKind - 1);
        int row = variant / Columns + (shrub ? RowsPerKind : 0);
        return new Rect2(
            variant % Columns * CellSize,
            row * CellSize, CellSize, CellSize);
    }

// =========================================================
// Load cached plant artwork or bake it, then prepare the shared wind material.
private static async Task BakeAsync(Node host)
{
    // Increase this after changing plant drawings, palettes or the baking shader.
    const int artworkRevision = 1;

    try
    {
        ShaderMaterial wind = ArtworkBaker.LoadMaterial(
            "res://VISUALS/Drawings/Vegetation/VegetationWind.gdshader");

        string key = $"plants-{artworkRevision}"
            + $"|variants={VariantsPerKind}|origin={Origin}";

        Texture = await ArtworkBaker.LoadOrBake(
            host, "VegetationBake", key,
            new Vector2I(Columns * CellSize, RowsPerKind * 2 * CellSize),
            () => new VegetationAtlas
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
    // Draw each plant family into its padded atlas cells.
    public override void _Draw()
    {
        for (int variant = 0; variant < VariantsPerKind; variant++)
        {
            DrawSetTransform(GetRegion(false, variant).Position - Origin);
            FrondDrawing.Draw(this, variant);

            DrawSetTransform(GetRegion(true, variant).Position - Origin);
            AlienShrubDrawing.Draw(this, variant);
        }
        DrawSetTransform(Vector2.Zero);
    }
    #endregion
}