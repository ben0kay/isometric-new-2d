// Applies optional base clipping while keeping collision and original textures untouched.
using Godot;

public static class RockBurialVisual
{
    // =========================================================
    // Move only artwork down; clip it back at the TerrainVisual ground anchor.
    public static void Attach(Rock rock, TerrainVisual visual, RockVariantDefinition variant)
    {
        if (!variant.BurialEnabled) return;
        Sprite2D sprite = visual.GetNode<Sprite2D>("Artwork");
        sprite.Position += Vector2.Down * variant.BurialDepthPixels * sprite.Scale.Y;
        float cutoff = -sprite.Position.Y / sprite.Scale.Y;
        sprite.Material = variant.BurialMaterial(sprite.Material as ShaderMaterial,
            sprite.GetRect(), cutoff, WorldLighting.Find(rock) != null);
    }
}
