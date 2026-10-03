// Builds the test obstacle layout, boundary collision, and camera limits once.
// Lives on a plain Node to separate setup logic from the visual world.
using Godot;

public partial class WorldSetup : Node
{
	#region References
	private Node2D _objects;
	private Terrain _terrain;
	#endregion

	#region Setup
	// =========================================================
	// Populate the test layout and enclose the ground area.
	public override void _Ready()
	{
		_objects = GetNode<Node2D>("../WorldObjects");
		_terrain = GetNode<Terrain>("../Terrain");

		SpawnObstacle("RockLarge", new(0, -100), Obstacle.ObstacleKind.Rock, new(112, 56), 90f);
		SpawnObstacle("RockWest", new(-280, 100), Obstacle.ObstacleKind.Rock, new(80, 40), 64f);
		SpawnObstacle("RockEast", new(340, -220), Obstacle.ObstacleKind.Rock, new(144, 72), 110f);
		SpawnObstacle("CrateEast", new(230, 100), Obstacle.ObstacleKind.Crate, new(80, 40), 52f);
		SpawnObstacle("CrateNorth", new(-220, -260), Obstacle.ObstacleKind.Crate, new(96, 48), 60f);

		CreateBoundaries(_terrain.WorldSize);
		ConfigureCamera(_terrain.WorldSize);
	}

	// =========================================================
	// Create one named obstacle with its footprint and height.
	private void SpawnObstacle(string nodeName, Vector2 point, Obstacle.ObstacleKind kind, Vector2 size, float height)
	{
		_objects.AddChild(new Obstacle
		{
			Name = nodeName, Position = point,
			Kind = kind, Footprint = size, Height = height
		});
	}

	// =========================================================
	// Group all four boundary shapes under one static body.
	private void CreateBoundaries(Vector2 size)
	{
		StaticBody2D boundary = new() { Name = "WorldBoundary" };
		AddChild(boundary);
		Vector2 half = size * 0.5f;

		AddBoundaryShape(boundary, "North", new(0, -half.Y - 16), new(size.X + 64, 32));
		AddBoundaryShape(boundary, "South", new(0, half.Y + 16), new(size.X + 64, 32));
		AddBoundaryShape(boundary, "West", new(-half.X - 16, 0), new(32, size.Y));
		AddBoundaryShape(boundary, "East", new(half.X + 16, 0), new(32, size.Y));
	}

	// =========================================================
	// Add one rectangular wall to the shared boundary body.
	private void AddBoundaryShape(StaticBody2D body, string nodeName, Vector2 point, Vector2 size)
	{
		body.AddChild(new CollisionShape2D
		{
			Name = nodeName, Position = point,
			Shape = new RectangleShape2D { Size = size }
		});
	}

	// =========================================================
	// Match camera limits to the configured terrain dimensions.
	private void ConfigureCamera(Vector2 size)
	{
		Camera2D camera = _objects.GetNode<Camera2D>("Player/Camera2D");
		Vector2 half = size * 0.5f;
		camera.LimitLeft = (int)-half.X;
		camera.LimitTop = (int)-half.Y;
		camera.LimitRight = (int)half.X;
		camera.LimitBottom = (int)half.Y;
	}
	#endregion
}
