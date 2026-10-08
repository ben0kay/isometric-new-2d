// Owns wildlife territory detection, threat memory and defend/flee responses.
// Generic group alerts feed the same response as local accepted damage.
using Godot;

public partial class EntityThreatResponseComponent : Node
{
    #region State
    private static readonly string[] ActorGroups =
        { "players", "enemies", "entities" };

    private Entity _actor;
    private Node2D _target;
    private double _memory, _attackCooldown;
    private readonly RandomNumberGenerator _rng = new();

    public bool HasThreat => Living(_target);
    #endregion

    #region Lifecycle
    // =========================================================
    // Bind threat events without adding a separate processing loop.
    public void Bind(Entity actor)
    {
        _actor = actor;
        _rng.Randomize();
        actor.Membership.ThreatReceived += ReactTo;
        actor.Membership.ThreatActive = () => HasThreat;
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Disconnect group alerts and release the native random generator.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_actor?.Membership))
        {
            _actor.Membership.ThreatReceived -= ReactTo;
            _actor.Membership.ThreatActive = null;
        }
        _rng.Dispose();
    }
    #endregion

    #region Decisions
    // =========================================================
    // Advance inexpensive combat timers through the actor's shared tick.
    public void Tick(double delta)
    {
        _memory -= delta;
        _attackCooldown -= delta;
    }

    // =========================================================
    // Handle threats on staggered decisions; report whether wandering should wait.
    public bool Decide()
    {
        ScanPersonalSpace();

        if (!HasThreat)
        {
            if (_target != null)
            {
                _target = null;
                _actor.Motor.Stop();
            }
            return false;
        }

        EntityDefinition definition = _actor.Definition;
        if (_memory <= 0.0 ||
            _actor.GlobalPosition.DistanceSquaredTo(_target.GlobalPosition) >
                definition.DisengageRange * definition.DisengageRange ||
            _actor.GlobalPosition.DistanceSquaredTo(_actor.Wandering.Centre) >
                definition.MaxPursuitDistance * definition.MaxPursuitDistance)
        {
            _target = null;
            _actor.Motor.Stop();
            _actor.Wandering.ReturnToCentre();
            return true;
        }

        Respond();
        return true;
    }

    // =========================================================
    // Apply species and group tolerance before selecting the nearest intruder.
    private void ScanPersonalSpace()
    {
        EntityDefinition definition = _actor.Definition;
        if (definition.ThreatResponse == EntityThreatResponse.Ignore) return;

        Node2D nearest = null;
        float distance = definition.PersonalSpaceRadius *
            definition.PersonalSpaceRadius;

        foreach (string group in ActorGroups)
        foreach (Node node in GetTree().GetNodesInGroup(group))
        {
            if (node is not Node2D candidate || candidate == _actor ||
                !Living(candidate) || !WorldLayerMember.Same(_actor, candidate))
                continue;

            bool sameSpecies = candidate is Entity entity &&
                entity.Definition.SpeciesId == definition.SpeciesId;

            EntityGroup ownGroup = _actor.Membership.Group;
            EntityGroupMember other = candidate.GetNodeOrNull<EntityGroupMember>(
                "Systems/Group");
            bool sameGroup = GodotObject.IsInstanceValid(ownGroup) &&
                other?.Group == ownGroup;

            if (definition.TerritoryPolicy == EntityTerritoryPolicy.OtherSpecies &&
                sameSpecies)
                continue;
            if (definition.TerritoryPolicy == EntityTerritoryPolicy.OutsideHerd &&
                sameGroup)
                continue;

            float candidateDistance = _actor.GlobalPosition.DistanceSquaredTo(
                candidate.GlobalPosition);
            if (candidateDistance >= distance) continue;

            distance = candidateDistance;
            nearest = candidate;
        }

        if (nearest != null) ReactTo(nearest);
    }

    // =========================================================
    // Refresh threat memory without repeatedly discarding movement routes.
    public void ReactTo(Node2D attacker)
    {
        if (_actor.Definition.ThreatResponse == EntityThreatResponse.Ignore ||
            attacker == _actor || !Living(attacker) ||
            !WorldLayerMember.Same(_actor, attacker))
            return;

        bool changed = _target != attacker;
        _target = attacker;
        _memory = _actor.Definition.ThreatMemorySeconds;

        if (changed)
            _actor.Wandering.Interrupt(false);
    }

    // =========================================================
    // Choose a reachable escape or pursue and strike through shared health.
    private void Respond()
    {
        WorldNavigation navigation = WorldNavigation.For(_actor);
        if (navigation == null) return;

        EntityDefinition definition = _actor.Definition;
        if (definition.ThreatResponse == EntityThreatResponse.Flee)
        {
            if (_actor.Motor.HasGoal &&
                !_actor.Motor.Arrived && !_actor.Motor.IsStuck)
                return;

            Vector2 away = _actor.GlobalPosition - _target.GlobalPosition;
            if (away.LengthSquared() < 0.01f) away = Vector2.Right;
            away = away.Normalized();

            for (int attempt = 0; attempt < 4; attempt++)
            {
                Vector2 point = _actor.GlobalPosition +
                    away.Rotated(_rng.RandfRange(-0.65f, 0.65f)) * 140f;

                if (point.DistanceSquaredTo(_actor.Wandering.Centre) >
                    definition.MaxPursuitDistance * definition.MaxPursuitDistance ||
                    !navigation.CanTravelDirectly(_actor.GlobalPosition, point))
                    continue;

                _actor.Motor.SetGoal(point, definition.ThreatSpeed, 12f);
                return;
            }

            _actor.Motor.Stop();
            return;
        }

        if (_actor.GlobalPosition.DistanceTo(_target.GlobalPosition) >
            definition.MeleeRange)
        {
            _actor.Motor.SetGoal(_target.GlobalPosition,
                definition.ThreatSpeed, definition.MeleeRange * 0.8f);
            return;
        }

        _actor.Motor.Stop();
        if (_attackCooldown > 0.0 ||
            !navigation.CanTravelDirectly(
                _actor.GlobalPosition, _target.GlobalPosition))
            return;

        _attackCooldown = definition.MeleeCooldown;
        _target.GetNodeOrNull<Health>("Systems/Health")?.Damage(
            definition.MeleeDamage, DamageType.Neutral, _actor);
    }

    // =========================================================
    // Reject absent, departing and dead actors.
    private static bool Living(Node2D actor)
    {
        return GodotObject.IsInstanceValid(actor) && actor.IsInsideTree() &&
            !actor.IsQueuedForDeletion() &&
            actor.GetNodeOrNull<Health>("Systems/Health")?.IsAlive == true;
    }
    #endregion
}