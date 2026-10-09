// Centralizes movement, sprint, jump, item use and the pause-menu binding.
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
    public const string Pause = "player_pause";
    #endregion

    #region State
    public Vector2 Movement { get; private set; }
    public bool SprintHeld { get; private set; }
    public bool JumpPressed { get; private set; }
    public bool UseHeld { get; private set; }
    public bool UsePressed { get; private set; }

    private readonly Player _player;
    private readonly InventoryHud _hud;
    private readonly Health _health;
    private ulong _frame = ulong.MaxValue;
    private bool _jumpHeld, _useHeld;
    #endregion

    #region Setup
    // =========================================================
    // Preserve configured bindings and attach the isolated pause interface.
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
        AddKeys(Pause, Key.Escape);

        PauseMenu.Attach(player, this);
    }

    // =========================================================
    // Supply default physical keys only when the action is missing.
    private static void AddKeys(string action, params Key[] keys)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);

        foreach (Key key in keys)
            InputMap.ActionAddEvent(action,
                new InputEventKey { PhysicalKeycode = key });
    }

    // =========================================================
    // Supply a mouse binding only when the action is missing.
    private static void AddMouse(string action, MouseButton button)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        InputMap.ActionAddEvent(action,
            new InputEventMouseButton { ButtonIndex = button });
    }
    #endregion

    #region Interface Input
    // =========================================================
    // Keep the binding here; the pause UI listens while gameplay is paused.
    public static bool IsPauseRequest(InputEvent input) =>
        input.IsActionPressed(Pause) && !input.IsEcho();

    // =========================================================
    // Clear held output and stop mining before freezing gameplay.
    public void Suspend()
    {
        Movement = Vector2.Zero;
        SprintHeld = JumpPressed = UseHeld = UsePressed = false;
        _player.Velocity = Vector2.Zero;
        _hud.BlocksWorldAttack();
    }

    // =========================================================
    // Prevent a fresh click or jump from buttons held during the menu.
    public void Resume()
    {
        _frame = ulong.MaxValue;
        _jumpHeld = Input.IsActionPressed(Jump);
        _useHeld = Input.IsActionPressed(Use);
    }
    #endregion

    #region Reading
    // =========================================================
    // Capture shared held states and one-shot presses for this physics tick.
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

        bool useAllowed = gameplayAllowed &&
            !_hud.BlocksWorldAttack();

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