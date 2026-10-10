// Shared entry point for data-driven major alerts and small gameplay toasts.
// Enforces priority, deduplication, cooldowns, bounded queues and pause-aware timing.
using Godot;
using System;
using System.Collections.Generic;

public partial class NotificationManager : CanvasLayer
{
    #region Configuration
    public const string ScenePath = "res://UI/Notifications/NotificationManager.tscn";

    [ExportGroup("Resources")]
    [Export] public MajorAlertCatalog Catalog { get; set; }
    [Export] public NotificationSettings Settings { get; set; }
    #endregion

    #region State
    private sealed class PendingAlert
    {
        public MajorAlertDefinition Definition;
        public double RequestedAt;
    }

    private MajorAlertDisplay _major;
    private ToastDisplay _toasts;
    private readonly List<PendingAlert> _pending = new();
    private static readonly HashSet<string> _onceThisSession = new();
    private readonly Dictionary<string, double> _lastDisplayed = new();
    private MajorAlertDefinition _current;
    private double _clock;
    #endregion

    #region Installation
    // =========================================================
    // Attach the system once to the existing player HUD.
    public static NotificationManager Attach(Node hud)
    {
        NotificationManager existing =
            hud.GetNodeOrNull<NotificationManager>("NotificationManager");
        if (existing != null) return existing;

        PackedScene scene = GD.Load<PackedScene>(ScenePath);
        if (scene == null)
        {
            GD.PushError("NotificationManager scene not found: " + ScenePath);
            return null;
        }

        NotificationManager manager = scene.Instantiate<NotificationManager>();
        manager.Name = "NotificationManager";
        hud.AddChild(manager);
        return manager;
    }

    // =========================================================
    // Find the notification manager belonging to the caller's viewport.
    public static NotificationManager Find(Node context)
    {
        if (context == null || !context.IsInsideTree()) return null;
        foreach (Node node in context.GetTree().GetNodesInGroup("notification_manager"))
            if (node is NotificationManager manager &&
                manager.GetViewport() == context.GetViewport())
                return manager;
        return null;
    }
    #endregion

    #region Lifecycle
    // =========================================================
    // Build presentations once; the shared resource configures both.
    public override void _Ready()
    {
        Layer = 22; // Above HUD 20 and below PauseMenu 200.
        ProcessMode = ProcessModeEnum.Always;
        AddToGroup("notification_manager");

        Settings ??= GD.Load<NotificationSettings>(
            "res://UI/Notifications/NotificationSettings.tres") ?? new();

        ProcessModeEnum displayMode = Settings.PauseTimersWhenPaused
            ? ProcessModeEnum.Pausable : ProcessModeEnum.Always;

        _major = new MajorAlertDisplay
        {
            Name = "MajorAlerts",
            ProcessMode = displayMode
        };
        AddChild(_major);
        _major.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _major.ApplySettings(Settings);

        _toasts = new ToastDisplay
        {
            Name = "Toasts",
            ProcessMode = displayMode
        };
        AddChild(_toasts);
        _toasts.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _toasts.ApplySettings(Settings);

        if (Catalog == null)
            GD.PushWarning("NotificationManager has no major alert catalog.");
    }

    // =========================================================
    // Advance the internal clock only while UI timers are allowed to advance.
    public override void _Process(double delta)
    {
        if (!Settings.PauseTimersWhenPaused || !GetTree().Paused)
            _clock += Math.Max(0.0, delta);
    }
    #endregion

    #region Major alerts
    // =========================================================
    // Request a named alert: reject duplicates/cooldowns, then queue by priority.
    public bool ShowMajor(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;

        MajorAlertDefinition alert = Catalog?.Find(id);
        if (alert == null)
        {
            GD.PushWarning("Unknown notification alert: " + id);
            return false;
        }

        if (alert.OncePerSession && _onceThisSession.Contains(id))
            return false;

        if (_current?.Id == id || _pending.Exists(x => x.Definition.Id == id))
            return false;

        if (_lastDisplayed.TryGetValue(id, out double previous) &&
            _clock - previous < Mathf.Max(0f, alert.CooldownSeconds))
            return false;

        int limit = Mathf.Max(1, Settings.MaximumPendingMajorAlerts);
        if (_pending.Count >= limit)
        {
            // A more important new warning may displace the lowest queued one.
            PendingAlert lowest = _pending[_pending.Count - 1];
            if (alert.Priority <= lowest.Definition.Priority) return false;
            _pending.RemoveAt(_pending.Count - 1);
        }

        PendingAlert request = new() { Definition = alert, RequestedAt = _clock };
        int insertion = _pending.FindIndex(x =>
            x.Definition.Priority < alert.Priority);
        if (insertion < 0) _pending.Add(request);
        else _pending.Insert(insertion, request);

        // A critical new danger can end a lesser active banner early.
        if (Settings.InterruptForCriticalAlerts &&
            alert.Priority >= Settings.CriticalPriorityThreshold &&
            _current != null && alert.Priority > _current.Priority)
            _major.Interrupt();

        PlayNext();
        return true;
    }

    // =========================================================
    // Skip stale entries and mark cooldown/once-only only when display begins.
    private void PlayNext()
    {
        if (_major.IsPlaying) return;

        while (_pending.Count > 0)
        {
            PendingAlert request = _pending[0];
            _pending.RemoveAt(0);
            MajorAlertDefinition alert = request.Definition;

            if (_clock - request.RequestedAt >
                Mathf.Max(1f, Settings.MajorQueueLifetimeSeconds))
                continue;

            if (alert.OncePerSession && _onceThisSession.Contains(alert.Id))
                continue;

            _current = alert;
            _lastDisplayed[alert.Id] = _clock;
            if (alert.OncePerSession) _onceThisSession.Add(alert.Id);

            _major.Play(alert, () =>
            {
                _current = null;
                PlayNext();
            });
            return;
        }
    }
    #endregion

    #region Small toasts
    // =========================================================
    // Accept general gameplay notices; the display owns merge and overflow policy.
    public void ShowToast(string key, string title, string detail = "",
        ToastTone tone = ToastTone.Information, int amount = 0)
    {
        _toasts.Push(new ToastNotification
        {
            Key = key,
            Title = title,
            Detail = detail,
            Tone = tone,
            Amount = amount
        });
    }

    // =========================================================
    // Merge repeated pickups by stable item ID, never by localized display name.
    public void ShowItem(string itemId, string displayName, int amount)
    {
        if (amount <= 0) return;
        ShowToast("item:" + itemId, displayName,
            "Added to inventory", ToastTone.Inventory, amount);
    }

    // =========================================================
    // Keep crafting notices separate from inventory pickup notices.
    public void ShowCrafted(string itemId, string displayName, int amount)
    {
        if (amount <= 0) return;
        ShowToast("craft:" + itemId, displayName,
            "Crafting complete", ToastTone.Crafted, amount);
    }
    #endregion
}
