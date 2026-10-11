// Shares cursor selection, wheel dispatch and nonmodal LMB arbitration for one player.
using Godot;
public partial class PlayerInteractionController : Node
{
    private Player _player;
    private StorageHud _storage;
    private InteractionTarget _target;
    private readonly InteractionResolver _resolver = new();
    private InteractionRadialHud _wheel;
    private double _nextQuery;
    // =========================================================
    // Attach under the player without editing any saved scene resource.
    public static void Attach(Player player, StorageHud storage)
    {
        if (player.GetNodeOrNull<PlayerInteractionController>("Interaction") != null) return;
        // StorageHud.Ready runs while Player is still attaching its original children.
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(player) || !player.IsInsideTree() ||
                player.IsQueuedForDeletion() || !GodotObject.IsInstanceValid(storage) ||
                storage.IsQueuedForDeletion() ||
                player.GetNodeOrNull<PlayerInteractionController>("Interaction") != null) return;
            player.AddChild(new PlayerInteractionController
            { Name = "Interaction", _player = player, _storage = storage });
        }).CallDeferred();
    }
    // =========================================================
    // Build one shared wheel above world artwork and below modal menus.
    public override void _Ready()
    {
        CanvasLayer layer = new() { Layer = 20 }; AddChild(layer);
        _wheel = new InteractionRadialHud(); layer.AddChild(_wheel);
        _wheel.Activate += Execute;
        SetPhysicsProcess(false);
    }
    // =========================================================
    // Follow the anchor smoothly; selection work remains throttled.
    public override void _Process(double delta) { UpdateTarget(false); }
    // =========================================================
    // Refresh before held-use polling, closing the same-frame firing loophole.
    public bool PrepareWorldUse()
    {
        UpdateTarget(false);
        if (!_wheel.Visible) return false;
        InputModes.For(this).SuppressWorldUseUntilRelease();
        return true;
    }
    // =========================================================
    // Refresh on a fresh click before GUI dispatch, including cursor motion this frame.
    public override void _Input(InputEvent input)
    {
        if (input is InputEventMouseButton mouse && mouse.ButtonIndex == MouseButton.Left && mouse.Pressed)
        {
            UpdateTarget(true);
            if (_wheel.Visible) InputModes.For(this).SuppressWorldUseUntilRelease();
        }
    }
    // =========================================================
    // Respect modal/UI ownership and retain the target while entering its own wheel.
    private void UpdateTarget(bool force)
    {
        if (_wheel == null) return;
        InputModes modes = InputModes.For(this);
        Control hovered = GetViewport().GuiGetHoveredControl();
        bool ownUi = hovered == _wheel || (hovered != null && _wheel.IsAncestorOf(hovered));
        if (!modes.GameplayAllowed || _storage.IsOpen ||
            !_player.GetNode<Health>("Systems/Health").IsAlive ||
            (hovered != null && !ownUi && hovered.MouseFilter != Control.MouseFilterEnum.Ignore))
        { Clear(); return; }
        if (!InteractionResolver.Valid(_target, _player)) Clear();
        double now = Time.GetTicksMsec() / 1000.0;
        if (!(_target != null && _wheel.ContainsCursor) && (force || now >= _nextQuery))
        {
            _nextQuery = now + 0.075;
            _target = _resolver.Resolve(_player, _player.GetGlobalMousePosition());
        }
        if (_target == null) { Clear(); return; }
        _wheel.Present(_target.GetGlobalTransformWithCanvas().Origin,
            _target.Provider.PrimaryAction.Label);
        modes.SuppressWorldUseUntilRelease();
    }
    // =========================================================
    // Revalidate and dispatch only the selected provider's primary action.
    private void Execute()
    {
        InputModes modes = InputModes.For(this);
        modes.SuppressWorldUseUntilRelease();
        if (modes.GameplayAllowed && InteractionResolver.Valid(_target, _player))
            _target.Provider.Execute(_player, _target.Provider.PrimaryAction.Id);
        Clear();
    }
    // =========================================================
    // Remove stale UI; shared InputModes retains suppression until mouse release.
    private void Clear() { _target = null; _wheel?.Hide(); }
}
