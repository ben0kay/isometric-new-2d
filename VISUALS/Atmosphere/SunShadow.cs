// Composites the eclipse over world visuals and supplies shared shadow uniforms.
// Lives beside Atmosphere so surface suspension never stops the world clock.
using Godot;

public partial class WorldEclipse : CanvasLayer
{
    #region Configuration
    [ExportGroup("Eclipse Movement")]
    [Export] public float WorldSpan { get; set; } = 240000f;
    [Export] public float EdgeWidth { get; set; } = 3000f;
    #endregion

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
    // Queue one independent eclipse service while the world is becoming ready.
    public static void Install(WorldAtmosphere atmosphere)
    {
        Node systems = atmosphere.GetParent();
        if (systems.GetNodeOrNull<WorldEclipse>("WorldEclipse") != null)
            return;

        systems.CallDeferred(Node.MethodName.AddChild, new WorldEclipse
        {
            Name = "WorldEclipse"
        });
    }

    // =========================================================
    // Find the eclipse service belonging to this world, not another viewport.
    public static WorldEclipse Find(Node context)
    {
        for (Node ancestor = context; ancestor != null;
             ancestor = ancestor.GetParent())
        {
            WorldEclipse eclipse = ancestor.GetNodeOrNull<WorldEclipse>(
                "Systems/WorldEclipse");
            if (eclipse != null) return eclipse;
        }
        return null;
    }
    #endregion

    #region Lifecycle
    // =========================================================
    // Build one screen pass, one shared shadow material and the player beam.
    public override void _Ready()
    {
        Layer = 5;
        Node world = GetParent().GetParent();
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
                "res://VISUALS/Atmosphere/SunShadow.gdshader")
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
        _flashlight = playerSystems.GetNodeOrNull<PlayerFlashlight>(
            "Flashlight");

        if (_flashlight == null)
        {
            _flashlight = new PlayerFlashlight { Name = "Flashlight" };
            playerSystems.AddChild(_flashlight);
        }

        // Keep world debug labels above the lighting pass.
        CanvasLayer hud = world.GetNodeOrNull<CanvasLayer>("HUD");
        if (hud != null && hud.Layer <= Layer)
            hud.Layer = Layer + 1;

        // Run after the normal movement and camera updates.
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
    // Keep shadow fading and the screen compositor on the same eclipse phase.
    private void UpdateEclipseUniforms(ShaderMaterial material, bool enabled)
    {
        material.SetShaderParameter("eclipse_enabled", enabled);
        material.SetShaderParameter("eclipse_phase", _clock.Phase);
        material.SetShaderParameter("eclipse_fraction", _clock.EclipseFraction);
        material.SetShaderParameter("eclipse_world_span",
            Mathf.Max(1f, WorldSpan));
        material.SetShaderParameter("eclipse_edge_width",
            Mathf.Clamp(EdgeWidth, 1f, Mathf.Max(1f, WorldSpan) * 0.1f));
        material.SetShaderParameter("eclipse_darkness_multiplier",
            Mathf.Max(0f, _config.EclipseDarknessMultiplier));
    }
    #endregion
}