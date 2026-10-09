// Save adapters stay beside the coordinator while each component owns its state.
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public partial class PlayerStats
{
    // =========================================================
    // Preserve base attributes and named modifier sources without editing resources.
    public void CaptureSave(PlayerSaveData data)
    {
        data.Stats = (float[])_base.Clone();
        data.Modifiers.Clear();
        foreach (var source in _sources)
            data.Modifiers[source.Key] = source.Value.Select(m => new ModifierSaveData
                { Stat = (int)m.Stat, Operation = (int)m.Operation, Amount = m.Amount }).ToList();
    }

    // =========================================================
    // Validate the entire attribute snapshot before changing calculated capacities.
    public void RestoreSave(PlayerSaveData data)
    {
        if (data.Stats == null || data.Stats.Length != _base.Length ||
            data.Stats.Any(v => !float.IsFinite(v) || v < 0) ||
            data.Modifiers == null || data.Modifiers.Count > 128)
            throw new InvalidDataException("Invalid saved player attributes.");
        Dictionary<string, ModifierValue[]> restored = new();
        foreach (var source in data.Modifiers)
        {
            if (string.IsNullOrWhiteSpace(source.Key) || source.Value == null || source.Value.Count > 128)
                throw new InvalidDataException("Invalid modifier source.");
            restored[source.Key] = source.Value.Select(m =>
            {
                if (m == null || m.Stat < 0 || m.Stat >= _base.Length ||
                    !Enum.IsDefined(typeof(StatModifierOperation), m.Operation) ||
                    !float.IsFinite(m.Amount) ||
                    (m.Operation == (int)StatModifierOperation.Multiply && m.Amount < 0))
                    throw new InvalidDataException("Invalid saved modifier.");
                return new ModifierValue((PlayerStat)m.Stat,
                    (StatModifierOperation)m.Operation, m.Amount);
            }).ToArray();
        }
        Array.Copy(data.Stats, _base, _base.Length);
        _sources.Clear();
        foreach (var source in restored) _sources.Add(source.Key, source.Value);
        Recalculate();
    }
}

public partial class PlayerVitals
{
    // =========================================================
    // Copy current reserves independently from their calculated maximum values.
    public void CaptureSave(PlayerSaveData data) { data.Reserves = (float[])_current.Clone(); }

    // =========================================================
    // Restore after attributes so capacity signals cannot replace saved reserves.
    public void RestoreSave(PlayerSaveData data)
    {
        if (data.Reserves == null || data.Reserves.Length != _current.Length ||
            data.Reserves.Any(v => !float.IsFinite(v) || v < 0) ||
            data.Health < 1 || data.Health > Health.MaxHealth)
            throw new InvalidDataException("Invalid saved vitality or reserves.");
        Health.RestoreState(data.Health);
        for (int i = 0; i < _current.Length; i++)
        {
            if (data.Reserves[i] > GetMaximum((PlayerReserve)i))
                throw new InvalidDataException("Saved reserve exceeds its capacity.");
            _current[i] = data.Reserves[i];
        }
        Changed?.Invoke();
    }
}

public partial class PlayerInventory
{
    // =========================================================
    // Copy physical slots; hotbar shortcuts are saved separately.
    public void CaptureSave(PlayerSaveData data)
    {
        data.Backpack = Equipment.Backpack?.Id ?? "";
        data.ItemResources.Clear();
        void Remember(ItemDefinition item)
        {
            if (item == null || string.IsNullOrEmpty(item.ResourcePath)) return;
            if (!item.ResourcePath.StartsWith("res://", StringComparison.Ordinal) ||
                item.ResourcePath.Contains("::"))
                throw new InvalidDataException("Save requires external item resources or catalog IDs.");
            data.ItemResources[item.Id] = item.ResourcePath;
        }
        Remember(Equipment.Backpack);
        foreach (ItemDefinition tool in Equipment.CopyTools()) Remember(tool);
        data.Tools = Equipment.CopyTools().Select(t => t?.Id ?? "").ToArray();
        data.SelectedTool = Equipment.SelectedSlot;
        data.Bag.Clear();
        for (int i = 0; i < _storage.SlotCount; i++)
        {
            InventoryStack stack = _storage.Get(i);
            Remember(stack.Item);
            data.Bag.Add(new StackSaveData { Item = stack.Item?.Id ?? "", Count = stack.Count });
        }
    }

