// Generates finite deposits on validated flat terrain and owns digging transactions.
// Four shader patches per chunk avoid individual deposit nodes and mesh rebuilds.
using Godot;
using System;
using System.Collections.Generic;

public partial class GroundResourceWorld : Node
{
    #region Configuration
    private const int PatchLimit = 4;
    private const int AttemptsPerPatch = 4;
    #endregion

    #region Runtime Data
    private sealed class Deposit
    {
        public GroundResourceDefinition Definition;
        public Vector2 Centre;
        public float Radius, Phase, Work;
        public int Remaining;
        public Vector3I Key;
    }

    private sealed class ChunkData
    {
        public readonly List<Deposit> Deposits = new();
        public ShaderMaterial Material;
    }

    private readonly Dictionary<Vector2I, ChunkData> _loaded = new();
    private readonly Dictionary<Vector3I, int> _removed = new();
    private GroundResourceCatalog _catalog;
    private ChunkController _chunks;
    private TerrainElevation _elevation;
    private Node2D _ground;
    private ResourceWorld _resources;
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve the world services after their normal initialization.
    public override void _Ready()
    {
        _chunks = GetNode<ChunkController>("../ChunkController");
        _elevation = GetNode<TerrainElevation>("../TerrainElevation");
        _ground = GetNode<Node2D>("../../GroundChunks");
        _resources = ResourceWorld.Find(this);
        _catalog = GD.Load<GroundResourceCatalog>(
            "res://WORLD/GroundResources/GroundResourceCatalog.tres");

        if (_resources == null || _catalog == null)
            throw new InvalidOperationException(
                "Ground resources require ResourceWorld and their catalog.");

        _catalog.Validate(_resources.Catalog);
        AddToGroup("ground_resources");
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Create one world-owned service when the first terrain chunk prepares.
    public static GroundResourceWorld Ensure(Node context)
    {
        GroundResourceWorld service = Find(context);
        if (service != null) return service;

        WorldAtmosphere atmosphere = context.GetTree()
            .GetFirstNodeInGroup("world_atmosphere") as WorldAtmosphere;
        if (atmosphere == null)
            throw new InvalidOperationException(
                "Ground resources require WorldAtmosphere.");

        service = new GroundResourceWorld { Name = "GroundResources" };
        atmosphere.GetParent().AddChild(service);
        return service;
    }

    // =========================================================
    // Resolve the service without assuming a scene root name.
    public static GroundResourceWorld Find(Node context)
    {
        return context.GetTree().GetFirstNodeInGroup("ground_resources")
            as GroundResourceWorld;
    }

    // =========================================================
    // Release any remaining chunk-owned shader materials.
    public override void _ExitTree()
    {
        foreach (ChunkData data in _loaded.Values)
            data.Material?.Dispose();
        _loaded.Clear();
        _removed.Clear();
    }
    #endregion

    #region Chunk Preparation
    // =========================================================
    // Try a bounded number of candidates under the existing chunk work iterator.
    public IEnumerable<ChunkBuildStage> Prepare(WorldChunk chunk)
    {
        Release(chunk.Coordinate);
        ChunkData data = new();
        _loaded.Add(chunk.Coordinate, data);

        Vector2 origin = new(
            chunk.Coordinate.X * chunk.ChunkSize,
            chunk.Coordinate.Y * chunk.ChunkSize);
        float low = -0.5f, high = chunk.ChunkSize - 0.5f;

        using RandomNumberGenerator rng = new();
        rng.Seed = IsoGrid.Hash(
            chunk.Coordinate.X, chunk.Coordinate.Y, chunk.Seed ^ 918273u);

        for (int slot = 0; slot < PatchLimit; slot++)
        {
            for (int attempt = 0; attempt < AttemptsPerPatch; attempt++)
            {
                yield return ChunkBuildStage.TerrainData;
                GroundResourceDefinition definition =
                    _catalog.Materials[rng.RandiRange(0, _catalog.Materials.Count - 1)];
                float radius = rng.RandfRange(
                    definition.RadiusTiles.X, definition.RadiusTiles.Y);
                float margin = radius * 1.1f + definition.ClearanceTiles + 1f;
                if (high - low <= margin * 2f) continue;

                Vector2 centre = origin + new Vector2(
                    rng.RandfRange(low + margin, high - margin),
                    rng.RandfRange(low + margin, high - margin));
                float phase = rng.RandfRange(0f, Mathf.Tau);

                bool overlap = false;
                foreach (Deposit existing in data.Deposits)
                {
                    float separation = (radius + existing.Radius) * 1.1f + 0.5f;
                    if (centre.DistanceSquaredTo(existing.Centre) <
                        separation * separation)
                    {
                        overlap = true;
                        break;
                    }
                }
                if (overlap || !IsFlat(centre,
                    radius * 1.1f + definition.ClearanceTiles,
                    definition.MaximumHeightVariation))
                    continue;

                Vector3I key = new(chunk.Coordinate.X, chunk.Coordinate.Y, slot);
                _removed.TryGetValue(key, out int removed);
                data.Deposits.Add(new Deposit
                {
                    Definition = definition,
                    Centre = centre,
                    Radius = radius,
                    Phase = phase,
                    Remaining = Math.Max(0, definition.UnitsPerDeposit - removed),
                    Key = key
                });
                break;
            }
        }
    }

// =========================================================
// Use the shared surface placement check without changing deposit eligibility.
private bool IsFlat(Vector2 centre, float radius, float tolerance)
{
    return SurfaceGeometry.IsFlat(_elevation, centre, radius, tolerance);
}

    // =========================================================
    // Give chunks containing deposits their own small set of shader parameters.
    public ShaderMaterial BindMaterial(WorldChunk chunk, ShaderMaterial source)
    {
        if (!_loaded.TryGetValue(chunk.Coordinate, out ChunkData data) ||
            data.Deposits.Count == 0) return source;

        data.Material = (ShaderMaterial)source.Duplicate();
        UpdateMaterial(data);
        return data.Material;
    }

    // =========================================================
    // Release visuals while retaining only the world's depletion records.
    public void Release(Vector2I coordinate)
    {
        if (!_loaded.TryGetValue(coordinate, out ChunkData data)) return;
        _loaded.Remove(coordinate);
        data.Material?.Dispose();
    }
    #endregion

    #region Shared Patch Shape
    // =========================================================
    // Shrink area proportionally to the material remaining in the deposit.
    private static float CurrentRadius(Deposit deposit)
    {
        return deposit.Radius * Mathf.Sqrt(
            deposit.Remaining / (float)deposit.Definition.UnitsPerDeposit);
    }

// =========================================================
// Keep deposit boundaries consistent with the shared surface shape formula.
private static float ShapeRadius(Deposit deposit, Vector2 difference)
{
    return CurrentRadius(deposit) *
        SurfaceGeometry.Edge(difference.Angle(), deposit.Phase);
}
    // =========================================================
    // Update four small shader records only when a deposit changes.
    private static void UpdateMaterial(ChunkData data)
    {
        for (int i = 0; i < PatchLimit; i++)
        {
            Vector4 shape = Vector4.Zero;
            Color tint = Colors.Transparent;
            if (i < data.Deposits.Count)
            {
                Deposit deposit = data.Deposits[i];
                shape = new Vector4(deposit.Centre.X, deposit.Centre.Y,
                    CurrentRadius(deposit), deposit.Phase);
                tint = deposit.Definition.SurfaceTint;
            }
            data.Material.SetShaderParameter($"deposit_{i}", shape);
            data.Material.SetShaderParameter($"deposit_color_{i}", tint);
        }
    }
    #endregion

    #region Digging
    // =========================================================
    // Apply shovel work to a visible deposit and spawn each extracted unit.
    public bool Dig(Vector2 globalPoint, int strength, float power)
    {
        if (power <= 0f || !float.IsFinite(power)) return false;
        Vector2 local = _ground.ToLocal(globalPoint);
        Vector2 tile = IsoGrid.WorldToTile(local, _chunks.TileSize);
        Vector2I coordinate = IsoGrid.WorldToChunk(
            local, _chunks.TileSize, _chunks.ChunkSize);

        if (!_loaded.TryGetValue(coordinate, out ChunkData data) ||
            data.Material == null) return false;

        foreach (Deposit deposit in data.Deposits)
        {
            if (deposit.Remaining <= 0 ||
                strength < deposit.Definition.RequiredShovelStrength) continue;

            Vector2 difference = tile - deposit.Centre;
            if (difference.Length() > ShapeRadius(deposit, difference)) continue;

            float required = deposit.Definition.WorkPerUnit;
            deposit.Work = Mathf.Min(required, deposit.Work + power);
            if (deposit.Work < required) return true;

            if (!_resources.Spawn(deposit.Definition.ItemId, 1, globalPoint))
                return false;

            deposit.Work = 0f;
            deposit.Remaining--;
            _removed[deposit.Key] =
                deposit.Definition.UnitsPerDeposit - deposit.Remaining;
            UpdateMaterial(data);
            return true;
        }
        return false;
    }
    #endregion

        #region Debug
    // =========================================================
    // Report generated deposits without counting depleted patches as available.
    public string GetDebugSummary()
    {
        int available = 0;
        int visible = 0;

        foreach (ChunkData data in _loaded.Values)
        foreach (Deposit deposit in data.Deposits)
        {
            if (deposit.Remaining <= 0) continue;
            available++;
            if (data.Material != null) visible++;
        }

        return $"Ground resources: {visible} rendered patches | " +
            $"{available} generated patches | {_loaded.Count} tracked chunks";
    }

    // =========================================================
    // Match the digging boundary and project it onto the existing terrain surface.
    public IEnumerable<(string Label, Vector2 Centre, Vector2[] Points)>
        GetDebugFootprints()
    {
        const int segments = 48;

        foreach (ChunkData data in _loaded.Values)
        {
            if (data.Material == null) continue;

            foreach (Deposit deposit in data.Deposits)
            {
                if (deposit.Remaining <= 0) continue;

                Vector2[] points = new Vector2[segments];
                for (int i = 0; i < segments; i++)
                {
                    float angle = Mathf.Tau * i / segments;
                    Vector2 direction = new(
                        Mathf.Cos(angle), Mathf.Sin(angle));
                    Vector2 tile = deposit.Centre +
                        direction * ShapeRadius(deposit, direction);

                    Vector2 point = _ground.ToGlobal(
                        IsoGrid.TileToWorld(tile, _chunks.TileSize));
                    points[i] = point +
                        Vector2.Up * _elevation.SampleWorldHeight(point);
                }

                Vector2 centre = _ground.ToGlobal(
                    IsoGrid.TileToWorld(deposit.Centre, _chunks.TileSize));
                centre += Vector2.Up * _elevation.SampleWorldHeight(centre);

                yield return (
                    $"{deposit.Definition.Id}: {deposit.Remaining}",
                    centre, points);
            }
        }
    }
    #endregion
}