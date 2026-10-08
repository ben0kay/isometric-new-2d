// Supplies one shared player-brushing effect to plant and grass materials.
// Smooth movement and strength make vegetation relax after the player passes.
using Godot;

public partial class VegetationInteraction : Node
{
    #region Configuration
    [ExportGroup("Brushing")]
    [Export] public bool Enabled { get; set; } = true;
    [Export] public Vector2 Radius { get; set; } = new(42, 24);
    [Export] public float FullStrengthSpeed { get; set; } = 120f;
    [Export] public float PositionResponse { get; set; } = 18f;
    [Export] public float BendResponse { get; set; } = 12f;
    [Export] public float RecoveryResponse { get; set; } = 6f;
    #endregion

    #region State
    private Player _player;
    private TerrainElevation _elevation;
    private Vector2 _previousPosition;
    private Vector2 _brushPosition;
    private Vector2 _direction = Vector2.Right;
    private float _strength;
    private bool _sampled;
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve the player's world using the existing world-generator hierarchy.
    public override void _Ready()
    {
        ResolvePlayer();
    }

    // =========================================================
    // Fade shared shader interaction when this controller leaves the scene.
    public override void _ExitTree()
    {
        ApplyBrush(0f);
    }

    // =========================================================
    // Update a few shared materials instead of processing individual plants.
    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(_player))
        {
            if (!ResolvePlayer())
            {
                ApplyBrush(0f);
                return;
            }

            _sampled = false;
        }

        float dt = Mathf.Max(0.0001f, (float)delta);
        Vector2 logicalPosition = _player.GlobalPosition;
        Vector2 renderedPosition = logicalPosition;

        if (!GodotObject.IsInstanceValid(_elevation))
            _elevation = GetTree()
                .GetFirstNodeInGroup("terrain_elevation") as TerrainElevation;

        if (GodotObject.IsInstanceValid(_elevation))
            renderedPosition.Y -=
                _elevation.SampleWorldHeight(logicalPosition);

        if (!_sampled)
        {
            _previousPosition = logicalPosition;
            _brushPosition = renderedPosition;
            _sampled = true;
        }

        Vector2 movement = logicalPosition - _previousPosition;
        _previousPosition = logicalPosition;

        // Teleports should not produce a large brushing pulse.
        bool teleported = movement.LengthSquared() > 256f * 256f;
        float speed = teleported ? 0f : movement.Length() / dt;

        if (movement.LengthSquared() > 0.001f && !teleported)
            _direction = movement.Normalized();

        if (teleported)
        {
            _brushPosition = renderedPosition;
            _strength = 0f;
        }

        float positionBlend = 1f - Mathf.Exp(
            -Mathf.Max(0.1f, PositionResponse) * dt);

        _brushPosition = _brushPosition.Lerp(
            renderedPosition, positionBlend);

        float target = Enabled
            ? Mathf.Clamp(speed / Mathf.Max(1f, FullStrengthSpeed), 0f, 1f)
            : 0f;

        float response = target > _strength
            ? BendResponse : RecoveryResponse;

        float strengthBlend = 1f - Mathf.Exp(
            -Mathf.Max(0.1f, response) * dt);

        _strength = Mathf.Lerp(_strength, target, strengthBlend);
        ApplyBrush(_strength);
    }
    #endregion

    #region World Resolution
    // =========================================================
    // Find the existing player without introducing another player input system.
    private bool ResolvePlayer()
    {
        WorldGenerator generator = GetTree()
            .GetFirstNodeInGroup("world_generator") as WorldGenerator;

        if (generator == null) return false;

        Node world = generator.GetParent()?.GetParent();
        _player = world?.GetNodeOrNull<Player>("WorldObjects/Player");

        return GodotObject.IsInstanceValid(_player);
    }
    #endregion

    #region Shader Updates
// =========================================================
// Publish one contact field shared by all vegetation materials.
private void ApplyBrush(float strength)
{
    Vector2 radius = new(
        Mathf.Max(1f, Radius.X), Mathf.Max(1f, Radius.Y));

    WorldLighting.UpdateBrush(
        _brushPosition, _direction, radius, strength);
}
    #endregion
}