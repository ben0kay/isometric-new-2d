// Provides a provisional helmet beam aimed toward the mouse.
// Equipment requirements and battery consumption can be added later.
using Godot;

public partial class PlayerFlashlight : Node
{
    #region Configuration
    [ExportGroup("Helmet Light")]
    [Export] public bool Enabled { get; set; } = true;
    [Export] public float Range { get; set; } = 600f;

    [Export(PropertyHint.Range, "5,80,1")]
    public float HalfAngleDegrees { get; set; } = 22f;

    [Export(PropertyHint.Range, "0,1,0.05")]
    public float Strength { get; set; } = 1f;
    #endregion

    #region State
    public Vector2 Position { get; private set; }
    public Vector2 Direction { get; private set; } = Vector2.Down;
    public bool Active => Enabled && _health != null && _health.IsAlive;

    private Player _player;
    private Health _health;
    #endregion

    #region Lifecycle
    // =========================================================
    // Locate the player through its grouped Systems parent.
    public override void _Ready()
    {
        _player = GetParent().GetParent<Player>();
        _health = _player.GetNode<Health>("Systems/Health");
    }

    // =========================================================
    // Toggle only when normal gameplay input is available.
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed ||
            key.Echo || key.PhysicalKeycode != Key.F)
            return;

        if (!_health.IsAlive || !InputModes.For(_player).GameplayAllowed)
            return;

        Enabled = !Enabled;
        GetViewport().SetInputAsHandled();
    }
    #endregion

    #region Beam
    // =========================================================
    // Sample the visual helmet position after movement and terrain adjustment.
    public void UpdateBeam()
    {
        float elevation = WorldLayerController.HeightFor(
            _player, _player.GlobalPosition);

        Position = _player.GlobalPosition + Vector2.Up *
            (elevation + _player.JumpHeight + 48f);

        Vector2 aim = _player.GetGlobalMousePosition() - Position;
        if (aim.LengthSquared() > 0.001f)
            Direction = aim.Normalized();
    }
    #endregion
}