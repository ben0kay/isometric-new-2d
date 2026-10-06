// Adds seeded, take-only loot to shared world storage and inventory controls.
// LootWorld retains contents independently from streamed container instances.
using Godot;
using System;

public partial class LootContainer : WorldStorage
{
    #region Configuration
    [ExportGroup("Loot")]
    [Export] public LootTable Table { get; set; }

    public override bool CanDeposit => false;
    private LootWorld _world;
    private string _key;
    #endregion

    #region Lifecycle
    // =========================================================
    // Restore session contents or generate this loot container once.
    public override void _Ready()
    {
        base._Ready();
        if (Table == null)
            throw new InvalidOperationException("LootContainer requires a LootTable.");

        _world = LootWorld.GetOrCreate(this);
        _key = string.IsNullOrWhiteSpace(PersistentId)
            ? $"placed:{Host.GetPath()}" : PersistentId;

        _contents = _world.GetContents(_key, Table, Definition);
        PublishContents();
    }

    // =========================================================
    // Retain committed contents before refreshing the shared inventory window.
    protected override void PublishContents()
    {
        if (GodotObject.IsInstanceValid(_world) && _key != null)
            _world.StoreContents(_key, _contents);
        base.PublishContents();
    }
    #endregion
}