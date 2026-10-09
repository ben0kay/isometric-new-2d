// Shows compact, stacked top-left notifications that slide in from the left.
// Same-key positive-quantity messages are combined and their lifetime refreshed.
using Godot;
using System;
using System.Collections.Generic;

public partial class ToastDisplay : Control
{
    #region Layout
    public int VisibleLimit { get; set; } = 3;
    public float Width { get; set; } = 368f;
    public float Height { get; set; } = 76f;
    public float TopMargin { get; set; } = 28f;
    public float LeftMargin { get; set; } = 18f;
    public float Spacing { get; set; } = 10f;
    public float MotionSeconds { get; set; } = 0.26f;
    #endregion

    #region State
    private sealed class ActiveToast
    {
        public ToastNotification Notice;
        public Control Card;
        public Label Title;
        public Tween Lifetime;
        public bool Closing;
    }

    private readonly List<ActiveToast> _visible = new();
    #endregion

    #region Lifecycle
    // =========================================================
    // This full-screen presentation layer never consumes pointer input.
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }
    #endregion

    #region Notifications
    // =========================================================
    // Show, merge or replace a toast without creating an unbounded HUD stack.
    public void Push(ToastNotification notice)
    {
        if (notice == null || string.IsNullOrWhiteSpace(notice.Title)) return;
        if (string.IsNullOrWhiteSpace(notice.Key))
            notice.Key = Guid.NewGuid().ToString("N");

        ActiveToast existing = _visible.Find(x =>
            !x.Closing && x.Notice.Key == notice.Key);

        if (existing != null)
        {
            if (existing.Notice.Amount > 0 && notice.Amount > 0 &&
                existing.Notice.Title == notice.Title &&
                existing.Notice.Tone == notice.Tone)
            {
                existing.Notice.Amount += notice.Amount;
                existing.Title.Text = FormatTitle(existing.Notice);
            }
            else
            {
                existing.Notice = notice;
                existing.Title.Text = FormatTitle(notice);
            }
            RestartLifetime(existing);
            return;
        }

        if (_visible.Count >= Mathf.Max(1, VisibleLimit))
            Dismiss(_visible[0]);

        ActiveToast entry = BuildToast(notice);
        _visible.Add(entry);
        entry.Card.Position = new Vector2(-entry.Card.Size.X, TargetY(_visible.Count - 1));
        Tween entrance = entry.Card.CreateTween();
        entrance.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        entrance.TweenProperty(entry.Card, "position:x", LeftMargin, MotionSeconds);
        RestartLifetime(entry);
    }

    // =========================================================
    // Create one lightweight hexagonal banner and its two text lines.
    private ActiveToast BuildToast(ToastNotification notice)
    {
        float width = Mathf.Min(Width, Mathf.Max(240f,
            GetViewport().GetVisibleRect().Size.X - 24f));
        Control card = new()
        {
            Name = "Toast",
            Size = new Vector2(width, Height),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(card);

        Color accent = AccentFor(notice.Tone);
        NotificationFrame frame = new()
        {
            Accent = accent,
            Fill = new Color(0.025f, 0.075f, 0.10f, 0.83f)
        };
        card.AddChild(frame);
        frame.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        Label symbol = new()
        {
            Text = notice.Tone == ToastTone.Warning ? "!" :
                notice.Tone == ToastTone.Crafted ? ">" : "+",
            Position = new Vector2(15f, 23f),
            Size = new Vector2(34f, 28f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        symbol.AddThemeFontSizeOverride("font_size", 19);
        symbol.AddThemeColorOverride("font_color", accent);
        card.AddChild(symbol);

        Label title = TextLabel(FormatTitle(notice), 18, new Color("#d7edf0"));
        title.Position = new Vector2(65f, 13f);
        title.Size = new Vector2(width - 98f, 29f);
        card.AddChild(title);

        Label detail = TextLabel(notice.Detail, 12, new Color("#9bb9c1"));
        detail.Position = new Vector2(65f, 43f);
        detail.Size = new Vector2(width - 98f, 21f);
        card.AddChild(detail);

        return new ActiveToast { Notice = notice, Card = card, Title = title };
    }

    // =========================================================
    // Restart an existing toast's life when another matching pickup arrives.
    private void RestartLifetime(ActiveToast entry)
    {
        if (entry.Lifetime != null && entry.Lifetime.IsRunning())
            entry.Lifetime.Kill();

        entry.Lifetime = entry.Card.CreateTween();
        entry.Lifetime.TweenInterval(Mathf.Max(0.5f, entry.Notice.DurationSeconds));
        entry.Lifetime.TweenCallback(Callable.From(() => Dismiss(entry)));
    }

    // =========================================================
    // Slide a toast away, then compact the surviving stack.
    private void Dismiss(ActiveToast entry)
    {
        if (entry == null || entry.Closing) return;
        entry.Closing = true;
        if (entry.Lifetime != null && entry.Lifetime.IsRunning())
            entry.Lifetime.Kill();

        _visible.Remove(entry);
        Tween exit = entry.Card.CreateTween().SetParallel(true);
        exit.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        exit.TweenProperty(entry.Card, "position:x", -entry.Card.Size.X, 0.22f);
        exit.TweenProperty(entry.Card, "modulate:a", 0f, 0.22f);
        exit.Chain().TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(entry.Card)) entry.Card.QueueFree();
        }));

        Reflow();
    }

    // =========================================================
    // Animate surviving banners to their new vertical positions.
    private void Reflow()
    {
        for (int i = 0; i < _visible.Count; i++)
        {
            ActiveToast entry = _visible[i];
            Tween reposition = entry.Card.CreateTween();
            reposition.SetTrans(Tween.TransitionType.Cubic)
                .SetEase(Tween.EaseType.Out);
            reposition.TweenProperty(entry.Card, "position:y", TargetY(i), 0.2f);
        }
    }

    private float TargetY(int index) => TopMargin + index * (Height + Spacing);

    private static string FormatTitle(ToastNotification notice)
    {
        if (notice.Amount <= 0) return notice.Title;
        string suffix = notice.Tone == ToastTone.Crafted ? " ×" : " +";
        return notice.Title + suffix + notice.Amount;
    }

    // =========================================================
    // Share the notification colour language across the small banners.
    private static Color AccentFor(ToastTone tone) => tone switch
    {
        ToastTone.Crafted => new Color("#80ca92"),
        ToastTone.Warning => new Color("#f0b36c"),
        ToastTone.Inventory => new Color("#68dce2"),
        _ => new Color("#7ba9e6")
    };

    private static Label TextLabel(string text, int size, Color colour)
    {
        Label label = new()
        {
            Text = text ?? "",
            ClipText = true,
            MouseFilter = MouseFilterEnum.Ignore
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour);
        return label;
    }
    #endregion
}
