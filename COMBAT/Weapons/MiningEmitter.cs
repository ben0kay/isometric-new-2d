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

    private Line2D _glow;
    private Line2D _core;
    private Vector2 _endpoint;
    private Vector2 _direction;
    private float _visualHeight;
    private double _remaining;
    private PlayerStats _stats;
    #endregion

    #region Lifecycle
// =========================================================
// Cache actor systems and create reusable mining query and beam objects.
public override void _Ready()
{
    _source = GetParent().GetParent<CharacterBody2D>();
    _stats = GetNodeOrNull<PlayerStats>("../Stats");
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
// Apply extraction and report successful mining to player survival.
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
        Node2D collider = hit["collider"].AsGodotObject() as Node2D;
        if (collider != null && !WorldLayerMember.Same(_source, collider)) return;
        bool worked = false;
        float efficiency = _stats?.Get(PlayerStat.MiningEfficiency) ?? 1f;
        float power = attack.MiningPower * efficiency;

        if (collider is IMiningTarget target &&
            attack.MiningStrength >= target.RequiredStrength)
        {
            ResourceWorld resources = ResourceWorld.Find(this);
            if (GodotObject.IsInstanceValid(resources))
            {
                Vector2 position = collider.GlobalPosition;
                worked = target.Mine(power, (item, count) =>
                    resources.Spawn(item.Id, count, position, collider));
            }
        }
        else if (collider != null)
        {
            ResourceHarvest harvest = collider.GetNodeOrNull<ResourceHarvest>(
                "Harvest");
            if (harvest != null)
                worked = harvest.Mine(attack.MiningStrength, power);
        }

        if (worked && _source is Player player)
            player.ReportWork(Math.Max(0.03, attack.Cooldown));

        if (!worked) color = new Color("#ffbd77");
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
// Place beam artwork against the source actor's current world layer.
private Vector2 ToVisible(Vector2 groundPoint)
{
    float height = WorldLayerController.HeightFor(_source, groundPoint);
    return groundPoint + Vector2.Up * (height + _visualHeight);
}
    #endregion
}
