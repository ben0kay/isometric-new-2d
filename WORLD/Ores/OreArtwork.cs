// Shares cached artwork tasks for all ore definitions using the existing baker.
// Individual ore resources select their drawing scene and cache revision.
using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public static class OreArtwork
{
    #region Cache
    private static readonly Dictionary<string, Task<ImageTexture>> Tasks = new();
    #endregion

    #region Baking
    // =========================================================
    // Load or bake one fallback texture for the selected ore artwork.
    public static Task<ImageTexture> Get(Node host, OreDefinition definition)
    {
        if (definition.FallbackDrawing == null)
            throw new InvalidOperationException(
                $"Ore '{definition.Id}' requires a fallback drawing scene.");

        string key = $"{definition.Id}|{definition.ArtworkRevision}|" +
            $"{definition.FallbackDrawing.ResourcePath}|{definition.BakeSize}";

        if (Tasks.TryGetValue(key, out Task<ImageTexture> task) &&
            !task.IsFaulted && !task.IsCanceled)
            return task;

        // A stable host prevents mining the requesting deposit from interrupting capture.
        task = ArtworkBaker.LoadOrBake(
            host.GetTree().Root, "OreBake", key, definition.BakeSize,
            () => definition.FallbackDrawing.Instantiate<Node2D>());

        Tasks[key] = task;
        return task;
    }
    #endregion
}