// Owns the world's item catalog and spawns independent dropped stacks.
// Drops remain separate from mined objects so deleting a resource cannot delete its yield.
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class ResourceWorld : Node
{
	#region Configuration
	[Export] public ItemCatalog Catalog { get; set; }
	[Export] public float PickupRadius { get; set; } = 48f;
	[Export] public double PickupDelay { get; set; } = 0.3;
	#endregion

	#region State
	private Node2D _objects;
    private readonly List<WorldPickup> _pending = new();
    private readonly Dictionary<HarvestProfile, HarvestDropPlan> _harvestPlans = new();
	#endregion

	#region Lifecycle
	// =========================================================
	// Initialize before world objects begin their own Ready callbacks.
	public override void _EnterTree()
	{
		if (Catalog == null)
			throw new InvalidOperationException("ResourceWorld requires an ItemCatalog.");

		Catalog.Initialize();
        _harvestPlans.Clear();
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
	public bool Spawn(string itemId, int count, Vector2 globalPosition,
        Node owner = null)
	{
		ItemDefinition item = Catalog.Get(itemId);
        Node2D objects = RootFor(owner);
		if (item == null || count <= 0 ||
			!GodotObject.IsInstanceValid(objects))
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
			Position = objects.ToLocal(globalPosition)
		};
		QueuePickup(objects, pickup);
		return true;
	}

// =========================================================
// Drop inventory items into the player's currently active world layer.
public bool SpawnItem(ItemDefinition item, int count, Vector2 globalPosition)
{
	Node2D objects = WorldLayerController.DropRoot(this, _objects);
	if (item == null || count <= 0 ||
		!GodotObject.IsInstanceValid(objects)) return false;

	WorldPickup pickup = new()
	{
		Name = "ItemDrop",
		Item = item,
		Count = count,
		PickupRadius = Mathf.Max(8f, PickupRadius),
		PickupDelay = Math.Max(0, PickupDelay),
		Position = objects.ToLocal(globalPosition)
	};
	QueuePickup(objects, pickup);
	return true;
}

    // =========================================================
    // Compile each shared profile once per world; cache no live resource hosts.
    public HarvestDropPlan PrepareHarvest(HarvestProfile profile)
    {
        if (profile == null)
            throw new InvalidOperationException("Missing HarvestDrops profile. Assign an external HarvestProfile to this species.");
        if (!_harvestPlans.TryGetValue(profile, out HarvestDropPlan plan))
        {
            plan = profile.Compile(Catalog);
            _harvestPlans.Add(profile, plan);
        }
        return plan;
    }

    // =========================================================
    // Accept a whole evaluated batch, including intentional zero-reward harvests.
    public bool SpawnHarvest(IReadOnlyList<HarvestDropPlan.Reward> rewards,
        Vector2 globalPosition, Node owner = null)
    {
        Node2D objects = RootFor(owner);
        if (rewards == null || rewards.Count > 64 ||
            !GodotObject.IsInstanceValid(objects) || !objects.IsInsideTree() ||
            objects.IsQueuedForDeletion() || IsQueuedForDeletion() || !IsInsideTree()) return false;

        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (HarvestDropPlan.Reward reward in rewards)
        {
            if (reward.Item == null || reward.Count < 1 || reward.Count > 100000 ||
                Catalog.Get(reward.Item.Id) != reward.Item || !ids.Add(reward.Item.Id))
            {
                GD.PushError("[Resources] Invalid harvest batch; no rewards accepted.");
                return false;
            }
        }

        List<WorldPickup> pickups = new();
        try
        {
            for (int i = 0; i < rewards.Count; i++)
            {
                Vector2 offset = rewards.Count == 1 ? Vector2.Zero :
                    Vector2.FromAngle(Mathf.Tau * i / rewards.Count) * 10f;
                pickups.Add(new WorldPickup
                {
                    Name = "ItemDrop", Item = rewards[i].Item, Count = rewards[i].Count,
                    PickupRadius = Mathf.Max(8f, PickupRadius),
                    PickupDelay = Math.Max(0, PickupDelay),
                    Position = objects.ToLocal(globalPosition + offset)
                });
            }
            foreach (WorldPickup pickup in pickups) QueuePickup(objects, pickup);
            return true;
        }
        catch (Exception error)
        {
            foreach (WorldPickup pickup in pickups)
            {
                _pending.Remove(pickup);
                if (GodotObject.IsInstanceValid(pickup) && !pickup.IsInsideTree()) pickup.Free();
            }
            GD.PushError($"[Resources] Harvest batch rejected: {error.Message}");
            return false;
        }
    }

// =========================================================
// Validate every reward before spawning any part of a harvested object's yield.
public bool SpawnHarvest(string primaryId, int primaryCount,
	string[] bonusIds, Vector2 globalPosition, Node owner = null)
{
    Node2D objects = RootFor(owner);
	if (primaryCount <= 0 || !GodotObject.IsInstanceValid(objects))
		return false;

	var rewards = new System.Collections.Generic.List<(ItemDefinition Item, int Count)>();
	ItemDefinition primary = Catalog.Get(primaryId);
	if (primary == null)
	{
		GD.PushError($"[Resources] Unknown harvest item: '{primaryId}'.");
		return false;
	}
	rewards.Add((primary, primaryCount));

	foreach (string id in bonusIds ?? Array.Empty<string>())
	{
		if (string.IsNullOrWhiteSpace(id)) continue;
		ItemDefinition item = Catalog.Get(id);
		if (item == null)
		{
			GD.PushError($"[Resources] Unknown bonus harvest item: '{id}'.");
			return false;
		}
		rewards.Add((item, 1));
	}

	var pickups = new System.Collections.Generic.List<WorldPickup>();
	for (int i = 0; i < rewards.Count; i++)
	{
		Vector2 offset = rewards.Count == 1 ? Vector2.Zero :
			Vector2.FromAngle(Mathf.Tau * i / rewards.Count) * 10f;
		pickups.Add(new WorldPickup
		{
			Name = "ItemDrop",
			Item = rewards[i].Item,
			Count = rewards[i].Count,
			PickupRadius = Mathf.Max(8f, PickupRadius),
			PickupDelay = Math.Max(0, PickupDelay),
			Position = objects.ToLocal(globalPosition + offset)
		});
	}

	foreach (WorldPickup pickup in pickups)
		QueuePickup(objects, pickup);
	return true;
}
	#endregion

	// =========================================================
// Deliver an actor's drops into that actor's layer, independent of the player.
public bool SpawnItemFor(
    Node owner, ItemDefinition item, int count, Vector2 globalPosition)
{
    Node2D objects = RootFor(owner);
    if (item == null || count <= 0 || !GodotObject.IsInstanceValid(objects))
        return false;

    WorldPickup pickup = new()
    {
        Name = "ItemDrop",
        Item = item,
        Count = count,
        PickupRadius = Mathf.Max(8f, PickupRadius),
        PickupDelay = Math.Max(0, PickupDelay),
        Position = objects.ToLocal(globalPosition)
    };
    QueuePickup(objects, pickup);
    return true;
}

    #region Persistence
    // =========================================================
    // Include accepted rewards immediately, even before deferred scene attachment.
    private void QueuePickup(Node2D root, WorldPickup pickup)
    {
        pickup.PendingLayer = WorldLayerMember.For(root);
        pickup.PendingGlobalPosition = root.ToGlobal(pickup.Position);
        _pending.Add(pickup);
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(pickup)) return;
            _pending.Remove(pickup);
            if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || IsQueuedForDeletion() ||
                !GodotObject.IsInstanceValid(root) || !root.IsInsideTree() || root.IsQueuedForDeletion())
            {
                pickup.Free();
                return;
            }
            root.AddChild(pickup);
        }).CallDeferred();
    }

    // =========================================================
    // Release unattached rewards if their world closes before its deferred calls run.
    public override void _ExitTree()
    {
        foreach (WorldPickup pickup in _pending)
            if (GodotObject.IsInstanceValid(pickup) && !pickup.IsInsideTree()) pickup.Free();
        _pending.Clear();
        _harvestPlans.Clear();
    }

    // =========================================================
    // Enumerate both attached pickups and accepted pending rewards without duplicates.
    public IEnumerable<WorldPickup> SaveDrops()
    {
        Node world = WorldConfig.Find(this).GetParent();
        foreach (Node node in GetTree().GetNodesInGroup("world_pickups"))
            if (node is WorldPickup pickup && world.IsAncestorOf(pickup)) yield return pickup;
        foreach (WorldPickup pickup in _pending) yield return pickup;
    }

    // =========================================================
    // Recreate an exact saved stack in its original layer without issuing a new reward.
    public void RestoreDrop(DropSaveData data, ItemDefinition item)
    {
        Node2D root = data.Layer == WorldLayerId.Surface ? _objects
            : WorldLayerRuntime.Find(this).ObjectsFor(data.Layer);
        root.AddChild(new WorldPickup
        {
            Name = "ItemDrop", PersistentId = data.Id, Item = item, Count = data.Count,
            Position = root.ToLocal(new Vector2(data.X, data.Y)),
            PickupRadius = data.PickupRadius, PickupDelay = data.PickupDelay,
            RemainingLifetimeSeconds = data.RemainingLifetimeSeconds
        });
    }
    #endregion

    // =========================================================
	// Resolve source ownership independently from the player's current depth.
	private Node2D RootFor(Node owner)
	{
		if (owner == null) return WorldLayerController.DropRoot(this, _objects);
		string layer = WorldLayerMember.For(owner);
		return layer == WorldLayerId.Surface ? _objects
			: (WorldLayerRuntime.Find(this) ??
				throw new InvalidOperationException("Missing layer runtime."))
				.ObjectsFor(layer);
	}
}
