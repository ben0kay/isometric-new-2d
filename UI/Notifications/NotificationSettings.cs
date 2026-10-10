// Central Inspector-editable limits, timing and layout for both notification displays.
// Per-alert title, tone, priority, duration and cooldown stay in individual alert .tres files.
using Godot;

[Tool, GlobalClass]
public partial class NotificationSettings : Resource
{
    #region Behaviour
    [ExportGroup("Global Behaviour")]
    [Export] public bool PauseTimersWhenPaused { get; set; } = true;
    #endregion

    #region Major alerts
    [ExportGroup("Major Alerts - Queue")]
    [Export(PropertyHint.Range, "1,32,1")]
    public int MaximumPendingMajorAlerts { get; set; } = 8;

    [Export(PropertyHint.Range, "1,300,1")]
    public float MajorQueueLifetimeSeconds { get; set; } = 20f;

    [Export] public bool InterruptForCriticalAlerts { get; set; } = true;

    [Export(PropertyHint.Range, "1,100,1")]
    public int CriticalPriorityThreshold { get; set; } = 90;

    [ExportGroup("Major Alerts - Presentation")]
    [Export(PropertyHint.Range, "260,1000,10")]
    public float MajorWidth { get; set; } = 600f;

    [Export(PropertyHint.Range, "80,260,2")]
    public float MajorHeight { get; set; } = 128f;

    [Export(PropertyHint.Range, "0,160,1")]
    public float MajorTopMargin { get; set; } = 26f;

    [Export(PropertyHint.Range, "0.05,2,0.01")]
    public float MajorEntranceSeconds { get; set; } = 0.34f;

    [Export(PropertyHint.Range, "0.05,2,0.01")]
    public float MajorExitSeconds { get; set; } = 0.25f;
    #endregion

    #region Small notifications
    [ExportGroup("Toasts - Limits")]
    [Export(PropertyHint.Range, "1,8,1")]
    public int MaximumVisibleToasts { get; set; } = 3;

    [Export(PropertyHint.Range, "0,30,1")]
    public int MaximumPendingToasts { get; set; } = 5;

    [ExportGroup("Toasts - Timing")]
    [Export(PropertyHint.Range, "0.5,30,0.1")]
    public float ToastDurationSeconds { get; set; } = 3.2f;

    [Export(PropertyHint.Range, "0.05,2,0.01")]
    public float ToastEntranceSeconds { get; set; } = 0.26f;

    [Export(PropertyHint.Range, "0.05,2,0.01")]
    public float ToastExitSeconds { get; set; } = 0.22f;

    [ExportGroup("Toasts - Layout")]
    [Export(PropertyHint.Range, "240,700,10")]
    public float ToastWidth { get; set; } = 368f;

    [Export(PropertyHint.Range, "58,160,2")]
    public float ToastHeight { get; set; } = 76f;

    [Export(PropertyHint.Range, "0,160,1")]
    public float ToastTopMargin { get; set; } = 28f;

    [Export(PropertyHint.Range, "0,120,1")]
    public float ToastLeftMargin { get; set; } = 18f;

    [Export(PropertyHint.Range, "0,40,1")]
    public float ToastSpacing { get; set; } = 10f;
    #endregion
}
