// Defines plant and compact grass cells in one shared cached texture.
// Uses ArtworkBaker for disk caching and separate wind materials for grass/plants.
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

    public const int GrassCellSize = 64;
    private const int AtlasWidth = Columns * CellSize;
    private const int GrassColumns = AtlasWidth / GrassCellSize;
    private const int GrassRowsPerKind =
        (VariantsPerKind + GrassColumns - 1) / GrassColumns;
    private const int GrassStartY = RowsPerKind * 2 * CellSize;
    private const int AtlasHeight =
        GrassStartY + GrassRowsPerKind * 3 * GrassCellSize;
    public static readonly Vector2 GrassOrigin = new(-32, -56);
    #endregion

    #region Shared Resources
    public static ImageTexture Texture { get; private set; }
    public static ShaderMaterial WindMaterial { get; private set; }
    public static ShaderMaterial GrassWindMaterial { get; private set; }
    private static Task _bakeTask;
    #endregion

    #region Baking
    // =========================================================
    // Share one load or bake between all plant and grass consumers.
    public static Task EnsureReady(Node host)
    {
        if (Texture != null) return Task.CompletedTask;
        return _bakeTask ??= BakeAsync(host);
    }

    // =========================================================
    // Preserve the existing frond and shrub atlas regions.
    public static Rect2 GetRegion(bool shrub, int variant)
    {
        variant = Mathf.Clamp(variant, 0, VariantsPerKind - 1);
        int row = variant / Columns + (shrub ? RowsPerKind : 0);
        return new Rect2(
            variant % Columns * CellSize,
            row * CellSize, CellSize, CellSize);
    }

    // =========================================================
    // Locate a compact grass cell for the requested height and colour variant.
    public static Rect2 GetGrassRegion(GrassHeight height, int variant)
    {
        int kind = Mathf.Clamp((int)height, 0, 2);
        variant = Mathf.Clamp(variant, 0, VariantsPerKind - 1);
        int row = kind * GrassRowsPerKind + variant / GrassColumns;
        return new Rect2(
            variant % GrassColumns * GrassCellSize,
            GrassStartY + row * GrassCellSize,
            GrassCellSize, GrassCellSize);
    }

    // =========================================================
    // Load cached artwork and prepare independent plant and grass wind materials.
    private static async Task BakeAsync(Node host)
    {
        const int artworkRevision = 2;
        try
        {
            ShaderMaterial wind = ArtworkBaker.LoadMaterial(
                "res://VISUALS/Drawings/Vegetation/VegetationWind.gdshader");
            ShaderMaterial grassWind = ArtworkBaker.LoadMaterial(
                "res://VISUALS/Drawings/Vegetation/GrassWind.gdshader");

            string key = $"plants-and-grass-{artworkRevision}"
                + $"|variants={VariantsPerKind}|origin={Origin}"
                + $"|grass-origin={GrassOrigin}";

            Texture = await ArtworkBaker.LoadOrBake(
                host, "VegetationBake", key,
                new Vector2I(AtlasWidth, AtlasHeight),
                () => new VegetationAtlas
                {
                    Material = ArtworkBaker.LoadMaterial(
                        "res://VISUALS/Drawings/Vegetation/VegetationBake.gdshader")
                });
            WindMaterial = wind;
            GrassWindMaterial = grassWind;
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
    // Bake existing plants and all three independent grass height definitions.
    public override void _Draw()
    {
        for (int variant = 0; variant < VariantsPerKind; variant++)
        {
            DrawSetTransform(GetRegion(false, variant).Position - Origin);
            FrondDrawing.Draw(this, variant);
            DrawSetTransform(GetRegion(true, variant).Position - Origin);
            AlienShrubDrawing.Draw(this, variant);

            DrawSetTransform(
                GetGrassRegion(GrassHeight.Short, variant).Position - GrassOrigin);
            ShortGrassDrawing.Draw(this, variant);
            DrawSetTransform(
                GetGrassRegion(GrassHeight.Medium, variant).Position - GrassOrigin);
            MediumGrassDrawing.Draw(this, variant);
            DrawSetTransform(
                GetGrassRegion(GrassHeight.Tall, variant).Position - GrassOrigin);
            TallGrassDrawing.Draw(this, variant);
        }
        DrawSetTransform(Vector2.Zero);
    }
    #endregion
}