// Defines one rock's authored artwork and ground footprint, independent of species size.
using Godot;
using System;

[Tool, GlobalClass]
public partial class RockVariantDefinition : Resource
{
    #region Configuration
    [Export] public VisualDefinition Visual { get; set; }
    [Export] public Vector2 GroundFootprint { get; set; } = new(128, 64);
    [Export] public float VisualHeight { get; set; } = 128f;
    [Export] public float SpawnWeight { get; set; } = 1f;
    #endregion

    #region Optional Burial Experiment
    [ExportGroup("Burial")]
    [Export] public bool BurialEnabled { get; set; }
    // Texture pixels; uniform artwork/instance scales also scale the burial amount.
    [Export(PropertyHint.Range, "0,64,1")]
    public float BurialDepthPixels { get; set; } = 14f;
    [Export(PropertyHint.Range, "0,8,0.25")]
    public float BurialEdgePixels { get; set; } = 2f;
    [Export(PropertyHint.Range, "0.1,4,0.1")]
    public float BurialSoftnessPixels { get; set; } = 1f;
    private ShaderMaterial _burialMaterial;
    private string _burialKey;
    #endregion

    // =========================================================
    // Fail explicitly instead of silently substituting primitive artwork.
    public void Validate()
    {
        if (!float.IsFinite(BurialDepthPixels) || BurialDepthPixels < 0 || BurialDepthPixels > 64 ||
            !float.IsFinite(BurialEdgePixels) || BurialEdgePixels < 0 || BurialEdgePixels > 8 ||
            !float.IsFinite(BurialSoftnessPixels) || BurialSoftnessPixels <= 0 || BurialSoftnessPixels > 4)
            throw new InvalidOperationException($"Invalid burial settings for '{ResourcePath}'.");
        if (Visual?.Image == null || Visual.VisualScene != null ||
            !string.IsNullOrWhiteSpace(Visual.ImageFolder) ||
            !float.IsFinite(GroundFootprint.X) || !float.IsFinite(GroundFootprint.Y) ||
            GroundFootprint.X <= 0 || GroundFootprint.Y <= 0 ||
            !float.IsFinite(VisualHeight) || VisualHeight <= 0 ||
            !float.IsFinite(SpawnWeight) || SpawnWeight < 0 ||
            !float.IsFinite(Visual.ArtworkScale.X) || Visual.ArtworkScale.X <= 0 ||
            Visual.ArtworkScale.X != Visual.ArtworkScale.Y ||
            !float.IsFinite(Visual.ImageAnchor.X) || !float.IsFinite(Visual.ImageAnchor.Y) ||
            !float.IsFinite(Visual.Offset.X) || !float.IsFinite(Visual.Offset.Y))
            throw new InvalidOperationException($"Invalid rock variant '{ResourcePath}': assign an image, positive footprint/height and uniform artwork scale.");
    }

    // =========================================================
    // Share exactly the same scaled dimensions between placement and the actor.
    public Vector2 FootprintFor(float size) => GroundFootprint * size;
    public float HeightFor(float size) => VisualHeight * size;
    // =========================================================
    // Share one cached material per variant/source, rather than duplicating per rock.
    public ShaderMaterial BurialMaterial(ShaderMaterial source, Rect2 bounds, float cutoff, bool lighting)
    {
        string key = FormattableString.Invariant($"{source?.GetInstanceId() ?? 0}|{bounds}|{cutoff:R}|{BurialEdgePixels:R}|{BurialSoftnessPixels:R}|{lighting}");
        if (_burialMaterial != null && _burialKey == key) return _burialMaterial;
        ShaderMaterial material = new()
        { Shader = GD.Load<Shader>("res://VISUALS/Lighting/RockBurial.gdshader") };
        // Preserve normal maps, shape lighting and brightness from the shared surface material.
        if (source != null)
        {
            foreach (string name in new[] { "surface_rect", "surface_lighting_enabled",
                "surface_normal_enabled", "surface_normal", "surface_normal_strength",
                "surface_shape_strength", "surface_brightness" })
            {
                Variant value = source.GetShaderParameter(name);
                if (value.VariantType != Variant.Type.Nil) material.SetShaderParameter(name, value);
            }
        }
        else
        {
            material.SetShaderParameter("surface_rect", new Vector4(bounds.Position.X,
                bounds.Position.Y, bounds.Size.X, bounds.Size.Y));
            material.SetShaderParameter("surface_lighting_enabled", lighting);
        }
        material.SetShaderParameter("burial_cutoff_y", cutoff);
        material.SetShaderParameter("burial_edge_pixels", BurialEdgePixels);
        material.SetShaderParameter("burial_softness_pixels", BurialSoftnessPixels);
        _burialKey = key; _burialMaterial = material;
        return material;
    }
}
