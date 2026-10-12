// Owns persisted hand jobs and one transient attended station batch.
// Unfinished ingredients stay in the backpack until a complete exchange succeeds.
using Godot;
using System;
using System.Collections.Generic;

public partial class PlayerCrafting : Node
{
    #region Configuration
    [Export] public CraftingCatalog Catalog { get; set; }

    [Export(PropertyHint.Range, "1,32,1")]
    public int MaximumQueueEntries { get; set; } = 8;

    [Export(PropertyHint.Range, "1,100,1")]
    public int MaximumBatchSize { get; set; } = 100;
    #endregion

    #region State
    public sealed class CraftJob
    {
        public CraftingRecipe Recipe { get; }
        public int Remaining { get; internal set; }
        public double Elapsed { get; internal set; }

        public double Progress => Math.Clamp(
            Elapsed / Recipe.DurationSeconds, 0.0, 1.0);

        public CraftJob(CraftingRecipe recipe, int quantity)
        {
            Recipe = recipe;
            Remaining = quantity;
        }
    }

    public IReadOnlyList<CraftJob> Jobs => _jobs;
    public ItemCatalog Items { get; private set; }
    public string Status { get; private set; } = "Choose a recipe.";

    private readonly List<CraftJob> _jobs = new();
    private PlayerInventory _inventory;
    private Health _health;
    private double _tick, _stationTick;
    private CraftJob _stationJob;
    private StationCrafting _station;
    public CraftJob StationJob => _stationJob;
    public StationCrafting ActiveStation => _station;
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve player components and validate the configured recipe catalog.
    public override void _Ready()
    {
        _inventory = GetParent().GetNode<PlayerInventory>("Inventory");
        _health = GetParent().GetNode<Health>("Health");

        Items = ResourceLoader.Load<ItemCatalog>(
            "res://ITEMS/ItemCatalog.tres");

        if (Items == null || Catalog == null)
            throw new InvalidOperationException(
                "PlayerCrafting requires item and crafting catalogs.");

        Items.Initialize();
        Catalog.Validate(Items);
    }

    // =========================================================
    // Advance the current recipe while the player is alive.
    public override void _Process(double delta)
    {
        if (_stationJob != null)
        {
            AdvanceStation(delta);
            return;
        }
        if (_jobs.Count == 0 || _health.Current <= 0)
        {
            _tick = 0.0;
            return;
        }

        _tick += delta;
        if (_tick < 0.1) return;

        double elapsed = _tick;
        _tick = 0.0;

        CraftJob job = _jobs[0];
        if (!job.Recipe.IsHandCraftable)
        {
            _jobs.RemoveAt(0);
            Status = "Station recipes cannot execute from a hand-crafting job.";
            return;
        }

        job.Elapsed = Math.Min(
            job.Recipe.DurationSeconds, job.Elapsed + elapsed);

        if (job.Elapsed < job.Recipe.DurationSeconds)
        {
            Status = $"Crafting {job.Recipe.Output.DisplayName}...";
            return;
        }

        if (!_inventory.TryCraft(job.Recipe, out string reason))
        {
            Status = reason;
            return;
        }

        job.Remaining--;
        job.Elapsed = 0.0;

        if (job.Remaining == 0)
            _jobs.RemoveAt(0);

        Status = $"Crafted {job.Recipe.Output.DisplayName}" +
            $" ×{job.Recipe.OutputCount}.";
    }
    #endregion

