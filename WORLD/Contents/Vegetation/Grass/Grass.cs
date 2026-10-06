// Represents walkable grass using shared cached artwork.
// Short grass keeps wind but does not react to player brushing.
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

    #region Shared Materials
    private static readonly ShaderMaterial[] Materials = new ShaderMaterial[4];

    // =========================================================
    // Reuse material variants instead of creating a material for every tuft.
    private static ShaderMaterial GetGrassMaterial(bool shortGrass, bool tinted)
    {
        int index = (shortGrass ? 2 : 0) + (tinted ? 1 : 0);
        ShaderMaterial material = Materials[index];

        if (material != null && GodotObject.IsInstanceValid(material))
            return material;

        material =
            (ShaderMaterial)VegetationAtlas.GrassWindMaterial.Duplicate();

        material.SetShaderParameter("biome_tint_enabled", tinted);
        material.SetShaderParameter("brush_enabled", !shortGrass);
        Materials[index] = material;
        return material;
    }

    // =========================================================
    // Update only the two shared material variants that permit brushing.
    public static void UpdateBrush(
        Vector2 position, Vector2 direction, Vector2 radius, float strength)
    {
        for (int i = 0; i < 2; i++)
        {
            ShaderMaterial material = Materials[i];

            if (material == null || !GodotObject.IsInstanceValid(material))
                continue;

            material.SetShaderParameter("brush_position", position);
            material.SetShaderParameter("brush_direction", direction);
            material.SetShaderParameter("brush_radius", radius);
            material.SetShaderParameter("brush_strength", strength);
        }
    }
    #endregion

    #region Lifecycle
    // =========================================================
    // Attach artwork with the appropriate shared tint and brushing settings.
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
            bool tinted = tintStrength > 0f;
            bool shortGrass = Definition.BakedHeight == GrassHeight.Short;
            ShaderMaterial material = GetGrassMaterial(shortGrass, tinted);

            TerrainVisual visual = TerrainVisual.Attach(
                this,
                VegetationAtlas.GetGrassRegion(Definition.BakedHeight, Variant),
                VegetationAtlas.GrassOrigin, Vector2.One, false,
                Definition.Visual, VegetationAtlas.Texture, material);

            float size = Mathf.Max(0.1f, SizeMultiplier);
            visual.Scale = new Vector2(Mirror ? -size : size, size);

            if (tinted &&
                visual.GetNodeOrNull<Sprite2D>("Artwork") is Sprite2D sprite &&
                sprite.Material == material)
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