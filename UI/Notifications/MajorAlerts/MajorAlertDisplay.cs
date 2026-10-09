// Renders one cinematic-but-subtle major alert at the top centre of the screen.
// The manager owns priority, cooldown and the pending queue.
using Godot;
using System;

public partial class MajorAlertDisplay : Control
{
    #region Layout
    public float PanelWidth { get; set; } = 600f;
    public float PanelHeight { get; set; } = 128f;
    public float TopMargin { get; set; } = 26f;
    public float MotionSeconds { get; set; } = 0.34f;
    #endregion

    #region State
    private Control _card;
    private Label _title, _description, _footer;
    private Action _completed;
    public bool IsPlaying { get; private set; }
    #endregion

    #region Lifecycle
    // =========================================================
    // Ignore mouse input so alerts never interfere with gameplay.
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        GetViewport().SizeChanged += FitCard;
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= FitCard;
    }
    #endregion

    #region Display
    // =========================================================
    // Display one alert, animate its arrival, then automatically dismiss it.
    public void Play(MajorAlertDefinition alert, Action completed)
    {
        if (alert == null || IsPlaying) return;

        IsPlaying = true;
        _completed = completed;
        Color accent = AccentFor(alert.Tone);

        _card = new Control
        {
            Name = "ActiveMajorAlert",
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_card);

        NotificationFrame frame = new()
        {
            Fill = new Color(0.025f, 0.072f, 0.095f, 0.89f),
            Accent = accent
        };
        _card.AddChild(frame);
        frame.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        Label badge = new()
        {
            Text = string.IsNullOrWhiteSpace(alert.BadgeText) ? "!" : alert.BadgeText,
            Position = new Vector2(13, 43),
            Size = new Vector2(38, 35),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        badge.AddThemeFontSizeOverride("font_size", 23);
        badge.AddThemeColorOverride("font_color", accent);
        _card.AddChild(badge);

        _title = MakeLabel(alert.Title, 23, accent);
        _description = MakeLabel(alert.Description, 16, new Color("#d7edf0"));
        _description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _footer = MakeLabel(alert.Footer.ToUpperInvariant(), 11, new Color("#94b2bd"));

        _card.AddChild(_title);
        _card.AddChild(_description);
        _card.AddChild(_footer);
        FitCard();
        _card.Position = new Vector2(_card.Position.X, -PanelHeight);
        _card.Modulate = new Color(1f, 1f, 1f, 0f);

        Tween enter = _card.CreateTween().SetParallel(true);
        enter.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        enter.TweenProperty(_card, "position:y", TopMargin, MotionSeconds);
        enter.TweenProperty(_card, "modulate:a", 1f, MotionSeconds);

        Tween life = _card.CreateTween();
        life.TweenInterval(Mathf.Max(1f, alert.DurationSeconds));
        life.TweenCallback(Callable.From(Dismiss));
    }

    // =========================================================
    // Slide a completed alert upward and notify the manager to play the next.
    private void Dismiss()
    {
        if (!IsPlaying || !GodotObject.IsInstanceValid(_card)) return;

        Tween exit = _card.CreateTween().SetParallel(true);
        exit.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        exit.TweenProperty(_card, "position:y", -PanelHeight, 0.25f);
        exit.TweenProperty(_card, "modulate:a", 0f, 0.25f);
        exit.Chain().TweenCallback(Callable.From(Finish));
    }

    // =========================================================
    // Release the old card before starting the next queued alert.
    private void Finish()
    {
        if (GodotObject.IsInstanceValid(_card)) _card.QueueFree();
        _card = null;
        IsPlaying = false;
        Action callback = _completed;
        _completed = null;
        callback?.Invoke();
    }

    // =========================================================
    // Centre alerts and keep their text within the framed safe area.
    private void FitCard()
    {
        if (!GodotObject.IsInstanceValid(_card)) return;
        float screenWidth = GetViewport().GetVisibleRect().Size.X;
        float width = Mathf.Min(PanelWidth, Mathf.Max(260f, screenWidth - 24f));
        _card.Size = new Vector2(width, PanelHeight);
        _card.Position = new Vector2((screenWidth - width) / 2f, _card.Position.Y);

        float textWidth = width - 108f;
        _title.Position = new Vector2(76f, 19f);
        _title.Size = new Vector2(textWidth, 30f);
        _description.Position = new Vector2(76f, 54f);
        _description.Size = new Vector2(textWidth, 45f);
        _footer.Position = new Vector2(76f, 108f);
        _footer.Size = new Vector2(textWidth, 18f);
    }

    // =========================================================
    // Consistent typography with the game's existing cyan interface.
    private static Label MakeLabel(string text, int size, Color colour)
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

    // =========================================================
    // Major-alert accents are determined by the data resource's tone.
    public static Color AccentFor(MajorAlertTone tone) => tone switch
    {
        MajorAlertTone.Danger => new Color("#ed686d"),
        MajorAlertTone.Warning => new Color("#f0b36c"),
        MajorAlertTone.Discovery => new Color("#68dce2"),
        MajorAlertTone.Achievement => new Color("#e8cc7e"),
        _ => new Color("#7ba9e6")
    };
    #endregion
}
