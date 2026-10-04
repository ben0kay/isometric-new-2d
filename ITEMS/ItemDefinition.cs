// Defines immutable item data shared by inventory stacks and equipment.
// Runtime quantities belong to storage, never to these resource definitions.
using Godot;

[Tool, GlobalClass]
public partial class ItemDefinition : Resource
{
    #region Identity
    [ExportGroup("Identity")]
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "Item";
    [Export] public string ShortName { get; set; } = "ITEM";
    #endregion

    #region Storage
    [ExportGroup("Storage")]
    [Export(PropertyHint.Range, "1,999,1")]
    public int MaxStack { get; set; } = 1;
    [Export] public float WeightKg { get; set; } = 1f;
    [Export] public float VolumeLitres { get; set; } = 1f;
    #endregion

    #region Appearance
    [ExportGroup("Appearance")]
    [Export] public Texture2D Icon { get; set; }
    [Export] public Color Tint { get; set; } = Colors.White;
    #endregion

    #region Use
    [ExportGroup("Use")]
    [Export] public AttackDefinition Attack { get; set; }
    #endregion
}