// Configures ground shadows independently of artwork and gameplay.
// Automatic mode preserves existing obstacle shadows and projects other sprites.
using Godot;

public enum GroundShadowMode
{
    Automatic,
    Disabled,
    Sprite,
    ContactOnly
}

[Tool, GlobalClass]
public partial class GroundShadowSettings : Resource
{
    #region Casting
    [ExportGroup("Ground Shadow")]
    [Export] public GroundShadowMode Mode { get; set; }

    [Export(PropertyHint.Range, "0,3,0.05")]
    public float LengthMultiplier { get; set; } = 0.6f;

    [Export(PropertyHint.Range, "0,1,0.05")]
    public float OpacityMultiplier { get; set; } = 0.75f;

    [Export] public Vector2 GroundOffset { get; set; }
    #endregion

    #region Contact
    [ExportGroup("Contact Shadow")]
    [Export] public bool ContactEnabled { get; set; } = true;
    [Export] public Vector2 ContactSize { get; set; } = new(48, 20);

    [Export(PropertyHint.Range, "0,1,0.05")]
    public float ContactOpacity { get; set; } = 0.3f;
    #endregion
}