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
    public int Count { get; private set; }
    public int DominantIndex { get; private set; }
    #endregion

    #region Access
    // =========================================================
    // Read one contribution from the fixed-capacity value.
    public readonly BiomeInfluence Get(int index)
    {
        return index switch
        {
            0 => _a, 1 => _b, 2 => _c,
            3 => _d, 4 => _e, 5 => _f,
            6 => _g, 7 => _h, 8 => _i,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
    }

    // =========================================================
    // Write one contribution without creating a managed array.
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

        if (Count >= 9)
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