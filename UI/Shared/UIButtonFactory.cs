// Shared button construction and visual defaults for menus and future interfaces.
using Godot;
using System;

public static class UIButtonFactory
{
    public static readonly Color Accent = new("#68dce2");
    public static readonly Color Ink = new("#09171f");
    public static readonly Color TextColour = new("#d7edf0");

    #region Buttons
    // =========================================================
    // Create a keyboard-accessible button with shared interaction states.
    public static Button Create(
        string text, Action pressed, float height = 48f)
    {
        Button button = new()
        {
            Text = text,
            CustomMinimumSize = new Vector2(0, height),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand
        };

        Apply(button);
        button.Pressed += pressed;
        return button;
    }

    // =========================================================
    // Restyle existing buttons, including dialogs and future inventory UI.
    public static void Apply(Button button)
    {
        button.AddThemeFontSizeOverride("font_size", 20);
        button.AddThemeColorOverride("font_color", TextColour);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_focus_color", Colors.White);
        button.AddThemeColorOverride(
            "font_disabled_color", new Color("#61717a"));

        button.AddThemeStyleboxOverride(
            "normal", Box(Ink, new Color("#34545e")));

        button.AddThemeStyleboxOverride(
            "hover", Box(new Color("#143039"), Accent));

        button.AddThemeStyleboxOverride(
            "pressed", Box(new Color("#204752"), Accent));

        button.AddThemeStyleboxOverride(
            "disabled", Box(
                new Color("#0b141a"), new Color("#24343d")));

        StyleBoxFlat focus = Box(Colors.Transparent, Accent);
        focus.DrawCenter = false;
        button.AddThemeStyleboxOverride("focus", focus);
    }

    // =========================================================
    // Add optional artwork while retaining normal button input and focus.
    public static Button CreateMenu(
        string text, Action pressed, Texture2D artwork, float height)
    {
        Button button = Create(text + "   >", pressed, height);
        button.Alignment = HorizontalAlignment.Right;

        Panel strip = new()
        {
            MouseFilter = Control.MouseFilterEnum.Ignore
        };

        button.AddChild(strip);
        strip.SetAnchorsAndOffsetsPreset(
            Control.LayoutPreset.LeftWide);

        strip.AnchorRight = 0.35f;
        strip.OffsetLeft = 5;
        strip.OffsetRight = 0;
        strip.OffsetTop = 5;
        strip.OffsetBottom = -5;

        strip.AddThemeStyleboxOverride(
            "panel", Box(
                new Color("#16343e"), new Color("#34545e")));

        strip.ClipContents = true;

        if (artwork != null)
        {
            TextureRect image = new()
            {
                Texture = artwork,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode =
                    TextureRect.StretchModeEnum.KeepAspectCovered,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };

            strip.AddChild(image);
            image.SetAnchorsAndOffsetsPreset(
                Control.LayoutPreset.FullRect);
        }
        else
        {
            Label placeholder = Label("/ / /", 22);
            placeholder.HorizontalAlignment =
                HorizontalAlignment.Center;
            placeholder.VerticalAlignment =
                VerticalAlignment.Center;

            strip.AddChild(placeholder);
            placeholder.SetAnchorsAndOffsetsPreset(
                Control.LayoutPreset.FullRect);
        }

        return button;
    }
    #endregion

    #region Shared Appearance
    // =========================================================
    // Construct a shared panel or button surface.
    public static StyleBoxFlat Box(Color fill, Color edge)
    {
        return new StyleBoxFlat
        {
            BgColor = fill,
            BorderColor = edge,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            ContentMarginLeft = 18,
            ContentMarginRight = 18,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3
        };
    }

    // =========================================================
    // Create consistent non-interactive text for these screens.
    public static Label Label(string text, int size = 18)
    {
        Label label = new()
        {
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };

        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", TextColour);
        return label;
    }
    #endregion
}