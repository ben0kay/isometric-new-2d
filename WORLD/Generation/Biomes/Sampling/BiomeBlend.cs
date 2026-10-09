// Stores normalized biome influences without allocating a collection per sample.
// Duplicate biome indices are merged before terrain and population queries.
using Godot;
using System;

public readonly struct BiomeInfluence
{
    #region Values
    public readonly int Index;
    public readonly float Weight;
    #endregion

    #region Construction
    // =========================================================
    // Store one biome's contribution.
    public BiomeInfluence(int index, float weight)
    {
        Index = index;
        Weight = weight;
    }
    #endregion
}

public struct BiomeBlend
{
    #region State
    private BiomeInfluence _a, _b, _c, _d, _e, _f, _g, _h, _i;
    private BiomeInfluence _j, _k, _l, _m, _n, _o, _p, _q, _r;
    private BiomeInfluence _s, _t, _u, _v, _w, _x, _y;
    public int Count { get; private set; }
    public int DominantIndex { get; private set; }
    #endregion

    #region Access
    // =========================================================
    // Read one influence from the allocation-free fixed-capacity blend.
    public readonly BiomeInfluence Get(int index)
    {
        return index switch
        {
            0 => _a, 1 => _b, 2 => _c, 3 => _d, 4 => _e,
            5 => _f, 6 => _g, 7 => _h, 8 => _i, 9 => _j,
            10 => _k, 11 => _l, 12 => _m, 13 => _n, 14 => _o,
            15 => _p, 16 => _q, 17 => _r, 18 => _s, 19 => _t,
            20 => _u, 21 => _v, 22 => _w, 23 => _x, 24 => _y,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }

    // =========================================================
    // Write one influence without allocating an array.
    private void Set(int index, BiomeInfluence value)
    {
        switch (index)
        {
            case 0: _a = value; break;
            case 1: _b = value; break;
            case 2: _c = value; break;
            case 3: _d = value; break;
            case 4: _e = value; break;
            case 5: _f = value; break;
            case 6: _g = value; break;
            case 7: _h = value; break;
            case 8: _i = value; break;
            case 9: _j = value; break;
            case 10: _k = value; break;
            case 11: _l = value; break;
            case 12: _m = value; break;
            case 13: _n = value; break;
            case 14: _o = value; break;
            case 15: _p = value; break;
            case 16: _q = value; break;
            case 17: _r = value; break;
            case 18: _s = value; break;
            case 19: _t = value; break;
            case 20: _u = value; break;
            case 21: _v = value; break;
            case 22: _w = value; break;
            case 23: _x = value; break;
            case 24: _y = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
    #endregion

    #region Construction
    // =========================================================
    // Merge repeated biome indices so each terrain generator is sampled once.
    public void Add(int index, float weight)
    {
        if (weight <= 0f) return;
        for (int i = 0; i < Count; i++)
        {
            BiomeInfluence entry = Get(i);
            if (entry.Index != index) continue;
            Set(i, new BiomeInfluence(index, entry.Weight + weight));
            return;
        }

        if (Count >= 25)
            throw new InvalidOperationException("Biome blend capacity exceeded.");
        Set(Count, new BiomeInfluence(index, weight));
        Count++;
    }

    // =========================================================
    // Normalize contributions and choose a deterministic dominant biome.
    public void Normalize()
    {
        float total = 0f;
        for (int i = 0; i < Count; i++) total += Get(i).Weight;
        if (total <= 0f)
            throw new InvalidOperationException("Biome blend has no influence.");

        float strongest = -1f;
        for (int i = 0; i < Count; i++)
        {
            BiomeInfluence entry = Get(i);
            float weight = entry.Weight / total;
            Set(i, new BiomeInfluence(entry.Index, weight));
            if (weight > strongest ||
                (weight == strongest && entry.Index < DominantIndex))
            {
                strongest = weight;
                DominantIndex = entry.Index;
            }
        }
    }
    #endregion

    #region Population
    // =========================================================
    // Choose one contributing biome for a population candidate or patch.
    public readonly int Pick(RandomNumberGenerator rng)
    {
        float roll = rng.Randf();
        for (int i = 0; i < Count; i++)
        {
            BiomeInfluence entry = Get(i);
            roll -= entry.Weight;
            if (roll <= 0f) return entry.Index;
        }
        return Get(Count - 1).Index;
    }
    #endregion
}