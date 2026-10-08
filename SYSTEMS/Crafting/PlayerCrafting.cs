// Owns the player's timed hand-crafting queue.
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
    private double _tick;
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

    #region Queue
    // =========================================================
    // Add a bounded batch after checking its current ingredient requirements.
    public bool TryQueue(CraftingRecipe recipe, int quantity)
    {
        if (recipe == null || !Catalog.Recipes.Contains(recipe) ||
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