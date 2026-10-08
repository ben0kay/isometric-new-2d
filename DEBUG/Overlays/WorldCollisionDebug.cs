// Draws green collision footprints and labelled ground-resource boundaries.
// F8 toggles the overlay without changing physics, terrain or world generation.
using Godot;
using System;
using System.Collections.Generic;

public partial class WorldCollisionDebug : Node2D, IDebugOptionProvider
{
	#region Configuration
	[Export] public bool Enabled { get; set; } = false;
	[Export] public bool AlignWithArtwork { get; set; } = true;
	[Export] public float RefreshSeconds { get; set; } = 0.1f;
	[Export] public Color FillColor { get; set; } = new(0.1f, 1f, 0.25f, 0.15f);
	[Export] public Color OutlineColor { get; set; } = new(0.2f, 1f, 0.35f, 0.95f);
	#endregion

	#region Runtime
	private readonly List<Node2D> _collisions = new();
	private Node _objects;
	private Label _status;
	private double _scanTimer, _drawTimer;
	private const int CircleSegments = 32;
	#endregion

		// =========================================================
// Register this overlay for automatic discovery by the F1 menu.
public override void _EnterTree()
{
	AddToGroup(DebugOption.Group);
}

// =========================================================
// Describe collision controls without putting overlay-specific code in the menu.
public IEnumerable<DebugOption> GetDebugOptions()
{
	yield return new DebugOption
	{
		Name = "Collision footprints",
		Order = 10,
		Read = () => Enabled,
		Write = SetEnabled
	};
}

	#region Lifecycle
// =========================================================
// Initialize independent debug overlays and the shared F1 menu.
public override void _Ready()
{
	ZIndex = 4095;
	ZAsRelative = false;
	_objects = GetParent().GetNode("WorldObjects");

	CanvasLayer hud = new() { Name = "DebugStatus", Layer = 20 };
	AddChild(hud);

	_status = new Label
	{
		Position = new Vector2(16, 170),
		MouseFilter = Control.MouseFilterEnum.Ignore
	};
	_status.AddThemeColorOverride("font_color", OutlineColor);
	_status.AddThemeColorOverride("font_shadow_color", Colors.Black);
	_status.AddThemeConstantOverride("shadow_offset_x", 1);
	_status.AddThemeConstantOverride("shadow_offset_y", 1);
	hud.AddChild(_status);

	if (GetNodeOrNull<WorldEnemyRangesDebug>("EnemyRanges") == null)
		AddChild(new WorldEnemyRangesDebug { Name = "EnemyRanges" });

	if (GetNodeOrNull<WorldWildlifeRangesDebug>("WildlifeRanges") == null)
		AddChild(new WorldWildlifeRangesDebug { Name = "WildlifeRanges" });

	if (GetNodeOrNull<DebugMenu>("DebugMenu") == null)
		AddChild(new DebugMenu { Name = "DebugMenu" });

	SetProcessInput(false);
	SetEnabled(Enabled);
}

// =========================================================
// Overlay hotkeys are handled centrally by the F1 menu.
public override void _Input(InputEvent input)
{
}

// =========================================================
// Discover streamed objects periodically and refresh collision drawings.
public override void _Process(double delta)
{
	_scanTimer -= delta;
	_drawTimer -= delta;

	if (_scanTimer <= 0)
	{
		_scanTimer = 0.5;
		_collisions.Clear();
		Collect(_objects);
	}

	if (_drawTimer > 0) return;
	_drawTimer = Math.Max(0.02, RefreshSeconds);

	GroundResourceWorld resources = GroundResourceWorld.Find(this);
	_status.Text = "Collision overlay · F1 debug menu\n" +
		(resources?.GetDebugSummary() ??
			"Ground-resource service not ready.");

	QueueRedraw();
}

// =========================================================
// Toggle collision presentation independently from the shared debug menu.
public void SetEnabled(bool enabled)
{
	Enabled = enabled;
	Visible = enabled;
	_scanTimer = _drawTimer = 0;
	SetProcess(enabled);

	if (GodotObject.IsInstanceValid(_status))
		_status.Visible = enabled;

	QueueRedraw();
}
	#endregion

	#region Discovery
	// =========================================================
	// Include bodies and interaction areas without adding debug nodes to each object.
	private void Collect(Node parent)
	{
		foreach (Node child in parent.GetChildren())
		{
			if (child is CollisionShape2D || child is CollisionPolygon2D)
				_collisions.Add((Node2D)child);

			Collect(child);
		}
	}
	#endregion

