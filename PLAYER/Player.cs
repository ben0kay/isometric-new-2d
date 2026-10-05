// Controls player movement, terrain-adjusted artwork, and equipped-tool use.
// Calculated movement speed and cached inventory penalties remain separate.
using Godot;

public partial class Player : CharacterBody2D
{
	#region Configuration
	[ExportGroup("Artwork")]
	[Export] public VisualDefinition VisualOverride { get; set; }
	#endregion

	#region State
	private TerrainVisual _visual;
	private Weapon _weapon;
	private Health _health;
	private PlayerStats _stats;
	private PlayerInventory _inventory;
	private InventoryHud _inventoryHud;
	private float _facing = 1f;
	#endregion

	#region Lifecycle
	// =========================================================
	// Resolve player systems and attach custom or baked artwork.
	public override async void _Ready()
	{
		MotionMode = MotionModeEnum.Floating;
		_weapon = GetNode<Weapon>("Systems/Weapon");
		_health = GetNode<Health>("Systems/Health");
		_stats = GetNode<PlayerStats>("Systems/Stats");
		_inventory = GetNode<PlayerInventory>("Systems/Inventory");
		_inventoryHud = GetNode<InventoryHud>("InventoryHud");
		SetPhysicsProcess(false);

		try
		{
			await PlaceholderAtlas.EnsureReady(this);
			if (!IsInsideTree() || IsQueuedForDeletion()) return;

			_visual = TerrainVisual.Attach(
				this, PlaceholderAtlas.PlayerRegion,
				new Vector2(-32, -72), Vector2.One, true, VisualOverride);
			SetPhysicsProcess(true);
		}
		catch (System.Exception error)
		{
			GD.PushError($"Player initialization failed: {error}");
		}
	}

    // =========================================================
    // Respect inventory windows while applying movement and terrain-aware tool use.
    public override void _PhysicsProcess(double delta)
    {
        _weapon.Tick(delta);
        bool attackBlocked = _inventoryHud.BlocksWorldAttack();
        if (!_health.IsAlive) { Velocity = Vector2.Zero; return; }

        Vector2 direction = _inventoryHud.BlocksWorldMovement
            ? Vector2.Zero : ReadMovement();
        Velocity = direction.Normalized() *
            _stats.Get(PlayerStat.MovementSpeed) * _inventory.MovementFactor;
        MoveAndSlide();
        _visual.UpdateHeight();

        bool firing = !attackBlocked && _weapon.Attack != null &&
            Input.IsMouseButtonPressed(MouseButton.Left);
        float horizontal = firing
            ? GetGlobalMousePosition().X - GlobalPosition.X : direction.X;

        if (Mathf.Abs(horizontal) > 0.001f)
        {
            float nextFacing = horizontal > 0f ? 1f : -1f;
            if (nextFacing != _facing)
            {
                _facing = nextFacing;
                _visual.Scale = new Vector2(_facing, 1f);
            }
        }

        if (firing) _weapon.TryFireAtCursor();
    }

	// =========================================================
	// Read four-direction arrow input independently from attributes and carrying rules.
	private static Vector2 ReadMovement()
	{
		return new Vector2(
			(Input.IsPhysicalKeyPressed(Key.Right) ? 1f : 0f) -
			(Input.IsPhysicalKeyPressed(Key.Left) ? 1f : 0f),
			(Input.IsPhysicalKeyPressed(Key.Down) ? 1f : 0f) -
			(Input.IsPhysicalKeyPressed(Key.Up) ? 1f : 0f));
	}
	#endregion
}
