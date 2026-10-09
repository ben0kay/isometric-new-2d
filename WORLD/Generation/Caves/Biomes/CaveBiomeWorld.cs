// Samples independent underground biome regions and blends neighbouring profiles.
// Uses shared catalog resources without borrowing the surface biome sampler.
using Godot;
using System;
using System.Collections.Generic;

public sealed class CaveBiomeWorld
{
    #region Blended Profile
    public readonly struct Sample
    {
        public readonly CaveTerrainGenerator A, B;
        public readonly float Weight;

        public Sample(
            CaveTerrainGenerator a, CaveTerrainGenerator b, float weight)
        {
            A = a;
            B = b;
            Weight = weight;
        }

        public CaveBiomeDefinition Definition =>
            Weight < 0.5f ? A.Definition : B.Definition;

        public float TunnelWidth => Mathf.Lerp(
            A.Definition.TunnelWidthTiles,
            B.Definition.TunnelWidthTiles, Weight);

        public float ExtraConnections => Mathf.Lerp(
            A.Definition.ExtraConnectionChance,
            B.Definition.ExtraConnectionChance, Weight);

        // =========================================================
        // Blend chamber boundaries without changing their shared centre.
        public float ChamberDistance(
            Vector2 point, Vector2 centre, int x, int y)
        {
            float a = A.ChamberDistance(point, centre, x, y);
            return A == B ? a : Mathf.Lerp(
                a, B.ChamberDistance(point, centre, x, y), Weight);
        }

        // =========================================================
        // Blend passage bends while preserving both endpoints.
        public float PassageOffset(float t, uint seed)
        {
            float a = A.PassageOffset(t, seed);
            return A == B ? a : Mathf.Lerp(
                a, B.PassageOffset(t, seed), Weight);
        }

        // =========================================================
        // Blend optional floor elevation between underground profiles.
        public float FloorHeight(Vector2 tile)
        {
            float a = A.FloorHeight(tile);
            return A == B ? a : Mathf.Lerp(a, B.FloorHeight(tile), Weight);
        }
    }
    #endregion

    #region State
    private readonly CaveTerrainGenerator[] _terrain;
    private readonly float[] _boundaries;
    private readonly FastNoiseLite _noise;
    private readonly float _blendWidth;
    private readonly int _testIndex = -1;
    #endregion

    #region Construction
    // =========================================================
    // Build only cave profiles, using a separate seed stream and region scale.
    public CaveBiomeWorld(CaveGenerationSettings settings, uint seed)
    {
        List<BiomeDefinition> definitions =
            settings.GetBiomeCatalog().GetEnabledBiomes();

        _terrain = new CaveTerrainGenerator[definitions.Count];
        _boundaries = new float[definitions.Count];

        float total = 0f;
        for (int i = 0; i < definitions.Count; i++)
        {
            if (definitions[i] is not CaveBiomeDefinition cave)
                throw new InvalidOperationException(
                    "The cave catalog may only contain cave biome definitions.");

            cave.ValidateCave();
            total += cave.SelectionWeight;
            _boundaries[i] = total;
            _terrain[i] = cave.CreateCaveTerrain(seed ^ 0xB10C731u);

            if (cave.Id == settings.TestBiomeId)
                _testIndex = i;
        }

        if (!float.IsFinite(total) || total <= 0f)
            throw new InvalidOperationException(
                "Cave biome selection weights are invalid.");

        if (!string.IsNullOrWhiteSpace(settings.TestBiomeId) &&
            _testIndex < 0)
            throw new InvalidOperationException(
                $"Unknown cave TestBiomeId: '{settings.TestBiomeId}'.");

        for (int i = 0; i < _boundaries.Length; i++)
            _boundaries[i] /= total;

        _blendWidth = settings.BiomeBlendWidth;
        _noise = new FastNoiseLite
        {
            Seed = unchecked((int)(seed ^ 0xCA8B10u)),
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            Frequency = 1f / settings.BiomeSizeTiles
        };

        GD.Print(
            $"[Caves] {definitions.Count} underground biome(s); " +
            $"scale: {settings.BiomeSizeTiles} tiles; " +
            $"test: {(string.IsNullOrEmpty(settings.TestBiomeId) ? "mixed" : settings.TestBiomeId)}");
    }
    #endregion

    #region Sampling
    // =========================================================
    // Sample a smooth underground distribution independently of surface climate.
    public Sample At(Vector2 tile)
    {
        if (_testIndex >= 0)
            return Single(_testIndex);

        float value = Mathf.Clamp(
            0.5f + _noise.GetNoise2D(tile.X, tile.Y) * 0.9f, 0f, 1f);

        for (int i = 0; i < _terrain.Length - 1; i++)
        {
            float boundary = _boundaries[i];

            float left = i == 0 ? 0f : _boundaries[i - 1];
            float right = _boundaries[i + 1];
            float halfWidth = Mathf.Min(
                _blendWidth * 0.5f,
                Mathf.Min(boundary - left, right - boundary) * 0.45f);

            if (halfWidth > 0f &&
                value >= boundary - halfWidth &&
                value <= boundary + halfWidth)
            {
                float t = (value - boundary + halfWidth) /
                    (halfWidth * 2f);
                t = t * t * (3f - 2f * t);
                return new Sample(_terrain[i], _terrain[i + 1], t);
            }
        }

        for (int i = 0; i < _boundaries.Length; i++)
            if (value <= _boundaries[i])
                return Single(i);

        return Single(_terrain.Length - 1);
    }

    // =========================================================
    // Expose the dominant cave biome for future content and debug queries.
    public CaveBiomeDefinition GetBiome(Vector2 tile)
    {
        return At(tile).Definition;
    }

    // =========================================================
    // Avoid duplicate terrain sampling in regions containing a single profile.
    private Sample Single(int index)
    {
        return new Sample(_terrain[index], _terrain[index], 0f);
    }
    #endregion
}