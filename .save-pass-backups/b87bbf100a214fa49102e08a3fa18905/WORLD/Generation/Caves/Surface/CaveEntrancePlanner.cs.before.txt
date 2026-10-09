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
    private readonly GenerationCellCache<WorldLayerConnection> _cells;
    private CaveWorld _cave;
    private readonly float _floorElevation;
    private readonly float _tunnelLength;

    private readonly int _offset, _stride;
    private readonly float _pitch, _reach, _clearTiles;
    private readonly Vector2 _spawn;

    public CaveGenerationSettings Settings { get; }
    public float HubX => Settings.EntranceTunnelLengthTiles + 8f;
    #endregion

    #region Construction
    // =========================================================
// Reserve a shared chamber lattice large enough for all cave biome profiles.
public CaveEntrancePlanner(
    Node world, ChunkController chunks, CaveGenerationSettings settings,
    float floorElevation)
{
    _world = world;
    _floorElevation = floorElevation;
    _chunks = chunks;
    _config = WorldConfig.Find(world);
    _ground = world.GetNode<Node2D>("GroundChunks");
    _basins = WaterBasinWorld.Find(world);
    _sampler = new CaveSurfaceSampler(world, chunks);
    _spawn = world.GetNode<Player>("WorldObjects/Player").GlobalPosition;

    Settings = (CaveGenerationSettings)settings.Duplicate();
    Settings.Validate();
    _tunnelLength = WorldLayerConnection.LengthFor(_config, Settings.EntranceTunnelLengthTiles);

    float minimum = _config.MinimumCaveHoleDistanceTiles;
    if (!float.IsFinite(minimum) || minimum < 64f)
        throw new InvalidOperationException(
            "Minimum cave-hole distance must be at least 64 tiles.");

    float maximumRadius = Settings.MaximumChamberRadius();
    float maximumWidth = Settings.MaximumTunnelWidth();

    _offset = Mathf.CeilToInt(
        maximumRadius + maximumWidth * 0.5f + 4f);

    Settings.CellSpacingTiles = Mathf.Max(
        Settings.CellSpacingTiles,
        Mathf.CeilToInt(_tunnelLength) + _offset +
        Mathf.CeilToInt(maximumWidth * 0.5f) + 6);

    Settings.Validate();

    _reach = new Vector2(
        _offset, _tunnelLength + _offset).Length();

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
        2f / (chunks.TileSize.X * chunks.TileSize.X) +
        2f / (chunks.TileSize.Y * chunks.TileSize.Y)) + 3f;

    _cells = new GenerationCellCache<WorldLayerConnection>(CacheLimit, hole =>
    {
        if (hole != null) WorldLayerRuntime.Find(_world)?.Connections.Remove(hole);
    });
}
    #endregion

    #region Planning

        // =========================================================
    // Use the same candidate rectangle for preparation and lifetime protection.
    private void CandidateRange(
        Rect2 area, out Vector2I first, out Vector2I last)
    {
        Rect2 nearby = area.Grow(
            _config.CaveDiscoveryRadiusTiles + _reach);

        first = new Vector2I(
            Mathf.FloorToInt((nearby.Position.X - HubX) / _pitch),
            Mathf.FloorToInt(nearby.Position.Y / _pitch));

        last = new Vector2I(
            Mathf.CeilToInt((nearby.End.X - HubX) / _pitch),
            Mathf.CeilToInt(nearby.End.Y / _pitch));
    }

    // =========================================================
    // Protect entrance decisions used by the requested chunk and discovery buffer.
    public IDisposable PinArea(Rect2 area)
    {
        CandidateRange(area, out Vector2I first, out Vector2I last);
        return _cells.Pin(first, last);
    }

        // =========================================================
    // Prepare shared entrances while protecting unfinished placement checks.
    public IEnumerable<int> PrepareArea(Rect2 area, CaveWorld cave)
    {
        _cave = cave;
        using IDisposable protection = PinArea(area);

        CandidateRange(area, out Vector2I first, out Vector2I last);

        for (int y = first.Y; y <= last.Y; y++)
        for (int x = first.X; x <= last.X; x++)
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
                sideY * (_tunnelLength + _offset));
            // Align mouths to the collision tile lattice while keeping ramp length continuous.
            mouth = new Vector2(Mathf.Round(mouth.X), Mathf.Round(mouth.Y));
            Vector2 direction = sideY > 0f ? Vector2.Up : Vector2.Down;

            Vector2 point = _ground.ToGlobal(
                IsoGrid.TileToWorld(mouth, _chunks.TileSize));
            WorldLayerConnection hole = null;

            if (point.DistanceSquaredTo(_spawn) >
                _chunks.SpawnClearRadius * _chunks.SpawnClearRadius)
            {
                Rect2 waterArea = new(
                    mouth - Vector2.One * _clearTiles,
                    Vector2.One * (_clearTiles * 2f));
                waterArea = waterArea.Grow(2f);

                using IDisposable waterProtection = _basins.PinArea(waterArea);

                foreach (int step in _basins.PrepareArea(waterArea))
                    yield return step;

                CaveSurfaceSampler.Result result = new();
                foreach (int step in _sampler.Evaluate(
                    point, direction, false, result))
                    yield return step;

                if (result.Accepted &&
                    result.RimHeight > _floorElevation + 32f)
                {
                    hole = new WorldLayerConnection(
                        $"C_{x}_{y}", WorldLayerId.Surface, cave.LayerId, mouth, direction, point,
                        result.RimHeight, _tunnelLength,
                        anchor)
                    {
                        ClearRadius = result.ClearRadius
                    };
                }
            }

            if (!_cells.TryAdd(cell, hole)) continue;

            if (hole != null)
            {
                WorldLayerRuntime.Find(_world).Connections.Register(hole);
                GD.Print($"[Caves] {hole.Id}: {hole.UpperPosition}");
            }
        }
    }

    // =========================================================
    // Supply only nearby registered mouths to floor and elevation sampling.
    public IEnumerable<WorldLayerConnection> Nearby(Vector2 tile)
    {
        int firstX = Mathf.FloorToInt((tile.X - HubX - _reach - 4f) / _pitch);
        int lastX = Mathf.CeilToInt((tile.X - HubX + _reach + 4f) / _pitch);
        int firstY = Mathf.FloorToInt((tile.Y - _reach - 4f) / _pitch);
        int lastY = Mathf.CeilToInt((tile.Y + _reach + 4f) / _pitch);

        for (int y = firstY; y <= lastY; y++)
        for (int x = firstX; x <= lastX; x++)
            if (_cells.TryGetValue(new Vector2I(x, y), out WorldLayerConnection hole) &&
                hole != null)
                yield return hole;
    }
    #endregion


}
