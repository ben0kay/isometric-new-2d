// Defines one biome using a stable ID and reusable terrain settings.
// Tool support lets the editor catalog inspect these resources.
using Godot;

[Tool, GlobalClass]
public partial class BiomeDefinition : Resource
{
    #region Identity
    [Export] public string Id { get; set; } = "basalt_flats";
    [Export] public string DisplayName { get; set; } = "Basalt Flats";
    [Export] public bool Enabled { get; set; } = true;
    #endregion

    #region Rolling Terrain
    [Export] public float RollingHeight { get; set; } = 64f;
    [Export] public float RollingFeatureSize { get; set; } = 12f;
    #endregion

    #region Plateaus
    [Export] public bool PlateausEnabled { get; set; } = true;
    [Export] public float PlateauHeight { get; set; } = 96f;
    [Export] public float PlateauSpacing { get; set; } = 32f;
    [Export] public float PlateauRadius { get; set; } = 6f;
    [Export] public float PlateauShoulder { get; set; } = 6f;
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float PlateauChance { get; set; } = 0.55f;
    #endregion
}