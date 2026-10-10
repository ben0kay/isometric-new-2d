// Shared world-space feedback host for health bars and damage numbers.
// Each effect is independent and follows the existing elevated TerrainVisual.
using Godot;
using System;

public partial class WorldFeedback : Node
{
    #region Shared Settings And State
    private const string HealthSettingsPath =
        "res://VISUALS/Feedback/HealthBars/DefaultHealthBarSettings.tres";
    private const string DamageSettingsPath =
        "res://VISUALS/Feedback/DamageNumbers/DefaultDamageNumberSettings.tres";

    private static HealthBarSettings _defaultHealthSettings;
    private static DamageNumberSettings _defaultDamageSettings;
    private HealthBar _bar;
    private DamageNumbers _numbers;
    #endregion

    #region Binding
    // =========================================================
    // Bind separate visual effects to any Health-owning actor and its artwork.
    public void Bind(Health health, Node2D visual, Rect2 visualBounds,
        float visualScale = 1f, HealthBarSettings healthOverride = null,
        DamageNumberSettings damageOverride = null)
    {
        if (!GodotObject.IsInstanceValid(health) || !GodotObject.IsInstanceValid(visual))
        {
            GD.PushWarning("WorldFeedback needs a valid health component and visual root.");
            return;
        }

        // Feedback must follow the actual artwork, not the generous
        // SpawnVisualBounds used for gameplay placement and clearance.
        Vector2 anchor = GetArtworkTop(visual, visualBounds, visualScale);
        float centreX = anchor.X;
        float topY = anchor.Y;

        BindHealthBar(health, visual, centreX, topY, healthOverride);
        BindDamageNumbers(health, visual, centreX, topY, damageOverride);
        SetFacing(visual.Scale.X);
    }

    // =========================================================
    // Position feedback at the rendered sprite's top in TerrainVisual space.
    // Fallback bounds remain available for custom drawn Node2D artwork.
    private static Vector2 GetArtworkTop(
        Node2D visual, Rect2 visualBounds, float visualScale)
    {
        float centreX = (visualBounds.Position.X +
            visualBounds.Size.X * 0.5f) * visualScale;
        float topY = visualBounds.Position.Y * visualScale;

        if (visual.GetNodeOrNull<Sprite2D>("Artwork") is not Sprite2D sprite ||
            sprite.Texture == null)
            return new Vector2(centreX, topY);

        Rect2 region = sprite.GetRect();
        if (region.Size.X <= 0f || region.Size.Y <= 0f)
            return new Vector2(centreX, topY);

        // Sprite rect includes its anchor/Offset; Transform includes artwork
        // scale, position and rotation. Sample all four transformed corners.
        Transform2D transform = sprite.Transform;
        Vector2 a = transform * region.Position;
        Vector2 b = transform * new Vector2(region.End.X, region.Position.Y);
        Vector2 c = transform * region.End;
        Vector2 d = transform * new Vector2(region.Position.X, region.End.Y);

        float left = Mathf.Min(Mathf.Min(a.X, b.X), Mathf.Min(c.X, d.X));
        float right = Mathf.Max(Mathf.Max(a.X, b.X), Mathf.Max(c.X, d.X));
        topY = Mathf.Min(Mathf.Min(a.Y, b.Y), Mathf.Min(c.Y, d.Y));
        return new Vector2((left + right) * 0.5f, topY);
    }

    // =========================================================
    // Keep health-bar validation separate so it cannot disable damage numbers.
    private void BindHealthBar(Health health, Node2D visual,
        float centreX, float topY, HealthBarSettings overrideSettings)
    {
        HealthBarSettings settings = overrideSettings ??
            (_defaultHealthSettings ??= GD.Load<HealthBarSettings>(HealthSettingsPath));
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
        _bar = new HealthBar
        {
            Name = "HealthBar",
            Position = new Vector2(centreX,
                topY - settings.VerticalGap - settings.BarHeight * 0.5f +
                settings.VerticalOffset),
            ZIndex = 4
        };
        visual.AddChild(_bar);
        _bar.Bind(health, settings);
    }

    // =========================================================
    // A single, normally idle display accumulates each actor's rapid hits.
    private void BindDamageNumbers(Health health, Node2D visual,
        float centreX, float topY, DamageNumberSettings overrideSettings)
    {
        DamageNumberSettings settings = overrideSettings ??
            (_defaultDamageSettings ??= GD.Load<DamageNumberSettings>(DamageSettingsPath));
        if (settings == null)
        {
            GD.PushWarning("WorldFeedback: default damage number settings are missing.");
            return;
        }

        try { settings.Validate(); }
        catch (Exception ex)
        {
            GD.PushWarning($"WorldFeedback disabled invalid damage numbers: {ex.Message}");
            return;
        }

        if (!settings.Enabled) return;
        _numbers = new DamageNumbers
        {
            Name = "DamageNumbers",
            Position = new Vector2(centreX, topY - settings.VerticalGap),
            ZIndex = 5
        };
        visual.AddChild(_numbers);
        _numbers.Bind(health, settings);
    }

    // =========================================================
    // Undo the actor artwork's horizontal flip so indicators remain readable.
    public void SetFacing(float visualFacing)
    {
        float correction = visualFacing < 0f ? -1f : 1f;
        if (GodotObject.IsInstanceValid(_bar))
            _bar.Scale = new Vector2(correction, 1f);
        if (GodotObject.IsInstanceValid(_numbers))
            _numbers.Scale = new Vector2(correction, 1f);
    }

    // =========================================================
    // Future Options menu hook: hides or permits numbers without editing .tres.
    // The settings resource's Enabled flag still acts as the master feature switch.
    public void SetDamageNumbersEnabled(bool enabled)
    {
        if (GodotObject.IsInstanceValid(_numbers))
            _numbers.SetEnabled(enabled);
    }
    #endregion
}
