// Isolated development-only stress tests for the two notification systems.
// Select a scenario in the Inspector, then press F4. Remove this node to unplug tests.
using Godot;

public enum NotificationTestScenario
{
    CycleSamples,
    ToastBurst,
    MajorPriority,
    DuplicateCooldown,
    PauseTiming
}

public partial class NotificationTester : Node
{
    #region Configuration
    [ExportGroup("Preview")]
    [Export] public bool Enabled { get; set; } = true;
    [Export] public Key PreviewKey { get; set; } = Key.F4;
    [Export] public NotificationTestScenario Scenario { get; set; }
        = NotificationTestScenario.CycleSamples;
    #endregion

#if DEBUG
    #region State
    private int _previewStep;
    #endregion

    #region Input
    // =========================================================
    // Exercise the current scenario without adding any debug code to the UI manager.
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (!Enabled || input is not InputEventKey key ||
            !key.Pressed || key.Echo ||
            key.PhysicalKeycode != PreviewKey)
            return;

        NotificationManager notifications = NotificationManager.Find(this);
        if (notifications == null)
        {
            GD.PushWarning("NotificationTester: notification HUD not found.");
            return;
        }

        switch (Scenario)
        {
            case NotificationTestScenario.ToastBurst:
                TestToastBurst(notifications);
                break;
            case NotificationTestScenario.MajorPriority:
                TestMajorPriority(notifications);
                break;
            case NotificationTestScenario.DuplicateCooldown:
                TestDuplicateCooldown(notifications);
                break;
            case NotificationTestScenario.PauseTiming:
                TestPauseTiming(notifications);
                break;
            default:
                TestCycle(notifications);
                break;
        }

        GetViewport().SetInputAsHandled();
    }
    #endregion

    #region Scenarios
    // =========================================================
    // Cycle the original seven small and major samples.
    private void TestCycle(NotificationManager notifications)
    {
        switch (_previewStep++ % 7)
        {
            case 0: notifications.ShowItem("plant_fibre", "Plant Fibre", 1); break;
            case 1: notifications.ShowItem("plant_fibre", "Plant Fibre", 2); break;
            case 2: notifications.ShowCrafted("iron_plate", "Iron Plate", 2); break;
            case 3: notifications.ShowMajor("eclipse_imminent"); break;
            case 4: notifications.ShowMajor("boss_detected"); break;
            case 5: notifications.ShowMajor("biome_discovered"); break;
            case 6: notifications.ShowMajor("hazard_detected"); break;
        }
    }

    // =========================================================
    // Flood the display with 20 notices, then combine repeated pending items.
    private static void TestToastBurst(NotificationManager notifications)
    {
        for (int i = 0; i < 20; i++)
            notifications.ShowItem("burst_" + (i % 8),
                "Sample Resource " + (i % 8), 1);

        for (int i = 0; i < 5; i++)
            notifications.ShowItem("burst_7", "Sample Resource 7", 2);
    }

    // =========================================================
    // Queue a warning then a critical boss threat to check interruption.
    private static void TestMajorPriority(NotificationManager notifications)
    {
        bool warning = notifications.ShowMajor("eclipse_imminent");
        bool critical = notifications.ShowMajor("boss_detected");
        GD.Print($"Notification priority test: warning={warning}, critical={critical}");
    }

    // =========================================================
    // Attempt immediate duplicate alert IDs and merge repeated same-key toasts.
    private static void TestDuplicateCooldown(NotificationManager notifications)
    {
        for (int i = 0; i < 6; i++)
            notifications.ShowCrafted("test_plate", "Test Plate", 1);

        bool first = notifications.ShowMajor("hazard_detected");
        bool duplicate = notifications.ShowMajor("hazard_detected");
        GD.Print($"Notification duplicate test: first={first}, duplicate={duplicate}");
    }

    // =========================================================
    // Start a long visible alert; press Escape to pause and check that it freezes.
    private static void TestPauseTiming(NotificationManager notifications)
    {
        notifications.ShowToast("pause_demo", "PAUSE TIMER TEST",
            "Open the pause menu now", ToastTone.Warning);
        notifications.ShowMajor("boss_detected");
    }
    #endregion
#endif
}
