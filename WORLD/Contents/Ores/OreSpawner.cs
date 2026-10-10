// Adds at most one surface deposit per chunk, across every biome.
// Uses an independent seed stream and existing save/placement/chunk ownership.
using Godot;
using System;
using System.Collections.Generic;

public partial class OreSpawner : Node
{
    public WorldGenerator Generator { get; set; }
    public OreSpawnSettings Settings { get; set; }

    public override void _Ready()
    {
        SetProcess(false);
        SetPhysicsProcess(false);
        if (Settings?.Enabled == true)
            Settings.Validate(ResourceWorld.Find(this)?.Catalog);
    }

    public static string CandidateIdentity(Vector2I chunk, int attempt) =>
        FormattableString.Invariant($"surface/ore/{chunk.X}/{chunk.Y}/{attempt}");

    public static bool Selected(Vector2I chunk, uint seed, double chance) =>
        IsoGrid.Hash(chunk.X, chunk.Y, seed ^ 0x0AE551u) / 4294967296.0 < chance;

    public IEnumerable<ChunkBuildStage> PopulateSteps(
        Vector2I coordinate, int chunkSize, Vector2 tileSize, uint seed,
        Node2D groundRoot, Node2D objects, Vector2 spawnPoint,
        float spawnClearRadius, List<Obstacle> owned)
    {
        if (Settings?.Enabled != true) yield break;
        ResourceChanges changes = ResourceChanges.Ensure(objects);
        OreDefinition definition = Settings.Definition;

        // A recorded source owns this chunk's single ore slot. Never replace a
        // depleted attempt with another location; restore partial deposits exactly.
        for (int i = 0; i < Settings.PlacementAttempts; i++)
        {
            ResourceChangeData saved = changes.GetByIdentity(CandidateIdentity(coordinate, i));
            if (saved == null) continue;
            if (saved.Definition != definition.ResourcePath)
                throw new InvalidOperationException("Saved generated ore definition changed.");
            if (!saved.Depleted && saved.Units > 0)
            {
                float size = saved.Width / definition.Footprint.X;
                Place(coordinate, i, size, new Vector2(saved.X, saved.Y),
                    definition, objects, owned, changes);
            }
            yield return ChunkBuildStage.Rocks;
            yield break;
        }
        if (!Selected(coordinate, seed, Settings.ChancePerChunk)) yield break;
        List<Obstacle> obstacles = WorldPlacement.CollectObstacles(objects);
        using RandomNumberGenerator rng = new();
        rng.Seed = IsoGrid.Hash(coordinate.X, coordinate.Y, seed ^ 0x0AE552u);
        for (int attempt = 0; attempt < Settings.PlacementAttempts; attempt++)
        {
            // Reuse the existing mineral population stage so loading UI and
            // streaming budgets remain compatible; no additional frame loop.
            yield return ChunkBuildStage.Rocks;
            Vector2 tile = new(coordinate.X * chunkSize - 0.5f + rng.Randf() * chunkSize,
                coordinate.Y * chunkSize - 0.5f + rng.Randf() * chunkSize);
            float size = definition.RollSize(rng);
            Vector2 footprint = definition.Footprint * size;
            Vector2 local = IsoGrid.TileToWorld(tile, tileSize);
            Vector2 point = groundRoot.ToGlobal(local);
            if (point.DistanceSquaredTo(spawnPoint) < spawnClearRadius * spawnClearRadius ||
                !FlatEnough(tile) ||
                !ChasmFeature.HasGroundClearance(local, tileSize, footprint.Length() * 0.5f + 8f) ||
                WorldPlacement.IsBlocked(objects, point, footprint, obstacles, Settings.ClearancePixels))
                continue;
            Place(coordinate, attempt, size, point, definition, objects, owned, changes);
            yield break;
        }
    }

    private bool FlatEnough(Vector2 tile)
    {
        float centre = Generator.GetHeight(tile);
        if (!float.IsFinite(centre)) return false;
        foreach (Vector2 offset in new[] { Vector2.Left, Vector2.Right, Vector2.Up, Vector2.Down })
        {
            float height = Generator.GetHeight(tile + offset);
            if (!float.IsFinite(height) || Mathf.Abs(height - centre) > Settings.MaximumHeightVariation)
                return false;
        }
        return true;
    }

    private static void Place(Vector2I coordinate, int attempt, float size, Vector2 point,
        OreDefinition definition, Node2D objects, List<Obstacle> owned, ResourceChanges changes)
    {
        OreDeposit ore = new()
        {
            Name = $"{definition.Id}_{coordinate.X}_{coordinate.Y}_{attempt}",
            Position = objects.ToLocal(point), Definition = definition, InstanceSize = size
        };
        if (!changes.BindGenerated(ore, definition, "ore", coordinate, attempt))
        {
            ore.Free();
            return;
        }
        objects.AddChild(ore);
        owned.Add(ore);
    }
}
