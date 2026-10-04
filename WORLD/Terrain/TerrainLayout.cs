// Preserves existing terrain query calls while generation moves into feature files.
// Delegates ravine classification and clearance to the shared ChasmFeature.
using Godot;

public static class TerrainLayout
{
    #region Configuration
    public static readonly Vector2I RavineStart = ChasmFeature.RavineStart;
    public const int RavineLength = ChasmFeature.RavineLength;
    public const float BendAmount = ChasmFeature.BendAmount;
    public const float WidthVariation = ChasmFeature.WidthVariation;
    public const float CliffDepth = ChasmFeature.CliffDepth;
    public const uint CollisionLayer = ChasmFeature.CollisionLayer;
    #endregion

    #region Queries
    // =========================================================
    // Read the shared ground-to-void classification.
    public static bool IsVoidTile(int x, int y)
    {
        return ChasmFeature.IsVoidTile(x, y);
    }

    // =========================================================
    // Check logical ground and optional clearance around the shared ravine.
    public static bool HasGroundClearance(
        Vector2 point, Vector2 tileSize, float radius = 0f)
    {
        return ChasmFeature.HasGroundClearance(point, tileSize, radius);
    }
    #endregion
}