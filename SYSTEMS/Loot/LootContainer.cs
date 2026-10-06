// Adds seeded, take-only loot to the existing world storage component.
// Contents are retained by LootWorld independently from the visible wreck.
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
    // Restore existing session contents or generate this container once.
    public override void _Ready()
    {
        base._Ready();

        if (Table == null)
            throw new InvalidOperationException("LootContainer requires a LootTable.");

        _world = LootWorld.Find(this);
        if (_world == null)
            throw new InvalidOperationException(
                "Add LootWorld.tscn under the world's Systems node.");

        _key = string.IsNullOrWhiteSpace(PersistentId)
            ? $"placed:{Host.GetPath()}" : PersistentId;

        _contents = _world.GetContents(_key, Table, Definition);
        PublishContents();
    }

    // =========================================================
    // Retain committed contents before refreshing the shared UI.
    protected override void PublishContents()
    {
        if (_world != null && _key != null)
            _world.StoreContents(_key, _contents);
        base.PublishContents();
    }
    #endregion
}