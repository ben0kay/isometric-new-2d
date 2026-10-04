// Bakes tree variants once into a separate shared texture.
// Releases drawing nodes after capture and shares one gameplay wind material.
using Godot;
using System.Threading.Tasks;

public partial class TreeAtlas : Node2D
{
    #region Layout
    public const int CellSize = 512;
    private const int Columns = 4;
    public static readonly Vector2 Origin = new(-256, -460);
    #endregion

    #region Shared Resources
    public static ImageTexture Texture { get; private set; }
    public static ShaderMaterial WindMaterial { get; private set; }
    private static Task _bakeTask;
    #endregion

    #region Baking
    // =========================================================
    // Share one startup capture between all tree instances.
    public static Task EnsureReady(Node host)
    {
        if (Texture != null) return Task.CompletedTask;
        return _bakeTask ??= BakeAsync(host);
    }

    // =========================================================
    // Locate the padded cell belonging to one tree variant.
    public static Rect2 GetRegion(int variant)
    {
        variant = Mathf.Clamp(variant, 0, CarbonTreeDrawing.VariantCount - 1);
        return new Rect2(
            variant % Columns * CellSize,
            variant / Columns * CellSize, CellSize, CellSize);
    }

    // =========================================================
    // Capture bark and foliage once, then release the temporary viewport.
    private static async Task BakeAsync(Node host)
    {
        Shader surfaceShader = GD.Load<Shader>(
            "res://VISUALS/Drawings/Vegetation/VegetationBake.gdshader");
        Shader windShader = GD.Load<Shader>(
            "res://VISUALS/Drawings/Vegetation/Trees/TreeWind.gdshader");

        if (surfaceShader == null || windShader == null)
            throw new System.InvalidOperationException("A tree shader was missing.");

        SubViewport viewport = new()
        {
            Name = "TreeBake",
            Size = new Vector2I(
                Columns * CellSize,
                CarbonTreeDrawing.VariantCount / Columns * CellSize),
            TransparentBg = true,
            Disable3D = true,
            World2D = new World2D(),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
        };
        host.AddChild(viewport);

        try
        {
            viewport.AddChild(new TreeAtlas
            {
                Material = new ShaderMaterial { Shader = surfaceShader }
            });

            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            await host.ToSignal(
                RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

            using Image image = viewport.GetTexture().GetImage();
            if (image.IsEmpty())
                throw new System.InvalidOperationException("Tree capture was empty.");

            Texture = ImageTexture.CreateFromImage(image);
            WindMaterial = new ShaderMaterial { Shader = windShader };
        }
        finally
        {
            viewport.QueueFree();
        }
    }
    #endregion

    #region Drawing
    // =========================================================
    // Draw each tree into its independent padded atlas cell.
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