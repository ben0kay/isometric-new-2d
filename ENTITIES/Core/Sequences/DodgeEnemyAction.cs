// Chooses sideways movement for any entity using the shared sequence runner.
// Destination searches occur only when this action starts.
using Godot;
using System;

[Tool, GlobalClass]
public partial class DodgeEnemyAction : EnemyAction
{
    #region Configuration
    [ExportGroup("Movement")]
    [Export] public float Distance { get; set; } = 110f;
    [Export] public float SpeedMultiplier { get; set; } = 2.2f;

    [ExportGroup("Timing")]
    [Export] public double Timeout { get; set; } = 1.2;
    #endregion

    #region Execution
    // =========================================================
    // Try both sideways directions and shorter alternatives around obstacles.
    public override EntityActionResult Begin(
        EntitySequence runner, ref EntityActionState state)
    {
        runner.Motor.Stop();

        WorldNavigation navigation = runner.Navigation;
        Node2D target = runner.Target;

        if (navigation == null ||
            !GodotObject.IsInstanceValid(target))
            return EntityActionResult.Failed;

        Vector2 origin = runner.Actor.GlobalPosition;
        Vector2 toward = target.GlobalPosition - origin;
        toward = toward.LengthSquared() > 0.001f
            ? toward.Normalized() : Vector2.Right;

        Vector2 sideways = new(-toward.Y, toward.X);
        float firstSide = runner.RandomSide();

        for (int i = 0; i < 4; i++)
        {
            float side = i % 2 == 0 ? firstSide : -firstSide;
            float distance = i < 2 ? Distance : Distance * 0.5f;
            Vector2 destination = origin + sideways * side * distance;

            if (!navigation.CanTravelDirectly(origin, destination))
                continue;

            state.Destination = destination;
            runner.Motor.SetGoal(
                destination, runner.MoveSpeed * SpeedMultiplier, 6f);

            return EntityActionResult.Running;
        }

        return EntityActionResult.Failed;
    }

    // =========================================================
    // Complete on arrival or fail when movement becomes stuck or times out.
    public override EntityActionResult Tick(
        EntitySequence runner, ref EntityActionState state, double delta)
    {
        if (runner.Motor.HasGoal && runner.Motor.Arrived)
            return EntityActionResult.Completed;

        return state.Elapsed >= Timeout || runner.Motor.IsStuck
            ? EntityActionResult.Failed : EntityActionResult.Running;
    }

    // =========================================================
    // Require positive finite distance, speed and timeout.
    public override void Validate()
    {
        base.Validate();

        if (!float.IsFinite(Distance) || Distance <= 6f ||
            !float.IsFinite(SpeedMultiplier) || SpeedMultiplier <= 0f ||
            !double.IsFinite(Timeout) || Timeout <= 0.0)
            throw new InvalidOperationException("Invalid dodge settings.");
    }
    #endregion
}