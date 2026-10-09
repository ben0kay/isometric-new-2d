// Holds generation metadata for the lifetime of one streamed chunk.
// Automatic tree cleanup releases protection when the chunk is removed.
using Godot;
using System;

public partial class GenerationMetadataLease : Node
{
    #region State
    private IDisposable _lease;
    #endregion

    #region Lifecycle
    // =========================================================
    // Attach protection once before the owning chunk starts construction.
    public static void Attach(
        Node owner, InfiniteWorldGeneration generation, Rect2 area,
        string layer = WorldLayerId.Surface)
    {
        if (owner.GetNodeOrNull<GenerationMetadataLease>(
            "GenerationMetadata") != null)
            return;

        GenerationMetadataLease helper = new()
        {
            Name = "GenerationMetadata",
            _lease = generation.PinArea(area, layer)
        };
        owner.AddChild(helper);
    }

    // =========================================================
    // Release protection after the owning chunk leaves the scene tree.
    public override void _ExitTree()
    {
        _lease?.Dispose();
        _lease = null;
    }
    #endregion
}
