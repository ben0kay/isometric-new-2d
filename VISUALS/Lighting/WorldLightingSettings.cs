// Defines the shared sunlight, ambient light and fixed shadow settings.
// Surface artwork supplies normals; this profile supplies the illumination.
using Godot;

[Tool, GlobalClass]
public partial class WorldLightingSettings : Resource
{
    #region Sunlight
    [ExportGroup("Sunlight")]
    [Export] public Vector2 SunDirection { get; set; } = new(-1f, -0.7f);
    [Export] public Color SunColour { get; set; } = new(1f, 0.94f, 0.82f);

    [Export(PropertyHint.Range, "0.05,2,0.05")]
    public float SunElevation { get; set; } = 0.65f;

    [Export(PropertyHint.Range, "0,3,0.05")]
    public float SunStrength { get; set; } = 1.15f;
    #endregion

    #region Ambient
    [ExportGroup("Ambient Light")]
    [Export(PropertyHint.Range, "0,2,0.05")]
    public float AmbientStrength { get; set; } = 0.3f;

    [Export(PropertyHint.Range, "0,2,0.05")]
    public float CaveAmbientStrength { get; set; } = 0.55f;
    #endregion

    #region Surface Shape
    [ExportGroup("Sprite Surface Approximation")]
    [Export(PropertyHint.Range, "0,6,0.1")]
    public float DefaultShapeStrength { get; set; } = 2.8f;
    #endregion

    #region Shadows
    [ExportGroup("Fixed Ground Shadows")]
    [Export(PropertyHint.Range, "0,5,0.1")]
    public float ShadowLength { get; set; } = 2.2f;

    [Export(PropertyHint.Range, "0,1,0.05")]
    public float ShadowOpacity { get; set; } = 0.68f;
    #endregion

    #region Directions
    // =========================================================
    // Return the world-space direction toward the light source.
    public Vector2 GetDirection()
    {
        return SunDirection.LengthSquared() > 0.0001f
            ? SunDirection.Normalized()
            : new Vector2(-1f, -0.7f).Normalized();
    }

    // =========================================================
    // Include the light's height above the illustrated world.
    public Vector3 GetSurfaceDirection()
    {
        Vector2 direction = GetDirection();
        return new Vector3(direction.X, direction.Y,
            Mathf.Max(0.05f, SunElevation)).Normalized();
    }
    #endregion
}