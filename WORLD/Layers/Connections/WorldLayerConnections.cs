// Registers shared connection metadata and indexes it into both underground worlds.
// Surface candidate discovery and debug placement feed the same traversal records.
using Godot;
using System;
using System.Collections.Generic;

public sealed class WorldLayerConnections
{
    #region State
    private readonly WorldLayerRuntime _runtime;
    private readonly Dictionary<string, WorldLayerConnection> _records = new();
    public IEnumerable<WorldLayerConnection> All => _records.Values;
    public WorldLayerConnections(WorldLayerRuntime runtime) { _runtime = runtime; }
    #endregion

    #region Registration
    // =========================================================
    // Validate both definitions before carving either side of a connection.
    public void Register(WorldLayerConnection connection, bool rebuild = false)
    {
        if (_records.TryGetValue(connection.Id, out WorldLayerConnection existing))
        {
            if (existing == connection) return;
            throw new InvalidOperationException($"Duplicate connection '{connection.Id}'.");
        }
        WorldLayerCatalog catalog = WorldConfig.Find(_runtime).GetLayerCatalog();
        WorldLayerDefinition upper = catalog.Get(connection.UpperLayer);
        WorldLayerDefinition lower = catalog.Get(connection.LowerLayer);
        if (lower.Kind != WorldLayerKind.Underground ||
            lower.DepthIndex <= upper.DepthIndex ||
            connection.RimHeight <= lower.FloorElevation + 32f)
            throw new InvalidOperationException("Connection must descend to a deeper layer.");

        _records.Add(connection.Id, connection);
        CaveWorld destination = _runtime.GetUnderground(connection.LowerLayer);
        connection.SampleArea = connection.RampArea.Expand(
            destination.Generator.RoomCentre(connection.AnchorCell)).Grow(
                destination.Settings.MaximumTunnelWidth() * 0.5f + 2f);
        destination.AttachConnection(connection);
        CaveWorld source = _runtime.TryGetUnderground(connection.UpperLayer);
        source?.AttachDeparture(connection);
        if (!rebuild) return;
        destination.Streaming.InvalidateArea(connection.RampArea.Expand(
            destination.Generator.RoomCentre(connection.AnchorCell)).Grow(4f));
        source?.Streaming.InvalidateArea(connection.RampArea.Expand(
            connection.UpperAnchor).Grow(4f));
    }

    // =========================================================
    // Release streamed surface decisions when no terrain or traveller pins them.
    public void Remove(WorldLayerConnection connection)
    {
        if (!_records.Remove(connection.Id)) return;
        _runtime.GetUnderground(connection.LowerLayer).DetachConnection(connection);
        _runtime.TryGetUnderground(connection.UpperLayer)?.DetachDeparture(connection);
    }
    #endregion

    #region Queries
    // =========================================================
    // Enumerate connections touching an exact layer, without depth-name assumptions.
    public IEnumerable<WorldLayerConnection> ForLayer(string layer)
    {
        foreach (WorldLayerConnection connection in _records.Values)
            if (connection.UpperLayer == layer || connection.LowerLayer == layer)
                yield return connection;
    }

    // =========================================================
    // Give pursuit the next known connection along the layer graph.
    public WorldLayerConnection Next(string from, string to, Vector2 position)
    {
        if (from == to) return null;
        Queue<string> queue = new();
        Dictionary<string, WorldLayerConnection> first = new();
        HashSet<string> visited = new() { from };
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            // Prefer a nearby mouth for the first hop when several join the same depths.
            List<WorldLayerConnection> links = new(ForLayer(current));
            if (current == from) links.Sort((a, b) =>
                a.UpperPosition.DistanceSquaredTo(position).CompareTo(
                    b.UpperPosition.DistanceSquaredTo(position)));
            foreach (WorldLayerConnection link in links)
            {
                string next = link.Other(current);
                if (!visited.Add(next)) continue;
                first[next] = current == from ? link : first[current];
                if (next == to) return first[next];
                queue.Enqueue(next);
            }
        }
        return null;
    }
    #endregion
}
