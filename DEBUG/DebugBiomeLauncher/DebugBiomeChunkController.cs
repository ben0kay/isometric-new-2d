// Delays normal chunk startup only in the inherited biome-debug scene.
// After choosing the spawn point, the existing ChunkController handles streaming.
using Godot;
using System;

public partial class DebugBiomeChunkController : ChunkController
{
    #region Lifecycle
    // =========================================================
    // Freeze starting objects, find the biome spawn, then start normal streaming.
    public override async void _Ready()
    {
        SetProcess(false);

        DebugBiomeWorld world = GetNode<DebugBiomeWorld>("../..");
        Node2D objects = world.GetNode<Node2D>("WorldObjects");
        ProcessModeEnum previousMode = objects.ProcessMode;
        objects.ProcessMode = ProcessModeEnum.Disabled;

        try
        {
            // Let all world services finish their synchronous Ready callbacks.
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            if (!IsInsideTree() || IsQueuedForDeletion()) return;

            Vector2? spawn = await DebugBiomeSpawnSearch.Find(world, this);

            if (!IsInsideTree() || IsQueuedForDeletion()) return;

            if (!spawn.HasValue)
            {
                world.ReturnToLauncher(
                    "No suitable starting area was found for that biome.");
                return;
            }

            world.PlaceStartingArea(spawn.Value);

            // The normal controller now takes over its own startup freeze.
            objects.ProcessMode = previousMode;
            base._Ready();
        }
        catch (Exception error)
        {
            GD.PushError($"[BiomeTest] {error}");
            world.ReturnToLauncher(error.Message);
        }
    }
    #endregion
}