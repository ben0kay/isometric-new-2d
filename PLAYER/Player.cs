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
private PlayerSurvival _survival;
private PlayerConsumption _consumption;
public PlayerInput Controls { get; private set; }
private PlayerPlacement _placement;
	
	#endregion

	#region Lifecycle
// =========================================================
// Resolve player systems, centralized controls, artwork, and ground shadow.
public override async void _Ready()
{
	MotionMode = MotionModeEnum.Floating;
	_weapon = GetNode<Weapon>("Systems/Weapon");
	_health = GetNode<Health>("Systems/Health");
	_stats = GetNode<PlayerStats>("Systems/Stats");
	_inventory = GetNode<PlayerInventory>("Systems/Inventory");
	_inventoryHud = GetNode<InventoryHud>("InventoryHud");
	_surfaceEffects = GetNode<PlayerSurfaceEffects>("Systems/SurfaceEffects");
	Controls = new PlayerInput(this, _inventoryHud, _health);
	SetPhysicsProcess(false);

	try
	{
		await PlaceholderAtlas.EnsureReady(this);
		if (!IsInsideTree() || IsQueuedForDeletion()) return;

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
// Route centralized controls to movement, placement, tools, and survival.
public override void _PhysicsProcess(double delta)
{
	_survival ??= GetNode<PlayerSurvival>("Systems/Survival");
	_consumption ??= GetNode<PlayerConsumption>("Systems/Consumption");
	_placement ??= GetNode<PlayerPlacement>("Systems/Placement");
	_weapon.Tick(delta);
	Controls.Read();

	bool jumped = _jump.Tick(delta, Controls.JumpPressed,
		_health.IsAlive, JumpPeakHeight, JumpDuration);

	if (jumped) _survival.OnJump();

	if (!_health.IsAlive)
	{
		_survival.Tick(delta, false);
		_consumption.Tick(delta, false);
		_placement.Tick(delta);
		Velocity = Vector2.Zero;
		_visual.UpdateHeight();
		_jump.UpdatePose(_visual);
		return;
	}

	_surfaceEffects.UpdateState(_visual);

	Vector2 direction = Controls.Movement;
	Vector2 beforeMovement = GlobalPosition;

	bool sprintRequested = Controls.SprintHeld &&
		direction.LengthSquared() > 0f && !IsAirborne && !jumped;

	float sprintMultiplier = _survival.UpdateSprint(delta, sprintRequested);

	Velocity = direction *
		_stats.Get(PlayerStat.MovementSpeed) *
		_inventory.MovementFactor *
		_surfaceEffects.MovementMultiplier *
		sprintMultiplier;

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
		_survival.Tick(delta, false);
		_consumption.Tick(delta, false);
		_placement.Tick(delta);
		Velocity = Vector2.Zero;
		_jump.Reset();
		return;
	}

	// Capture the selected attack before consuming or placing the final item.
	AttackDefinition attack = _weapon.Attack;
	bool firing = Controls.UseHeld && attack != null;

	_consumption.Tick(delta, Controls.UseHeld);
	_placement.Tick(delta);

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

	if (firing && _weapon.TryFireAtCursor() &&
		attack is ProjectileAttack)
		ReportWork(System.Math.Max(0.03, attack.Cooldown));

	bool moving = direction.LengthSquared() > 0f &&
		!IsAirborne && !jumped &&
		GlobalPosition.DistanceSquaredTo(beforeMovement) > 0.000001f;

	_survival.Tick(delta, moving, moving && _survival.IsSprinting);

	if (!_health.IsAlive)
	{
		Velocity = Vector2.Zero;
		_jump.Reset();
	}
}

	#endregion

	// =========================================================
// Forward successful action duration to the player's survival helper.
public void ReportWork(double seconds)
{
    _survival ??= GetNode<PlayerSurvival>("Systems/Survival");
    _survival.ReportWork(seconds);
}
}
