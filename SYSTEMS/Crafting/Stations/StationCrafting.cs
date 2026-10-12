// Object-owned station adapter; timed work remains in PlayerCrafting, not a second engine.
using Godot;
using System;

public partial class StationCrafting : Node
{
    [Export] public StationDefinition Definition { get; set; }
    [Export] public bool Enabled { get; set; } = true;
    public PlacedObject Host { get; private set; }
    private bool _ready;

    // =========================================================
    // Attach the shared cursor target once the placed host has its terrain-adjusted artwork.
    public override void _Ready()
    {
        SetProcess(false); SetPhysicsProcess(false);
        Host = GetParent().GetParent<PlacedObject>();
        if (Host.IsNodeReady()) AttachTarget();
        else Host.Ready += AttachTarget;
    }

    // =========================================================
    // Validate once and register without adding per-station polling or input handlers.
    private void AttachTarget()
    {
        if (!GodotObject.IsInstanceValid(Host) || Host.IsQueuedForDeletion()) return;
        Host.Ready -= AttachTarget;
        try
        {
            if (Definition == null) throw new InvalidOperationException("Station definition is missing.");
            Definition.Validate();
            InteractionTarget.Attach(Host.GetNode<Node2D>("Visual"), Definition.CursorBounds,
                new StationInteractionProvider(this));
            _ready = true;
        }
        catch (Exception error) { GD.PushError("Station setup failed: " + error.Message); }
    }

    // =========================================================
    // Release the one-time readiness subscription if this structure exits early.
    public override void _ExitTree()
    {
        _ready = false;
        if (GodotObject.IsInstanceValid(Host)) Host.Ready -= AttachTarget;
    }

    // =========================================================
    // Recheck exact host life, player life, range and layer for every open/queue/exchange.
    public bool CanUse(Player player) => _ready && Enabled && IsInsideTree() && !IsQueuedForDeletion() &&
        GodotObject.IsInstanceValid(Host) && Host.IsInsideTree() && !Host.IsQueuedForDeletion() &&
        GodotObject.IsInstanceValid(Host.ObjectHealth) && Host.ObjectHealth.IsAlive &&
        GodotObject.IsInstanceValid(player) && player.IsInsideTree() && !player.IsQueuedForDeletion() &&
        player.GetNode<Health>("Systems/Health").IsAlive && WorldLayerMember.Same(player, Host) &&
        player.GlobalPosition.DistanceSquaredTo(Host.GlobalPosition) <= Definition.InteractionRange * Definition.InteractionRange;

    // =========================================================
    // Match capability requirements without tying recipes to this station's class or scene name.
    public bool CanCraft(Player player, CraftingRecipe recipe) => CanUse(player) && Definition.Provides(recipe);
}
