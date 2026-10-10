// Presents a single top-centre major alert with interruptible, pause-aware motion.
// NotificationManager controls priority, deduplication and the waiting queue.
using Godot;
using System;

public partial class MajorAlertDisplay : Control
{
    #region Configuration
    private float _panelWidth = 600f, _panelHeight = 128f, _topMargin = 26f;
    private float _entranceSeconds = 0.34f, _exitSeconds = 0.25f;
    #endregion

    #region State
    private Control _card;
    private Label _title, _description, _footer;
    private Tween _entrance, _lifetime, _exit;
    private Action _completed;
    private bool _closing;
    public bool IsPlaying { get; private set; }
    #endregion

    #region Configuration
    // =========================================================
    // Apply the shared Inspector resource before showing notifications.
    public void ApplySettings(NotificationSettings settings)
    {
        _panelWidth = settings.MajorWidth;
        _panelHeight = settings.MajorHeight;
        _topMargin = settings.MajorTopMargin;
        _entranceSeconds = Mathf.Max(0.05f, settings.MajorEntranceSeconds);
        _exitSeconds = Mathf.Max(0.05f, settings.MajorExitSeconds);
        FitCard();
    }
    #endregion

    #region Lifecycle
    // =========================================================
    // Keep the banner centred without consuming player input.
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

    #region Presentation
    // =========================================================
    // Show a major alert and arrange its visual lifetime.
    public void Play(MajorAlertDefinition alert, Action completed)
    {
        if (alert == null || IsPlaying) return;

        IsPlaying = true;
        _closing = false;
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
            Position = new Vector2(13f, 43f),
            Size = new Vector2(38f, 35f),
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
        _footer = MakeLabel(alert.Footer?.ToUpperInvariant(), 11, new Color("#94b2bd"));

        _card.AddChild(_title);
        _card.AddChild(_description);
        _card.AddChild(_footer);
        FitCard();
        _card.Position = new Vector2(_card.Position.X, -_panelHeight);
        _card.Modulate = new Color(1f, 1f, 1f, 0f);

        _entrance = _card.CreateTween().SetParallel(true);
        _entrance.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _entrance.TweenProperty(_card, "position:y", _topMargin, _entranceSeconds);
        _entrance.TweenProperty(_card, "modulate:a", 1f, _entranceSeconds);

        _lifetime = _card.CreateTween();
        _lifetime.TweenInterval(Mathf.Max(1f, alert.DurationSeconds));
        _lifetime.TweenCallback(Callable.From(Dismiss));
    }

    // =========================================================
    // End a lower-priority banner early when a critical event arrives.
    public void Interrupt()
    {
        Dismiss();
    }

    // =========================================================
    // Close once, cancelling old animations before the exit tween starts.
    private void Dismiss()
    {
        if (!IsPlaying || _closing ||
            !GodotObject.IsInstanceValid(_card)) return;

        _closing = true;
        StopTween(_entrance);
        StopTween(_lifetime);

        _exit = _card.CreateTween().SetParallel(true);
        _exit.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        _exit.TweenProperty(_card, "position:y", -_panelHeight, _exitSeconds);
        _exit.TweenProperty(_card, "modulate:a", 0f, _exitSeconds);
        _exit.Chain().TweenCallback(Callable.From(Finish));
    }

    // =========================================================
    // Free visual children and let the manager start the next queued alert.
    private void Finish()
    {
        if (GodotObject.IsInstanceValid(_card)) _card.QueueFree();
        _card = null;
        _entrance = null;
        _lifetime = null;
        _exit = null;
        _closing = false;
        IsPlaying = false;
        Action callback = _completed;
        _completed = null;
        callback?.Invoke();
    }

    // =========================================================
    // Keep the active banner inside the viewport after resolution changes.
    private void FitCard()
    {
        if (!GodotObject.IsInstanceValid(_card)) return;
        float screenWidth = GetViewport().GetVisibleRect().Size.X;
        float width = Mathf.Min(_panelWidth, Mathf.Max(260f, screenWidth - 24f));
        _card.Size = new Vector2(width, _panelHeight);
        _card.Position = new Vector2((screenWidth - width) / 2f, _card.Position.Y);

        float textWidth = width - 108f;
        _title.Position = new Vector2(76f, 19f);
        _title.Size = new Vector2(textWidth, 30f);
        _description.Position = new Vector2(76f, 54f);
        _description.Size = new Vector2(textWidth, 45f);
        _footer.Position = new Vector2(76f, _panelHeight - 20f);
        _footer.Size = new Vector2(textWidth, 18f);
    }
    #endregion

    #region Helpers
    // =========================================================
    // Killing a completed tween is harmless; never leave old tweens competing.
    private static void StopTween(Tween tween)
    {
        if (tween != null && tween.IsValid()) tween.Kill();
    }

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
    // Retain existing severity colours without adding presentation dependencies.
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
