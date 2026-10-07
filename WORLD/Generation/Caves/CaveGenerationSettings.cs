// Defines the cave network and its streaming budgets.
// Cave-specific tuning lives here rather than in the surface generator.
using Godot;
using System;

[GlobalClass]
public partial class CaveGenerationSettings : Resource
{
    #region Generation
    [ExportGroup("Seed")]
    [Export] public uint SeedOffset { get; set; } = 73129;

    [ExportGroup("Entrance")]
    [Export] public float DepthPixels { get; set; } = 160f;
    [Export] public int EntranceTunnelLengthTiles { get; set; } = 14;

    [ExportGroup("Network")]
    [Export] public int CellsAcross { get; set; } = 8;
    [Export] public int CellsEitherSide { get; set; } = 4;
    [Export] public int CellSpacingTiles { get; set; } = 32;
    [Export] public Vector2 ChamberRadiusRange { get; set; } = new(4, 8);
    [Export] public float TunnelWidthTiles { get; set; } = 3f;
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float ExtraConnectionChance { get; set; } = 0.25f;
    #endregion

    #region Streaming
    [ExportGroup("Streaming")]
    [Export] public int ChunkSize { get; set; } = 8;
    [Export] public int LoadRadiusChunks { get; set; } = 3;
    [Export] public int RetainRadiusChunks { get; set; } = 4;
    [Export] public double BuildBudgetMs { get; set; } = 1.0;
    [Export] public int RetireChunksPerFrame { get; set; } = 1;
    #endregion

    #region Validation
    // =========================================================
    // Reject settings that could create disconnected or impractical geometry.
    public void Validate()
    {
        if (!float.IsFinite(DepthPixels) || DepthPixels < 32f ||
            EntranceTunnelLengthTiles < 6 ||
            CellsAcross < 1 || CellsAcross > 128 ||
            CellsEitherSide < 0 || CellsEitherSide > 128 ||
            CellSpacingTiles < 24 ||
            ChamberRadiusRange.X < 3f ||
            ChamberRadiusRange.Y < ChamberRadiusRange.X ||
            ChamberRadiusRange.Y > CellSpacingTiles * 0.25f ||
            !float.IsFinite(TunnelWidthTiles) ||
            TunnelWidthTiles < 3f ||
            TunnelWidthTiles > CellSpacingTiles * 0.25f ||
            !float.IsFinite(ExtraConnectionChance) ||
            ExtraConnectionChance < 0f || ExtraConnectionChance > 1f ||
            ChunkSize < 4 || ChunkSize > 16 ||
            LoadRadiusChunks < 1 || LoadRadiusChunks > 12 ||
            RetainRadiusChunks < LoadRadiusChunks ||
            RetainRadiusChunks > 16 ||
            !double.IsFinite(BuildBudgetMs) ||
            BuildBudgetMs <= 0 || RetireChunksPerFrame < 1)
            throw new InvalidOperationException(
                "Invalid cave generation settings. Check sizes, widths and streaming distances.");
    }
    #endregion
}