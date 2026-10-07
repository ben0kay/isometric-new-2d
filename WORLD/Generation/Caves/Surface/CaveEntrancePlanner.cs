// Plans shared surface/cave entrances from absolute seeded coordinates.
// Sparse candidate spacing guarantees separation independently of discovery order.
using Godot;
using System;
using System.Collections.Generic;

public sealed class CaveEntrancePlanner
{
    #region State
    private const int CacheLimit = 1024;

    private readonly Node _world;
    private readonly ChunkController _chunks;
    private readonly WorldConfig _config;
    private readonly Node2D _ground;
    private readonly WaterBasinWorld _basins;
    private readonly CaveSurfaceSampler _sampler;

    private readonly Dictionary<Vector2I, CaveHole> _cells = new();
    private readonly Queue<Vector2I> _order = new();

    private readonly int _offset, _stride;
    private readonly float _pitch, _reach, _clearTiles;
    private readonly Vector2 _spawn;

    public CaveGenerationSettings Settings { get; }
    public float HubX => Settings.EntranceTunnelLengthTiles + 8f;
    #endregion

    #region Construction
    // =========================================================
    // Snapshot a globally consistent chamber lattice and sparse entrance lattice.
    public CaveEntrancePlanner(
        Node world, ChunkController chunks, CaveGenerationSettings settings)
    {
        _world = world;
        _chunks = chunks;
        _config = WorldConfig.Find(world);
        _ground = world.GetNode<Node2D>("GroundChunks");
        _basins = WaterBasinWorld.Find(world);
        _sampler = new CaveSurfaceSampler(world, chunks);
        _spawn = world.GetNode<Player>("WorldObjects/Player").GlobalPosition;

        Settings = (CaveGenerationSettings)settings.Duplicate();
        Settings.Validate();

        float minimum = _config.MinimumCaveHoleDistanceTiles;
        if (!float.IsFinite(minimum) || minimum < 64f)
            throw new InvalidOperationException(
                "Minimum cave-hole distance must be at least 64 tiles.");

        _offset = Mathf.CeilToInt(
            Settings.ChamberRadiusRange.Y +
            Settings.TunnelWidthTiles * 0.5f + 4f);

        Settings.CellSpacingTiles = Mathf.Max(
            Settings.CellSpacingTiles,
            Settings.EntranceTunnelLengthTiles + _offset +
            Mathf.CeilToInt(Settings.TunnelWidthTiles * 0.5f) + 6);
        Settings.Validate();

        _reach = new Vector2(
            _offset, Settings.EntranceTunnelLengthTiles + _offset).Length();

        _stride = Mathf.CeilToInt(
            (minimum + _reach * 2f) / Settings.CellSpacingTiles);
        _pitch = _stride * Settings.CellSpacingTiles;

        float maximumClearance = 0f;
        WorldGenerator generator =
            world.GetNode<WorldGenerator>("Systems/WorldGenerator");

        foreach (BiomeDefinition biome in generator.Catalog.GetEnabledBiomes())
        {
            CaveHoleProfile profile =
                biome.GetFeature<CaveHoleProfile>("cave_holes");
            if (profile == null || !profile.Enabled) continue;
            profile.Validate();
            maximumClearance = Mathf.Max(maximumClearance, profile.ClearRadius);
        }

        _clearTiles = maximumClearance * Mathf.Sqrt(
            1f / (chunks.TileSize.X * chunks.TileSize.X) +
            1f / (chunks.TileSize.Y * chunks.TileSize.Y)) + 3f;
    }
    #endregion

    #region Planning
    // =========================================================
    // Prepare entrances around either surface or underground construction.
    public IEnumerable<int> PrepareArea(Rect2 area, CaveWorld cave)
    {
        Rect2 nearby = area.Grow(
            _config.CaveDiscoveryRadiusTiles + _reach);

        int firstX = Mathf.FloorToInt((nearby.Position.X - HubX) / _pitch);
        int lastX = Mathf.CeilToInt((nearby.End.X - HubX) / _pitch);
        int firstY = Mathf.FloorToInt(nearby.Position.Y / _pitch);
        int lastY = Mathf.CeilToInt(nearby.End.Y / _pitch);

        for (int y = firstY; y <= lastY; y++)
        for (int x = firstX; x <= lastX; x++)
        {
            Vector2I cell = new(x, y);
            if (_cells.ContainsKey(cell)) continue;
            yield return 0;

            Vector2I anchor = cell * _stride;
            Vector2 room = new(
                HubX + anchor.X * Settings.CellSpacingTiles,
                anchor.Y * Settings.CellSpacingTiles);

            uint hash = IsoGrid.Hash(
                x, y, _chunks.WorldSeed ^ Settings.SeedOffset ^ 0xCA7E021u);

            float sideX = (hash & 1u) == 0 ? -1f : 1f;
            float sideY = (hash & 2u) == 0 ? -1f : 1f;
            Vector2 mouth = room + new Vector2(
                sideX * _offset,
                sideY * (Settings.EntranceTunnelLengthTiles + _offset));
            Vector2 direction = sideY > 0f ? Vector2.Up : Vector2.Down;

            Vector2 point = _ground.ToGlobal(
                IsoGrid.TileToWorld(mouth, _chunks.TileSize));

            CaveHole hole = null;

            if (point.DistanceSquaredTo(_spawn) >
                _chunks.SpawnClearRadius * _chunks.SpawnClearRadius)
            {
                // Water decisions must exist before validating this mouth.
                foreach (int step in _basins.PrepareArea(new Rect2(
                    mouth - Vector2.One * _clearTiles,
                    Vector2.One * (_clearTiles * 2f))))
                    yield return step;

                CaveSurfaceSampler.Result result = new();
                foreach (int step in _sampler.Evaluate(
                    point, direction, false, result))
                    yield return step;

                if (result.Accepted &&
                    result.RimHeight > _config.CaveFloorElevation + 32f)
                {
                    hole = new CaveHole(
                        $"C_{x}_{y}", mouth, direction, point,
                        result.RimHeight, Settings.EntranceTunnelLengthTiles,
                        anchor)
                    {
                        SurfaceClearRadius = result.ClearRadius
                    };
                }
            }

            if (_cells.ContainsKey(cell)) continue;

            while (_cells.Count >= CacheLimit)
            {
                Vector2I old = _order.Dequeue();
                CaveHole departing = _cells[old];
                _cells.Remove(old);
                if (departing != null) cave.RemoveHole(departing);
            }

            _cells.Add(cell, hole);
            _order.Enqueue(cell);

            if (hole != null)
            {
                cave.AddHole(hole);
                GD.Print($"[Caves] {hole.Id}: {hole.SurfacePosition}");
            }
        }
    }

    // =========================================================
    // Supply only nearby registered mouths to floor and elevation sampling.
    public IEnumerable<CaveHole> Nearby(Vector2 tile)
    {
        int firstX = Mathf.FloorToInt((tile.X - HubX - _reach - 4f) / _pitch);
        int lastX = Mathf.CeilToInt((tile.X - HubX + _reach + 4f) / _pitch);
        int firstY = Mathf.FloorToInt((tile.Y - _reach - 4f) / _pitch);
        int lastY = Mathf.CeilToInt((tile.Y + _reach + 4f) / _pitch);

        for (int y = firstY; y <= lastY; y++)
        for (int x = firstX; x <= lastX; x++)
            if (_cells.TryGetValue(new Vector2I(x, y), out CaveHole hole) &&
                hole != null)
                yield return hole;
    }
    #endregion
}