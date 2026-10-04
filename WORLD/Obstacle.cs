// Creates obstacle collision, terrain-adjusted baked artwork and a cached shadow.
// Rocks select a stable atlas variant without running a surface shader in gameplay.
using Godot;

public partial class Obstacle : StaticBody2D
{
	#region Configuration
	public enum ObstacleKind { Rock, Crate }

	[Export] public ObstacleKind Kind { get; set; } = ObstacleKind.Rock;
	[Export] public Vector2 Footprint { get; set; } = new(96, 48);
	[Export] public float Height { get; set; } = 72f;
	[Export] public int RockVariant { get; set; } = -1;
	#endregion

	#region Lifecycle
	// =========================================================
	// Build collision, select baked artwork and project the obstacle's shadow.
    public override async void _Ready()
    {
        AddChild(new CollisionShape2D
        {
            Name = "Footprint",
            Shape = new RectangleShape2D { Size = Footprint }
        });

        try
        {
            await PlaceholderAtlas.EnsureReady(this);
            if (!IsInsideTree() || IsQueuedForDeletion()) return;

            int variant = RockVariant;
            if (variant < 0)
            {
                variant = (int)(IsoGrid.Hash(
                    Mathf.RoundToInt(GlobalPosition.X),
                    Mathf.RoundToInt(GlobalPosition.Y), 64127u)
                    % (uint)RockDrawing.VariantCount);
            }

            Rect2 region = Kind == ObstacleKind.Rock
                ? PlaceholderAtlas.GetRockRegion(variant)
                : PlaceholderAtlas.CrateRegion;

            TerrainVisual.Attach(this, region, new Vector2(-80, -120),
                new Vector2(Footprint.X / 96f, Height / 72f), false);

            WorldAtmosphere atmosphere =
                GetTree().GetFirstNodeInGroup("world_atmosphere") as WorldAtmosphere;
            atmosphere?.CreateObstacleShadow(this);
        }
        catch (System.Exception error)
        {
            GD.PushError($"Obstacle artwork failed: {error}");
        }
    }
    #endregion
}
