// Applies wildlife territory, threat memory and optional defend/flee policy.
// Uses shared targeting, combat movement and damage delivery.
using Godot;

public partial class EntityThreatResponseComponent : Node
{
    #region State
    private static readonly string[] ActorGroups =
        { "players", "enemies", "entities" };

    private Entity _actor;
    private EntityTargeting _targeting;
    private EntityCombatMovement _movement;
    private EntityCombat _combat;
    private double _memory;
    private readonly RandomNumberGenerator _rng = new();

    public bool HasThreat => _targeting?.HasTarget == true;
    private Node2D Target => _targeting?.Target;
    #endregion

    #region Lifecycle
    // =========================================================
    // Bind shared helpers and independent group threat notifications.
    public void Bind(Entity actor)
    {
        _actor = actor;
        _rng.Randomize();

        _targeting = new EntityTargeting(actor, ActorGroups);
        _movement = new EntityCombatMovement(
            actor, actor.Wandering, _rng);
        _combat = new EntityCombat(actor);

        actor.Membership.ThreatReceived += ReactTo;
        actor.Membership.ThreatActive = () => HasThreat;
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Disconnect group alerts and release owned native resources.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_actor?.Membership))
        {
            _actor.Membership.ThreatReceived -= ReactTo;
            _actor.Membership.ThreatActive = null;
        }

        _targeting?.Dispose();
        _rng.Dispose();
    }
    #endregion

    #region Decisions
    // =========================================================
    // Advance threat memory and shared combat cooldown through the actor tick.
    public void Tick(double delta)
    {
        _memory -= delta;
        _combat?.Tick(delta);
    }

    // =========================================================
    // Apply threat policy on the existing staggered decision schedule.
    public bool Decide()
    {
        EntityDefinition definition = _actor.Definition;

        if (definition.ThreatResponse == EntityThreatResponse.Ignore)
        {
            ClearThreat();
            return false;
        }

        ScanPersonalSpace();

        if (!HasThreat)
        {
            ClearThreat();
            return false;
        }

        if (_memory <= 0.0 ||
            !WorldLayerMember.Same(_actor, Target) ||
            _actor.GlobalPosition.DistanceSquaredTo(Target.GlobalPosition) >
                definition.DisengageRange * definition.DisengageRange ||
            _actor.GlobalPosition.DistanceSquaredTo(_actor.Wandering.Centre) >
                definition.MaxPursuitDistance * definition.MaxPursuitDistance)
        {
            ClearThreat();
            _actor.Wandering.ReturnToCentre();
            return true;
        }

        Respond();
        return true;
    }

    // =========================================================
    // Apply species and group tolerance before selecting a nearby intruder.
    private void ScanPersonalSpace()
    {
        EntityDefinition definition = _actor.Definition;
        Node2D nearest = null;
        float distance = definition.PersonalSpaceRadius *
            definition.PersonalSpaceRadius;

        EntityGroup ownGroup = _actor.Membership.Group;

        foreach (string group in ActorGroups)
        foreach (Node node in GetTree().GetNodesInGroup(group))
        {
            if (node is not Node2D candidate || candidate == _actor ||
                !EntityCombat.IsLiving(candidate) ||
                !WorldLayerMember.Same(_actor, candidate))
                continue;

            bool sameSpecies = candidate is Entity entity &&
                entity.Definition.SpeciesId == definition.SpeciesId;

            EntityGroupMember other =
                candidate.GetNodeOrNull<EntityGroupMember>("Systems/Group");

            bool sameGroup = GodotObject.IsInstanceValid(ownGroup) &&
                other?.Group == ownGroup;

            if (definition.TerritoryPolicy ==
                EntityTerritoryPolicy.OtherSpecies && sameSpecies)
                continue;

            if (definition.TerritoryPolicy ==
                EntityTerritoryPolicy.OutsideHerd && sameGroup)
                continue;

            float candidateDistance =
                _actor.GlobalPosition.DistanceSquaredTo(
                    candidate.GlobalPosition);

            if (candidateDistance >= distance) continue;

            distance = candidateDistance;
            nearest = candidate;
        }

        if (nearest != null) ReactTo(nearest);
    }

    // =========================================================
    // Accept local damage or a group alert without rebroadcasting it.
    public void ReactTo(Node2D attacker)
    {
        if (_actor.Definition.ThreatResponse == EntityThreatResponse.Ignore ||
            attacker == _actor || !EntityCombat.IsLiving(attacker) ||
            !WorldLayerMember.Same(_actor, attacker))
            return;

        bool changed = _targeting.SetTarget(attacker);
        _memory = _actor.Definition.ThreatMemorySeconds;

        if (changed)
        {
            _movement.Reset();
            _actor.Wandering.Interrupt(false);
        }
    }

    // =========================================================
    // Clear remembered combat without interrupting unrelated peaceful movement.
    private void ClearThreat()
    {
        if (_targeting.Target == null) return;

        _targeting.SetTarget(null);
        _movement.Reset();
        _actor.Motor.Stop();
        _memory = 0.0;
    }
    #endregion

    #region Responses
    // =========================================================
    // Choose optional fleeing or shared chasing and melee damage delivery.
    private void Respond()
    {
        WorldNavigation navigation = _actor.Navigation;
        if (navigation == null) return;

        EntityDefinition definition = _actor.Definition;

        if (definition.ThreatResponse == EntityThreatResponse.Flee)
        {
            DecideFlee(navigation, definition);
            return;
        }

        if (_actor.GlobalPosition.DistanceSquaredTo(Target.GlobalPosition) >
            definition.MeleeRange * definition.MeleeRange)
        {
            _movement.Decide(Target, false,
                new EntityCombatMovementSettings
                {
                    Positioning = EntityCombatPositioning.Chase,
                    Speed = definition.ThreatSpeed,
                    StopDistance = definition.MeleeRange * 0.8f
                });
            return;
        }

        _actor.Motor.Stop();

        _combat.TryMelee(
            Target, definition.MeleeRange, definition.MeleeDamage,
            DamageType.Neutral, definition.MeleeCooldown,
            cooldownOnRejectedDamage: true);
    }

    // =========================================================
    // Retain bounded escape choices inside the permitted pursuit area.
    private void DecideFlee(
        WorldNavigation navigation, EntityDefinition definition)
    {
        if (_actor.Motor.HasGoal &&
            !_actor.Motor.Arrived && !_actor.Motor.IsStuck)
            return;

        Vector2 away = _actor.GlobalPosition - Target.GlobalPosition;
        if (away.LengthSquared() < 0.01f) away = Vector2.Right;
        away = away.Normalized();

        for (int attempt = 0; attempt < 4; attempt++)
        {
            Vector2 point = _actor.GlobalPosition +
                away.Rotated(_rng.RandfRange(-0.65f, 0.65f)) * 140f;

            if (point.DistanceSquaredTo(_actor.Wandering.Centre) >
                definition.MaxPursuitDistance * definition.MaxPursuitDistance ||
                !navigation.CanTravelDirectly(
                    _actor.GlobalPosition, point))
                continue;

            _actor.Motor.SetGoal(point, definition.ThreatSpeed, 12f);
            return;
        }

        _actor.Motor.Stop();
    }
    #endregion
}