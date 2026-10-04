// Defines a biome's stable identity and terrain configuration.
// Inspector groups keep related settings together; regions organize the code.
using Godot;

[Tool, GlobalClass]
public partial class BiomeDefinition : Resource
{
    #region Identity
    [ExportGroup("Identity")]
    [Export] public string Id { get; set; } = "basalt_flats";
    [Export] public string DisplayName { get; set; } = "Basalt Flats";
    [Export] public bool Enabled { get; set; } = true;
    #endregion

    #region Terrain
    [ExportGroup("Terrain")]

    #region Rolling
    [ExportSubgroup("Rolling")]
    [Export] public float RollingHeight { get; set; } = 64f;
    [Export] public float RollingFeatureSize { get; set; } = 12f;
    #endregion

    #region Plateaus
    [ExportSubgroup("Plateaus")]
    [Export] public bool PlateausEnabled { get; set; } = true;
    [Export] public float PlateauHeight { get; set; } = 96f;
    [Export] public float PlateauSpacing { get; set; } = 32f;
    [Export] public float PlateauRadius { get; set; } = 6f;
    [Export] public float PlateauShoulder { get; set; } = 6f;
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float PlateauChance { get; set; } = 0.55f;
    #endregion

    #endregion
}