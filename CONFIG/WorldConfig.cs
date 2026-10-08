// Holds terrain slope rules for the world's CONFIG node.
// Inherits shared game settings from GlobalConfig.
using Godot;
using System;

public partial class WorldConfig : GlobalConfig
{
    #region Defaults
    public const float DefaultSlopeAngle = 30f;
    public const float DefaultSlopeHeightScale = 1f;
    #endregion

    #region Terrain Slopes
    [ExportGroup("TERRAIN SLOPES")]
    [Export] public bool TerrainSlopesEnabled { get; set; } = true;

    [Export(PropertyHint.Range, "5,85,1")]
    public float MaxWalkableSlopeAngle { get; set; } = DefaultSlopeAngle;

    [Export(PropertyHint.Range, "0.1,8,0.1")]
    public float SlopeHeightScale { get; set; } = DefaultSlopeHeightScale;
    #endregion

    #region Lookup
    // =========================================================
    // Locate optional CONFIG without relying on scene ready order.
    public static WorldConfig TryFind(Node context)
    {
        for (Node ancestor = context; ancestor != null;
             ancestor = ancestor.GetParent())
        {
            WorldConfig config =
                ancestor.GetNodeOrNull<WorldConfig>("CONFIG");
            if (config != null) return config;
        }
        return null;
    }

    // =========================================================
    // Require CONFIG for systems using explicit world configuration.
    public static WorldConfig Find(Node context)
    {
        return TryFind(context) ?? throw new InvalidOperationException(
            "World requires a CONFIG node using WorldConfig.cs.");
    }
    #endregion
}