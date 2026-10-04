// Captures four frond and four shrub variants into one shared vegetation texture.
// Temporary drawing nodes and the viewport are removed after capture.
using Godot;
using System.Threading.Tasks;

public partial class VegetationAtlas : Node2D
{
    #region Layout
    public const int VariantsPerKind = 4;
    public const int CellSize = 256;
    public static readonly Vector2 Origin = new(-128, -220);
    #endregion

    #region Shared Resources
    public static ImageTexture Texture { get; private set; }
    public static ShaderMaterial WindMaterial { get; private set; }
    private static Task _bakeTask;
    #endregion

    #region Baking
    // =========================================================
    // Share a single bake between all vegetation consumers.
    public static Task EnsureReady(Node host)
    {
        if (Texture != null) return Task.CompletedTask;
        return _bakeTask ??= BakeAsync(host);
    }

    // =========================================================
    // Locate a padded variant cell within the shared texture.
    public static Rect2 GetRegion(bool shrub, int variant)
    {
        variant = Mathf.Clamp(variant, 0, VariantsPerKind - 1);
        return new Rect2(
            variant * CellSize, shrub ? CellSize : 0,
            CellSize, CellSize);
    }

    // =========================================================
    // Capture the drawing output once and release the temporary viewport.
    private static async Task BakeAsync(Node host)
    {
        SubViewport viewport = new()
        {
            Name = "VegetationBake",
            Size = new Vector2I(CellSize * VariantsPerKind, CellSize * 2),
            TransparentBg = true,
            Disable3D = true,
            World2D = new World2D(),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
        };

        host.AddChild(viewport);
        viewport.AddChild(new VegetationAtlas());

        try
        {
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            await host.ToSignal(
                RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

            using Image image = viewport.GetTexture().GetImage();
            if (image.IsEmpty())
                throw new System.InvalidOperationException("Vegetation capture was empty.");

            Shader shader = GD.Load<Shader>(
                "res://VISUALS/Vegetation/VegetationWind.gdshader");
            if (shader == null)
                throw new System.InvalidOperationException("Vegetation wind shader was missing.");

            WindMaterial = new ShaderMaterial { Shader = shader };
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
    // Draw independent plant families into their padded atlas cells.
    public override void _Draw()
    {
        for (int variant = 0; variant < VariantsPerKind; variant++)
        {
            DrawSetTransform(
                GetRegion(false, variant).Position - Origin);
            BlueFrondDrawing.Draw(this, variant);

            DrawSetTransform(
                GetRegion(true, variant).Position - Origin);
            AlienShrubDrawing.Draw(this, variant);
        }
        DrawSetTransform(Vector2.Zero);
    }
    #endregion
}