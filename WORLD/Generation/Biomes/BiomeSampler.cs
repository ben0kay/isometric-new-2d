// Selects broad sandbox biome bands using absolute tile coordinates.
// Also supplies smooth terrain blending across their shared boundaries.
using Godot;

public sealed class BiomeSampler
{
    #region State
    private readonly int _count, _singleIndex;
    private readonly bool _comparison;
    private readonly float _width, _blend;
    #endregion

    #region Construction
    // =========================================================
    // Cache validated sampling settings without per-query allocations.
    public BiomeSampler(
        int count, bool comparison, int singleIndex,
        float width, float blend)
    {
        _count = Mathf.Max(1, count);
        _comparison = comparison && _count > 1;
        _singleIndex = singleIndex;
        _width = Mathf.Max(8f, width);
        _blend = Mathf.Clamp(blend, 0f, _width * 0.45f);
    }
    #endregion

    #region Sampling
    // =========================================================
    // Return the dominant biome at a logical tile position.
    public int GetIndex(Vector2 tile)
    {
        if (!_comparison) return _singleIndex;
        float position = tile.X - tile.Y;
        int band = Mathf.FloorToInt((position + _width * 0.5f) / _width);
        return Wrap(band);
    }

    // =========================================================
    // Blend neighbouring bands continuously while preserving their flat interiors.
    public void GetBlend(Vector2 tile, out int a, out int b, out float weight)
    {
        a = b = _singleIndex;
        weight = 0f;
        if (!_comparison) return;

        float position = tile.X - tile.Y;
        int band = Mathf.FloorToInt((position + _width * 0.5f) / _width);
        float offset = position - band * _width;
        a = b = Wrap(band);

        float half = _width * 0.5f;
        if (_blend <= 0f || Mathf.Abs(offset) <= half - _blend) return;

        b = Wrap(band + (offset >= 0f ? 1 : -1));
        weight = Mathf.SmoothStep(
            half - _blend, half + _blend, Mathf.Abs(offset));
    }

    // =========================================================
    // Keep negative and positive band indices within the registered biome list.
    private int Wrap(int index)
    {
        return (index % _count + _count) % _count;
    }
    #endregion
}