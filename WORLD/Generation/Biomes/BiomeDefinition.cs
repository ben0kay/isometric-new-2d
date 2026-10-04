// Describes terrain settings for one biome.
// Shared terrain generators consume these settings; biomes do not duplicate generation.
using Godot;

[GlobalClass]
public partial class BiomeDefinition : Resource
{
    #region Identity
    [Export] public BiomeType Type { get; set; } = BiomeType.BasaltFlats;
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