    #region Attended Station Work
    // =========================================================
    // Queue one manual batch only through the exact attended station and canonical recipe.
    public bool TryQueueAtStation(StationCrafting station, CraftingRecipe recipe, int quantity)
    {
        if (recipe == null || !GodotObject.IsInstanceValid(station))
        { Status = "A valid workstation is required."; return false; }
        Player player = GetParent().GetParent<Player>();
        StationCraftingHud hud = player.GetNodeOrNull<StationCraftingHud>("StationCraftingHud");
        if (!GodotObject.IsInstanceValid(station) || !GodotObject.IsInstanceValid(hud) ||
            !hud.CanWorkAt(station) || !station.CanCraft(player, recipe) || !Catalog.Recipes.Contains(recipe) ||
            quantity < 1 || quantity > MaximumBatchSize || _stationJob != null)
        { Status = "A suitable attended workstation and an idle station batch are required."; return false; }
        if (!_inventory.HasCraftingIngredients(recipe, quantity))
        { Status = "Not enough ingredients in your backpack."; return false; }
        _station = station; _stationJob = new CraftJob(recipe, quantity); _stationTick = 0;
        Status = "Station batch queued."; return true;
    }

    // =========================================================
    // Cancel unpaid unfinished operations; completed outputs already belong to inventory.
    public void CancelStation(StationCrafting station)
    {
        if (_station != station) return;
        _stationJob = null; _station = null; _stationTick = 0;
        Status = "Unfinished station work cancelled; ingredients retained.";
    }

    // =========================================================
    // Use the same timed job model and atomic inventory exchange, rechecking attendance at payout.
    private void AdvanceStation(double delta)
    {
        Player player = GetParent().GetParent<Player>();
        StationCraftingHud hud = player.GetNodeOrNull<StationCraftingHud>("StationCraftingHud");
        if (!GodotObject.IsInstanceValid(_station) || !GodotObject.IsInstanceValid(hud) ||
            !hud.IsAttending(_station) || !_station.CanCraft(player, _stationJob.Recipe))
        { CancelStation(_station); return; }
        if (!hud.CanWorkAt(_station)) return;
        _stationTick += delta;
        if (_stationTick < 0.1) return;
        CraftJob job = _stationJob;
        job.Elapsed = Math.Min(job.Recipe.DurationSeconds, job.Elapsed + _stationTick); _stationTick = 0;
        if (job.Elapsed < job.Recipe.DurationSeconds)
        { Status = $"Assembling {job.Recipe.Output.DisplayName}..."; return; }
        if (!_inventory.TryCraftAtStation(job.Recipe, _station, out string reason))
        { Status = reason; return; }
        job.Remaining--; job.Elapsed = 0;
        if (job.Remaining == 0) { _stationJob = null; _station = null; }
        Status = $"Assembled {job.Recipe.Output.DisplayName} ×{job.Recipe.OutputCount}.";
    }
    #endregion

    #region Queue
    // =========================================================
    // Add a bounded batch after checking its current ingredient requirements.
    public bool TryQueue(CraftingRecipe recipe, int quantity)
    {
        if (recipe == null || !recipe.IsHandCraftable || !Catalog.Recipes.Contains(recipe) ||
            quantity < 1 || quantity > MaximumBatchSize)
        {
            Status = "Invalid crafting quantity or recipe.";
            return false;
        }

        if (_health.Current <= 0)
        {
            Status = "Cannot craft while dead.";
            return false;
        }

        if (_jobs.Count >= MaximumQueueEntries)
        {
            Status = "The crafting queue is full.";
            return false;
        }

        if (!_inventory.HasCraftingIngredients(recipe, quantity))
        {
            Status = "Not enough ingredients in your backpack.";
            return false;
        }

        _jobs.Add(new CraftJob(recipe, quantity));
        Status = $"Queued {recipe.Output.DisplayName} ×" +
            $"{(long)recipe.OutputCount * quantity}.";
        return true;
    }

    // =========================================================
    // Cancel unfinished operations without removing or refunding any ingredients.
    public void Cancel(int index)
    {
        if (index < 0 || index >= _jobs.Count) return;

        string name = _jobs[index].Recipe.Output.DisplayName;
        _jobs.RemoveAt(index);

        if (index == 0) _tick = 0.0;

        Status = $"Cancelled {name}.";
    }
    #endregion
}
