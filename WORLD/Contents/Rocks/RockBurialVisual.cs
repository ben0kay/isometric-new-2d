// Applies optional base clipping while keeping collision and original textures untouched.
using Godot;

public static class RockBurialVisual
{
    // =========================================================
    // Move only artwork down; clip it back at the TerrainVisual ground anchor.
    public static void Attach(Rock rock, TerrainVisual visual, RockVariantDefinition variant)
    {
        if (!variant.BurialEnabled) return;
        float depth = WorldConfig.TryFind(rock)?.RockBurialDepthPixels
            ?? variant.BurialDepthPixels;
        if (!float.IsFinite(depth) || depth < 0 || depth > 64)
            throw new System.InvalidOperationException("Rock burial depth must be between 0 and 64 texture pixels.");
        Sprite2D sprite = visual.GetNode<Sprite2D>("Artwork");
        sprite.Position += Vector2.Down * depth * sprite.Scale.Y;
        float cutoff = -sprite.Position.Y / sprite.Scale.Y;
        sprite.Material = variant.BurialMaterial(sprite.Material as ShaderMaterial,
            sprite.GetRect(), cutoff, WorldLighting.Find(rock) != null);
    }
}
