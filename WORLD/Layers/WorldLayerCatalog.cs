// Resolves authored world-layer definitions through one validated runtime index.
// IDs are indexed at startup; changing definitions requires restarting the world.
using Godot;
using System;
using System.Collections.Generic;

[Tool, GlobalClass]
public partial class WorldLayerCatalog : Resource
{
    #region Definitions
    [Export] public string SurfaceEntranceLayerId { get; set; }
        = WorldLayerId.Underground1;
    [Export] public Godot.Collections.Array<WorldLayerDefinition> Layers { get; set; }
        = new();

    private Dictionary<string, WorldLayerDefinition> _index;
    #endregion

    #region Queries
    // =========================================================
    // Build the index once and reject missing or duplicate definitions.
    public void Initialize()
    {
        if (_index != null) return;

        Dictionary<string, WorldLayerDefinition> index =
            new(StringComparer.Ordinal);

        foreach (WorldLayerDefinition layer in Layers)
        {
            if (layer == null)
                throw new InvalidOperationException("The layer catalog has a missing entry.");

            layer.Validate();
            if (!index.TryAdd(layer.Id, layer))
                throw new InvalidOperationException($"Duplicate layer ID: '{layer.Id}'.");
        }

        if (!index.ContainsKey(WorldLayerId.Surface))
            throw new InvalidOperationException("The layer catalog requires 'surface'.");

        if (!index.TryGetValue(SurfaceEntranceLayerId, out WorldLayerDefinition entry) ||
            entry.Kind != WorldLayerKind.Underground)
            throw new InvalidOperationException(
                "SurfaceEntranceLayerId must identify an underground layer.");

        _index = index;
    }

    // =========================================================
    // Require an exact identity rather than silently selecting another layer.
    public WorldLayerDefinition Get(string id)
    {
        Initialize();
        WorldLayerId.Validate(id);

        return _index.TryGetValue(id, out WorldLayerDefinition layer)
            ? layer : throw new InvalidOperationException($"Unknown world layer: '{id}'.");
    }
    #endregion
}
