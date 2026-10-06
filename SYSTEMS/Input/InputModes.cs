// Owns a stack of player input modes without pausing world simulation.
// Interfaces claim control and release only their own entry.
using Godot;
using System;
using System.Collections.Generic;

public enum PlayerInputMode
{
    Gameplay,
    Inventory,
    Container,
    DebugMap
}

public partial class InputModes : Node
{
    #region State
    private readonly List<Entry> _stack = new();
    private bool _waitForMouseRelease;

    private readonly struct Entry
    {
        public readonly Node Owner;
        public readonly PlayerInputMode Mode;

        // =========================================================
        // Record the interface responsible for one input claim.
        public Entry(Node owner, PlayerInputMode mode)
        {
            Owner = owner;
            Mode = mode;
        }
    }

    public PlayerInputMode CurrentMode
    {
        get
        {
            Prune();
            return _stack.Count == 0
                ? PlayerInputMode.Gameplay
                : _stack[_stack.Count - 1].Mode;
        }
    }

    public bool GameplayAllowed =>
        CurrentMode == PlayerInputMode.Gameplay;

    public bool WorldAttackAllowed
    {
        get
        {
            if (_waitForMouseRelease)
            {
                if (!Input.IsMouseButtonPressed(MouseButton.Left))
                    _waitForMouseRelease = false;

                return false;
            }

            return GameplayAllowed;
        }
    }
    #endregion

    #region Lifecycle
    // =========================================================
    // Register before other scene nodes begin their Ready callbacks.
    public override void _EnterTree()
    {
        AddToGroup("input_modes");
    }

    // =========================================================
    // Input claims require no per-frame processing.
    public override void _Ready()
    {
        SetProcess(false);
        SetPhysicsProcess(false);
    }
    #endregion

    #region Resolution
    // =========================================================
    // Find the controller belonging to this viewport.
    public static InputModes For(Node context)
    {
        foreach (Node node in context.GetTree().GetNodesInGroup("input_modes"))
        {
            if (node is InputModes modes &&
                modes.GetViewport() == context.GetViewport())
                return modes;
        }

        throw new InvalidOperationException(
            "Add InputModes.tscn to this world scene.");
    }
    #endregion

    #region Ownership
    // =========================================================
    // Give an interface control without duplicating its claim.
    public void Push(Node owner, PlayerInputMode mode)
    {
        if (!GodotObject.IsInstanceValid(owner))
            throw new ArgumentException("Input mode requires a valid owner.");

        if (mode == PlayerInputMode.Gameplay)
            throw new ArgumentException(
                "Gameplay is the fallback mode; release your claim instead.");

        Prune();

        if (_stack.Count > 0 &&
            _stack[_stack.Count - 1].Owner == owner &&
            _stack[_stack.Count - 1].Mode == mode)
            return;

        RemoveOwner(owner);
        _stack.Add(new Entry(owner, mode));
        _waitForMouseRelease = true;
    }

    // =========================================================
    // Release this interface even when another interface is above it.
    public void Release(Node owner)
    {
        if (RemoveOwner(owner))
            _waitForMouseRelease = true;
    }

    // =========================================================
    // Allow only the interface at the top of the stack to read its controls.
    public bool OwnsInput(Node owner)
    {
        Prune();
        return _stack.Count > 0 &&
            _stack[_stack.Count - 1].Owner == owner;
    }

    // =========================================================
    // Remove all entries belonging to one interface.
    private bool RemoveOwner(Node owner)
    {
        bool removed = false;

        for (int i = _stack.Count - 1; i >= 0; i--)
        {
            if (_stack[i].Owner != owner) continue;
            _stack.RemoveAt(i);
            removed = true;
        }

        return removed;
    }

    // =========================================================
    // Recover automatically if an owning interface is removed unexpectedly.
    private void Prune()
    {
        for (int i = _stack.Count - 1; i >= 0; i--)
        {
            Node owner = _stack[i].Owner;

            if (GodotObject.IsInstanceValid(owner) &&
                owner.IsInsideTree() && !owner.IsQueuedForDeletion())
                continue;

            _stack.RemoveAt(i);
            _waitForMouseRelease = true;
        }
    }
    #endregion
}