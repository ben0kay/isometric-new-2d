// Registers permanent water basins before terrain heights are cached.
// Water fill can change independently while basin geometry remains intact.
using Godot;
using System.Collections.Generic;
using System;
using System.Diagnostics;

public partial class WaterBasinWorld : Node
{
    #region Basin Data
    public sealed class Basin
    {
        public WaterDefinition Definition;
        public Vector2 Centre;
        public float Phase, RimHeight, Fill = 1f;
        public WaterPatch Patch;

                public bool Resident = true;

        public float SmallestRadius =>
            Mathf.Min(Definition.RadiusTiles.X, Definition.RadiusTiles.Y);
        public float Extent =>
            Mathf.Max(Definition.RadiusTiles.X, Definition.RadiusTiles.Y) * 1.1f;
        public float Rotation =>
            Mathf.DegToRad(Definition.RotationDegrees);
        public float WaterDrop => Definition.BasinDepth -
            (Definition.BasinDepth - Definition.WaterSurfaceDrop) * Fill;
        public float WaterHeight => RimHeight - WaterDrop;

        // =========================================================
        // Measure approximate inward distance from the irregular basin rim.
        public float InwardDistance(Vector2 tile)
        {
            Vector2 offset = (tile - Centre).Rotated(-Rotation);
            Vector2 q = new(offset.X / Definition.RadiusTiles.X,
                offset.Y / Definition.RadiusTiles.Y);
            return (SurfaceGeometry.Edge(q.Angle(), Phase) - q.Length())
                * SmallestRadius;
        }

        // =========================================================
        // Form a smooth bank leading down to a flat basin floor.
        public float DepthAt(Vector2 tile)
        {
            float t = Mathf.Clamp(
                InwardDistance(tile) / Definition.ShoreWidthTiles, 0f, 1f);
            return Definition.BasinDepth * t * t * (3f - 2f * t);
        }

        // =========================================================
        // Calculate the contour where basin floor meets the current water level.
        public float ShoreInsetNormalized()
        {
            float target = WaterDrop / Definition.BasinDepth;
            float low = 0f, high = 1f;
            for (int i = 0; i < 16; i++)
            {
                float middle = (low + high) * 0.5f;
                float depth = middle * middle * (3f - 2f * middle);
                if (depth < target) low = middle;
                else high = middle;
            }
            return (low + high) * 0.5f *
                Definition.ShoreWidthTiles / SmallestRadius;
        }
    }
    #endregion

    #region State
    private const int CacheLimit = 1024;
    private const int MaximumPendingCells = 256;

    private sealed class CellJob
    {
        public Basin Result;
        public IEnumerator<int> Work;
    }

    private readonly List<Basin> _basins = new();
    private readonly Dictionary<Vector2I, CellJob> _jobs = new();
    private readonly Queue<Vector2I> _pending = new();

    private GenerationCellCache<Basin> _cells;
    private WorldGenerator _generator;
    private ChunkController _chunks;
    private Node2D _ground;
    private Vector2 _spawnTile;
    private float _spacing;
    private bool _enabled;
    private double _queryBudget;

    public IReadOnlyList<Basin> Basins => _basins;
    public float MaximumDepth { get; private set; }
    #endregion

    #region Lifecycle
    // =========================================================
    // Register this scene-owned service without static world state.
    public override void _EnterTree()
    {
        AddToGroup("water_basins");
        SetProcess(false);
    }

    // =========================================================
    // Resolve the basin service from any actor or world system.
    public static WaterBasinWorld Find(Node context)
    {
        return context.GetTree().GetFirstNodeInGroup("water_basins")
            as WaterBasinWorld;
    }

