// Represents any walkable grass species using cached or imported artwork.
// GrassHeight remains the shared atlas selector used by the baking code.
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
    #endregion

    #region Lifecycle
    // =========================================================
    // Attach terrain-adjusted grass artwork without creating collision.
    public override async void _Ready()
    {
        try
        {
            if (Definition == null)
                throw new System.InvalidOperationException(
                    "Grass requires a GrassDefinition.");

            await VegetationAtlas.EnsureReady(this);
            if (!IsInsideTree() || IsQueuedForDeletion()) return;

            TerrainVisual visual = TerrainVisual.Attach(
                this,
                VegetationAtlas.GetGrassRegion(Definition.BakedHeight, Variant),
                VegetationAtlas.GrassOrigin, Vector2.One, false,
                Definition.Visual, VegetationAtlas.Texture,
                VegetationAtlas.GrassWindMaterial);

            float size = Mathf.Max(0.1f, SizeMultiplier);
            visual.Scale = new Vector2(Mirror ? -size : size, size);
            SetProcess(false);
        }
        catch (System.Exception error)
        {
            GD.PushError($"Grass '{Name}' artwork failed: {error}");
        }
    }
    #endregion
}