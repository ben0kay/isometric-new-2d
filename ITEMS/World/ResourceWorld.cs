// Owns the world's item catalog and spawns independent dropped stacks.
// Drops remain separate from mined objects so deleting a resource cannot delete its yield.
using Godot;
using System;

public partial class ResourceWorld : Node
{
    #region Configuration
    [Export] public ItemCatalog Catalog { get; set; }
    [Export] public float PickupRadius { get; set; } = 48f;
    [Export] public double PickupDelay { get; set; } = 0.3;
    #endregion

    #region State
    private Node2D _objects;
    #endregion

    #region Lifecycle
    // =========================================================
    // Initialize before world objects begin their own Ready callbacks.
    public override void _EnterTree()
    {
        if (Catalog == null)
            throw new InvalidOperationException("ResourceWorld requires an ItemCatalog.");

        Catalog.Initialize();
        _objects = GetNode<Node2D>("../../WorldObjects");
        AddToGroup("resource_world");
        SetProcess(false);
        SetPhysicsProcess(false);
    }
    #endregion

    #region Drops
    // =========================================================
    // Resolve the service belonging to the current scene tree.
    public static ResourceWorld Find(Node node)
    {
        return node.GetTree().GetFirstNodeInGroup("resource_world") as ResourceWorld;
    }

    // =========================================================
    // Resolve an item and defer scene attachment outside physics queries.
    public bool Spawn(string itemId, int count, Vector2 globalPosition)
    {
        ItemDefinition item = Catalog.Get(itemId);
        if (item == null || count <= 0 ||
            !GodotObject.IsInstanceValid(_objects))
        {
            GD.PushError($"[Resources] Cannot drop '{itemId}' ×{count}.");
            return false;
        }

        WorldPickup pickup = new()
        {
            Name = "ItemDrop",
            Item = item,
            Count = count,
            PickupRadius = Mathf.Max(8f, PickupRadius),
            PickupDelay = Math.Max(0, PickupDelay),
            Position = _objects.ToLocal(globalPosition)
        };
        _objects.CallDeferred(Node.MethodName.AddChild, pickup);
        return true;
    }
    #endregion
}