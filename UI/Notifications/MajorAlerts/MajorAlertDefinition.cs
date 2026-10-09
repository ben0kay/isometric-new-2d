// A reusable, Inspector-editable major alert definition.
// Instances live in .tres files; gameplay triggers alerts using their stable IDs.
using Godot;

public enum MajorAlertTone { Information, Discovery, Warning, Danger, Achievement }

[Tool, GlobalClass]
public partial class MajorAlertDefinition : Resource
{
    #region Identity
    [ExportGroup("Identity")]
    [Export] public string Id { get; set; } = "";
    [Export] public string Title { get; set; } = "MAJOR ALERT";
    [Export(PropertyHint.MultilineText)]
    public string Description { get; set; } = "";
    [Export] public string Footer { get; set; } = "";
    [Export] public string BadgeText { get; set; } = "!";
    #endregion

    #region Behaviour
    [ExportGroup("Behaviour")]
    [Export] public MajorAlertTone Tone { get; set; } = MajorAlertTone.Warning;
    [Export(PropertyHint.Range, "1,100,1")]
    public int Priority { get; set; } = 50;
    [Export(PropertyHint.Range, "1,20,0.5")]
    public float DurationSeconds { get; set; } = 5f;
    [Export(PropertyHint.Range, "0,600,1")]
    public float CooldownSeconds { get; set; } = 60f;
    [Export] public bool OncePerSession { get; set; } = false;
    #endregion

    #region Future hooks
    [ExportGroup("Future Audio")]
    [Export] public string VoiceCueId { get; set; } = "";
    #endregion
}
