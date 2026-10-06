// Represents walkable grass using shared cached artwork and wind materials.
// Optional biome tinting uses one shared recolouring material for all tinted tufts.
using Godot;

public enum GrassHeight { Short, Medium, Tall }

public partial class Grass : Node2D
{
    #region Configuration
    [ExportGroup("Grass")]
    [Export] public GrassDefinition Definition { get; set; }
    [Export] public int Variant { get; set; }
    [Export] public float SizeMultiplier { get; set; } = 1f;
    [Export] public bool Mirror { get; set; }

    public Color BiomeTint { get; set; } = Colors.White;
    public float BiomeTintStrength { get; set; }
    #endregion

    #region Shared Material
    private static ShaderMaterial _tintedWind;

    // =========================================================
    // Share one alternative material rather than duplicate a material per tuft.
    private static ShaderMaterial GetTintedWind()
    {
        if (_tintedWind != null &&
            GodotObject.IsInstanceValid(_tintedWind))
            return _tintedWind;

        _tintedWind =
            (ShaderMaterial)VegetationAtlas.GrassWindMaterial.Duplicate();

        _tintedWind.SetShaderParameter("biome_tint_enabled", true);
        return _tintedWind;
    }
    #endregion

    #region Lifecycle
    // =========================================================
    // Attach terrain-adjusted artwork and apply optional biome colouring.
    public override async void _Ready()
    {
        try
        {
            if (Definition == null)
                throw new System.InvalidOperationException(
                    "Grass requires a GrassDefinition.");

            await VegetationAtlas.EnsureReady(this);
            if (!IsInsideTree() || IsQueuedForDeletion()) return;

            float tintStrength = Mathf.Clamp(BiomeTintStrength, 0f, 1f);

            TerrainVisual visual = TerrainVisual.Attach(
                this,
                VegetationAtlas.GetGrassRegion(Definition.BakedHeight, Variant),
                VegetationAtlas.GrassOrigin, Vector2.One, false,
                Definition.Visual, VegetationAtlas.Texture,
                tintStrength > 0f
                    ? GetTintedWind()
                    : VegetationAtlas.GrassWindMaterial);

            float size = Mathf.Max(0.1f, SizeMultiplier);
            visual.Scale = new Vector2(Mirror ? -size : size, size);

            // The fallback sprite uses the shared tint-aware wind material.
            if (tintStrength > 0f &&
                visual.GetNodeOrNull<Sprite2D>("Artwork") is Sprite2D sprite &&
                sprite.Material == _tintedWind)
            {
                sprite.SelfModulate = new Color(
                    BiomeTint.R, BiomeTint.G, BiomeTint.B, tintStrength);
            }

            SetProcess(false);
        }
        catch (System.Exception error)
        {
            GD.PushError($"Grass '{Name}' artwork failed: {error}");
        }
    }
    #endregion
}