    // =========================================================
    // Snapshot basin rules and enable budgeted background data preparation.
    public void Initialize(
        WorldGenerator generator, ChunkController chunks, Node2D ground)
    {
        _generator = generator;
        _chunks = chunks;
        _ground = ground;
        _cells = new GenerationCellCache<Basin>(CacheLimit, ReleaseBasin);

        WorldConfig config = WorldConfig.Find(generator);
        _enabled = config.GenerateBiomeBasins;
        _spacing = config.BasinCandidateSpacingTiles;
        _queryBudget = config.BasinQueryBudgetMs;

        if (!float.IsFinite(_spacing) || _spacing < 16f)
            throw new InvalidOperationException(
                "BasinCandidateSpacingTiles must be finite and at least 16.");

        if (!double.IsFinite(_queryBudget) || _queryBudget <= 0)
            throw new InvalidOperationException(
                "BasinQueryBudgetMs must be finite and positive.");

        Player player = generator.GetNode<Player>(
            "../../WorldObjects/Player");
        _spawnTile = IsoGrid.WorldToTile(
            ground.ToLocal(player.GlobalPosition), chunks.TileSize);

        if (_enabled)
        {
            HashSet<BiomeBasinProfile> validated = new();

            foreach (BiomeDefinition biome in
                generator.Catalog.GetEnabledBiomes())
            {
                BiomeBasinProfile profile =
                    biome.GetFeature<BiomeBasinProfile>("basins");
                if (profile == null || !profile.Enabled) continue;

                if (validated.Add(profile)) profile.Validate(biome.Id);
                MaximumDepth = Mathf.Max(MaximumDepth, profile.BasinDepth);

                foreach (WaterDefinition template in profile.Templates)
                {
                    float largest = Mathf.Max(
                        template.RadiusTiles.X, template.RadiusTiles.Y) *
                        profile.SizeMultiplierRange.Y * 1.1f +
                        template.ClearanceTiles;

                    if (largest > _spacing * 0.4f)
                        throw new InvalidOperationException(
                            $"Biome '{biome.Id}' needs larger basin cells. " +
                            "Increase BasinCandidateSpacingTiles or reduce basin size.");
                }
            }
        }

        SetProcess(_enabled);
    }
    #endregion

    #region Local Planning
    // =========================================================
    // Prepare an area under the caller's chunk-building budget.
    public IEnumerable<int> PrepareArea(Rect2 area)
    {
        if (!_enabled) yield break;

        using IDisposable protection = PinArea(area);
        Vector2I first = CellAt(area.Position);
        Vector2I last = CellAt(area.End);

        for (int y = first.Y; y <= last.Y; y++)
        for (int x = first.X; x <= last.X; x++)
        {
            Vector2I cell = new(x, y);

            while (!_cells.ContainsKey(cell))
            {
                RequestCell(cell);
                StepCell(cell);
                yield return 0;
            }
        }
    }

    // =========================================================
    // Protect metadata required by a chunk or an incremental placement check.
    public IDisposable PinArea(Rect2 area)
    {
        if (!_enabled) return new GenerationLease(null);
        return _cells.Pin(CellAt(area.Position), CellAt(area.End));
    }

    // =========================================================
    // Use floor division for positive and negative generation coordinates.
    private Vector2I CellAt(Vector2 tile)
    {
        return new Vector2I(
            Mathf.FloorToInt(tile.X / _spacing),
            Mathf.FloorToInt(tile.Y / _spacing));
    }

    // =========================================================
    // Queue one shared job without doing basin validation inside the query.
    private void RequestCell(Vector2I cell)
    {
        if (!_enabled || _cells.ContainsKey(cell) ||
            _jobs.ContainsKey(cell) || _jobs.Count >= MaximumPendingCells)
            return;

        CellJob job = new();
        job.Work = BiomeBasinGenerator.PrepareCell(
            _generator, _chunks, _spawnTile, _spacing, cell,
            basin => job.Result = basin).GetEnumerator();

        _jobs.Add(cell, job);
        _pending.Enqueue(cell);
    }

    // =========================================================
    // Resume one small generation step, publishing only completed decisions.
    private void StepCell(Vector2I cell)
    {
        if (!_jobs.TryGetValue(cell, out CellJob job)) return;
        if (job.Work.MoveNext()) return;

        job.Work.Dispose();
        _jobs.Remove(cell);

        if (_cells.TryAdd(cell, job.Result) && job.Result != null)
            _basins.Add(job.Result);
    }

    // =========================================================
    // Advance cold-query work under its own small frame budget.
    public override void _Process(double delta)
    {
        long started = Stopwatch.GetTimestamp();

        while (_pending.Count > 0 &&
            (Stopwatch.GetTimestamp() - started) * 1000.0 /
            Stopwatch.Frequency < _queryBudget)
        {
            Vector2I cell = _pending.Peek();

            if (!_jobs.ContainsKey(cell))
            {
                _pending.Dequeue();
                continue;
            }

            StepCell(cell);
        }
    }

