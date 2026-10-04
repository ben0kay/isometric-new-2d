// Responds to health signals with hit feedback and actor death handling.
// Player instances can respawn; enemy instances are removed on death.
using Godot;

public partial class CombatLife : Node
{
    #region Configuration
    [Export] public bool Respawn { get; set; }
    [Export] public double RespawnDelay { get; set; } = 1.5;
    #endregion

    #region State
    private CharacterBody2D _actor;
    private Health _health;
    private CanvasItem _visual;
    private Label _label;
    private Vector2 _spawn;
    private uint _collisionLayer;
    private double _flash, _respawn;
    private bool _waiting;
    #endregion

    #region Lifecycle
    // =========================================================
    // Connect health feedback and optionally create the player's compact HUD.
    public override void _Ready()
    {
        _actor = GetParent().GetParent<CharacterBody2D>();
        _health = GetNode<Health>("../Health");
        _spawn = _actor.GlobalPosition;
        _collisionLayer = _actor.CollisionLayer;
        _health.Hit += OnHit;
        _health.Died += OnDeath;
        _health.Changed += OnChanged;
        SetProcess(false);

        if (!Respawn) return;
        CanvasLayer hud = new() { Name = "CombatHUD", Layer = 2 };
        AddChild(hud);
        _label = new Label { Position = new Vector2(16, 72) };
        _label.AddThemeFontSizeOverride("font_size", 16);
        _label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        hud.AddChild(_label);
        OnChanged(_health.Current, _health.MaxHealth);
    }

    // =========================================================
    // Remove managed event subscriptions when this actor leaves the tree.
    public override void _ExitTree()
    {
        if (!GodotObject.IsInstanceValid(_health)) return;
        _health.Hit -= OnHit;
        _health.Died -= OnDeath;
        _health.Changed -= OnChanged;
    }

    // =========================================================
    // Process feedback only while a flash or respawn countdown is active.
    public override void _Process(double delta)
    {
        if (_flash > 0.0)
        {
            _flash -= delta;
            if (_flash <= 0.0 && GodotObject.IsInstanceValid(_visual))
                _visual.Modulate = Colors.White;
        }

        if (_waiting)
        {
            _respawn -= delta;
            if (_respawn <= 0.0) Revive();
        }
        if (_flash <= 0.0 && !_waiting) SetProcess(false);
    }
    #endregion

    #region Feedback
    // =========================================================
    // Brighten the existing sprite briefly without rebuilding its artwork.
    private void OnHit()
    {
        _visual = _actor.GetNodeOrNull<CanvasItem>("Visual");
        if (_visual != null) _visual.Modulate = new Color(3f, 3f, 3f, 1f);
        _flash = 0.08;
        SetProcess(true);
    }

    // =========================================================
    // Update health text whenever damage or restoration changes the value.
    private void OnChanged(int current, int maximum)
    {
        if (_label == null) return;
        _label.Text = $"HEALTH {current} / {maximum}";
        _label.Modulate = current <= maximum / 3 ? new Color("#ff7164") : Colors.White;
    }
    #endregion

    #region Death And Respawn
    // =========================================================
    // Stop the actor and either remove it or begin its respawn countdown.
    private void OnDeath()
    {
        _actor.Velocity = Vector2.Zero;
        _actor.SetPhysicsProcess(false);
        _actor.CollisionLayer = 0;

        if (!Respawn) { _actor.QueueFree(); return; }
        _actor.Hide();
        _waiting = true;
        _respawn = System.Math.Max(0.1, RespawnDelay);
        _label.Text = "DOWN — respawning...";
        SetProcess(true);
    }

    // =========================================================
    // Return the player to its starting point with temporary damage protection.
    private void Revive()
    {
        _waiting = false;
        _actor.GlobalPosition = _spawn;
        _actor.Velocity = Vector2.Zero;
        _actor.CollisionLayer = _collisionLayer;
        _health.Restore();
        _actor.Show();
        _actor.SetPhysicsProcess(true);

        Camera2D camera = _actor.GetNodeOrNull<Camera2D>("Camera2D");
        if (camera == null) return;
        camera.ResetSmoothing();
        camera.ForceUpdateScroll();
    }
    #endregion
}