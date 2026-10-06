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
    private readonly List<Basin> _basins = new();
    private readonly Dictionary<Vector2I, List<Basin>> _index = new();
    private WorldGenerator _generator;
    private ChunkController _chunks;
    private Node2D _ground;
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
    // Prepare marker basins, then search the finite world for the nearest valid test.
    public void Initialize(
        WorldGenerator generator, ChunkController chunks, Node2D ground)
    {
        _generator = generator;
        _chunks = chunks;
        _ground = ground;

        foreach (Node node in GetTree().GetNodesInGroup("water_placements"))
        {
            if (node is not WaterPlacement marker ||
                marker.Definition == null) continue;

            Vector2 centre = IsoGrid.WorldToTile(
                _ground.ToLocal(marker.GlobalPosition), _chunks.TileSize);
            Basin basin = TryRegister(
                marker.Definition, centre,
                marker.ShapePhase, marker.InitialFill);

            if (basin == null)
                GD.PushWarning(
                    $"Basin '{marker.Name}' rejected at tile {centre}.");
            else
                GD.Print(
                    $"Basin '{marker.Name}' registered at tile {centre}.");
        }

        SurfaceWorld surfaces =
            generator.GetParent().GetNodeOrNull<SurfaceWorld>("Surfaces");
        if (surfaces?.PlaceTestWater != true || surfaces.TestWater == null)
            return;

        WaterDefinition definition = surfaces.TestWater;
        definition.Validate();

        Player player = generator.GetNode<Player>(
            "../../WorldObjects/Player");
        Vector2 origin = IsoGrid.WorldToTile(
            _ground.ToLocal(player.GlobalPosition), _chunks.TileSize);

        float minimum = -(_chunks.WorldChunksPerAxis / 2) *
            _chunks.ChunkSize - 0.5f;
        float span = _chunks.WorldChunksPerAxis * _chunks.ChunkSize;
        float maximum = minimum + span;
        float extent = Mathf.Max(
            definition.RadiusTiles.X, definition.RadiusTiles.Y) * 1.1f;
        float margin = extent + definition.ClearanceTiles;
        float usable = span - margin * 2f;

        if (usable <= 0f)
        {
            GD.PushWarning("[Water] Test basin is larger than the world.");
            return;
        }

        int budget = Mathf.Clamp(surfaces.TestAttempts, 16, 4096);
        int side = Mathf.Max(4, Mathf.FloorToInt(Mathf.Sqrt(budget)));
        List<Vector2> candidates = new(side * side);

        for (int y = 0; y < side; y++)
        for (int x = 0; x < side; x++)
        {
            Vector2 centre = new(
                minimum + margin + (x + 0.5f) / side * usable,
                minimum + margin + (y + 0.5f) / side * usable);

            // Keep the test body away from the starting equipment.
            if (centre.DistanceTo(origin) < extent + 6f) continue;
            candidates.Add(centre);
        }

        candidates.Sort((a, b) =>
        {
            int distance = a.DistanceSquaredTo(origin)
                .CompareTo(b.DistanceSquaredTo(origin));
            if (distance != 0) return distance;
            int order = a.Y.CompareTo(b.Y);
            return order != 0 ? order : a.X.CompareTo(b.X);
        });

        int checkedCount = 0;
        foreach (Vector2 centre in candidates)
        {
            checkedCount++;
            Basin basin = TryRegister(definition, centre, 1.7f, 1f);
            if (basin == null) continue;

            string biome = generator.GetBiome(centre).DisplayName;
            Vector2 worldPoint = _ground.ToGlobal(
                IsoGrid.TileToWorld(centre, _chunks.TileSize));

            GD.Print(
                $"[Water] Test basin accepted in {biome}; " +
                $"tile {centre}; world {worldPoint}; " +
                $"checked {checkedCount} location(s).");
            return;
        }

        GD.PushWarning(
            $"[Water] No valid test basin among {checkedCount} locations. " +
            $"Height variation limit: {definition.MaximumHeightVariation}. " +
            "No basin was carved.");
    }
    #endregion

    #region Placement
    // =========================================================
    // Validate original terrain, bounds, chasms and separation before carving.
    private Basin TryRegister(
        WaterDefinition definition, Vector2 centre, float phase, float fill)
    {
        definition.Validate();
        float extent = Mathf.Max(
            definition.RadiusTiles.X, definition.RadiusTiles.Y) * 1.1f;
        float checkedRadius = extent + definition.ClearanceTiles;
        float minimum = -(_chunks.WorldChunksPerAxis / 2) *
            _chunks.ChunkSize - 0.5f;
        float maximum = minimum +
            _chunks.WorldChunksPerAxis * _chunks.ChunkSize;

        if (centre.X - checkedRadius < minimum ||
            centre.Y - checkedRadius < minimum ||
            centre.X + checkedRadius > maximum ||
            centre.Y + checkedRadius > maximum)
            return null;

        foreach (Basin existing in _basins)
        {
            float separation = checkedRadius + existing.Extent +
                existing.Definition.ClearanceTiles;
            if (centre.DistanceSquaredTo(existing.Centre) <
                separation * separation)
                return null;
        }

        int left = Mathf.FloorToInt((centre.X - checkedRadius) * 2f);
        int right = Mathf.CeilToInt((centre.X + checkedRadius) * 2f);
        int top = Mathf.FloorToInt((centre.Y - checkedRadius) * 2f);
        int bottom = Mathf.CeilToInt((centre.Y + checkedRadius) * 2f);
        float lowest = float.PositiveInfinity;
        float highest = float.NegativeInfinity;

        for (int y = top; y <= bottom; y++)
        for (int x = left; x <= right; x++)
        {
            Vector2 tile = new(x * 0.5f, y * 0.5f);
            if (ChasmFeature.IsVoidTile(
                Mathf.FloorToInt(tile.X + 0.5f),
                Mathf.FloorToInt(tile.Y + 0.5f)))
                return null;

            float height = _generator.GetBaseHeight(tile);
            lowest = Mathf.Min(lowest, height);
            highest = Mathf.Max(highest, height);
            if (highest - lowest > definition.MaximumHeightVariation)
                return null;
        }

        Basin basin = new()
        {
            Definition = definition,
            Centre = centre,
            Phase = phase,
            RimHeight = lowest,
            Fill = Mathf.Clamp(fill, 0f, 1f)
        };
        _basins.Add(basin);
        MaximumDepth = Mathf.Max(MaximumDepth, definition.BasinDepth);

        Vector2I first = ChunkAt(centre - Vector2.One * extent);
        Vector2I last = ChunkAt(centre + Vector2.One * extent);
        for (int y = first.Y; y <= last.Y; y++)
        for (int x = first.X; x <= last.X; x++)
        {
            Vector2I key = new(x, y);
            if (!_index.TryGetValue(key, out List<Basin> entries))
                _index.Add(key, entries = new());
            entries.Add(basin);
        }
        return basin;
    }
    #endregion

    #region Terrain Queries
    // =========================================================
    // Match the world's existing chunk-coordinate convention.
    private Vector2I ChunkAt(Vector2 tile)
    {
        return new Vector2I(
            Mathf.FloorToInt((tile.X + 0.5f) / _chunks.ChunkSize),
            Mathf.FloorToInt((tile.Y + 0.5f) / _chunks.ChunkSize));
    }

    // =========================================================
    // Carve only indexed basin footprints, independently from water fill.
    public float ApplyHeight(Vector2 tile, float originalHeight)
    {
        if (!_index.TryGetValue(ChunkAt(tile), out List<Basin> entries))
            return originalHeight;

        foreach (Basin basin in entries)
            originalHeight -= basin.DepthAt(tile);
        return originalHeight;
    }

    // =========================================================
    // Keep diggable ground deposits out of basin reservations, including drained ones.
    public bool Overlaps(Vector2 centre, float radius)
    {
        foreach (Basin basin in _basins)
        {
            float separation = basin.Extent + radius;
            if (centre.DistanceSquaredTo(basin.Centre) <
                separation * separation)
                return true;
        }
        return false;
    }
    #endregion
        // =========================================================
    // Query prepared basin records without loading any world chunks.
    public Basin GetBasinAt(Vector2 tile)
    {
        if (!_index.TryGetValue(ChunkAt(tile), out List<Basin> entries))
            return null;

        foreach (Basin basin in entries)
            if (basin.InwardDistance(tile) > 0f) return basin;
        return null;
    }
}