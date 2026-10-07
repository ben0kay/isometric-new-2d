// Controls player movement, terrain-adjusted artwork, and equipped-tool use.
// Calculated movement speed and cached inventory penalties remain separate.
using Godot;

public partial class Player : CharacterBody2D
{
#region Configuration
[ExportGroup("Artwork")]
[Export] public VisualDefinition VisualOverride { get; set; }

[ExportGroup("Cover")]
[Export(PropertyHint.Range, "1,256,1")]
public float BodyHeight { get; set; } = CombatCover.DefaultHeight;

[ExportGroup("Jump")]
[Export(PropertyHint.Range, "8,128,1")]
public float JumpPeakHeight { get; set; } = 48f;

[Export(PropertyHint.Range, "0.15,1.5,0.05")]
public float JumpDuration { get; set; } = 0.55f;
#endregion

	#region State
	private TerrainVisual _visual;
	private Weapon _weapon;
	private Health _health;
	private PlayerStats _stats;
	private PlayerInventory _inventory;
	private InventoryHud _inventoryHud;
	private PlayerSurfaceEffects _surfaceEffects;
	private float _facing = 1f;
	private PlayerJump _jump;
public float JumpHeight => _jump?.Height ?? 0f;
public bool IsAirborne => _jump?.IsAirborne ?? false;
	
	#endregion

	#region Lifecycle
// =========================================================
// Resolve player systems and attach artwork with its separate ground shadow.
public override async void _Ready()
{
	MotionMode = MotionModeEnum.Floating;
	_weapon = GetNode<Weapon>("Systems/Weapon");
	_health = GetNode<Health>("Systems/Health");
	_stats = GetNode<PlayerStats>("Systems/Stats");
	_inventory = GetNode<PlayerInventory>("Systems/Inventory");
	_inventoryHud = GetNode<InventoryHud>("InventoryHud");
	_surfaceEffects = GetNode<PlayerSurfaceEffects>("Systems/SurfaceEffects");
	SetPhysicsProcess(false);

	try
	{
		await PlaceholderAtlas.EnsureReady(this);
		if (!IsInsideTree() || IsQueuedForDeletion()) return;

		// Add the shadow before the visual so artwork draws above it.
		_jump = new PlayerJump { Name = "Jump" };
		AddChild(_jump);

		_visual = TerrainVisual.Attach(
			this, PlaceholderAtlas.PlayerRegion,
			new Vector2(-32, -72), Vector2.One, true, VisualOverride);

		_jump.Bind(_visual);
		_jump.UpdatePose(_visual);
		SetPhysicsProcess(true);
	}
	catch (System.Exception error)
	{
		GD.PushError($"Player initialization failed: {error}");
	}
}

// =========================================================
// Update movement, RMB jumping, terrain artwork and liquid exposure.
public override void _PhysicsProcess(double delta)
{
	_weapon.Tick(delta);
	bool attackBlocked = _inventoryHud.BlocksWorldAttack();
	bool movementAllowed = !_inventoryHud.BlocksWorldMovement &&
		InputModes.For(this).GameplayAllowed;

	_jump.Tick(delta,
		movementAllowed && !attackBlocked, _health.IsAlive,
		JumpPeakHeight, JumpDuration);

	if (!_health.IsAlive)
	{
		Velocity = Vector2.Zero;
		_visual.UpdateHeight();
		_jump.UpdatePose(_visual);
		return;
	}

	_surfaceEffects.UpdateState(_visual);

	Vector2 direction = movementAllowed
		? ReadMovement() : Vector2.Zero;

	Velocity = direction.Normalized() *
		_stats.Get(PlayerStat.MovementSpeed) *
		_inventory.MovementFactor *
		_surfaceEffects.MovementMultiplier;

	WorldLayerController layers = WorldLayerController.Find(this);
	if (layers != null)
		Velocity = layers.ConstrainVelocity(GlobalPosition, Velocity, delta);

	MoveAndSlide();
	_visual.UpdateHeight();
	_jump.UpdatePose(_visual);
	_surfaceEffects.UpdateState(_visual);
	_surfaceEffects.TickExposure(delta);

	if (!_health.IsAlive)
	{
		Velocity = Vector2.Zero;
		_jump.Reset();
		return;
	}

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
// Read movement only when gameplay owns player input.
private Vector2 ReadMovement()
{
	if (!InputModes.For(this).GameplayAllowed)
		return Vector2.Zero;

	return new Vector2(
		(Input.IsPhysicalKeyPressed(Key.Right) ? 1f : 0f) -
		(Input.IsPhysicalKeyPressed(Key.Left) ? 1f : 0f),
		(Input.IsPhysicalKeyPressed(Key.Down) ? 1f : 0f) -
		(Input.IsPhysicalKeyPressed(Key.Up) ? 1f : 0f));
}
	#endregion
}
