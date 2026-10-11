// Draws one lightweight cyan action wheel with a real clickable centre.
using Godot;
using System;
public partial class InteractionRadialHud : Control
{
    public event Action Activate;
    private Button _button;
    public bool ContainsCursor => Visible && GetGlobalRect().HasPoint(GetGlobalMousePosition());
    // =========================================================
    // Build once; the backdrop consumes mouse input without claiming movement.
    public override void _Ready()
    {
        Size = new Vector2(116, 116); MouseFilter = MouseFilterEnum.Stop;
        _button = new Button
        {
            Text = "Interact", Position = new Vector2(20, 36), Size = new Vector2(76, 44),
            FocusMode = FocusModeEnum.None, ActionMode = BaseButton.ActionModeEnum.Press
        };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.24f, 0.28f, 0.78f),
            BorderColor = new Color(0.35f, 0.9f, 1f, 0.6f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 20, CornerRadiusTopRight = 20,
            CornerRadiusBottomLeft = 20, CornerRadiusBottomRight = 20
        };
        _button.AddThemeStyleboxOverride("normal", style);
        _button.AddThemeStyleboxOverride("hover", style);
        _button.AddThemeStyleboxOverride("pressed", style);
        _button.AddThemeFontSizeOverride("font_size", 13);
        AddChild(_button); _button.Pressed += () => Activate?.Invoke(); Hide();
    }
    // =========================================================
    // Use a faint single-action circle; no fake secondary actions.
    public override void _Draw()
    {
        DrawCircle(new Vector2(58, 58), 55, new Color(0.1f, 0.6f, 0.7f, 0.12f));
        DrawArc(new Vector2(58, 58), 55, 0, Mathf.Tau, 48,
            new Color(0.4f, 0.9f, 1f, 0.35f), 1.5f, true);
    }
    // =========================================================
    // Follow the visual centre in screen coordinates and clamp to viewport margins.
    public void Present(Vector2 screenAnchor, string label)
    {
        _button.Text = label;
        Vector2 limit = GetViewportRect().Size - Size - Vector2.One * 8;
        Position = (screenAnchor - Size / 2).Clamp(Vector2.One * 8, limit.Max(Vector2.One * 8));
        Show();
    }
}
