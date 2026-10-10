// Compiles ore references once and prepares reproducible complete extraction batches.
// Deposit progress, not transient node IDs or time, identifies each bonus roll.
using System;
using System.Collections.Generic;
using System.Globalization;

public sealed class OreBatchPlan
{
    private readonly ItemDefinition _primary;
    private readonly HarvestDropPlan _bonus;
    public OreBatchPlan(OreDefinition definition, ItemCatalog catalog)
    {
        if (definition == null || catalog == null || definition.TotalUnits < 1 ||
            definition.TotalUnits > 100000 || definition.UnitsPerBatch < 1 ||
            definition.UnitsPerBatch > 100000 || !float.IsFinite(definition.WorkPerBatch) ||
            definition.WorkPerBatch <= 0 || definition.RequiredMiningStrength < 1)
            throw new InvalidOperationException("Invalid ore extraction settings.");
        string id = definition.YieldItemId;
        if (definition.YieldItem != null)
        {
            if (!string.IsNullOrWhiteSpace(id) && id != definition.YieldItem.Id)
                throw new InvalidOperationException("Ore YieldItem and YieldItemId disagree.");
            id = definition.YieldItem.Id;
        }
        _primary = catalog.Get(id ?? "") ??
            throw new InvalidOperationException($"Unknown ore yield: '{id}'.");
        if (definition.YieldItem != null && definition.YieldItem != _primary)
            throw new InvalidOperationException("Ore YieldItem must be the canonical catalog resource.");
        if (definition.BonusDrops != null)
        {
            // Keeps the complete payout within ResourceWorld's 64-entry limit.
            if (definition.BonusDrops.Drops == null || definition.BonusDrops.Drops.Count > 63)
                throw new InvalidOperationException("Ore supports at most 63 bonus entries.");
            _bonus = definition.BonusDrops.Compile(catalog);
            foreach (HarvestDropEntry entry in definition.BonusDrops.Drops)
                if (entry.ItemId == _primary.Id)
                    throw new InvalidOperationException("Ore bonuses must not duplicate the primary yield.");
        }
    }

    public IReadOnlyList<HarvestDropPlan.Reward> Prepare(
        uint worldSeed, string identity, string path, int total, int remaining, int batchSize)
    {
        if (remaining < 1 || remaining > total || total > 100000 || batchSize < 1 || batchSize > 100000)
            throw new InvalidOperationException("Invalid ore batch progress.");
        List<HarvestDropPlan.Reward> rewards = new()
        {
            new(_primary, Math.Min(remaining, batchSize))
        };
        if (_bonus != null)
        {
            string eventId = identity + "/ore-batch/" +
                (total - remaining).ToString(CultureInfo.InvariantCulture);
            foreach (var reward in _bonus.Roll(HarvestDropPlan.Seed(worldSeed, eventId, path)))
                rewards.Add(reward);
        }
        return rewards;
    }
}
