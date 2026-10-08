// Creates one removable test herd on valid terrain near the player's start.
// This prototype is deliberately separate from procedural biome populations.
using Godot;

public partial class TallowbackTest : Node
{
    #region State
    private readonly PackedScene _scene = GD.Load<PackedScene>(
        "res://ENTITIES/Core/Entity.tscn");
    private readonly EntityDefinition _definition = GD.Load<EntityDefinition>(
        "res://ENTITIES/Species/Tallowback/Tallowback.tres");

    private Node2D _objects;
    private Player _player;
    private ChunkController _chunks;
    private EntityHerd _herd;
    private readonly RandomNumberGenerator _rng = new();
    private int _created;
    private double _timer;
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve services while allowing terrain generation to finish normally.
    public override void _Ready()
    {
        Node world = GetParent().GetParent();
        _objects = world.GetNode<Node2D>("WorldObjects");
        _player = _objects.GetNode<Player>("Player");
        _chunks = GetParent().GetNode<ChunkController>("ChunkController");
        _rng.Randomize();
    }

    // =========================================================
    // Release the test helper's random generator.
    public override void _ExitTree()
    {
        _rng.Dispose();
    }

    // =========================================================
    // Spread a small number of placement checks over time.
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
            GD.PushError("Add Tallow.tres to ItemCatalog before running this test.");
            SetPhysicsProcess(false);
            return;
        }

        GrazingWorld.GetOrCreate(this);

        if (_herd == null)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                Vector2 centre = _player.GlobalPosition +
                    Vector2.FromAngle(_rng.RandfRange(0f, Mathf.Tau)) * 260f;

                if (!navigation.CanTravelDirectly(
                    _player.GlobalPosition, centre))
                    continue;

                _herd = new EntityHerd
                {
                    Name = "TallowbackTestHerd",
                    HerdId = "test_tallowback_herd",
                    Position = _objects.ToLocal(centre)
                };
                _objects.AddChild(_herd);
                break;
            }
            return;
        }

        for (int attempt = 0; attempt < 4; attempt++)
        {
            Vector2 point = _herd.GlobalPosition +
                Vector2.FromAngle(_rng.RandfRange(0f, Mathf.Tau)) *
                _rng.RandfRange(35f, 85f);

            if (_player.GlobalPosition.DistanceSquaredTo(point) < 130f * 130f ||
                !navigation.CanTravelDirectly(_herd.GlobalPosition, point))
                continue;

            bool crowded = false;
            foreach (Node node in GetTree().GetNodesInGroup("entities"))
                if (node is Entity entity &&
                    entity.GlobalPosition.DistanceSquaredTo(point) < 32f * 32f)
                    crowded = true;
            if (crowded) continue;

            Entity creature = _scene.Instantiate<Entity>();
            creature.Name = $"Tallowback_{_created}";
            creature.Definition = _definition;
            creature.HerdId = _herd.HerdId;
            creature.Position = _objects.ToLocal(point);
            _objects.AddChild(creature);
            _created++;

            if (_created >= 3) SetPhysicsProcess(false);
            return;
        }
    }
    #endregion
}