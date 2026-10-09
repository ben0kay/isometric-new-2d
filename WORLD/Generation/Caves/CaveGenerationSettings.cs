// Stores cave streaming, entrance layout and independent biome selection settings.
// Biomes own chamber, passage and content choices.
using Godot;
using System;

[Tool, GlobalClass]
public partial class CaveGenerationSettings : Resource
{
    #region Generation
    [ExportGroup("Seed")]
    [Export] public uint SeedOffset { get; set; } = 73129;

    [ExportGroup("Entrance")]
    [Export] public int EntranceTunnelLengthTiles { get; set; } = 14;

    [ExportGroup("Network")]
    [Export] public int CellSpacingTiles { get; set; } = 32;

    // Retained for existing resources and conservative entrance sizing.
    [Export] public Vector2 ChamberRadiusRange { get; set; } = new(4, 8);
    [Export] public float TunnelWidthTiles { get; set; } = 3f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float ExtraConnectionChance { get; set; } = 0.25f;
    #endregion

    #region Biomes
    [ExportGroup("Cave Biomes")]
    [Export] public BiomeCatalog Biomes { get; set; }
    [Export] public float BiomeSizeTiles { get; set; } = 384f;

    [Export(PropertyHint.Range, "0,0.5,0.01")]
    public float BiomeBlendWidth { get; set; } = 0.08f;

    [ExportGroup("Cave Biomes / Testing")]
    // Empty uses mixed cave biomes. Otherwise use an enabled cave biome ID.
    [Export] public string TestBiomeId { get; set; } = "";
    #endregion

    #region Streaming
    [ExportGroup("Streaming")]
    [Export] public int ChunkSize { get; set; } = 8;
    [Export] public int LoadRadiusChunks { get; set; } = 3;
    [Export] public int RetainRadiusChunks { get; set; } = 4;
    [Export] public double BuildBudgetMs { get; set; } = 1.0;
    [Export] public int RetireChunksPerFrame { get; set; } = 1;
    #endregion

    #region Catalog Queries
    // =========================================================
    // Resolve the dedicated cave catalog without editing existing scene resources.
    public BiomeCatalog GetBiomeCatalog()
    {
        return Biomes ?? GD.Load<BiomeCatalog>(
            "res://WORLD/Generation/Caves/Biomes/CaveBiomeCatalog.tres")
            ?? throw new InvalidOperationException("Missing cave biome catalog.");
    }

    // =========================================================
    // Size entrance offsets for the largest possible cave chamber.
    public float MaximumChamberRadius()
    {
        float radius = ChamberRadiusRange.Y;

        foreach (BiomeDefinition definition in GetBiomeCatalog().GetEnabledBiomes())
        {
            if (definition is not CaveBiomeDefinition cave)
                throw new InvalidOperationException(
                    "The cave catalog contains a surface biome.");

            cave.ValidateCave();
            radius = Mathf.Max(radius,
                cave.ChamberRadiusRange.Y + cave.WallIrregularityTiles);
        }

        return radius;
    }

    // =========================================================
    // Reserve enough entrance space for the widest biome passage.
    public float MaximumTunnelWidth()
    {
        float width = TunnelWidthTiles;

        foreach (BiomeDefinition definition in GetBiomeCatalog().GetEnabledBiomes())
        {
            if (definition is not CaveBiomeDefinition cave)
                throw new InvalidOperationException(
                    "The cave catalog contains a surface biome.");

            width = Mathf.Max(width, cave.TunnelWidthTiles);
        }

        return width;
    }
    #endregion

    #region Validation
    // =========================================================
    // Validate global layout and streaming settings before building the cave.
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
            !float.IsFinite(BiomeSizeTiles) || BiomeSizeTiles < 64f ||
            !float.IsFinite(BiomeBlendWidth) ||
            BiomeBlendWidth < 0f || BiomeBlendWidth > 0.5f ||
            ChunkSize < 4 || ChunkSize > 16 ||
            LoadRadiusChunks < 1 || LoadRadiusChunks > 12 ||
            RetainRadiusChunks < LoadRadiusChunks ||
            RetainRadiusChunks > 16 ||
            !double.IsFinite(BuildBudgetMs) ||
            BuildBudgetMs <= 0 || RetireChunksPerFrame < 1)
        {
            throw new InvalidOperationException(
                "Invalid cave generation settings.");
        }
    }
    #endregion
}