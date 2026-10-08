// Composites the eclipse over world visuals and supplies shared shadow uniforms.
// Lives beside Atmosphere so surface suspension never stops the world clock.
using Godot;

public partial class WorldEclipse : CanvasLayer
{


    #region State
    public ShaderMaterial ShadowMaterial { get; private set; }

    private WorldClock _clock;
    private WorldConfig _config;
    private Player _player;
    private PlayerFlashlight _flashlight;
    private WorldLayerController _layers;
    private ShaderMaterial _lighting;
    private ColorRect _screen;
    private BackBufferCopy _copy;
    #endregion

    #region Installation
// =========================================================
// Install one eclipse compositor beneath the main lighting authority.
public static void Install(WorldLighting lighting)
{
    if (lighting.GetNodeOrNull<WorldEclipse>("WorldEclipse") != null)
        return;

    lighting.CallDeferred(Node.MethodName.AddChild, new WorldEclipse
    {
        Name = "WorldEclipse"
    });
}

// =========================================================
// Find the eclipse belonging to the caller's world lighting system.
public static WorldEclipse Find(Node context)
{
    return WorldLighting.Find(context)
        ?.GetNodeOrNull<WorldEclipse>("WorldEclipse");
}

// =========================================================
// Build the existing eclipse, shadow fading and helmet-light compositor.
public override void _Ready()
{
    Layer = 5;
    Node world = GetParent<WorldLighting>().GetParent().GetParent();
    _config = WorldConfig.Find(this);
    _player = world.GetNode<Player>("WorldObjects/Player");

    _clock = new WorldClock { Name = "WorldClock" };
    AddChild(_clock);

    _lighting = new ShaderMaterial
    {
        Shader = GD.Load<Shader>(
            "res://VISUALS/Atmosphere/WorldEclipse.gdshader")
    };

    ShadowMaterial = new ShaderMaterial
    {
        Shader = GD.Load<Shader>(
            "res://VISUALS/Shadows/SunShadow.gdshader")
    };

    _copy = new BackBufferCopy
    {
        Name = "WorldCopy",
        CopyMode = BackBufferCopy.CopyModeEnum.Viewport
    };
    AddChild(_copy);

    _screen = new ColorRect
    {
        Name = "WorldLighting",
        Material = _lighting,
        MouseFilter = Control.MouseFilterEnum.Ignore
    };
    AddChild(_screen);
    _screen.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

    Node playerSystems = _player.GetNode("Systems");
    _flashlight = playerSystems.GetNodeOrNull<PlayerFlashlight>("Flashlight");

    if (_flashlight == null)
    {
        _flashlight = new PlayerFlashlight { Name = "Flashlight" };
        playerSystems.AddChild(_flashlight);
    }

    CanvasLayer hud = world.GetNodeOrNull<CanvasLayer>("HUD");
    if (hud != null && hud.Layer <= Layer)
        hud.Layer = Layer + 1;

    ProcessPriority = 100;
}

    // =========================================================
    // Update a constant number of uniforms, regardless of object count.
    public override void _Process(double delta)
    {
        _layers ??= WorldLayerController.Find(this);
        bool surface = _layers == null ||
            _layers.Current == WorldLayer.Surface;

        _screen.Visible = surface;
        _copy.CopyMode = surface
            ? BackBufferCopy.CopyModeEnum.Viewport
            : BackBufferCopy.CopyModeEnum.Disabled;

        UpdateEclipseUniforms(_lighting, surface);
        UpdateEclipseUniforms(ShadowMaterial, surface);

        if (!surface) return;

        // SCREEN_UV uses viewport pixels, so invert the actual canvas transform.
        Transform2D inverse = GetViewport().GetCanvasTransform().AffineInverse();
        _lighting.SetShaderParameter("screen_to_world_origin", inverse.Origin);
        _lighting.SetShaderParameter("screen_to_world_x", inverse.X);
        _lighting.SetShaderParameter("screen_to_world_y", inverse.Y);

        _flashlight.UpdateBeam();
        _lighting.SetShaderParameter("flashlight_enabled", _flashlight.Active);
        _lighting.SetShaderParameter("flashlight_position", _flashlight.Position);
        _lighting.SetShaderParameter("flashlight_direction", _flashlight.Direction);
        _lighting.SetShaderParameter("flashlight_range",
            Mathf.Max(1f, _flashlight.Range));
        _lighting.SetShaderParameter("flashlight_half_angle",
            Mathf.DegToRad(Mathf.Clamp(_flashlight.HalfAngleDegrees, 5f, 80f)));
        _lighting.SetShaderParameter("flashlight_strength",
            Mathf.Clamp(_flashlight.Strength, 0f, 1f));
    }
    #endregion

    #region Shared Lighting
// =========================================================
// Share global timing and brief screen transitions across both materials.
private void UpdateEclipseUniforms(ShaderMaterial material, bool enabled)
{
    double eclipseSeconds =
        _clock.CycleSeconds * _clock.EclipseFraction;

    double transitionSeconds =
        System.Math.Min(30.0, eclipseSeconds * 0.1);

    float transitionFraction = (float)(
        transitionSeconds /
        System.Math.Max(0.001, _clock.CycleSeconds));

    material.SetShaderParameter("eclipse_enabled", enabled);
    material.SetShaderParameter("eclipse_phase", _clock.Phase);
    material.SetShaderParameter("eclipse_fraction", _clock.EclipseFraction);
    material.SetShaderParameter(
        "eclipse_transition_fraction", transitionFraction);
    material.SetShaderParameter(
        "eclipse_darkness_multiplier",
        Mathf.Max(0f, _config.EclipseDarknessMultiplier));
}
    #endregion
}