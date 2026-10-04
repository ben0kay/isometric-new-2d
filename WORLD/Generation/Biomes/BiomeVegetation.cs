// Stores one biome's vegetation density and optional artwork overrides.
// Plants, trees and grass share the existing generic runtime spawners.
using Godot;

[Tool, GlobalClass]
public partial class BiomeVegetation : Resource
{
    #region Plants
    [ExportGroup("Plants")]
    [Export] public int PlantPatches { get; set; } = 3;
    [Export] public int PlantsPerPatch { get; set; } = 4;
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float ShrubChance { get; set; } = 0.35f;
    [Export] public VisualDefinition FrondVisual { get; set; }
    [Export] public VisualDefinition ShrubVisual { get; set; }
    #endregion

    #region Trees
    [ExportGroup("Trees")]
    [Export] public int TreesPerChunk { get; set; } = 3;
    [Export] public Vector2 TreeSizeRange { get; set; } = new(0.8f, 1.2f);
    [Export] public VisualDefinition TreeVisual { get; set; }
    #endregion

    #region Grass
    [ExportGroup("Grass")]
    [Export] public int GrassPatches { get; set; } = 6;
    [Export] public int GrassTuftsPerPatch { get; set; } = 10;
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float MediumGrassChance { get; set; } = 0.12f;
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float TallGrassChance { get; set; }
    [Export] public VisualDefinition ShortGrassVisual { get; set; }
    [Export] public VisualDefinition MediumGrassVisual { get; set; }
    [Export] public VisualDefinition TallGrassVisual { get; set; }
    #endregion
}