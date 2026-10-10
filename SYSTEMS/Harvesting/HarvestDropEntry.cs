// Configures one independent harvest reward; quantities belong to runtime batches.
using Godot;

[Tool, GlobalClass]
public partial class HarvestDropEntry : Resource
{
    #region Configuration
    [ExportGroup("Reward")]
    [Export] public string ItemId { get; set; } = "";
    [Export(PropertyHint.Range, "1,100000,1")]
    public int MinimumCount { get; set; } = 1;
    [Export(PropertyHint.Range, "1,100000,1")]
    public int MaximumCount { get; set; } = 1;
    [Export(PropertyHint.Range, "0,1,0.01")]
    public double Chance { get; set; } = 1.0;
    #endregion
}
