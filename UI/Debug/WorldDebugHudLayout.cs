// Stacks world diagnostics at the top right, away from player health.
// Reuses existing labels and updates layout at a modest refresh rate.
using Godot;
using System.Collections.Generic;

public partial class WorldDebugHudLayout : Node
{
    #region Configuration
    [Export] public float Margin { get; set; } = 16f;
    [Export] public float LabelSpacing { get; set; } = 8f;
    #endregion

    #region State
    private readonly List<Label> _labels = new();
    private double _timer;
    #endregion

    #region Lifecycle
    // =========================================================
    // Refresh positions after text changes or viewport resizing.
    public override void _Process(double delta)
    {
        _timer -= delta;
        if (_timer > 0) return;
        _timer = 0.25;

        _labels.Clear();
        Node hud = GetParent();

        AddLabel(hud.GetNodeOrNull<Label>("FPS"));
        AddLabel(hud.GetNodeOrNull<Label>("ChunkInfo"));

        WorldLayerController layers = WorldLayerController.Find(this);
        CanvasLayer layerHud =
            layers?.GetNodeOrNull<CanvasLayer>("LayerHUD");

        if (layerHud != null)
            foreach (Node child in layerHud.GetChildren())
                if (child is Label label) AddLabel(label);

        float right = GetViewport().GetVisibleRect().Size.X - Margin;
        float top = Margin;

        foreach (Label label in _labels)
        {
            label.HorizontalAlignment = HorizontalAlignment.Right;
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
            label.SetAnchorsPreset(Control.LayoutPreset.TopLeft);

            Vector2 size = label.GetCombinedMinimumSize();
            label.Size = size;
            label.Position = new Vector2(right - size.X, top);
            top += size.Y + LabelSpacing;
        }
    }
    #endregion

    #region Labels
    // =========================================================
    // Include each valid diagnostic label once.
    private void AddLabel(Label label)
    {
        if (GodotObject.IsInstanceValid(label) && !_labels.Contains(label))
            _labels.Add(label);
    }
    #endregion
}