    // =========================================================
    // Stage exact contents before committing, avoiding collection or starter grants.
    public void RestoreSave(PlayerSaveData data, ItemCatalog catalog)
    {
        if (data.ItemResources == null || data.ItemResources.Count > 128)
            throw new InvalidDataException("Invalid saved item references.");
        ItemDefinition Resolve(string id)
        {
            ItemDefinition item = catalog.Get(id);
            if (item != null) return item;
            if (!data.ItemResources.TryGetValue(id, out string path) ||
                !path.StartsWith("res://", StringComparison.Ordinal) || path.Contains("::") ||
                !path.EndsWith(".tres", StringComparison.OrdinalIgnoreCase))
                return null;
            item = ResourceLoader.Load<ItemDefinition>(path);
            if (item?.Id != id) throw new InvalidDataException("Saved item identity does not match its resource.");
            return item;
        }
        BackpackDefinition pack = string.IsNullOrEmpty(data.Backpack) ? null
            : Resolve(data.Backpack) as BackpackDefinition
                ?? throw new InvalidDataException("Saved backpack is unavailable.");
        if (data.Bag == null || data.Bag.Count != PackSlots(pack) ||
            data.Tools == null || data.Tools.Length != Equipment.ToolSlotCount ||
            data.SelectedTool < 0 || data.SelectedTool >= Equipment.ToolSlotCount)
            throw new InvalidDataException("Saved equipment layout is invalid.");
        InventoryStorage staged = new(data.Bag.Count);
        for (int i = 0; i < data.Bag.Count; i++)
        {
            StackSaveData entry = data.Bag[i] ?? throw new InvalidDataException("Missing saved slot.");
            if (string.IsNullOrEmpty(entry.Item) && entry.Count == 0) continue;
            ItemDefinition item = Resolve(entry.Item)
                ?? throw new InvalidDataException($"Saved item '{entry.Item}' is unavailable.");
            if (entry.Count < 1 || entry.Count > Math.Max(1, item.MaxStack))
                throw new InvalidDataException("Invalid saved stack quantity.");
            staged.Set(i, new InventoryStack(item, entry.Count));
        }
        ItemDefinition[] tools = new ItemDefinition[data.Tools.Length];
        for (int i = 0; i < tools.Length; i++)
        {
            if (string.IsNullOrEmpty(data.Tools[i])) continue;
            tools[i] = Resolve(data.Tools[i]);
            if (tools[i]?.Attack == null)
                throw new InvalidDataException("Saved tool is unavailable or incompatible.");
        }
        _storage = staged;
        Equipment.ApplyContents(tools, pack);
        Equipment.Select(data.SelectedTool);
        Recalculate();
    }
}

public partial class PlayerHotbar
{
    // =========================================================
    // Preserve slot references and expected items without creating additional stacks.
    public void CaptureSave(PlayerSaveData data)
    {
        data.SelectedHotbar = SelectedSlot;
        data.Hotbar.Clear();
        for (int i = 0; i < SlotCount; i++)
        {
            InventoryAddress? address = GetAddress(i);
            data.Hotbar.Add(address.HasValue ? new HotbarSaveData
            {
                Area = (int)address.Value.Area, Index = address.Value.Index,
                Item = GetStack(i).Item.Id
            } : new HotbarSaveData());
        }
    }

