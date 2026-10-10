// Plans permanent cave-to-cave corridors from stable layer IDs and absolute cells.
// Both endpoints prepare identical metadata; unused decisions have a bounded cache.
using Godot;
using System;
using System.Collections.Generic;

public sealed class LayerConnectionPlanner
{
    #region Plans
    private sealed class Pair
    {
        public CaveWorld Upper, Lower;
        public float Pitch, Reach, Length, Chance;
        public uint Seed;
        public GenerationCellCache<WorldLayerConnection> Cells;
    }
    private readonly List<Pair> _pairs = new();
    private readonly Dictionary<string, List<Pair>> _byLayer = new(StringComparer.Ordinal);
    private readonly WorldLayerRuntime _runtime;
    #endregion

    #region Construction
    // =========================================================
    // Snapshot rules once; adding a depth requires definitions rather than new code.
    public LayerConnectionPlanner(WorldLayerRuntime runtime, WorldConfig config)
    {
        _runtime = runtime;
        foreach (WorldLayerDefinition definition in config.GetLayerCatalog().Layers)
        {
            if (definition.Kind != WorldLayerKind.Underground || string.IsNullOrEmpty(definition.DownwardLayerId)) continue;
            CaveWorld upper = runtime.GetUnderground(definition.Id);
            CaveWorld lower = runtime.GetUnderground(definition.DownwardLayerId);
            float length = WorldLayerConnection.LengthFor(config, definition.ConnectionLengthTiles);
            float reach = upper.Settings.CellSpacingTiles * 1.5f + upper.Settings.MaximumChamberRadius() + 8f + length +
                lower.Settings.CellSpacingTiles * 1.5f + lower.Settings.MaximumChamberRadius() +
                Mathf.Max(upper.Settings.MaximumTunnelWidth(), lower.Settings.MaximumTunnelWidth()) + 8f;
            Pair pair = new()
            {
                Upper = upper, Lower = lower, Length = length, Reach = reach,
                Pitch = Mathf.Max(definition.ConnectionSpacingTiles, reach * 2f + 16f),
                Chance = config.ScaleLayerConnectionChance(definition.Id, definition.ConnectionChance),
                Seed = StableSeed(upper.WorldSeed, definition.Id + ">" + lower.LayerId)
            };
            pair.Cells = new GenerationCellCache<WorldLayerConnection>(512, connection =>
            {
                if (connection != null) runtime.Connections.Remove(connection);
            });
            _pairs.Add(pair);
        }
        _pairs.Sort((a, b) => string.CompareOrdinal(a.Upper.LayerId, b.Upper.LayerId));
        foreach (Pair pair in _pairs)
            foreach (string layer in new[] { pair.Upper.LayerId, pair.Lower.LayerId })
            {
                if (!_byLayer.TryGetValue(layer, out List<Pair> pairs))
                    _byLayer.Add(layer, pairs = new List<Pair>());
                pairs.Add(pair);
            }
    }

    // =========================================================
    // Stable hashing is independent of runtime string hash randomization.
    private static uint StableSeed(uint seed, string identity)
    {
        unchecked { foreach (char character in identity) seed = (seed ^ character) * 16777619u; }
        return seed;
    }
    #endregion

    #region Candidate Geometry
    // =========================================================
    // Resolve only the pairs touching this layer, without scanning every depth.
    private IEnumerable<Pair> PairsFor(string layer) =>
        _byLayer.TryGetValue(layer, out List<Pair> pairs) ? pairs : Array.Empty<Pair>();

    // =========================================================
    // Conservatively include corridors whose remote endpoint touches this area.
    private static void Range(Pair pair, Rect2 area, out Vector2I first, out Vector2I last)
    {
        Rect2 nearby = area.Grow(pair.Reach);
        first = new(Mathf.FloorToInt(nearby.Position.X / pair.Pitch), Mathf.FloorToInt(nearby.Position.Y / pair.Pitch));
        last = new(Mathf.CeilToInt(nearby.End.X / pair.Pitch), Mathf.CeilToInt(nearby.End.Y / pair.Pitch));
    }

