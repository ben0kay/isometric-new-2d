// Distributes periodic work using a stable phase for each node.
using Godot;

public static class StaggeredUpdate
{
    #region Scheduling
    // =========================================================
    // Return true on this node's assigned physics tick.
    public static bool Due(Node owner, int intervalTicks, int channel = 0)
    {
        if (!GodotObject.IsInstanceValid(owner)) return false;

        ulong interval = (ulong)Mathf.Max(1, intervalTicks);
        ulong phase = (owner.GetInstanceId() +
            (ulong)(uint)channel) % interval;

        return ((ulong)Engine.GetPhysicsFrames() + phase) % interval == 0;
    }

    // =========================================================
    // Convert a seconds interval into staggered physics ticks.
    public static bool DueSeconds(
        Node owner, double intervalSeconds, int channel = 0)
    {
        int ticks = Mathf.Max(1, Mathf.CeilToInt(
            (float)intervalSeconds * Engine.PhysicsTicksPerSecond));

        return Due(owner, ticks, channel);
    }
    #endregion
}