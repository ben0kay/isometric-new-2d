// Holds the major alert definitions known to the UI.
// Add a new .tres resource to the catalog instead of writing new UI branches.
using Godot;

[Tool, GlobalClass]
public partial class MajorAlertCatalog : Resource
{
    [Export] public Godot.Collections.Array<MajorAlertDefinition> Alerts
        { get; set; } = new();

    // =========================================================
    // Resolve an alert by its stable ID. A small linear search is sufficient.
    public MajorAlertDefinition Find(string id)
    {
        foreach (MajorAlertDefinition alert in Alerts)
            if (alert != null && alert.Id == id)
                return alert;
        return null;
    }
}