    // =========================================================
    // Compute raw decisions without consulting registered routes or physical chunks.
    private static WorldLayerConnection Candidate(Pair pair, Vector2I cell)
    {
        uint hash = IsoGrid.Hash(cell.X, cell.Y, pair.Seed);
        if (hash / 4294967296.0 >= pair.Chance) return null;
        Vector2 centre = new((cell.X + 0.5f) * pair.Pitch, (cell.Y + 0.5f) * pair.Pitch);
        Vector2I upperCell = pair.Upper.Generator.RoomCell(centre);
        Vector2 anchor = pair.Upper.Generator.RoomCentre(upperCell);
        Vector2 direction = (hash & 3u) switch
        { 0 => Vector2.Right, 1 => Vector2.Left, 2 => Vector2.Up, _ => Vector2.Down };
        Vector2 mouth = anchor + direction * Mathf.Ceil(pair.Upper.Settings.MaximumChamberRadius() + 6f);
        mouth = new(Mathf.Round(mouth.X), Mathf.Round(mouth.Y));
        Vector2 end = mouth + direction * pair.Length;
        Vector2I lowerCell = pair.Lower.Generator.RoomCell(end + direction * (pair.Lower.Settings.MaximumChamberRadius() + 4f));
        float height = pair.Upper.FloorElevation + pair.Upper.Generator.Biomes.At(mouth).FloorHeight(mouth);
        if (height <= pair.Lower.FloorElevation + 32f) return null;
        WorldLayerConnection connection = new($"L_{pair.Upper.LayerId}_{pair.Lower.LayerId}_{cell.X}_{cell.Y}",
            pair.Upper.LayerId, pair.Lower.LayerId, mouth, direction,
            pair.Upper.TileToWorld(mouth), height, pair.Length, lowerCell, anchor);
        connection.SampleArea = connection.RampArea.Expand(anchor).Expand(pair.Lower.Generator.RoomCentre(lowerCell)).Grow(
            Mathf.Max(8f, Mathf.Max(pair.Upper.Settings.MaximumTunnelWidth(), pair.Lower.Settings.MaximumTunnelWidth()) * 0.5f + 4f));
        return connection;
    }

    // =========================================================
    // Resolve adjacent-depth overlaps from raw seeded plans, never discovery order.
    private bool Conflicts(Pair owner, WorldLayerConnection candidate)
    {
        foreach (Pair other in _pairs)
        {
            if (other == owner || (other.Upper.LayerId != owner.Lower.LayerId && other.Lower.LayerId != owner.Upper.LayerId &&
                other.Upper.LayerId != owner.Upper.LayerId && other.Lower.LayerId != owner.Lower.LayerId)) continue;
            Range(other, candidate.SampleArea, out Vector2I first, out Vector2I last);
            for (int y = first.Y; y <= last.Y; y++)
            for (int x = first.X; x <= last.X; x++)
            {
                WorldLayerConnection neighbor = Candidate(other, new(x, y));
                if (neighbor != null && string.CompareOrdinal(neighbor.Id, candidate.Id) < 0 &&
                    neighbor.SampleArea.Intersects(candidate.SampleArea)) return true;
            }
        }
        return false;
    }
    #endregion

    #region Chunk Preparation And Lifetime
    // =========================================================
    // Pin every relevant pair until the requesting chunk or traveller releases it.
    public IDisposable PinArea(Rect2 area, string layer)
    {
        List<IDisposable> leases = new();
        foreach (Pair pair in PairsFor(layer))
        {
            if (pair.Upper.LayerId != layer && pair.Lower.LayerId != layer) continue;
            Range(pair, area, out Vector2I first, out Vector2I last);
            leases.Add(pair.Cells.Pin(first, last));
        }
        return new GenerationLease(() => { foreach (IDisposable lease in leases) lease.Dispose(); });
    }

    // =========================================================
    // Yield between candidates so existing chunk build budgets bound this work.
    public IEnumerable<int> PrepareArea(Rect2 area, string layer)
    {
        using IDisposable protection = PinArea(area, layer);
        foreach (Pair pair in PairsFor(layer))
        {
            if (pair.Upper.LayerId != layer && pair.Lower.LayerId != layer) continue;
            Range(pair, area, out Vector2I first, out Vector2I last);
            for (int y = first.Y; y <= last.Y; y++)
            for (int x = first.X; x <= last.X; x++)
            {
                Vector2I cell = new(x, y);
                if (pair.Cells.ContainsKey(cell)) continue;
                yield return 0;
                WorldLayerConnection connection = Candidate(pair, cell);
                if (connection != null && Conflicts(pair, connection)) connection = null;
                if (!pair.Cells.TryAdd(cell, connection) || connection == null) continue;
                _runtime.Connections.Register(connection);
            }
        }
    }
    #endregion
}
