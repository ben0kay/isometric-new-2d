// Owns the active world's lighting and shared artwork material cache.
// Uploads a fixed number of GPU globals, independent of world object count.
using Godot;

public partial class WorldLighting : Node
{
    #region Configuration
    [ExportGroup("Lighting Profile")]
    [Export] public WorldLightingSettings Profile { get; set; } = new();
    #endregion

    #region State
    public static readonly WorldLightingSettings DefaultProfile = new();

    private WorldLightingMaterials _materials;
    private WorldLayerController _layers;
    private double _remaining;
    private bool _lastSurface = true;

    public WorldLightingMaterials Materials =>
        _materials ??= new WorldLightingMaterials();

    public WorldLightingSettings Settings => Profile ?? DefaultProfile;
    #endregion

    #region Resolution
    // =========================================================
    // Find the lighting node belonging to the caller's world.
    public static WorldLighting Find(Node context)
    {
        for (Node ancestor = context; ancestor != null;
             ancestor = ancestor.GetParent())
        {
            WorldLighting lighting = ancestor
                .GetNodeOrNull<WorldLighting>("Systems/WorldLighting");
            if (lighting != null) return lighting;
        }
        return null;
    }
    #endregion

    #region Lifecycle
    // =========================================================
    // Publish defaults before world artwork begins requesting materials.
    public override void _EnterTree()
    {
        Publish(true);
    }

// =========================================================
// Install the eclipse helper while keeping lighting active across world layers.
public override void _Ready()
{
    ProcessPriority = 80;
    SetPhysicsProcess(false);
    WorldEclipse.Install(this);
}

    // =========================================================
    // Refresh profile edits periodically and layer changes immediately.
    public override void _Process(double delta)
    {
        _layers ??= WorldLayerController.Find(this);
        bool surface = _layers == null ||
            _layers.Current == WorldLayerId.Surface;

        _remaining -= delta;
        if (surface == _lastSurface && _remaining > 0.0) return;

        _remaining = 0.2;
        _lastSurface = surface;
        Publish(surface);
    }

    // =========================================================
    // Release this world's material cache and clear player brushing.
    public override void _ExitTree()
    {
        _materials?.Clear();
        RenderingServer.GlobalShaderParameterSet("wl_brush_strength", 0f);
    }
    #endregion

    #region GPU State
    // =========================================================
    // Give every participating shader the same light direction and intensity.
    private void Publish(bool surface)
    {
        WorldLightingSettings settings = Settings;

        RenderingServer.GlobalShaderParameterSet(
            "wl_sun_direction", settings.GetSurfaceDirection());
        RenderingServer.GlobalShaderParameterSet(
            "wl_sun_color", settings.SunColour);
        RenderingServer.GlobalShaderParameterSet(
            "wl_ambient", Mathf.Max(0f, surface
                ? settings.AmbientStrength
                : settings.CaveAmbientStrength));
        RenderingServer.GlobalShaderParameterSet(
            "wl_sun_strength", surface
                ? Mathf.Max(0f, settings.SunStrength) : 0f);
        RenderingServer.GlobalShaderParameterSet(
            "wl_shape_strength", Mathf.Max(0f, settings.DefaultShapeStrength));

        if (!surface)
            RenderingServer.GlobalShaderParameterSet("wl_brush_strength", 0f);
    }

    // =========================================================
    // Share player contact without updating individual vegetation materials.
    public static void UpdateBrush(
        Vector2 position, Vector2 direction, Vector2 radius, float strength)
    {
        RenderingServer.GlobalShaderParameterSet("wl_brush_position", position);
        RenderingServer.GlobalShaderParameterSet("wl_brush_direction", direction);
        RenderingServer.GlobalShaderParameterSet("wl_brush_radius", radius);
        RenderingServer.GlobalShaderParameterSet(
            "wl_brush_strength", Mathf.Clamp(strength, 0f, 1f));
    }
    #endregion
}