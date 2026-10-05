// Reuses shared obstacle collision/shadows and bakes an assigned drawing scene.
// All instances share a cached texture; optional replacement artwork uses TerrainVisual.

//Increase an object’s BakeRevision after changing its drawing to invalidate its disk cache. ???????
using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public partial class BakedObstacle : Obstacle
{
    #region Configuration
    [ExportGroup("Baked Artwork")]
    [Export] public PackedScene DrawingScene { get; set; }
    [Export] public string BakeName { get; set; } = "world_object";
    [Export] public int BakeRevision { get; set; } = 1;
    [Export] public Vector2I BakeSize { get; set; } = new(256, 256);
    [Export] public Vector2 ArtworkOrigin { get; set; } = new(-128, -192);
    #endregion

    #region Shared Cache
    private static readonly Dictionary<string, Task<ImageTexture>> Textures = new();
    #endregion

    #region Artwork
    // =========================================================
    // Attach custom artwork when assigned, with a shared baked drawing as fallback.
    protected override async Task AttachArtworkAsync()
    {
        if (DrawingScene == null || string.IsNullOrWhiteSpace(BakeName) ||
            BakeSize.X <= 0 || BakeSize.Y <= 0)
            throw new InvalidOperationException(
                $"BakedObstacle '{Name}' requires valid drawing and bake settings.");

        string key = $"{BakeName}|{DrawingScene.ResourcePath}|{BakeRevision}" +
            $"|{BakeSize.X}x{BakeSize.Y}";

        if (!Textures.TryGetValue(key, out Task<ImageTexture> task))
        {
            task = ArtworkBaker.LoadOrBake(
                GetTree().Root, BakeName, key, BakeSize, CreatePainter);
            Textures.Add(key, task);
        }

        ImageTexture texture;
        try { texture = await task; }
        catch
        {
            Textures.Remove(key);
            throw;
        }

        if (!IsInsideTree() || IsQueuedForDeletion()) return;

        TerrainVisual.Attach(
            this, new Rect2(Vector2.Zero, new Vector2(BakeSize.X, BakeSize.Y)),
            ArtworkOrigin, Vector2.One, false, VisualOverride, texture);
    }

    // =========================================================
    // Instantiate the resource-assigned painter only when baking is necessary.
    private Node2D CreatePainter()
    {
        Node instance = DrawingScene.Instantiate();
        if (instance is Node2D painter) return painter;

        instance.Free();
        throw new InvalidOperationException(
            $"Drawing scene for '{BakeName}' requires a Node2D root.");
    }
    #endregion
}