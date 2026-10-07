// Plans stable entrance records for the finite world's shared cave network.
// Uses generation data only, before surface props or cave geometry are built.
using Godot;
using System;
using System.Collections.Generic;

public sealed class CaveEntrancePlanner
{
    #region Results
    public CaveGenerationSettings Settings { get; }
    public Vector2 Origin { get; private set; }
    public float RimHeight { get; private set; }
    public List<CaveHole> Holes { get; } = new();
    #endregion

    #region State
    private readonly Node _world;
    private readonly ChunkController _chunks;
    private readonly WorldConfig _config;
    #endregion

    #region Construction
    // =========================================================
    // Copy generation settings so shared resource files remain unchanged.
    public CaveEntrancePlanner(
        Node world, ChunkController chunks,
        CaveGenerationSettings settings)
    {
        _world = world;
        _chunks = chunks;
        _config = WorldConfig.Find(world);
        Settings = (CaveGenerationSettings)settings.Duplicate();
        Settings.Validate();
    }
    #endregion

    #region Planning
    // =========================================================
    // Establish a fixed network and evaluate entrance candidates in stable order.
    public IEnumerable<int> Prepare()
    {
        float configuredDistance = _config.MinimumCaveHoleDistanceTiles;
        if (!float.IsFinite(configuredDistance) || configuredDistance < 64f)
            throw new InvalidOperationException(
                "Minimum cave-hole distance must be finite and at least 64 tiles.");

        Node2D ground = _world.GetNode<Node2D>("GroundChunks");
        TerrainElevation elevation =
            _world.GetNode<TerrainElevation>("Systems/TerrainElevation");
        CaveSurfaceSampler sampler = new(_world, _chunks);

        // Put ramps beside the connecting routes, keeping those routes intact.
        int offset = Mathf.CeilToInt(
            Settings.ChamberRadiusRange.Y +
            Settings.TunnelWidthTiles * 0.5f + 4f);

        int spacing = Mathf.Max(
            Settings.CellSpacingTiles,
            Settings.EntranceTunnelLengthTiles + offset +
            Mathf.CeilToInt(Settings.TunnelWidthTiles * 0.5f) + 6);

        Settings.CellSpacingTiles = spacing;

        int worldTiles = _chunks.WorldChunksPerAxis * _chunks.ChunkSize;
        int low = -(_chunks.WorldChunksPerAxis / 2) * _chunks.ChunkSize;
        float centreY = low + worldTiles * 0.5f;
        float hub = Settings.EntranceTunnelLengthTiles + 8f;

        // Keep the chamber grid one cell inside the finite surface boundary.
        if (worldTiles < spacing * 2)
        {
            GD.PushWarning("[Caves] World is too small for this cave profile.");
            yield break;
        }

        Settings.CellsAcross =
            Mathf.FloorToInt((worldTiles - spacing * 2f) / spacing) + 1;
        Settings.CellsEitherSide =
            Mathf.FloorToInt((worldTiles * 0.5f - spacing) / spacing);
        Settings.Validate();

        Vector2 originTile = new(low + spacing - hub, centreY);
        Origin = ground.ToGlobal(
            IsoGrid.TileToWorld(originTile, _chunks.TileSize));

        Vector2 surfaceCentre = ground.ToGlobal(
            IsoGrid.TileToWorld(
                new Vector2(low + worldTiles * 0.5f, centreY),
                _chunks.TileSize));
        RimHeight = elevation.SampleWorldHeight(surfaceCentre);

        float minimum = Mathf.Max(
            configuredDistance,
            2f * (Settings.EntranceTunnelLengthTiles + offset) + 8f);
        float minimumSquared = minimum * minimum;

        for (int y = -Settings.CellsEitherSide;
            y <= Settings.CellsEitherSide; y++)
        for (int x = 0; x < Settings.CellsAcross; x++)
        {
            yield return 0;

            uint hash = IsoGrid.Hash(
                x, y, _chunks.WorldSeed ^ Settings.SeedOffset ^ 0xCA7E021u);

            float sideX = (hash & 1u) == 0 ? -1f : 1f;
            float sideY = (hash & 2u) == 0 ? -1f : 1f;

            Vector2 room = new(hub + x * spacing, y * spacing);
            Vector2 mouth = room + new Vector2(
                sideX * offset,
                sideY * (Settings.EntranceTunnelLengthTiles + offset));
            Vector2 direction = sideY > 0f ? Vector2.Up : Vector2.Down;

            bool tooClose = false;
            foreach (CaveHole existing in Holes)
            {
                if (mouth.DistanceSquaredTo(existing.MouthTile) >= minimumSquared)
                    continue;
                tooClose = true;
                break;
            }
            if (tooClose) continue;

            Vector2 surfaceTile = originTile + mouth;
            Vector2 point = ground.ToGlobal(
                IsoGrid.TileToWorld(surfaceTile, _chunks.TileSize));

            CaveSurfaceSampler.Result result = new();
            foreach (int step in sampler.Evaluate(
                point, direction, false, result))
                yield return step;

            if (!result.Accepted) continue;

            Holes.Add(new CaveHole(
                $"C_{x}_{y}",
                mouth, direction, point, result.RimHeight,
                Settings.EntranceTunnelLengthTiles,
                new Vector2I(x, y))
            {
                SurfaceClearRadius = result.ClearRadius
            });

            GD.Print(
                $"[Caves] C_{x}_{y}: {point}; surface biome: {result.BiomeId}.");
        }
    }
    #endregion
}