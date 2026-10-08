// Names streaming stages shown in code and world diagnostics.
// Queued, Prepared, and Ready are states; other values identify scheduled work.
public enum ChunkBuildStage
{
    Queued, TerrainData, Prepared, TerrainUpload, TerrainCollision,
    Rocks, Trees, Plants, Grass, Ready, Retiring
}