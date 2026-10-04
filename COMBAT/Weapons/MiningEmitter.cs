// Performs cooldown-driven mining queries and displays a reusable beam.
// Collection is routed through the inventory's public transaction interface.
using Godot;
using System;

public partial class MiningEmitter : Node
{
    #region Configuration
    [ExportGroup("Beam")]
    [Export] public float BeamHoldTime { get; set; } = 0.14f;
    #endregion

    #region State
    private CharacterBody2D _source;
    private TerrainElevation _elevation;
    private PhysicsRayQueryParameters2D _query;
    private Func<ItemDefinition, int, bool> _collect;
    private Line2D _glow;
    private Line2D _core;
    private Vector2 _endpoint;
    private Vector2 _direction;
    private float _visualHeight;
    private double _remaining;
    #endregion

    #region Lifecycle
    // =========================================================
    // Cache actor references and create reusable query and beam objects.
    public override void _Ready()
    {
        _source = GetParent().GetParent<CharacterBody2D>();
        _collect = GetNode<PlayerInventory>("../Inventory").TryCollect;
        _query = new PhysicsRayQueryParameters2D
        {
            CollisionMask = 9,
            HitFromInside = true
        };

        _glow = CreateLine("Glow", 9f);
        _core = CreateLine("Core", 2f);
        Stop();
    }

    // =========================================================
    // Keep an active beam attached to the actor and stop after its short hold time.
    public override void _PhysicsProcess(double delta)
    {
        _remaining -= delta;
        if (_remaining <= 0.0) { Stop(); return; }
        UpdateBeam();
    }

    // =========================================================
    // Release the native query when this actor leaves the scene.
    public override void _ExitTree()
    {
        _query?.Dispose();
    }

    // =========================================================
    // Create an unshaded line with two reusable points.
    private Line2D CreateLine(string name, float width)
    {
        Line2D line = new()
        {
            Name = name,
            Width = width,
            Antialiased = true,
            ZIndex = 5,
            Material = new CanvasItemMaterial
            {
                LightMode = CanvasItemMaterial.LightModeEnum.Unshaded,
                BlendMode = CanvasItemMaterial.BlendModeEnum.Add
            }
        };
        line.AddPoint(Vector2.Zero);
        line.AddPoint(Vector2.Zero);
        AddChild(line);
        return line;
    }
    #endregion

    #region Mining
    // =========================================================
    // Stop at the first solid obstacle and mine only compatible targets.
    public void Emit(MiningAttack attack, Vector2 direction)
    {
        if (direction.LengthSquared() < 0.0001f) return;

        _direction = direction.Normalized();
        Vector2 origin = _source.GlobalPosition;
        _endpoint = origin + _direction * Mathf.Max(8f, attack.Range);
        _query.From = origin;
        _query.To = _endpoint;

        Godot.Collections.Dictionary hit = _source.GetWorld2D()
            .DirectSpaceState.IntersectRay(_query);
        Color color = attack.Tint;

        if (hit.Count > 0)
        {
            _endpoint = hit["position"].AsVector2();
            if (hit["collider"].AsGodotObject() is IMiningTarget target)
            {
                if (!target.Mine(attack.MiningPower, _collect))
                    color = new Color("#ffbd77");
            }
            else color = new Color("#ffbd77");
        }

        _visualHeight = attack.VisualHeight;
        _remaining = Mathf.Max(0.05f, BeamHoldTime);
        _core.DefaultColor = color;
        _glow.DefaultColor = new Color(color.R, color.G, color.B, 0.18f);
        _core.Show();
        _glow.Show();
        UpdateBeam();
        SetPhysicsProcess(true);
    }

    // =========================================================
    // Hide the beam when switching tools or opening the backpack.
    public void Stop()
    {
        _remaining = 0.0;
        _core?.Hide();
        _glow?.Hide();
        SetPhysicsProcess(false);
    }
    #endregion

    #region Beam Placement
    // =========================================================
    // Update only the existing beam points between mining pulses.
    private void UpdateBeam()
    {
        Vector2 origin = _source.GlobalPosition;
        float distance = origin.DistanceTo(_endpoint);
        Vector2 start = origin + _direction * Mathf.Min(18f, distance * 0.3f);
        Vector2 visibleStart = ToVisible(start);
        Vector2 visibleEnd = ToVisible(_endpoint);

        _core.SetPointPosition(0, _core.ToLocal(visibleStart));
        _core.SetPointPosition(1, _core.ToLocal(visibleEnd));
        _glow.SetPointPosition(0, _glow.ToLocal(visibleStart));
        _glow.SetPointPosition(1, _glow.ToLocal(visibleEnd));
    }

    // =========================================================
    // Match terrain elevation and the attack's visual aiming height.
    private Vector2 ToVisible(Vector2 groundPoint)
    {
        _elevation ??= GetTree().GetFirstNodeInGroup("terrain_elevation")
            as TerrainElevation;
        float terrainHeight = _elevation?.SampleWorldHeight(groundPoint) ?? 0f;
        return groundPoint + Vector2.Up * (terrainHeight + _visualHeight);
    }
    #endregion
}