// Owns a shared wander area that steps periodically around its original anchor.
// Membership changes dissolve a completed herd when fewer than two remain.
using Godot;
using System;
using System.Collections.Generic;

public partial class EntityHerd : Node2D
{
    #region Configuration
    [Export] public string HerdId { get; set; } = "";
    [Export] public EntityDefinition Definition { get; set; }
    #endregion

    #region Public State
    public Vector2 Home { get; private set; }
    public float WanderRadius => Definition.HerdWanderRadius;
    public float RoamRadius => Definition.HerdRoamRadius;
    public int MemberCount => _members.Count;
    public bool FormationComplete { get; private set; }
    #endregion

    #region State
    private readonly List<Entity> _members = new();
    private readonly RandomNumberGenerator _rng = new();
    private Timer _stepTimer;
    private bool _dissolving;
    #endregion

    #region Lifecycle
    // =========================================================
    // Register the herd and prepare a timer without a herd processing loop.
    public override void _Ready()
    {
        if (Definition == null)
            throw new InvalidOperationException(
                "EntityHerd requires a species Definition.");

        Definition.Validate();
        AddToGroup("entity_herds");
        Home = GlobalPosition;
        _rng.Randomize();
        WorldLayerMember.Attach(this, WorldLayer.Surface);

        _stepTimer = new Timer
        {
            Name = "CentreStepTimer",
            WaitTime = Definition.HerdCentreIntervalSeconds,
            OneShot = false,
            ProcessCallback = Timer.TimerProcessCallback.Physics
        };
        AddChild(_stepTimer);
        _stepTimer.Timeout += OnCentreStep;

        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Release the timer subscription and native random generator.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_stepTimer))
            _stepTimer.Timeout -= OnCentreStep;

        _rng.Dispose();
    }
    #endregion

    #region Roaming
    // =========================================================
    // Try a small centre step only when the timer expires and members are calm.
    private void OnCentreStep()
    {
        _stepTimer.WaitTime = Math.Max(
            1.0, Definition.HerdCentreIntervalSeconds);

        if (_dissolving || !FormationComplete || _members.Count < 2)
            return;

        foreach (Entity member in _members)
            if (GodotObject.IsInstanceValid(member) && member.HasThreat)
                return;

        float step = Definition.HerdCentreStepDistance;
        if (step <= 0f || RoamRadius <= 0f) return;

        WorldNavigation navigation = WorldNavigation.For(this);
        if (navigation == null) return;

        for (int attempt = 0; attempt < 4; attempt++)
        {
            Vector2 point = GlobalPosition +
                Vector2.FromAngle(_rng.RandfRange(0f, Mathf.Tau)) * step;

            if (point.DistanceSquaredTo(Home) > RoamRadius * RoamRadius ||
                !navigation.CanTravelDirectly(GlobalPosition, point))
                continue;

            GlobalPosition = point;

            foreach (Entity member in _members)
                if (GodotObject.IsInstanceValid(member) &&
                    !member.IsQueuedForDeletion())
                    member.ReconsiderWanderArea();

            return;
        }
    }
    #endregion

    #region Membership
    // =========================================================
    // Find a compatible, present herd within the creature's world layer.
    public static EntityHerd Find(Entity actor, string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        foreach (Node node in actor.GetTree().GetNodesInGroup("entity_herds"))
            if (node is EntityHerd herd &&
                !herd.IsQueuedForDeletion() && !herd._dissolving &&
                herd.HerdId == id && herd.Definition != null &&
                herd.Definition.SpeciesId == actor.Definition.SpeciesId &&
                WorldLayerMember.Same(actor, herd))
                return herd;

        return null;
    }

    // =========================================================
    // Add one member while retaining the forming herd's shared centre.
    public void Join(Entity member)
    {
        if (_dissolving || _members.Contains(member)) return;
        _members.Add(member);
    }

    // =========================================================
    // Start roaming only after the spawner has finished creating members.
    public void CompleteFormation()
    {
        if (FormationComplete || _dissolving) return;

        FormationComplete = true;
        CheckSurvivors();

        if (!_dissolving)
            _stepTimer.Start();
    }

    // =========================================================
    // Remove a departing member and check whether the herd still exists.
    public void Leave(Entity member)
    {
        if (!_members.Remove(member) || !FormationComplete || _dissolving)
            return;

        CheckSurvivors();
    }

    // =========================================================
    // Convert the last living member to solo wandering and remove the herd.
    private void CheckSurvivors()
    {
        for (int i = _members.Count - 1; i >= 0; i--)
        {
            Entity member = _members[i];
            if (!GodotObject.IsInstanceValid(member) ||
                member.IsQueuedForDeletion() ||
                member.Health?.IsAlive != true)
                _members.RemoveAt(i);
        }

        if (_members.Count >= 2) return;

        _dissolving = true;
        _stepTimer.Stop();

        if (_members.Count == 1)
            _members[0].BecomeSolo(this);

        _members.Clear();
        QueueFree();
    }

    // =========================================================
    // Broadcast damage alerts without recipients rebroadcasting them.
    public void Alert(Node2D attacker)
    {
        foreach (Entity member in _members)
            if (GodotObject.IsInstanceValid(member) &&
                !member.IsQueuedForDeletion() &&
                member.Health?.IsAlive == true)
                member.ReactTo(attacker);
    }
    #endregion
}