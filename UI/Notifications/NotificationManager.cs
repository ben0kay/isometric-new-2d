// One gameplay-facing entry point for major alerts and small toast notifications.
// Owns a priority queue and cooldowns; presenters own the visual animations.
using Godot;
using System;
using System.Collections.Generic;

public partial class NotificationManager : CanvasLayer
{
    #region Configuration
    public const string ScenePath = "res://UI/Notifications/NotificationManager.tscn";
    [ExportGroup("Major Alerts")]
    [Export] public MajorAlertCatalog Catalog { get; set; }
    [Export(PropertyHint.Range, "1,16,1")]
    public int MaximumPendingAlerts { get; set; } = 8;

#if DEBUG
    [ExportGroup("Development")]
    [Export] public bool EnablePreviewKey { get; set; } = true;
#endif
    #endregion

    #region State
    private MajorAlertDisplay _major;
    private ToastDisplay _toasts;
    private readonly List<MajorAlertDefinition> _pending = new();
    private static readonly HashSet<string> _onceThisSession = new();
    private readonly Dictionary<string, double> _lastRequested = new();
    private string _currentId = "";
#if DEBUG
    private int _previewStep;
#endif
    #endregion

    #region Installation
    // =========================================================
    // Attach the notification scene to an existing HUD without editing world scenes.
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
    // Resolve the notification HUD in the same viewport as the requesting node.
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
    // Create the two display systems, independent of any gameplay event source.
    public override void _Ready()
    {
        Layer = 22; // Above normal HUD (20), below pause menus (200).
        ProcessMode = ProcessModeEnum.Always;
        AddToGroup("notification_manager");

        _major = new MajorAlertDisplay { Name = "MajorAlerts" };
        AddChild(_major);
        _major.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _toasts = new ToastDisplay { Name = "Toasts" };
        AddChild(_toasts);
        _toasts.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        if (Catalog == null)
            GD.PushWarning("NotificationManager has no major alert catalog.");
    }
    #endregion

    #region Major alerts
    // =========================================================
    // Queue a data-driven alert by ID, respecting priority and repeat rules.
    public bool ShowMajor(string id)
    {
        MajorAlertDefinition alert = Catalog?.Find(id);
        if (alert == null)
        {
            GD.PushWarning("Unknown notification alert: " + id);
            return false;
        }

        if (alert.OncePerSession && _onceThisSession.Contains(id))
            return false;

        double seconds = Time.GetTicksMsec() / 1000.0;
        if (_lastRequested.TryGetValue(id, out double previous) &&
            seconds - previous < Mathf.Max(0f, alert.CooldownSeconds))
            return false;

        if (_currentId == id || _pending.Exists(x => x.Id == id) ||
            _pending.Count >= Mathf.Max(1, MaximumPendingAlerts))
            return false;

        _lastRequested[id] = seconds;
        if (alert.OncePerSession) _onceThisSession.Add(id);

        _pending.Add(alert);
        _pending.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        PlayNext();
        return true;
    }

    // =========================================================
    // Play one pending major alert at a time, in priority order.
    private void PlayNext()
    {
        if (_major.IsPlaying || _pending.Count == 0) return;

        MajorAlertDefinition alert = _pending[0];
        _pending.RemoveAt(0);
        _currentId = alert.Id;
        _major.Play(alert, () =>
        {
            _currentId = "";
            PlayNext();
        });
    }
    #endregion

    #region Small toasts
    // =========================================================
    // Display a general notification; a stable key lets duplicates coalesce.
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
    // Convenience helpers for future inventory and crafting event subscriptions.
    public void ShowItem(string itemId, string displayName, int amount)
    {
        if (amount <= 0) return;
        ShowToast("item:" + itemId, displayName,
            "Added to inventory", ToastTone.Inventory, amount);
    }

    public void ShowCrafted(string itemId, string displayName, int amount)
    {
        if (amount <= 0) return;
        ShowToast("craft:" + itemId, displayName,
            "Crafting complete", ToastTone.Crafted, amount);
    }
    #endregion

#if DEBUG
    #region Preview
    // =========================================================
    // F8 cycles through samples in editor/debug builds; never enabled in release.
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (!EnablePreviewKey || input is not InputEventKey key ||
            !key.Pressed || key.Echo || key.PhysicalKeycode != Key.F8)
            return;

        switch (_previewStep++ % 7)
        {
            case 0: ShowItem("plant_fibre", "Plant Fibre", 1); break;
            case 1: ShowItem("plant_fibre", "Plant Fibre", 2); break;
            case 2: ShowCrafted("iron_plate", "Iron Plate", 2); break;
            case 3: ShowMajor("eclipse_imminent"); break;
            case 4: ShowMajor("boss_detected"); break;
            case 5: ShowMajor("biome_discovered"); break;
            case 6: ShowMajor("hazard_detected"); break;
        }

        GetViewport().SetInputAsHandled();
    }
    #endregion
#endif
}
