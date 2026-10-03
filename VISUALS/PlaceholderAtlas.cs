// Bakes placeholder primitives into one shared texture atlas on first use.
// The temporary viewport is freed after capture; all instances reuse the texture.
using Godot;
using System.Threading.Tasks;

public partial class PlaceholderAtlas : Node2D
{
    #region Shared Resources
    public static ImageTexture Texture { get; private set; }
    public static readonly Rect2 RockRegion = new(2, 80, 160, 160);
    public static readonly Rect2 CrateRegion = new(166, 80, 160, 160);
    public static readonly Rect2 PlayerRegion = new(330, 80, 64, 96);

    public static readonly Rect2 EnemyRegion = new(398, 80, 96, 96);

    // Shared blend mode for artwork captured from the transparent viewport.
public static readonly CanvasItemMaterial BakedMaterial = new()
{
    BlendMode = CanvasItemMaterial.BlendModeEnum.PremultAlpha
};

    private static Task _bakeTask;
    #endregion

    #region Baking
    // =========================================================
    // Share a single bake task between the player and world controller.
    public static Task EnsureReady(Node host)
    {
        if (Texture != null) return Task.CompletedTask;
        return _bakeTask ??= BakeAsync(host);
    }

    // =========================================================
    // Render the atlas once, capture it, and remove the temporary viewport.
    private static async Task BakeAsync(Node host)
    {
        SubViewport viewport = new()
        {
            Name = "PlaceholderBake",
            Size = new Vector2I(1024, 256),
            TransparentBg = true,
            Disable3D = true,
            World2D = new World2D(),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
        };

        host.AddChild(viewport);
        viewport.AddChild(new PlaceholderAtlas());

        try
        {
            // Allow node setup and drawing notifications to complete first.
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

            using Image image = viewport.GetTexture().GetImage();
            if (image.IsEmpty()) throw new System.InvalidOperationException("Placeholder atlas capture was empty.");
            Texture = ImageTexture.CreateFromImage(image);
        }
        finally
        {
            viewport.QueueFree();
        }

        // Resume consumers during scene processing rather than render completion.
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    // =========================================================
    // Return the source rectangle for one of seven ground variations.
    public static Rect2 TileRegion(int variant)
    {
        return new Rect2(2 + variant * 132, 2, 128, 64);
    }
    #endregion

    #region Atlas Drawing
    // =========================================================
// Draw all placeholder artwork into its assigned atlas regions.
public override void _Draw()
{
    for (int variant = 0; variant < 7; variant++)
    {
        DrawSetTransform(new Vector2(66 + variant * 132, 34));
        DrawGroundTile(variant);
    }

    DrawSetTransform(RockRegion.Position + new Vector2(80, 120));
    DrawShadow(62.4f, 0.5f, 0.3f);
    DrawRock();

    DrawSetTransform(CrateRegion.Position + new Vector2(80, 120));
    DrawShadow(62.4f, 0.5f, 0.3f);
    DrawCrate();

    DrawSetTransform(PlayerRegion.Position + new Vector2(32, 72));
    DrawPlayer();

    DrawSetTransform(EnemyRegion.Position + new Vector2(48, 64));
    DrawEnemy();
    DrawSetTransform(Vector2.Zero);
}

    // =========================================================
    // Bake one diamond tile including seams and subtle grain.
    private void DrawGroundTile(int variant)
    {
        float shade = 0.085f + variant * 0.003f;
        DrawColoredPolygon(new Vector2[]
        {
            new(0, -32), new(64, 0), new(0, 32), new(-64, 0)
        }, new Color(shade, shade + 0.018f, shade + 0.035f));

        DrawPolyline(new Vector2[]
        {
            new(-64, 0), new(0, -32), new(64, 0)
        }, new Color("#252e37"), 1f);

        uint hash = (uint)variant + 64u;
        for (int grain = 0; grain < 6; grain++)
        {
            hash = IsoGrid.Hash(variant, grain, hash);
            float x = ((hash & 255u) / 255f - 0.5f) * 64f;
            float y = (((hash >> 8) & 255u) / 255f - 0.5f) * 32f;
            DrawRect(new Rect2(x, y, 2, 1), new Color(0.32f, 0.38f, 0.43f, 0.16f));
        }
    }

    // =========================================================
    // Bake an elliptical shadow without changing the current draw transform.
    private void DrawShadow(float radius, float verticalScale, float opacity)
    {
        Vector2[] points = new Vector2[32];
        for (int i = 0; i < points.Length; i++)
        {
            float angle = Mathf.Tau * i / points.Length;
            points[i] = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * verticalScale);
        }
        DrawColoredPolygon(points, new Color(0, 0, 0, opacity));
    }

