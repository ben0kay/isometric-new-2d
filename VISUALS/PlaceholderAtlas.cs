// Bakes varied rocks, crates and characters into one reusable texture atlas.
// Rock shaders run during capture; gameplay draws their resulting sprites.
using Godot;
using System.Threading.Tasks;

public partial class PlaceholderAtlas : Node2D
{
    #region Layout
    private const int RockColumns = 4;
    private const int RockStride = 164;

    public static readonly Rect2 RockRegion = new(2, 2, 160, 160);
    public static readonly Rect2 CrateRegion = new(2, 332, 160, 160);
    public static readonly Rect2 PlayerRegion = new(166, 332, 64, 96);
    public static readonly Rect2 EnemyRegion = new(234, 332, 96, 96);
    #endregion

    #region Shared Resources
    public static ImageTexture Texture { get; private set; }
    public static readonly CanvasItemMaterial BakedMaterial = new()
    {
        BlendMode = CanvasItemMaterial.BlendModeEnum.PremultAlpha
    };

    private static Task _bakeTask;
    private WorldAtmosphere _atmosphere;
    #endregion

    #region Baking
    // =========================================================
    // Share one initial atlas capture between all consumers.
    public static Task EnsureReady(Node host)
    {
        if (Texture != null) return Task.CompletedTask;
        return _bakeTask ??= BakeAsync(host);
    }

    // =========================================================
    // Locate a rock variant within the atlas.
    public static Rect2 GetRockRegion(int variant)
    {
        variant = Mathf.Clamp(variant, 0, RockDrawing.VariantCount - 1);
        return new Rect2(
            2 + variant % RockColumns * RockStride,
            2 + variant / RockColumns * RockStride, 160, 160);
    }

// =========================================================
// Cache rocks and characters using both artwork revision and baked sunlight.
private static async Task BakeAsync(Node host)
{
    // Increase this after changing these drawings or their baking shaders.
    const int artworkRevision = 1;

    try
    {
        WorldAtmosphere atmosphere = host.GetTree().GetFirstNodeInGroup(
            "world_atmosphere") as WorldAtmosphere;

string lighting = atmosphere == null
    ? "no-atmosphere"
    : System.FormattableString.Invariant(
        $"{atmosphere.SunDirection.X:R}|{atmosphere.SunDirection.Y:R}|{atmosphere.SunTint.R:R}|{atmosphere.SunTint.G:R}|{atmosphere.SunTint.B:R}|{atmosphere.FaceAmbient:R}|{atmosphere.FaceSunStrength:R}");

        string key = $"placeholders-{artworkRevision}"
            + $"|rocks={RockDrawing.VariantCount}|light={lighting}";

        Texture = await ArtworkBaker.LoadOrBake(
            host, "PlaceholderBake", key, new Vector2I(768, 512),
            () => new PlaceholderAtlas { _atmosphere = atmosphere });
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
    // Create temporary rock painters whose shader output becomes baked artwork.
    public override void _Ready()
    {
        for (int variant = 0; variant < RockDrawing.VariantCount; variant++)
        {
            AddChild(new RockBakePainter
            {
                Name = $"Rock_{variant}",
                Variant = variant,
                Atmosphere = _atmosphere,
                Position = GetRockRegion(variant).Position + new Vector2(80, 120)
            });
        }
        SetProcess(false);
    }

    // =========================================================
    // Draw feet shadows and the remaining independently defined artwork.
    public override void _Draw()
    {
        for (int variant = 0; variant < RockDrawing.VariantCount; variant++)
        {
            DrawSetTransform(GetRockRegion(variant).Position + new Vector2(80, 120));
            DrawingHelpers.Shadow(this, 58f, 0.42f, 0.26f);
        }

        DrawSetTransform(CrateRegion.Position + new Vector2(80, 120));
        CrateDrawing.Draw(this, _atmosphere);

        DrawSetTransform(PlayerRegion.Position + new Vector2(32, 72));
        PlayerDrawing.Draw(this);

        DrawSetTransform(EnemyRegion.Position + new Vector2(48, 64));
        EnemyDrawing.Draw(this);

        DrawSetTransform(Vector2.Zero);
    }
    #endregion
}