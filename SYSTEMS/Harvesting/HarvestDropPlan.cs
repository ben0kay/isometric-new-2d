// Evaluates a compiled profile only at harvest completion using a reproducible source seed.
// Holds item resources and scalar settings, never live world-object references.
using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

public sealed class HarvestDropPlan
{
    #region Data
    public readonly record struct Entry(ItemDefinition Item, int Minimum, int Maximum, double Chance);
    public readonly record struct Reward(ItemDefinition Item, int Count);
    private readonly Entry[] _entries;

    public HarvestDropPlan(Entry[] entries) { _entries = entries; }
    #endregion

    #region Evaluation
    // =========================================================
    // Scope the seed to the campaign, layer/source identity and persistent species path.
    public static ulong Seed(uint worldSeed, string identity, string definitionPath)
    {
        if (string.IsNullOrWhiteSpace(identity) || string.IsNullOrWhiteSpace(definitionPath))
            throw new InvalidOperationException("Harvest randomness requires persistent source identity.");
        string key = "harvest-v1\n" + worldSeed.ToString(CultureInfo.InvariantCulture) +
            "\n" + identity + "\n" + definitionPath;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return BinaryPrimitives.ReadUInt64LittleEndian(hash);
    }

    // =========================================================
    // Roll each entry independently; zero rewards is a successful optional-only harvest.
    public IReadOnlyList<Reward> Roll(ulong seed)
    {
        StableRandom rng = new(seed);
        List<Reward> rewards = new();
        foreach (Entry entry in _entries)
        {
            if (entry.Chance <= 0.0) continue;
            if (entry.Chance < 1.0 && rng.Unit() >= entry.Chance) continue;
            int count = entry.Minimum == entry.Maximum ? entry.Minimum :
                entry.Minimum + rng.Bounded(entry.Maximum - entry.Minimum + 1);
            rewards.Add(new Reward(entry.Item, count));
        }
        return rewards;
    }
    #endregion

    #region Stable Randomness
    // A defined SplitMix64 stream avoids runtime-dependent Random/GetHashCode behavior.
    private struct StableRandom
    {
        private ulong _state;
        public StableRandom(ulong seed) { _state = seed; }

        // =========================================================
        // Advance one deterministic unsigned 64-bit value with intentional wraparound.
        private ulong Next()
        {
            unchecked
            {
                ulong z = (_state += 0x9E3779B97F4A7C15UL);
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        // =========================================================
        // Return a 53-bit fraction strictly below one.
        public double Unit() => (Next() >> 11) * (1.0 / 9007199254740992.0);

        // =========================================================
        // Rejection sampling preserves uniform integer quantities without modulo bias.
        public int Bounded(int range)
        {
            ulong width = (ulong)range;
            ulong threshold = unchecked(0UL - width) % width;
            ulong value;
            do { value = Next(); } while (value < threshold);
            return (int)(value % width);
        }
    }
    #endregion
}
