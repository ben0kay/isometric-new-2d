// Creates one test group using the species' herd size and roaming settings.
// Placement checks are budgeted; biome population streaming is a later pass.
using Godot;

public partial class TallowbackTest : Node
{
    #region State
    private readonly PackedScene _scene = GD.Load<PackedScene>(
        "res://ENTITIES/Core/Entity.tscn");
    private readonly EntityDefinition _definition = GD.Load<EntityDefinition>(
        "res://ENTITIES/Species/Wildlife/Tallowback/Tallowback.tres");

    private Node2D _objects;
    private Player _player;
    private ChunkController _chunks;
 private EntityGroup _group;
    private readonly RandomNumberGenerator _rng = new();
    private int _created, _targetCount;
    private double _timer;
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve world services and choose the group size once.
    public override void _Ready()
    {
        Node world = GetParent().GetParent();
        _objects = world.GetNode<Node2D>("WorldObjects");
        _player = _objects.GetNode<Player>("Player");
        _chunks = GetParent().GetNode<ChunkController>("ChunkController");

        if (_scene == null || _definition == null)
        {
            GD.PushError("TallowbackTest requires its scene and species resource.");
            SetPhysicsProcess(false);
            return;
        }

        _definition.Validate();
        _rng.Randomize();
        _targetCount = _rng.RandiRange(
            _definition.HerdSizeMin, _definition.HerdSizeMax);
    }

    // =========================================================
    // Release the test spawner's random generator.
    public override void _ExitTree()
    {
        _rng.Dispose();
    }

// =========================================================
// Create a test group with optional shared roaming and budgeted placement checks.
public override void _PhysicsProcess(double delta)
{
    if (!_chunks.WorldReady) return;

    _timer -= delta;
    if (_timer > 0.0) return;
    _timer = 0.25;

    WorldNavigation navigation = WorldNavigation.For(_player);
    if (navigation == null ||
        WorldLayerMember.For(_player) != WorldLayer.Surface)
        return;

    ResourceWorld resources = ResourceWorld.Find(this);
    if (resources?.Catalog.Get("tallow") == null)
    {
        GD.PushError("TallowbackTest requires 'tallow' in the item catalog.");
        SetPhysicsProcess(false);
        return;
    }

    GrazingWorld.GetOrCreate(this);

    if (!GodotObject.IsInstanceValid(_group))
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            Vector2 centre = _player.GlobalPosition +
                Vector2.FromAngle(_rng.RandfRange(0f, Mathf.Tau)) * 260f;

            if (!navigation.CanTravelDirectly(
                _player.GlobalPosition, centre))
                continue;

            _group = new EntityGroup
            {
                Name = "TallowbackTestGroup",
                GroupId = "test_tallowback_group",
                DisplayName = "Tallowback herd",
                MinimumMembers = 2,
                ShareThreats = true,
                Position = _objects.ToLocal(centre)
            };

            Node systems = new() { Name = "Systems" };
            _group.AddChild(systems);
            systems.AddChild(new GroupRoaming
            {
                Name = "Roaming",
                Mode = GroupRoamingMode.PeriodicSteps,
                WanderRadius = _definition.HerdWanderRadius,
                RoamRadius = _definition.HerdRoamRadius,
                StepDistance = _definition.HerdCentreStepDistance,
                IntervalSeconds = _definition.HerdCentreIntervalSeconds
            });

            _objects.AddChild(_group);
            break;
        }
        return;
    }

    float spread = Mathf.Min(85f, _group.Roaming.WanderRadius);
    for (int attempt = 0; attempt < 4; attempt++)
    {
        Vector2 point = _group.GlobalPosition +
            Vector2.FromAngle(_rng.RandfRange(0f, Mathf.Tau)) *
            Mathf.Sqrt(_rng.Randf()) * spread;

        if (_player.GlobalPosition.DistanceSquaredTo(point) < 130f * 130f ||
            !navigation.CanTravelDirectly(_group.GlobalPosition, point))
            continue;

        bool crowded = false;
        foreach (Node node in GetTree().GetNodesInGroup("entities"))
        {
            if (node is not Entity entity || entity.IsQueuedForDeletion() ||
                !WorldLayerMember.Same(_player, entity))
                continue;

            if (entity.GlobalPosition.DistanceSquaredTo(point) < 32f * 32f)
            {
                crowded = true;
                break;
            }
        }
        if (crowded) continue;

        Entity creature = _scene.Instantiate<Entity>();
        creature.Name = $"Tallowback_{_created}";
        creature.Definition = _definition;
        creature.GetNode<EntityGroupMember>("Systems/Group").GroupId =
            _group.GroupId;
        creature.Position = _objects.ToLocal(point);
        _objects.AddChild(creature);
        _created++;

        if (_created >= _targetCount)
        {
            _group.CompleteFormation();
            SetPhysicsProcess(false);
        }
        return;
    }
}
    #endregion
}