    // =========================================================
    // Bake the canonical rock; instances scale this shared artwork.
    private void DrawRock()
    {
        const float w = 62.4f, d = 24f, height = 72f;
        DrawColoredPolygon(new Vector2[]
        {
            new(-w, -d), new(-w * 0.75f, -height),
            new(-w * 0.15f, -height - 16), new(w * 0.65f, -height + 4),
            new(w, -d), new(w * 0.55f, d), new(-w * 0.55f, d)
        }, new Color("#47505c"));

        DrawColoredPolygon(new Vector2[]
        {
            new(-w, -d), new(-w * 0.75f, -height),
            new(-w * 0.15f, -height - 16), new(w * 0.1f, -d * 0.4f),
            new(-w * 0.55f, d)
        }, new Color("#5c6876"));

        DrawPolyline(new Vector2[]
        {
            new(-w * 0.4f, -height * 0.8f),
            new(-w * 0.1f, -height * 0.5f),
            new(w * 0.3f, -height * 0.35f)
        }, new Color("#71b5c4"), 3f, true);
    }

    // =========================================================
    // Bake the canonical crate with shaded sides and a cyan light strip.
    private void DrawCrate()
    {
        const float w = 48f, d = 24f, height = 72f;
        Vector2 a = new(-w, -height), b = new(0, -height - d);
        Vector2 c = new(w, -height), e = new(0, -height + d);

        DrawColoredPolygon(new Vector2[] { a, e, new(0, d), new(-w, 0) }, new Color("#344756"));
        DrawColoredPolygon(new Vector2[] { e, c, new(w, 0), new(0, d) }, new Color("#263541"));
        DrawColoredPolygon(new Vector2[] { a, b, c, e }, new Color("#61798a"));
        DrawPolyline(new Vector2[] { a, b, c, e, a }, new Color("#8297a5"), 2f, true);
        DrawLine(new Vector2(w * 0.25f, -height * 0.45f), new Vector2(w * 0.75f, -height * 0.65f), new Color("#76e2e7"), 3f);
    }

    // =========================================================
    // Bake the right-facing player; runtime facing mirrors this texture.
    private void DrawPlayer()
    {
        DrawShadow(19f, 0.45f, 0.35f);
        DrawLine(new Vector2(-7, -15), new Vector2(-7, -3), new Color("#24313e"), 7f);
        DrawLine(new Vector2(7, -15), new Vector2(7, -3), new Color("#24313e"), 7f);
        DrawRect(new Rect2(-13, -39, 26, 27), new Color("#465d70"));
        DrawRect(new Rect2(-13, -39, 26, 27), new Color("#8398a6"), false, 2f);
        DrawCircle(new Vector2(0, -46), 12f, new Color("#708697"));
        DrawRect(new Rect2(-4, -50, 12, 7), new Color("#77e5ee"));
        DrawLine(new Vector2(13, -31), new Vector2(23, -24), new Color("#354b5c"), 6f);
        DrawRect(new Rect2(-5, -34, 10, 4), new Color("#77e5ee"));
    }

    // =========================================================
// Bake a compact red security drone with an angular body and glowing sensor.
private void DrawEnemy()
{
    DrawShadow(22f, 0.45f, 0.35f);
    DrawLine(new Vector2(-12, -18), new Vector2(-18, -3), new Color("#472b35"), 6f);
    DrawLine(new Vector2(12, -18), new Vector2(18, -3), new Color("#472b35"), 6f);

    Vector2[] body =
    {
        new(-22, -29), new(-13, -45), new(13, -45),
        new(22, -29), new(12, -13), new(-12, -13)
    };
    DrawColoredPolygon(body, new Color("#763a49"));
    DrawPolyline(new Vector2[]
    {
        new(-22, -29), new(-13, -45), new(13, -45),
        new(22, -29), new(12, -13), new(-12, -13), new(-22, -29)
    }, new Color("#ba6472"), 2f, true);

    DrawRect(new Rect2(-13, -34, 26, 8), new Color("#231d29"));
    DrawRect(new Rect2(-9, -32, 18, 4), new Color("#ff7164"));
    DrawLine(new Vector2(-8, -19), new Vector2(8, -19), new Color("#b55362"), 3f);
}
    #endregion
}