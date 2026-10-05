// Draws green collision footprints and labelled ground-resource boundaries.
// F8 toggles the overlay without changing physics, terrain or world generation.
using Godot;
using System;
using System.Collections.Generic;

public partial class WorldCollisionDebug : Node2D
{
    #region Configuration
    [Export] public bool Enabled { get; set; } = true;
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

    #region Lifecycle
    // =========================================================
    // Keep world outlines above artwork and diagnostics above the normal HUD.
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

        SetProcess(Enabled);
        _status.Visible = Enabled;
    }

    // =========================================================
    // Toggle without depending on an Input Map action.
    public override void _Input(InputEvent input)
    {
        if (input is not InputEventKey key ||
            !key.Pressed || key.Echo || key.Keycode != Key.F8)
            return;

        Enabled = !Enabled;
        _status.Visible = Enabled;
        _scanTimer = _drawTimer = 0;
        SetProcess(Enabled);
        QueueRedraw();
        GetViewport().SetInputAsHandled();
    }

    // =========================================================
    // Discover streamed objects periodically and refresh moving footprints.
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
        _status.Text = "F8: collision debug\n" +
            (resources?.GetDebugSummary() ?? "Ground-resource service not ready.");
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
    // Draw active collision geometry and the current shrinking deposit footprints.
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

            Vector2[] points;
            bool fill = true;

            if (node is CollisionShape2D shape)
            {
                if (shape.Disabled || shape.Shape == null) continue;
                points = MakeOutline(shape.Shape);
                fill = shape.Shape is not ConcavePolygonShape2D &&
                    shape.Shape is not SegmentShape2D;
            }
            else if (node is CollisionPolygon2D polygon)
            {
                if (polygon.Disabled) continue;
                points = polygon.Polygon;
                fill = polygon.BuildMode ==
                    CollisionPolygon2D.BuildModeEnum.Solids;
            }
            else continue;

            if (points.Length < 2) continue;

            Vector2 offset = Vector2.Zero;
            if (AlignWithArtwork &&
                owner.GetNodeOrNull<Node2D>("Visual") is Node2D visual)
                offset = visual.GlobalPosition - owner.GlobalPosition;

            Vector2[] projected = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++)
                projected[i] = ToLocal(node.ToGlobal(points[i]) + offset);

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
        if (resources == null) return;

        foreach (var patch in resources.GetDebugFootprints())
        {
            Vector2[] points = new Vector2[patch.Points.Length];
            for (int i = 0; i < points.Length; i++)
                points[i] = ToLocal(patch.Points[i]);

            Paint(points, true);
            DrawString(ThemeDB.FallbackFont, ToLocal(patch.Centre),
                patch.Label, HorizontalAlignment.Left, -1, 16, OutlineColor);
        }
    }

    // =========================================================
    // Fill simple polygons and close their bright outlines.
    private void Paint(Vector2[] points, bool fill)
    {
        if (fill && points.Length >= 3)
            DrawColoredPolygon(points, FillColor);

        Vector2[] outline = new Vector2[points.Length + 1];
        Array.Copy(points, outline, points.Length);
        outline[^1] = points[0];
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
    // Approximate circular edges, retaining the straight middle of capsules.
    private static Vector2[] RoundedOutline(float radius, float halfMiddle)
    {
        Vector2[] points = new Vector2[CircleSegments + 2];
        int halfSegments = CircleSegments / 2;

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