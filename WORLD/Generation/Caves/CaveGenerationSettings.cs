// Defines an unbounded cave chamber lattice and local streaming budgets.
// Entrance placement is shared with surface generation.
using Godot;
using System;

[GlobalClass]
public partial class CaveGenerationSettings : Resource
{
    #region Generation
    [ExportGroup("Seed")]
    [Export] public uint SeedOffset { get; set; } = 73129;

    [ExportGroup("Entrance")]
    [Export] public int EntranceTunnelLengthTiles { get; set; } = 14;

    [ExportGroup("Network")]
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
    // Reject settings that cannot support connected rooms and entrance ramps.
    public void Validate()
    {
        if (EntranceTunnelLengthTiles < 6 ||
            CellSpacingTiles < 24 ||
            !float.IsFinite(ChamberRadiusRange.X) ||
            !float.IsFinite(ChamberRadiusRange.Y) ||
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
                "Invalid cave generation settings.");
    }
    #endregion
}