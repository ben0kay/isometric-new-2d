// Holds shared game-wide tuning settings.
// WorldConfig inherits these settings on the existing CONFIG node.
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

		// Additional budget for basin data requested outside normal chunk preparation.
	[Export(PropertyHint.Range, "0.05,2,0.05")]
	public double BasinQueryBudgetMs { get; set; } = 0.25;
	#endregion

	#region Caves
	[ExportGroup("CAVES")]
	[Export] public bool GenerateCaves { get; set; } = true;
		// Absolute terrain elevation of the main underground network.
	[Export] public float CaveFloorElevation { get; set; } = -160f;

	// Minimum logical tile distance between surface entrance mouths.
	// Large tunnel profiles may require a greater safety distance.
	[Export(PropertyHint.Range, "64,1024,8")]
	public float MinimumCaveHoleDistanceTiles { get; set; } = 96f;
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

#region Enemy Updates
[ExportGroup("ENEMY UPDATES")]

[ExportSubgroup("Scheduling")]
[Export(PropertyHint.Range, "1,12,1")]
public int EnemyOnScreenStaggerTicks { get; set; } = 3;

[Export(PropertyHint.Range, "3,60,1")]
public int EnemyOffScreenStaggerTicks { get; set; } = 12;

[ExportSubgroup("Off Screen")]
[Export(PropertyHint.Range, "0.1,3,0.05")]
public double EnemyOffScreenTargetInterval { get; set; } = 0.75;

[Export(PropertyHint.Range, "0.1,2,0.05")]
public double EnemyOffScreenDecisionInterval { get; set; } = 0.4;

[ExportSubgroup("Screen Checks")]
[Export(PropertyHint.Range, "1,60,1")]
public int EnemyScreenCheckTicks { get; set; } = 12;

[Export(PropertyHint.Range, "0,512,16")]
public float EnemyScreenMarginPixels { get; set; } = 128f;
#endregion

#region Eclipse
[ExportGroup("ECLIPSE")]

[Export(PropertyHint.Range, "0.1,240,0.1,or_greater")]
public double DayDurationMinutes { get; set; } = 45.0;

[Export(PropertyHint.Range, "0,120,0.1,or_greater")]
public double EclipseDurationMinutes { get; set; } = 15.0;

[Export(PropertyHint.Range, "0,4,0.05,or_greater")]
public float EclipseDarknessMultiplier { get; set; } = 1f;
#endregion
}
