// Checks surface generation data without loading surface chunks.
// Results are cached within this world instance and evaluated incrementally.
using Godot;
using System.Collections.Generic;

public sealed class CaveSurfaceSampler
{
    #region Results
    public sealed class Result
    {
        public bool Accepted;
        public string Reason = "Not checked";
        public string BiomeId;
        public float RimHeight;
        public float ClearRadius;

        // =========================================================
        // Reuse a completed cached decision.
        public void CopyFrom(Result other)
        {
            Accepted = other.Accepted;
            Reason = other.Reason;
            BiomeId = other.BiomeId;
            RimHeight = other.RimHeight;
            ClearRadius = other.ClearRadius;
        }
    }
    #endregion

    #region State
    private const int CacheLimit = 256;

    private readonly Node _world;
    private readonly ChunkController _chunks;
    private readonly WorldGenerator _generator;
    private readonly TerrainElevation _elevation;
    private readonly TerrainSlopeWorld _slopes;
    private readonly Node2D _ground;
    private readonly WaterBasinWorld _basins;

    private readonly Dictionary<
        (Vector2 Point, Vector2 Direction, bool IgnoreChance), Result> _cache = new();

    private readonly Queue<
        (Vector2 Point, Vector2 Direction, bool IgnoreChance)> _order = new();
    #endregion

    #region Construction
    // =========================================================
    // Connect to lightweight surface services belonging to this world.
    public CaveSurfaceSampler(Node world, ChunkController chunks)
    {
        _world = world;
        _chunks = chunks;
        _generator = world.GetNode<WorldGenerator>("Systems/WorldGenerator");
        _elevation = world.GetNode<TerrainElevation>("Systems/TerrainElevation");
        _ground = world.GetNode<Node2D>("GroundChunks");
        _slopes = TerrainSlopeWorld.Ensure(world);
        _basins = WaterBasinWorld.Find(world);
    }
    #endregion

    #region Sampling
    // =========================================================
    // Validate a mouth and its outside landing using generation data only.
    public IEnumerable<int> Evaluate(
        Vector2 point, Vector2 direction,
        bool ignoreChance, Result result)
    {
        var key = (point, direction, ignoreChance);

        if (_cache.TryGetValue(key, out Result cached))
        {
            result.CopyFrom(cached);
            yield break;
        }

        result.Accepted = false;
        result.Reason = "Outside world";

        Vector2 tile = IsoGrid.WorldToTile(
            _ground.ToLocal(point), _chunks.TileSize);

        BiomeDefinition biome = _generator.GetBiome(tile);
        CaveHoleProfile profile =
            biome.GetFeature<CaveHoleProfile>("cave_holes");

        result.BiomeId = biome.Id;

        if (profile == null || !profile.Enabled)
        {
            result.Reason = "Biome does not permit cave holes";
            Remember(key, result);
            yield break;
        }

        profile.Validate();
        result.ClearRadius = profile.ClearRadius;

        yield return 0;

        uint hash = IsoGrid.Hash(
            Mathf.RoundToInt(tile.X),
            Mathf.RoundToInt(tile.Y),
            _chunks.WorldSeed ^ 0xCA7E015u);

        double roll = hash / 4294967296.0;

        if (!ignoreChance && roll >= profile.Chance)
        {
            result.Reason = "Probability check";
            Remember(key, result);
            yield break;
        }

        Vector2 outside = point -
            IsoGrid.TileToWorld(direction * 0.9f, _chunks.TileSize);

        Vector2 footprint = Vector2.One * (profile.ClearRadius * 2f);

        foreach (Vector2 centre in new[] { point, outside })
        {
            if (!_chunks.IsDestinationWithinBounds(
                    centre, profile.ClearRadius))
            {
                result.Reason = "Outside world";
                Remember(key, result);
                yield break;
            }

            if (_basins != null &&
                _basins.OverlapsWorldFootprint(
                    centre, footprint, Vector2.Zero))
            {
                result.Reason = "Basin reservation";
                Remember(key, result);
                yield break;
            }

            if (!ChasmFeature.HasGroundClearance(
                    _ground.ToLocal(centre),
                    _chunks.TileSize,
                    profile.ClearRadius))
            {
                result.Reason = "Chasm clearance";
                Remember(key, result);
                yield break;
            }

            yield return 0;
        }

        float minimum = float.MaxValue;
        float maximum = float.MinValue;

        // Sample both the mouth and the outside landing.
        foreach (Vector2 centre in new[] { point, outside })
        {
            for (int y = -2; y <= 2; y++)
            for (int x = -2; x <= 2; x++)
            {
                Vector2 samplePoint = centre +
                    new Vector2(x, y) * (profile.ClearRadius * 0.5f);

                Vector2 sampleTile = IsoGrid.WorldToTile(
                    _ground.ToLocal(samplePoint), _chunks.TileSize);

                BiomeDefinition sampleBiome =
                    _generator.GetBiome(sampleTile);

                CaveHoleProfile sampleProfile =
                    sampleBiome.GetFeature<CaveHoleProfile>("cave_holes");

                if (sampleProfile == null || !sampleProfile.Enabled)
                {
                    result.Reason = "Clearance crosses a forbidden biome";
                    Remember(key, result);
                    yield break;
                }

                if (!_slopes.HasClearance(samplePoint, 0f))
                {
                    result.Reason = "Terrain too steep";
                    Remember(key, result);
                    yield break;
                }

                float height = _elevation.SampleWorldHeight(samplePoint);
                minimum = Mathf.Min(minimum, height);
                maximum = Mathf.Max(maximum, height);

                if (maximum - minimum > profile.MaximumHeightVariation)
                {
                    result.Reason = "Height variation too large";
                    Remember(key, result);
                    yield break;
                }

                yield return 0;
            }
        }

        result.RimHeight = _elevation.SampleWorldHeight(point);
        result.Accepted = true;
        result.Reason = "Suitable";
        Remember(key, result);
    }

    // =========================================================
    // Bound cached decisions so sampling cannot grow memory indefinitely.
    private void Remember(
        (Vector2 Point, Vector2 Direction, bool IgnoreChance) key,
        Result result)
    {
        if (_cache.ContainsKey(key)) return;

        while (_cache.Count >= CacheLimit)
            _cache.Remove(_order.Dequeue());

        Result copy = new();
        copy.CopyFrom(result);
        _cache.Add(key, copy);
        _order.Enqueue(key);
    }
    #endregion
}