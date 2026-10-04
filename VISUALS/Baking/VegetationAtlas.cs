// Bakes independent vegetation shapes and palettes into one shared texture.
// Surface shaders run only during capture; gameplay retains the separate wind shader.
using Godot;
using System.Threading.Tasks;

public partial class VegetationAtlas : Node2D
{
    #region Layout
    public const int VariantsPerKind =
        VegetationPalette.ShapeCount * VegetationPalette.PaletteCount;
    public const int CellSize = 256;
    private const int Columns = 4;
    private const int RowsPerKind = VariantsPerKind / Columns;
    public static readonly Vector2 Origin = new(-128, -220);
    #endregion

    #region Shared Resources
    public static ImageTexture Texture { get; private set; }
    public static ShaderMaterial WindMaterial { get; private set; }
    private static Task _bakeTask;
    #endregion

    #region Baking
    // =========================================================
    // Share one initial capture between vegetation consumers.
    public static Task EnsureReady(Node host)
    {
        if (Texture != null) return Task.CompletedTask;
        return _bakeTask ??= BakeAsync(host);
    }

    // =========================================================
    // Locate a padded shape-and-palette cell for the selected family.
    public static Rect2 GetRegion(bool shrub, int variant)
    {
        variant = Mathf.Clamp(variant, 0, VariantsPerKind - 1);
        int row = variant / Columns + (shrub ? RowsPerKind : 0);
        return new Rect2(
            variant % Columns * CellSize,
            row * CellSize, CellSize, CellSize);
    }

    // =========================================================
    // Capture surface-detailed artwork and release its temporary rendering nodes.
    private static async Task BakeAsync(Node host)
    {
        Shader surfaceShader = GD.Load<Shader>(
            "res://VISUALS/Drawings/Vegetation/VegetationBake.gdshader");
        Shader windShader = GD.Load<Shader>(
            "res://VISUALS/Drawings/Vegetation/VegetationWind.gdshader");

        if (surfaceShader == null || windShader == null)
            throw new System.InvalidOperationException(
                "A vegetation shader was missing. Check the supplied resource paths.");

        SubViewport viewport = new()
        {
            Name = "VegetationBake",
            Size = new Vector2I(
                Columns * CellSize, RowsPerKind * 2 * CellSize),
            TransparentBg = true,
            Disable3D = true,
            World2D = new World2D(),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
        };

        host.AddChild(viewport);

        try
        {
            viewport.AddChild(new VegetationAtlas
            {
                Material = new ShaderMaterial { Shader = surfaceShader }
            });

            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            await host.ToSignal(
                RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

            using Image image = viewport.GetTexture().GetImage();
            if (image.IsEmpty())
                throw new System.InvalidOperationException("Vegetation capture was empty.");

            WindMaterial = new ShaderMaterial { Shader = windShader };
            Texture = ImageTexture.CreateFromImage(image);
        }
        finally
        {
            viewport.QueueFree();
        }
    }
    #endregion

    #region Drawing
    // =========================================================
    // Draw each family using independently selected shapes and palettes.
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