// Chooses a clear sideways dodge and executes it through the shared enemy motor.
// Destination searches occur at action start rather than every physics tick.
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
    // Try both sideways directions and shorter alternatives against solid terrain.
    public override EnemyActionResult Begin(
        EnemySequence runner, ref EnemyActionState state)
    {
        runner.Motor.Stop();
        WorldNavigation navigation = runner.Navigation;
        if (navigation == null) return EnemyActionResult.Failed;

        Vector2 origin = runner.Actor.GlobalPosition;
        Vector2 toward = runner.Actor.Target.GlobalPosition - origin;
        toward = toward.LengthSquared() > 0.001f
            ? toward.Normalized() : Vector2.Right;
        Vector2 sideways = new(-toward.Y, toward.X);
        float firstSide = runner.RandomSide();

        for (int i = 0; i < 4; i++)
        {
            float side = i % 2 == 0 ? firstSide : -firstSide;
            float distance = i < 2 ? Distance : Distance * 0.5f;
            Vector2 destination = origin + sideways * side * distance;
            if (!navigation.CanTravelDirectly(origin, destination)) continue;

            state.Destination = destination;
            runner.Motor.SetGoal(
                destination, runner.Actor.Definition.MoveSpeed * SpeedMultiplier, 6f);
            return EnemyActionResult.Running;
        }
        return EnemyActionResult.Failed;
    }

    // =========================================================
    // Finish at the destination or fail when movement cannot complete in time.
    public override EnemyActionResult Tick(
        EnemySequence runner, ref EnemyActionState state, double delta)
    {
        if (runner.Motor.HasGoal && runner.Motor.Arrived)
            return EnemyActionResult.Completed;

        return state.Elapsed >= Timeout || runner.Motor.IsStuck
            ? EnemyActionResult.Failed : EnemyActionResult.Running;
    }

    // =========================================================
    // Require positive finite dodge distances, speed, and timeout.
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