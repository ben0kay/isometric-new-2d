// Supplies an optional shared wander area for a generic group.
// Fixed areas do no roaming work; periodic areas use one timer.
using Godot;

public enum GroupRoamingMode { Fixed, PeriodicSteps }

public partial class GroupRoaming : Node
{
    #region Configuration
    [Export] public GroupRoamingMode Mode { get; set; } =
        GroupRoamingMode.PeriodicSteps;
    [Export] public float WanderRadius { get; set; } = 300f;
    [Export] public float RoamRadius { get; set; } = 500f;
    [Export] public float StepDistance { get; set; } = 60f;
    [Export] public double IntervalSeconds { get; set; } = 60.0;
    #endregion

    #region State
    public Vector2 Home { get; private set; }
    public Vector2 Centre => _group.GlobalPosition;

    private EntityGroup _group;
    private Timer _timer;
    private readonly RandomNumberGenerator _rng = new();
    #endregion

    #region Lifecycle
    // =========================================================
    // Retain the original anchor without starting a processing loop.
    public override void _Ready()
    {
        _group = GetParent().GetParent<EntityGroup>();
        Home = _group.GlobalPosition;
        _rng.Randomize();
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Release roaming resources when the group ends.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_timer))
            _timer.Timeout -= Step;

        _rng.Dispose();
    }
    #endregion

    #region Roaming
    // =========================================================
    // Start periodic movement only after group formation is complete.
    public void Start()
    {
        if (Mode == GroupRoamingMode.Fixed) return;

        if (_timer == null)
        {
            _timer = new Timer
            {
                Name = "CentreStepTimer",
                ProcessCallback = Timer.TimerProcessCallback.Physics
            };
            AddChild(_timer);
            _timer.Timeout += Step;
        }

        _timer.Start(System.Math.Max(1.0, IntervalSeconds));
    }

    // =========================================================
    // Suspend future roaming steps without moving the current centre.
    public void Stop()
    {
        if (GodotObject.IsInstanceValid(_timer))
            _timer.Stop();
    }

    // =========================================================
    // Attempt one bounded centre step while the group is calm.
    private void Step()
    {
        _timer.WaitTime = System.Math.Max(1.0, IntervalSeconds);

        if (Mode == GroupRoamingMode.Fixed)
        {
            Stop();
            return;
        }

        if (_group.IsQueuedForDeletion() ||
            !_group.FormationComplete || _group.HasThreat() ||
            StepDistance <= 0f || RoamRadius <= 0f)
            return;

        WorldNavigation navigation = WorldNavigation.For(_group);
        if (navigation == null) return;

        for (int attempt = 0; attempt < 4; attempt++)
        {
            Vector2 point = Centre +
                Vector2.FromAngle(_rng.RandfRange(0f, Mathf.Tau)) * StepDistance;

            if (point.DistanceSquaredTo(Home) > RoamRadius * RoamRadius ||
                !navigation.CanTravelDirectly(Centre, point))
                continue;

            _group.GlobalPosition = point;
            _group.NotifyAreaChanged();
            return;
        }
    }
    #endregion
}