// Optional world_infinite test placement for Upper Caverns -> Deep Caverns.
// Remove this scene node and restart to remove the test connection entirely.
using Godot;
using System;

public partial class DeepCavernsTest : Node
{
    #region Configuration
    [ExportGroup("Test Connection")]
    [Export] public bool Enabled { get; set; } = true;
    [Export] public string UpperLayerId { get; set; } = "underground_1";
    [Export] public string LowerLayerId { get; set; } = "underground_2";
    [Export] public float BaseTunnelLengthTiles { get; set; } = 24f;
    [Export] public float ApproachExtraTiles { get; set; } = 4f;
    #endregion

    #region State
    private WorldLayerController _layers;
    private WorldLayerConnection _connection;
    private double _timer;
    #endregion

    #region Lifecycle
    // =========================================================
    // Keep placement separate from core generation and optional per scene.
    public override void _Ready() { SetProcess(Enabled); }

    // =========================================================
    // Add the test near the first surface entrance actually used in this run.
    public override void _Process(double delta)
    {
        _timer -= delta;
        if (_timer > 0) return;
        _timer = 0.2;
        _layers ??= WorldLayerController.Find(this);
        if (_layers?.Worlds == null) return;
        _layers.LayerChanged += OnLayerChanged;
        SetProcess(false);
        TryPlace();
    }

    // =========================================================
    // React to the shared layer notification instead of polling throughout gameplay.
    private void OnLayerChanged(string from, string to, WorldLayerConnection connection)
    {
        TryPlace();
    }

    // =========================================================
    // Reserve both sides before generating the test branch's terrain.
    private void TryPlace()
    {
        if (!Enabled || _connection != null || _layers.Current != UpperLayerId ||
            _layers.LastSurfaceConnection == null) return;
        if (!float.IsFinite(BaseTunnelLengthTiles) || !float.IsFinite(ApproachExtraTiles) ||
            ApproachExtraTiles < 3f)
            throw new InvalidOperationException("Invalid DeepCavernsTest settings.");
        WorldLayerRuntime runtime = _layers.Worlds;
        CaveWorld upper = runtime.GetUnderground(UpperLayerId);
        CaveWorld lower = runtime.GetUnderground(LowerLayerId);
        WorldLayerConnection surface = _layers.LastSurfaceConnection;
        if (surface.LowerLayer != UpperLayerId)
            throw new InvalidOperationException("Test upper layer must join the selected surface entrance.");
        Vector2 anchor = upper.Generator.RoomCentre(surface.AnchorCell);
        // Place outside the existing chamber, away from its surface ramp.
        Vector2 direction = Vector2.Right;
        Vector2 mouth = anchor + direction * Mathf.Ceil(
            upper.Settings.MaximumChamberRadius() + ApproachExtraTiles);
        float length = WorldLayerConnection.LengthFor(WorldConfig.Find(this), BaseTunnelLengthTiles);
        Vector2 end = mouth + direction * length;
        Vector2I lowerAnchor = lower.Generator.RoomCell(end + direction * (
            lower.Settings.MaximumChamberRadius() + lower.Settings.CellSpacingTiles + 8f));
        float height = upper.Generator.VertexHeight(mouth);
        _connection = new WorldLayerConnection($"TEST_DEEP_{surface.Id}",
            UpperLayerId, LowerLayerId, mouth, direction, upper.TileToWorld(mouth),
            height, length, lowerAnchor, anchor);
        runtime.Connections.Register(_connection, rebuild: true);
        if (_connection.Marker != null)
        {
            _connection.Marker.ShowDebugMarker = true;
            _connection.Marker.QueueRedraw();
        }
        GD.Print($"[DeepCavernsTest] Follow the chamber's right-hand passage to " +
            $"{lower.Definition.DisplayName}. Connection {_connection.Id}, " +
            $"mouth {_connection.UpperPosition}, length {length:0.00} tiles.");
        SetProcess(false);
    }

    // =========================================================
    // Release the debug record when the optional node is removed.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_layers)) _layers.LayerChanged -= OnLayerChanged;
        if (_connection != null && GodotObject.IsInstanceValid(_layers) &&
            GodotObject.IsInstanceValid(_layers.Worlds) && _layers.Worlds.IsInsideTree())
            _layers.Worlds.Connections.Remove(_connection);
    }
    #endregion
}
