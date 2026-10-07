// Registers permanent water basins before terrain heights are cached.
// Water fill can change independently while basin geometry remains intact.
using Godot;
using System.Collections.Generic;

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

    private readonly List<Basin> _basins = new();
    private readonly Dictionary<Vector2I, Basin> _cells = new();
    private readonly Queue<Vector2I> _order = new();

    private WorldGenerator _generator;
    private ChunkController _chunks;
    private Node2D _ground;
    private Vector2 _spawnTile;
    private float _spacing;
    private bool _enabled;

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
    // Snapshot generation settings without scanning the world.
    public void Initialize(
        WorldGenerator generator, ChunkController chunks, Node2D ground)
    {
        _generator = generator;
        _chunks = chunks;
        _ground = ground;

        WorldConfig config = WorldConfig.Find(generator);
        _enabled = config.GenerateBiomeBasins;
        _spacing = config.BasinCandidateSpacingTiles;

        if (!float.IsFinite(_spacing) || _spacing < 16f)
            throw new System.InvalidOperationException(
                "BasinCandidateSpacingTiles must be finite and at least 16.");

        Player player = generator.GetNode<Player>(
            "../../WorldObjects/Player");
        _spawnTile = IsoGrid.WorldToTile(
            ground.ToLocal(player.GlobalPosition), chunks.TileSize);

        if (!_enabled) return;

        HashSet<BiomeBasinProfile> validated = new();
        foreach (BiomeDefinition biome in generator.Catalog.GetEnabledBiomes())
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
                    throw new System.InvalidOperationException(
                        $"Biome '{biome.Id}' needs larger basin cells. " +
                        "Increase BasinCandidateSpacingTiles or reduce basin size.");
            }
        }
    }
    #endregion

        #region Local Planning
    // =========================================================
    // Prepare all basin cells affecting a requested tile-space area.
    public IEnumerable<int> PrepareArea(Rect2 area)
    {
        if (!_enabled) yield break;

        Vector2I first = CellAt(area.Position);
        Vector2I last = CellAt(area.End);

        for (int y = first.Y; y <= last.Y; y++)
        for (int x = first.X; x <= last.X; x++)
            foreach (int step in PrepareCell(new Vector2I(x, y)))
                yield return step;
    }

    // =========================================================
    // Cache completed decisions, including cells with no basin.
    private IEnumerable<int> PrepareCell(Vector2I cell)
    {
        if (_cells.ContainsKey(cell)) yield break;

        Basin result = null;
        foreach (int step in BiomeBasinGenerator.PrepareCell(
            _generator, _chunks, _spawnTile, _spacing, cell,
            basin => result = basin))
            yield return step;

        // Another query may have finished this cell while this iterator yielded.
        if (_cells.ContainsKey(cell)) yield break;

        while (_cells.Count >= CacheLimit)
        {
            Vector2I old = _order.Dequeue();
            Basin departing = _cells[old];
            _cells.Remove(old);

            if (departing == null) continue;
            departing.Resident = false;
            _basins.Remove(departing);

            if (GodotObject.IsInstanceValid(departing.Patch))
                departing.Patch.QueueFree();
        }

        _cells.Add(cell, result);
        _order.Enqueue(cell);
        if (result != null) _basins.Add(result);
    }

    // =========================================================
    // Use floor division on both sides of the world origin.
    private Vector2I CellAt(Vector2 tile)
    {
        return new Vector2I(
            Mathf.FloorToInt(tile.X / _spacing),
            Mathf.FloorToInt(tile.Y / _spacing));
    }

    // =========================================================
    // Complete cold data queries so terrain never caches an incomplete height.
    private Basin ReadCell(Vector2I cell)
    {
        if (!_enabled) return null;

        if (!_cells.TryGetValue(cell, out Basin basin))
        {
            foreach (int step in PrepareCell(cell)) { }
            basin = _cells[cell];
        }

        return basin;
    }
    #endregion


    // =========================================================
    // Carve the deterministic basin belonging to this coordinate's cell.
    public float ApplyHeight(Vector2 tile, float originalHeight)
    {
        Basin basin = ReadCell(CellAt(tile));
        return basin == null
            ? originalHeight : originalHeight - basin.DepthAt(tile);
    }

    // =========================================================
    // Query basin geometry without loading surface artwork.
    public Basin GetBasinAt(Vector2 tile)
    {
        Basin basin = ReadCell(CellAt(tile));
        return basin != null && basin.InwardDistance(tile) > 0f
            ? basin : null;
    }

    // =========================================================
    // Check only cells intersecting the requested footprint.
    public bool Overlaps(Vector2 centre, float radius)
    {
        Vector2I first = CellAt(centre - Vector2.One * radius);
        Vector2I last = CellAt(centre + Vector2.One * radius);

        for (int y = first.Y; y <= last.Y; y++)
        for (int x = first.X; x <= last.X; x++)
        {
            Basin basin = ReadCell(new Vector2I(x, y));
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