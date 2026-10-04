// Names the streaming stages shown in code and the sandbox timing label.
// Queued/Prepared/Ready are states; the other values identify scheduled work.
public enum ChunkBuildStage
{
    Queued, TerrainData, Prepared, TerrainUpload, TerrainCollision,
    Rocks, Crates, Trees, Plants, Grass, Ready, Retiring
}
