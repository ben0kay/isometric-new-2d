// Displays small sliding notifications with bounded overflow and same-key merging.
// Uses one shared settings resource; UI tweens inherit the configured pause mode.
using Godot;
using System;
using System.Collections.Generic;

public partial class ToastDisplay : Control
{
    #region Configuration
    private NotificationSettings _settings;
    #endregion

    #region State
    private sealed class ActiveToast
    {
        public ToastNotification Notice;
        public Control Card;
        public Label Title;
        public Label Detail;
        public Tween Lifetime;
        public Tween MoveTween;
        public bool Closing;
    }

    private readonly List<ActiveToast> _visible = new();
    private readonly List<ToastNotification> _pending = new();
    #endregion

    #region Lifecycle
    // =========================================================
    // Set shared limits before the first incoming toast.
    public void ApplySettings(NotificationSettings settings)
    {
        _settings = settings;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }
    #endregion

    #region Queue
    // =========================================================
    // Merge equal notices, show immediately if possible, or add to bounded queue.
    public void Push(ToastNotification notice)
    {
        if (notice == null || string.IsNullOrWhiteSpace(notice.Title)) return;

        if (string.IsNullOrWhiteSpace(notice.Key))
            notice.Key = Guid.NewGuid().ToString("N");

        ActiveToast active = _visible.Find(x =>
            !x.Closing && Matches(x.Notice, notice));

        if (active != null)
        {
            Merge(active.Notice, notice);
            active.Title.Text = FormatTitle(active.Notice);
            active.Detail.Text = active.Notice.Detail;
            RestartLifetime(active);
            return;
        }

        ToastNotification waiting = _pending.Find(x => Matches(x, notice));
        if (waiting != null)
        {
            Merge(waiting, notice);
            return;
        }

        if (_visible.Count < Mathf.Max(1, _settings.MaximumVisibleToasts))
        {
            Show(notice);
            return;
        }

        int queueLimit = Mathf.Max(0, _settings.MaximumPendingToasts);
        if (queueLimit <= 0) return;

        // Prefer recent activity to stale backlog during long harvesting bursts.
        if (_pending.Count >= queueLimit) _pending.RemoveAt(0);
        _pending.Add(notice);
    }

    // =========================================================
    // Only additive notices with the same identity and tone share a counter.
    private static bool Matches(ToastNotification a, ToastNotification b)
    {
        return a.Key == b.Key && a.Tone == b.Tone &&
            a.Title == b.Title;
    }

    // =========================================================
    // Safely combine item counts and refresh the newest descriptive message.
    private static void Merge(ToastNotification target, ToastNotification incoming)
    {
        if (target.Amount > 0 && incoming.Amount > 0)
            target.Amount = (int)Math.Min(int.MaxValue,
                (long)target.Amount + incoming.Amount);
        else
            target.Amount = incoming.Amount;

        target.Detail = incoming.Detail;
        if (incoming.DurationSeconds > 0f)
            target.DurationSeconds = incoming.DurationSeconds;
    }

    // =========================================================
    // Promote waiting notices when slots free up.
    private void DrainQueue()
    {
        int maximum = Mathf.Max(1, _settings.MaximumVisibleToasts);
        while (_visible.Count < maximum && _pending.Count > 0)
        {
            ToastNotification notice = _pending[0];
            _pending.RemoveAt(0);
            Show(notice);
        }
    }
    #endregion

    #region Presentation
    // =========================================================
    // Construct and slide in a toast without displacing the existing banners.
    private void Show(ToastNotification notice)
    {
        ActiveToast entry = BuildToast(notice);
        _visible.Add(entry);
        entry.Card.Position = new Vector2(-entry.Card.Size.X, TargetY(_visible.Count - 1));

        Tween arrival = entry.Card.CreateTween();
        arrival.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        arrival.TweenProperty(entry.Card, "position:x",
            _settings.ToastLeftMargin,
            Mathf.Max(0.05f, _settings.ToastEntranceSeconds));

        RestartLifetime(entry);
    }

    // =========================================================
    // Create a compact hexagonal banner with its icon and two lines of text.
    private ActiveToast BuildToast(ToastNotification notice)
    {
        float width = Mathf.Min(_settings.ToastWidth, Mathf.Max(240f,
            GetViewport().GetVisibleRect().Size.X - 24f));

        Control card = new()
        {
            Name = "Toast",
            Size = new Vector2(width, _settings.ToastHeight),
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

        return new ActiveToast
        {
            Notice = notice, Card = card, Title = title, Detail = detail
        };
    }

    // =========================================================
    // Refresh the lifetime when another matching message arrives.
    private void RestartLifetime(ActiveToast entry)
    {
        StopTween(entry.Lifetime);
        float duration = entry.Notice.DurationSeconds > 0f
            ? entry.Notice.DurationSeconds : _settings.ToastDurationSeconds;

        entry.Lifetime = entry.Card.CreateTween();
        entry.Lifetime.TweenInterval(Mathf.Max(0.5f, duration));
        entry.Lifetime.TweenCallback(Callable.From(() => Dismiss(entry)));
    }

    // =========================================================
    // Retire a toast once, reflow the survivors and display the next waiting one.
    private void Dismiss(ActiveToast entry)
    {
        if (entry == null || entry.Closing) return;
        entry.Closing = true;
        StopTween(entry.Lifetime);
        StopTween(entry.MoveTween);
        _visible.Remove(entry);

        Tween exit = entry.Card.CreateTween().SetParallel(true);
        exit.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        exit.TweenProperty(entry.Card, "position:x", -entry.Card.Size.X,
            Mathf.Max(0.05f, _settings.ToastExitSeconds));
        exit.TweenProperty(entry.Card, "modulate:a", 0f,
            Mathf.Max(0.05f, _settings.ToastExitSeconds));
        exit.Chain().TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(entry.Card))
                entry.Card.QueueFree();
        }));

        Reflow();
        DrainQueue();
    }

    // =========================================================
    // Slide remaining banners upward after one expires.
    private void Reflow()
    {
        for (int i = 0; i < _visible.Count; i++)
        {
            ActiveToast entry = _visible[i];
            StopTween(entry.MoveTween);
            entry.MoveTween = entry.Card.CreateTween();
            entry.MoveTween.SetTrans(Tween.TransitionType.Cubic)
                .SetEase(Tween.EaseType.Out);
            entry.MoveTween.TweenProperty(entry.Card, "position:y",
                TargetY(i), 0.2f);
        }
    }

    private float TargetY(int index) =>
        _settings.ToastTopMargin + index *
        (_settings.ToastHeight + _settings.ToastSpacing);
    #endregion

    #region Helpers
    // =========================================================
    // Keep tween ownership local so repeated merges don't create extra timers.
    private static void StopTween(Tween tween)
    {
        if (tween != null && tween.IsValid()) tween.Kill();
    }

    private static string FormatTitle(ToastNotification notice)
    {
        if (notice.Amount <= 0) return notice.Title;
        string suffix = notice.Tone == ToastTone.Crafted ? " ×" : " +";
        return notice.Title + suffix + notice.Amount;
    }

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
