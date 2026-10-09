// Local preview controls for the notification HUD, isolated from production UI.
// Remove or disable WorldInfinite/DEBUG/NotificationTester to unplug all previews.
using Godot;

public partial class NotificationTester : Node
{
    #region Configuration
    [ExportGroup("Preview")]
    [Export] public bool Enabled { get; set; } = true;
    [Export] public Key PreviewKey { get; set; } = Key.F4;
    #endregion

#if DEBUG
    #region State
    private int _previewStep;
    #endregion

    #region Input
    // =========================================================
    // Cycle sample notifications when the development-only preview key is pressed.
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

        GetViewport().SetInputAsHandled();
    }
    #endregion
#endif
}