    // =========================================================
    // Replace starter bindings only after physical inventory restoration.
    public void RestoreSave(PlayerSaveData data)
    {
        if (data.Hotbar == null || data.Hotbar.Count != SlotCount ||
            data.SelectedHotbar < 0 || data.SelectedHotbar >= SlotCount)
            throw new InvalidDataException("Invalid saved hotbar.");
        InventoryAddress?[] bindings = new InventoryAddress?[SlotCount];
        ItemDefinition[] expected = new ItemDefinition[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            HotbarSaveData entry = data.Hotbar[i]
                ?? throw new InvalidDataException("Missing saved hotbar slot.");
            if (entry.Area == -1) continue;
            if (!Enum.IsDefined(typeof(InventoryArea), entry.Area))
                throw new InvalidDataException("Invalid hotbar address.");
            InventoryAddress address = new((InventoryArea)entry.Area, entry.Index);
            InventoryStack stack = _inventory.GetStack(address);
            if (!_inventory.HasAddress(address) || stack.IsEmpty || stack.Item.Id != entry.Item)
                throw new InvalidDataException("Saved hotbar references a missing item.");
            bindings[i] = address;
            expected[i] = stack.Item;
        }
        _bindings = bindings; _expected = expected;
        SelectedSlot = data.SelectedHotbar;
        Publish();
    }
}

public partial class PlayerCrafting
{
    // =========================================================
    // Preserve elapsed work without taking ingredients for unfinished jobs.
    public void CaptureSave(PlayerSaveData data)
    {
        data.Crafting = _jobs.Select((job, index) => new CraftSaveData
        {
            Recipe = job.Recipe.Id, Remaining = job.Remaining,
            Elapsed = Math.Min(job.Recipe.DurationSeconds,
                job.Elapsed + (index == 0 ? _tick : 0))
        }).ToList();
    }

    // =========================================================
    // Rebuild the queue directly; ordinary queueing checks are for new requests.
    public void RestoreSave(PlayerSaveData data)
    {
        if (data.Crafting == null || data.Crafting.Count > MaximumQueueEntries)
            throw new InvalidDataException("Invalid saved crafting queue.");
        List<CraftJob> staged = new();
        foreach (CraftSaveData entry in data.Crafting)
        {
            if (entry == null) throw new InvalidDataException("Missing crafting job.");
            CraftingRecipe recipe = Catalog.Recipes.FirstOrDefault(r => r.Id == entry.Recipe)
                ?? throw new InvalidDataException($"Saved recipe '{entry.Recipe}' is unavailable.");
            if (entry.Remaining < 1 || entry.Remaining > MaximumBatchSize ||
                !double.IsFinite(entry.Elapsed) || entry.Elapsed < 0 || entry.Elapsed > recipe.DurationSeconds)
                throw new InvalidDataException("Invalid saved crafting progress.");
            staged.Add(new CraftJob(recipe, entry.Remaining) { Elapsed = entry.Elapsed });
        }
        _jobs.Clear(); _jobs.AddRange(staged); _tick = 0;
        Status = "Crafting queue restored.";
    }
}

public partial class WorldClock
{
    // =========================================================
    // Restore the eclipse clock after its configuration has been initialized.
    public void RestoreSave(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            throw new InvalidDataException("Invalid saved world time.");
        ElapsedSeconds = seconds;
        UpdateCycle();
    }
}

public partial class PlayerSurvival
{
    // =========================================================
    // Keep pending survival costs and recovery delays across a reload.
    public void CaptureSave(PlayerSaveData data)
    {
        data.Survival = new[] { _elapsed, _workRemaining, _foodPending,
            _waterPending, _fatiguePending, _staminaRecoveryDelay };
        data.SprintExhausted = _sprintExhausted;
    }

    // =========================================================
    // Restore accumulated costs without resuming a held sprint input.
    public void RestoreSave(PlayerSaveData data)
    {
        if (data.Survival == null || data.Survival.Length != 6 ||
            data.Survival.Any(v => !double.IsFinite(v) || v < 0))
            throw new InvalidDataException("Invalid saved survival timing.");
        _elapsed = data.Survival[0]; _workRemaining = data.Survival[1];
        _foodPending = data.Survival[2]; _waterPending = data.Survival[3];
        _fatiguePending = data.Survival[4]; _staminaRecoveryDelay = data.Survival[5];
        _sprintExhausted = data.SprintExhausted; IsSprinting = false;
    }
}