	#region Drawing
// =========================================================
// Draw collisions, ground deposits and water boundaries in separate passes.
public override void _Draw()
{
	if (!Enabled) return;

	foreach (Node2D node in _collisions)
	{
		if (!GodotObject.IsInstanceValid(node) ||
			!node.IsInsideTree() || node.IsQueuedForDeletion())
			continue;

		if (node.GetParent() is not CollisionObject2D owner ||
			(owner.CollisionLayer == 0 && owner.CollisionMask == 0))
			continue;

		if (owner is Enemy enemy && !enemy.IsActivated)
			continue;

		Vector2[] collisionPoints;
		bool fill = true;

		if (node is CollisionShape2D shape)
		{
			if (shape.Disabled || shape.Shape == null) continue;
			collisionPoints = MakeOutline(shape.Shape);
			fill = shape.Shape is not ConcavePolygonShape2D &&
				shape.Shape is not SegmentShape2D;
		}
		else if (node is CollisionPolygon2D polygon)
		{
			if (polygon.Disabled) continue;
			collisionPoints = polygon.Polygon;
			fill = polygon.BuildMode ==
				CollisionPolygon2D.BuildModeEnum.Solids;
		}
		else continue;

		if (collisionPoints.Length < 2) continue;

		Vector2 offset = Vector2.Zero;
		if (AlignWithArtwork &&
			owner.GetNodeOrNull<Node2D>("Visual") is Node2D visual)
			offset = visual.GlobalPosition - owner.GlobalPosition;

		Vector2[] projected = new Vector2[collisionPoints.Length];
		for (int i = 0; i < collisionPoints.Length; i++)
			projected[i] = ToLocal(node.ToGlobal(collisionPoints[i]) + offset);

		if (node is CollisionShape2D segmentShape &&
			segmentShape.Shape is ConcavePolygonShape2D)
		{
			for (int i = 0; i + 1 < projected.Length; i += 2)
				DrawLine(projected[i], projected[i + 1],
					OutlineColor, 2f, true);
		}
		else
			Paint(projected, fill);
	}

	GroundResourceWorld resources = GroundResourceWorld.Find(this);
	if (resources != null)
	{
		foreach (var patch in resources.GetDebugFootprints())
		{
			Vector2[] depositPoints = new Vector2[patch.Points.Length];
			for (int i = 0; i < depositPoints.Length; i++)
				depositPoints[i] = ToLocal(patch.Points[i]);

			Paint(depositPoints, true);
			DrawString(ThemeDB.FallbackFont, ToLocal(patch.Centre),
				patch.Label, HorizontalAlignment.Left, -1, 16, OutlineColor);
		}
	}

	SurfaceWorld surfaces = SurfaceWorld.Find(this);
	if (surfaces != null)
	{
		foreach (SurfacePatch patch in surfaces.GetDebugPatches())
		{
			Vector2[] surfacePoints = patch.GetDebugOutline();
			for (int i = 0; i < surfacePoints.Length; i++)
				surfacePoints[i] = ToLocal(surfacePoints[i]);

			Paint(surfacePoints, true);
			DrawString(ThemeDB.FallbackFont, ToLocal(patch.GlobalPosition),
				patch.Definition.Id, HorizontalAlignment.Left, -1, 16,
				OutlineColor);
		}
	}
}

// =========================================================
// Remove duplicate vertices and draw only successfully triangulated fills.
private void Paint(Vector2[] points, bool fill)
{
	List<Vector2> clean = new(points.Length);
	foreach (Vector2 point in points)
	{
		if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
			return;

		if (clean.Count == 0 ||
			clean[^1].DistanceSquaredTo(point) > 0.000001f)
			clean.Add(point);
	}

	if (clean.Count > 1 &&
		clean[0].DistanceSquaredTo(clean[^1]) <= 0.000001f)
		clean.RemoveAt(clean.Count - 1);

	if (clean.Count < 2) return;
	Vector2[] polygon = clean.ToArray();

	if (fill && polygon.Length >= 3)
	{
		int[] triangles = Geometry2D.TriangulatePolygon(polygon);
		Color[] colors = { FillColor, FillColor, FillColor };

		for (int i = 0; i + 2 < triangles.Length; i += 3)
		{
			DrawPrimitive(new[]
			{
				polygon[triangles[i]],
				polygon[triangles[i + 1]],
				polygon[triangles[i + 2]]
			}, colors, Array.Empty<Vector2>());
		}
	}

	Vector2[] outline = new Vector2[polygon.Length + 1];
	Array.Copy(polygon, outline, polygon.Length);
	outline[^1] = polygon[0];
	DrawPolyline(outline, OutlineColor, 2f, true);
}

	// =========================================================
	// Read real shape dimensions rather than guessing from artwork.
	private static Vector2[] MakeOutline(Shape2D shape)
	{
		switch (shape)
		{
			case RectangleShape2D rectangle:
				Vector2 half = rectangle.Size * 0.5f;
				return new[]
				{
					new Vector2(-half.X, -half.Y),
					new Vector2(half.X, -half.Y),
					new Vector2(half.X, half.Y),
					new Vector2(-half.X, half.Y)
				};

			case CircleShape2D circle:
				return RoundedOutline(circle.Radius, 0f);

			case CapsuleShape2D capsule:
				return RoundedOutline(capsule.Radius,
					Mathf.Max(0f, capsule.Height * 0.5f - capsule.Radius));

			case ConvexPolygonShape2D convex:
				return convex.Points;

			case ConcavePolygonShape2D concave:
				return concave.Segments;

			case SegmentShape2D segment:
				return new[] { segment.A, segment.B };

			default:
				return Array.Empty<Vector2>();
		}
	}

// =========================================================
// Build circles without duplicate vertices and capsules with separate end caps.
private static Vector2[] RoundedOutline(float radius, float halfMiddle)
{
	if (halfMiddle <= 0.0001f)
	{
		Vector2[] circle = new Vector2[CircleSegments];
		for (int i = 0; i < circle.Length; i++)
		{
			float angle = Mathf.Tau * i / circle.Length;
			circle[i] = new Vector2(
				Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
		}
		return circle;
	}

	int halfSegments = CircleSegments / 2;
	Vector2[] points = new Vector2[CircleSegments + 2];

	for (int i = 0; i <= halfSegments; i++)
	{
		float angle = Mathf.Pi + Mathf.Pi * i / halfSegments;
		points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle))
			* radius + Vector2.Up * halfMiddle;
	}

	for (int i = 0; i <= halfSegments; i++)
	{
		float angle = Mathf.Pi * i / halfSegments;
		points[halfSegments + 1 + i] =
			new Vector2(Mathf.Cos(angle), Mathf.Sin(angle))
			* radius + Vector2.Down * halfMiddle;
	}
	return points;
}
	#endregion

}
