// Holds shared game settings inherited by the world's CONFIG node.
using Godot;

public partial class GlobalConfig : Node
{
    #region Biomes
    [ExportGroup("BIOMES")]
    [Export(PropertyHint.Range, "0.25,8,0.25")]
    public float BiomeScaleMultiplier { get; set; } = 1f;
    #endregion

    #region Basins
    [ExportGroup("BASINS")]
    [Export] public bool GenerateBiomeBasins { get; set; } = true;

    [Export(PropertyHint.Range, "16,256,8")]
    public float BasinCandidateSpacingTiles { get; set; } = 64f;

    // Additional budget for basin queries outside chunk preparation.
    [Export(PropertyHint.Range, "0.05,2,0.05")]
    public double BasinQueryBudgetMs { get; set; } = 0.25;
    #endregion

    #region Caves
    [ExportGroup("CAVES")]
    [Export] public bool GenerateCaves { get; set; } = true;
    [Export] public float CaveFloorElevation { get; set; } = -160f;

    // Minimum logical tile distance between entrance mouths.
    [Export(PropertyHint.Range, "64,1024,8")]
    public float MinimumCaveHoleDistanceTiles { get; set; } = 96f;
    #endregion

    #region Navigation
    [ExportGroup("NAVIGATION")]

    [ExportSubgroup("Work Budget")]
    // Shared soft budget across surface and cave route planning.
    [Export(PropertyHint.Range, "0.05,3,0.05")]
    public double NavigationBudgetMs { get; set; } = 0.35;

    [Export(PropertyHint.Range, "1,8,1")]
    public int NavigationSearchesPerTick { get; set; } = 2;

    [ExportSubgroup("Search Areas")]
    [Export(PropertyHint.Range, "16,64,8")]
    public int NavigationCellSize { get; set; } = 32;

    [Export(PropertyHint.Range, "1,32,1")]
    public float NavigationAgentClearance { get; set; } = 12f;

    // Padding uses logical world units, rather than terrain tiles.
    [Export(PropertyHint.Range, "64,512,32")]
    public int NavigationInitialPadding { get; set; } = 128;

    [Export(PropertyHint.Range, "128,2048,64")]
    public int NavigationMaximumPadding { get; set; } = 1024;

    [Export(PropertyHint.Range, "256,16384,256")]
    public int NavigationMaximumGridCells { get; set; } = 4096;

    [Export(PropertyHint.Range, "1,16,1")]
    public int NavigationCachedGridsPerLayer { get; set; } = 4;

    [ExportSubgroup("Request Timing")]
    // Urgent requests are distributed across this many physics ticks.
    [Export(PropertyHint.Range, "1,12,1")]
    public int NavigationStaggerTicks { get; set; } = 4;

    [Export(PropertyHint.Range, "0.25,5,0.25")]
    public double NavigationFailedRetrySeconds { get; set; } = 1.0;
    #endregion

    #region Visibility
    [ExportGroup("VISIBILITY")]
    [Export] public bool ObstructionFadingEnabled { get; set; } = true;

    [Export(PropertyHint.Range, "0,100,1")]
    public float ObstructingSpriteOpacityPercent { get; set; } = 35f;

    [Export(PropertyHint.Range, "0.05,2,0.05")]
    public float ObstructionFadeSeconds { get; set; } = 0.2f;
    #endregion

    #region Debug Map
    [ExportGroup("DEBUG MAP")]

    [ExportSubgroup("Biome Preview")]
    [Export(PropertyHint.Range, "16,8192,16")]
    public float DebugMapRadiusTiles { get; set; } = 1024f;

    [Export(PropertyHint.Range, "128,4096,128")]
    public float DebugBiomeSearchRadiusTiles { get; set; } = 1024f;

    [ExportSubgroup("Points Of Interest")]
    [Export(PropertyHint.Range, "16,1024,16")]
    public float DebugMapPoiRadiusTiles { get; set; } = 128f;

    [Export(PropertyHint.Range, "0.1,2,0.1")]
    public double DebugMapPoiBudgetMs { get; set; } = 0.5;
    #endregion

    #region Cave Discovery
    [ExportGroup("CAVE DISCOVERY")]
    [Export(PropertyHint.Range, "128,512,16")]
    public float CaveDiscoveryRadiusTiles { get; set; } = 128f;
    #endregion
}