    // =========================================================
    // Dispose suspended generation work when the world closes.
    public override void _ExitTree()
    {
        foreach (CellJob job in _jobs.Values)
            job.Work.Dispose();

        _jobs.Clear();
        _pending.Clear();
    }

    // =========================================================
    // Release only a basin whose metadata is no longer protected.
    private void ReleaseBasin(Basin basin)
    {
        if (basin == null) return;

        basin.Resident = false;
        _basins.Remove(basin);

        if (GodotObject.IsInstanceValid(basin.Patch) &&
            !basin.Patch.IsQueuedForDeletion())
            basin.Patch.QueueFree();
    }

    // =========================================================
    // Check readiness while requesting missing data without generating it inline.
    public bool IsAreaReady(Rect2 area)
    {
        if (!_enabled) return true;

        Vector2I first = CellAt(area.Position);
        Vector2I last = CellAt(area.End);
        bool ready = true;

        for (int y = first.Y; y <= last.Y; y++)
        for (int x = first.X; x <= last.X; x++)
        {
            Vector2I cell = new(x, y);
            if (_cells.ContainsKey(cell)) continue;

            RequestCell(cell);
            ready = false;
        }

        return ready;
    }

    // =========================================================
    // Return a completed height or an explicitly temporary uncarved height.
    public bool TryApplyHeight(
        Vector2 tile, float originalHeight, out float height)
    {
        height = originalHeight;
        if (!_enabled) return true;

        Vector2I cell = CellAt(tile);

        if (!_cells.TryGetValue(cell, out Basin basin))
        {
            RequestCell(cell);
            return false;
        }

        if (basin != null) height -= basin.DepthAt(tile);
        return true;
    }
    #endregion

    // =========================================================
    // Preserve existing callers without completing cold generation synchronously.
    public float ApplyHeight(Vector2 tile, float originalHeight)
    {
        TryApplyHeight(tile, originalHeight, out float height);
        return height;
    }

    // =========================================================
    // Query completed basin data, requesting unchecked cells for later frames.
    public Basin GetBasinAt(Vector2 tile)
    {
        if (!_enabled) return null;

        Vector2I cell = CellAt(tile);
        if (!_cells.TryGetValue(cell, out Basin basin))
        {
            RequestCell(cell);
            return null;
        }

        return basin != null && basin.InwardDistance(tile) > 0f
            ? basin : null;
    }

    // =========================================================
    // Keep unchecked footprints unavailable instead of assuming they are dry.
    public bool Overlaps(Vector2 centre, float radius)
    {
        if (!_enabled) return false;

        Rect2 area = new(
            centre - Vector2.One * radius,
            Vector2.One * (radius * 2f));

        if (!IsAreaReady(area)) return true;

        Vector2I first = CellAt(area.Position);
        Vector2I last = CellAt(area.End);

        for (int y = first.Y; y <= last.Y; y++)
        for (int x = first.X; x <= last.X; x++)
        {
            _cells.TryGetValue(new Vector2I(x, y), out Basin basin);
            if (basin == null) continue;

            float separation = basin.Extent + radius;
            if (centre.DistanceSquaredTo(basin.Centre) <
                separation * separation)
                return true;
        }

        return false;
    }

    // =========================================================
// Reserve the permanent basin against an object's logical ground footprint.
// Uses a conservative tile-space radius, independently of current water fill.
public bool OverlapsWorldFootprint(
    Vector2 globalPoint, Vector2 footprint, Vector2 padding)
{
    Vector2 centre = IsoGrid.WorldToTile(
        _ground.ToLocal(globalPoint), _chunks.TileSize);

    Vector2 half = footprint.Abs() * 0.5f + padding.Abs();
    float radius = 0f;

    for (int y = -1; y <= 1; y += 2)
    for (int x = -1; x <= 1; x += 2)
    {
        Vector2 corner = globalPoint +
            new Vector2(half.X * x, half.Y * y);

        Vector2 tile = IsoGrid.WorldToTile(
            _ground.ToLocal(corner), _chunks.TileSize);

        radius = Mathf.Max(radius, centre.DistanceTo(tile));
    }

    return Overlaps(centre, radius);
}
}