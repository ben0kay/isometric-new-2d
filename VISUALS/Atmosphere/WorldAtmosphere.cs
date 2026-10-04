// Shares fixed sunlight settings, cached obstacle shadows and one ravine fog material.
// Static artwork is generated at load time; fog animation runs in the shader.
using Godot;

public partial class WorldAtmosphere : Node
{
    #region Configuration
    [Export] public Vector2 SunDirection { get; set; } = new(-1f, -0.7f);
    [Export] public Color SunTint { get; set; } = new(1f, 0.96f, 0.87f);
    [Export] public float FaceAmbient { get; set; } = 0.65f;
    [Export] public float FaceSunStrength { get; set; } = 0.55f;
    [Export] public float ShadowLength { get; set; } = 1.8f;
    [Export] public float ShadowOpacity { get; set; } = 0.38f;
    [Export] public Color FogColor { get; set; } = new("#294f6d");
    [Export] public float FogDensity { get; set; } = 0.65f;
    [Export] public float FogNoiseScale { get; set; } = 0.012f;
    [Export] public Vector2 FogDrift { get; set; } = new(3f, -1f);
    #endregion

    #region State
    public ShaderMaterial FogMaterial { get; private set; }
    public Vector2 LightDirection => SunDirection.LengthSquared() > 0.0001f
        ? SunDirection.Normalized() : new Vector2(-1f, -0.7f).Normalized();

    private TerrainElevation _elevation;
    private ChunkController _chunks;
    private Node2D _ground;
    #endregion

    #region Lifecycle
    // =========================================================
    // Register before scene children begin building their artwork.
    public override void _EnterTree()
    {
        AddToGroup("world_atmosphere");
    }

    // =========================================================
    // Prepare one fog material and resolve the shared terrain services.
    public override void _Ready()
    {
        _elevation = GetNode<TerrainElevation>("../TerrainElevation");
        _chunks = GetNode<ChunkController>("../ChunkController");
        _ground = GetNode<Node2D>("../../GroundChunks");

        FogMaterial = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://VISUALS/Atmosphere/RavineFog.gdshader")
        };
        FogMaterial.SetShaderParameter("fog_color", FogColor);
        FogMaterial.SetShaderParameter("fog_density", Mathf.Clamp(FogDensity, 0f, 1f));
        FogMaterial.SetShaderParameter("noise_scale", Mathf.Max(0.001f, FogNoiseScale));
        FogMaterial.SetShaderParameter("drift", FogDrift);
    }
    #endregion

    #region Sunlight
    // =========================================================
    // Tint a baked face according to its approximate screen-space orientation.
    public Color ShadeFace(Color baseColor, Vector2 outwardNormal)
    {
        float facing = Mathf.Max(0f, outwardNormal.Normalized().Dot(LightDirection));
        float intensity = Mathf.Max(0f, FaceAmbient + facing * FaceSunStrength);
        return new Color(
            baseColor.R * SunTint.R * intensity,
            baseColor.G * SunTint.G * intensity,
            baseColor.B * SunTint.B * intensity,
            baseColor.A);
    }
    #endregion

    #region Shadows
    // =========================================================
    // Project a finite shadow from an obstacle footprint and cache its polygon.
    public void CreateObstacleShadow(Obstacle obstacle)
    {
        float length = obstacle.Height * Mathf.Max(0f, ShadowLength);
        float opacity = Mathf.Clamp(ShadowOpacity, 0f, 1f);
        if (length < 1f || opacity <= 0f) return;

        Vector2 direction = -LightDirection;
        Vector2 extension = direction * length;
        float width = obstacle.Footprint.X * 0.48f;
        float depth = obstacle.Footprint.Y * 0.4f;

        Vector2[] basePoints =
        {
            new(-width, -depth), new(width, -depth),
            new(width, depth), new(-width, depth)
        };
        Vector2[] candidates = new Vector2[8];

        for (int i = 0; i < 4; i++)
        {
            candidates[i] = basePoints[i];
            candidates[i + 4] = extension + basePoints[i] * 0.7f;
        }

        Vector2[] hull = Geometry2D.ConvexHull(candidates);
        if (hull.Length > 1 && hull[0].IsEqualApprox(hull[hull.Length - 1]))
            System.Array.Resize(ref hull, hull.Length - 1);
        if (hull.Length < 3) return;

        Vector2[] points = new Vector2[hull.Length];
        Color[] colors = new Color[hull.Length];

        for (int i = 0; i < hull.Length; i++)
        {
            Vector2 globalPoint = obstacle.ToGlobal(hull[i]);

            // Omit shadows whose projected corners enter the ravine.
            // This avoids drawing ground shadows across open air in this first pass.
            if (!TerrainLayout.HasGroundClearance(
                _ground.ToLocal(globalPoint), _chunks.TileSize))
                return;

            Vector2 visualPoint = globalPoint;
            visualPoint.Y -= _elevation.SampleWorldHeight(globalPoint);
            points[i] = obstacle.ToLocal(visualPoint);

            float fraction = Mathf.Clamp(hull[i].Dot(direction) / length, 0f, 1f);
            colors[i] = new Color(0.015f, 0.025f, 0.04f,
                opacity * (1f - fraction * 0.65f));
        }

        obstacle.AddChild(new Polygon2D
        {
            Name = "SunShadow",
            Polygon = points,
            VertexColors = colors,
            Color = Colors.White,
            ZAsRelative = false,
            ZIndex = -1
        });
    }
    #endregion
}