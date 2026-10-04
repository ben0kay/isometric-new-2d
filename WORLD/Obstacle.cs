// Shares footprint collision and cached shadows for solid world objects.
// Trees and rocks override configuration/artwork while navigation sees one base type.
using Godot;
using System.Threading.Tasks;

public partial class Obstacle : StaticBody2D
{
	#region Configuration
	public enum ObstacleKind { Rock, Crate }

	[ExportGroup("Obstacle")]
	[Export] public ObstacleKind Kind { get; set; } = ObstacleKind.Rock;
	[Export] public Vector2 Footprint { get; set; } = new(96, 48);
	[Export] public float Height { get; set; } = 72f;
	[Export] public int RockVariant { get; set; } = -1;
	[Export] public VisualDefinition VisualOverride { get; set; }
	#endregion

	#region Lifecycle
	// =========================================================
	// Configure the instance, build its collision and attach artwork and sunlight shadow.
	public override async void _Ready()
	{
		try
		{
			ConfigureInstance();
			Footprint = new Vector2(
				Mathf.Max(1f, Footprint.X), Mathf.Max(1f, Footprint.Y));
			CollisionLayer = 1;
			CollisionMask = 0;

			AddChild(new CollisionShape2D
			{
				Name = "Footprint",
				Shape = new RectangleShape2D { Size = Footprint }
			});

			await AttachArtworkAsync();
			if (!IsInsideTree() || IsQueuedForDeletion()) return;

			WorldAtmosphere atmosphere = GetTree().GetFirstNodeInGroup(
				"world_atmosphere") as WorldAtmosphere;
			atmosphere?.CreateObstacleShadow(this);
			SetProcess(false);
		}
		catch (System.Exception error)
		{
			GD.PushError($"Obstacle '{Name}' initialization failed: {error}");
		}
	}

	// =========================================================
	// Let a derived family configure its footprint before collision is created.
	protected virtual void ConfigureInstance()
	{
	}
	#endregion

	#region Artwork
	// =========================================================
	// Preserve the existing crate and generic obstacle visual behaviour.
	protected virtual async Task AttachArtworkAsync()
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

		TerrainVisual.Attach(
			this, region, new Vector2(-80, -120),
			new Vector2(Footprint.X / 96f, Height / 72f),
			false, VisualOverride);
	}
	#endregion
}
