// Bakes independently defined props and characters into one shared texture.
// Ground surfaces are handled separately by the continuous terrain material.
using Godot;
using System.Threading.Tasks;

public partial class PlaceholderAtlas : Node2D
{
    #region Layout
    public static readonly Rect2 RockRegion = new(2, 2, 160, 160);
    public static readonly Rect2 CrateRegion = new(166, 2, 160, 160);
    public static readonly Rect2 PlayerRegion = new(330, 2, 64, 96);
    public static readonly Rect2 EnemyRegion = new(398, 2, 96, 96);
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
    // Share one initial bake task between all atlas consumers.
    public static Task EnsureReady(Node host)
    {
        if (Texture != null) return Task.CompletedTask;
        return _bakeTask ??= BakeAsync(host);
    }

    // =========================================================
    // Capture the prop and character artwork once, then remove the viewport.
    private static async Task BakeAsync(Node host)
    {
        SubViewport viewport = new()
        {
            Name = "PlaceholderBake",
            Size = new Vector2I(512, 192),
            TransparentBg = true,
            Disable3D = true,
            World2D = new World2D(),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
        };

        PlaceholderAtlas painter = new()
        {
            _atmosphere = host.GetTree().GetFirstNodeInGroup("world_atmosphere")
                as WorldAtmosphere
        };

        host.AddChild(viewport);
        viewport.AddChild(painter);

        try
        {
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

            using Image image = viewport.GetTexture().GetImage();
            if (image.IsEmpty())
                throw new System.InvalidOperationException("Placeholder atlas capture was empty.");
            Texture = ImageTexture.CreateFromImage(image);
        }
        finally
        {
            viewport.QueueFree();
        }

        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    #endregion

    #region Drawing
    // =========================================================
    // Dispatch each independent drawing into its assigned sprite region.
    public override void _Draw()
    {
        DrawSetTransform(RockRegion.Position + new Vector2(80, 120));
        RockDrawing.Draw(this, _atmosphere);

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