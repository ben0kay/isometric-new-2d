// Shared presentation attachment for health bars and future world feedback effects.
// Render under elevated artwork so no extra transform polling is required.
using Godot;
using System;

public partial class WorldFeedback : Node
{
    #region Configuration
    private const string DefaultPath =
        "res://VISUALS/Feedback/HealthBars/DefaultHealthBarSettings.tres";
    private static HealthBarSettings _defaultSettings;
    private HealthBar _bar;
    #endregion

    #region Binding
    // =========================================================
    // Bind a generic Health source to any actor's visual root and its visual bounds.
    public void Bind(Health health, Node2D visual, Rect2 visualBounds,
        float visualScale = 1f, HealthBarSettings overrideSettings = null)
    {
        if (!GodotObject.IsInstanceValid(health) || !GodotObject.IsInstanceValid(visual))
        {
            GD.PushWarning("WorldFeedback needs a valid health component and visual root.");
            return;
        }

        HealthBarSettings settings = overrideSettings ??
            (_defaultSettings ??= GD.Load<HealthBarSettings>(DefaultPath));
        if (settings == null)
        {
            GD.PushWarning("WorldFeedback: default health bar settings are missing.");
            return;
        }

        try { settings.Validate(); }
        catch (Exception ex)
        {
            GD.PushWarning($"WorldFeedback disabled an invalid health bar: {ex.Message}");
            return;
        }

        if (!settings.Enabled) return;
        float centreX = (visualBounds.Position.X + visualBounds.Size.X * 0.5f) * visualScale;
        float topY = visualBounds.Position.Y * visualScale;

        _bar = new HealthBar
        {
            Name = "HealthBar",
            Position = new Vector2(centreX, topY - settings.VerticalGap - settings.BarHeight * 0.5f),
            ZIndex = 4
        };
        visual.AddChild(_bar);
        _bar.Bind(health, settings);
        SetFacing(visual.Scale.X);
    }

    // =========================================================
    // Counter the visual's horizontal mirroring to keep HP filling left-to-right.
    public void SetFacing(float visualFacing)
    {
        if (GodotObject.IsInstanceValid(_bar))
            _bar.Scale = new Vector2(visualFacing < 0f ? -1f : 1f, 1f);
    }
    #endregion
}
