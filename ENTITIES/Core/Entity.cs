// Coordinates wildlife lifecycle, shared component ticks and artwork.
// Behaviour and state belong to the components under Systems.
using Godot;
using System;

public partial class Entity : CharacterBody2D
{
    #region Configuration
    [Export] public EntityDefinition Definition { get; set; }
    #endregion

    #region Components
    public Health Health { get; private set; }
    public EnemyMotor Motor { get; private set; }
    public EntityGroupMember Membership { get; private set; }
    public EntityWandering Wandering { get; private set; }
    public EntityGrazing Grazing { get; private set; }
    public EntityThreatResponseComponent Threats { get; private set; }

    public bool HasThreat => Threats?.HasThreat == true;
    #endregion

    #region State
    private TerrainVisual _visual;
    private bool _ready;
    #endregion

    #region Lifecycle
    // =========================================================
    // Apply species health before child components initialize.
    public override void _EnterTree()
    {
        if (Definition == null)
            throw new InvalidOperationException("Entity requires a Definition.");

        Definition.Validate();
        MotionMode = MotionModeEnum.Floating;
        GetNode<Health>("Systems/Health").MaxHealth = Definition.MaxHealth;
        AddToGroup("entities");
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Bind behaviour components and prepare replaceable artwork.
    public override async void _Ready()
    {
        try
        {
            Health = GetNode<Health>("Systems/Health");
            Motor = GetNode<EnemyMotor>("Systems/Motor");
            Membership = GetNode<EntityGroupMember>("Systems/Group");
            Grazing = GetNode<EntityGrazing>("Systems/Grazing");
            Wandering = GetNode<EntityWandering>("Systems/Wandering");
            Threats = GetNode<EntityThreatResponseComponent>("Systems/Threats");

            WorldLayerMember.Attach(this, WorldLayer.Surface);
            Grazing.Bind(this);
            Wandering.Bind(this);
            Threats.Bind(this);
            Membership.JoinAssignedGroup();

            GetNode("Systems").AddChild(
                new EntityDeathLoot { Name = "DeathLoot" });
            Health.Died += OnDeath;

            ImageTexture texture = await Definition.GetArtwork(this);
            if (!IsInsideTree() || IsQueuedForDeletion()) return;

            _visual = TerrainVisual.Attach(
                this, new Rect2(Vector2.Zero, Definition.ArtworkSize),
                Definition.ArtworkOrigin, Vector2.One, true,
                Definition.VisualOverride, texture);

            _ready = true;
            SetPhysicsProcess(true);
        }
        catch (Exception error)
        {
            GD.PushError($"Entity '{Name}' initialization failed: {error}");
            QueueFree();
        }
    }

    // =========================================================
    // Disconnect the actor's lifecycle subscription.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Health))
            Health.Died -= OnDeath;
    }

    // =========================================================
    // Share one tick and staggered decision schedule across behaviour components.
    public override void _PhysicsProcess(double delta)
    {
        if (!_ready || !Health.IsAlive) return;

        Threats.Tick(delta);
        Wandering.Tick(delta);

        if (StaggeredUpdate.DueSeconds(this, 0.35, 23) &&
            !Threats.Decide())
            Wandering.Decide();

        if (Velocity.LengthSquared() > 0.1f)
            _visual.Scale = new Vector2(Velocity.X < 0f ? -1f : 1f, 1f);

        _visual.UpdateHeight();
    }

    // =========================================================
    // Stop the actor after membership and death loot receive the death event.
    private void OnDeath()
    {
        Motor.Stop();
        Grazing.Release();
        QueueFree();
    }
    #endregion
}