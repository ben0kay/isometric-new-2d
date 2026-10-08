// Centralizes player movement, sprint, jump, and held item-use input.
// Reads once per physics tick and applies gameplay, HUD, and death restrictions.
using Godot;

public sealed class PlayerInput
{
    #region Actions
    public const string MoveLeft = "player_move_left";
    public const string MoveRight = "player_move_right";
    public const string MoveUp = "player_move_up";
    public const string MoveDown = "player_move_down";
    public const string Sprint = "player_sprint";
    public const string Jump = "player_jump";
    public const string Use = "player_use";
    #endregion

    #region State
    public Vector2 Movement { get; private set; }
    public bool SprintHeld { get; private set; }
    public bool JumpPressed { get; private set; }
    public bool UseHeld { get; private set; }

    private readonly Player _player;
    private readonly InventoryHud _hud;
    private readonly Health _health;
    private ulong _frame = ulong.MaxValue;
    private bool _jumpHeld;
    public bool UsePressed { get; private set; }
private bool _useHeld;
    #endregion

    #region Setup
    // =========================================================
    // Cache input restrictions and register missing default bindings.
    public PlayerInput(Player player, InventoryHud hud, Health health)
    {
        _player = player;
        _hud = hud;
        _health = health;

        AddKeys(MoveLeft, Key.Left, Key.A);
        AddKeys(MoveRight, Key.Right, Key.D);
        AddKeys(MoveUp, Key.Up, Key.W);
        AddKeys(MoveDown, Key.Down, Key.S);
        AddKeys(Sprint, Key.Shift);
        AddMouse(Jump, MouseButton.Right);
        AddMouse(Use, MouseButton.Left);
    }

    // =========================================================
    // Preserve configured actions; supply physical keys only when missing.
    private static void AddKeys(string action, params Key[] keys)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);

        foreach (Key key in keys)
            InputMap.ActionAddEvent(action,
                new InputEventKey { PhysicalKeycode = key });
    }

    // =========================================================
    // Supply a mouse binding only when its action is missing.
    private static void AddMouse(string action, MouseButton button)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        InputMap.ActionAddEvent(action,
            new InputEventMouseButton { ButtonIndex = button });
    }
    #endregion

    #region Reading
// =========================================================
// Capture shared held states and one-shot presses for the current physics tick.
public void Read()
{
    ulong frame = Engine.GetPhysicsFrames();
    if (_frame == frame) return;
    _frame = frame;

    bool jumpHeld = Input.IsActionPressed(Jump);
    bool jumpPressed = jumpHeld && !_jumpHeld;
    _jumpHeld = jumpHeld;

    bool useHeld = Input.IsActionPressed(Use);
    bool usePressed = useHeld && !_useHeld;
    _useHeld = useHeld;

    bool gameplayAllowed = _health.IsAlive &&
        InputModes.For(_player).GameplayAllowed;
    bool movementAllowed = gameplayAllowed &&
        !_hud.BlocksWorldMovement;
    bool useAllowed = gameplayAllowed && !_hud.BlocksWorldAttack();

    Movement = movementAllowed
        ? Input.GetVector(MoveLeft, MoveRight, MoveUp, MoveDown)
        : Vector2.Zero;

    SprintHeld = movementAllowed && Input.IsActionPressed(Sprint);
    JumpPressed = movementAllowed && useAllowed && jumpPressed;
    UseHeld = useAllowed && useHeld;
    UsePressed = useAllowed && usePressed;
}

    #endregion
}