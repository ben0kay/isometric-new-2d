// Registers artwork with shared ground-shadow batches.
// Static objects retain their projections; moving actors use one shared updater.
using Godot;
using System.Collections.Generic;

public partial class GroundShadow : Node2D
{
    #region Installation

    // =========================================================
    // Preserve existing obstacle shadows and register supported sprite artwork.
    public static void Attach(
        Node2D owner, TerrainVisual visual, VisualDefinition definition)
    {
        GroundShadowSettings settings = definition?.Shadows;
        GroundShadowMode mode =
            settings?.Mode ?? GroundShadowMode.Automatic;

        if (owner is Obstacle && mode == GroundShadowMode.Automatic)
            return;

        if (mode == GroundShadowMode.Disabled)
        {
            if (owner is Obstacle)
                owner.SetMeta("shared_shadow_override", true);
            return;
        }

        List<Sprite2D> sprites = new();
        CollectSprites(visual.GetNode<Node>("Artwork"), sprites);
        if (sprites.Count == 0) return;

        GroundShadowWorld world = GroundShadowWorld.Ensure(owner);
        if (world == null) return;

        if (owner is Obstacle)
            owner.SetMeta("shared_shadow_override", true);

        // Dense grass gets only a small contact shadow automatically.
        // Explicit Sprite mode can still request a projected grass silhouette.
        bool cast = mode == GroundShadowMode.Sprite ||
            (mode == GroundShadowMode.Automatic && owner is not Grass);

        bool contact = mode == GroundShadowMode.ContactOnly ||
            (settings?.ContactEnabled ?? true);

        if (!cast && !contact) return;

        for (int i = 0; i < sprites.Count; i++)
        {
            if (!cast && i > 0) break;

            world.Register(new Entry
            {
                Owner = owner,
                Visual = visual,
                Source = sprites[i],
                Settings = settings,
                Cast = cast,
                Contact = contact && i == 0,
                Moving = visual.FollowMovement,
                Layer = WorldLayerMember.For(owner)
            });
        }
    }

    // =========================================================
    // Find sprite pieces without creating a helper for each piece.
    private static void CollectSprites(Node node, List<Sprite2D> result)
    {
        if (node is Sprite2D sprite && sprite.Texture != null)
            result.Add(sprite);

        foreach (Node child in node.GetChildren())
            CollectSprites(child, result);
    }

    #endregion

    #region Shared Projection Data

    public sealed class Entry
    {
        public Node2D Owner;
        public TerrainVisual Visual;
        public Sprite2D Source;
        public GroundShadowSettings Settings;
        public GroundShadowWorld.Group Group;

        public string Layer;
        public bool Cast, Contact, Moving;
        public bool Alive = true, Ready, Visible;

        public Texture2D Texture;
        public Rect2 Rectangle, Region;
        public Transform2D CastTransform, ContactTransform;
        public Color CastColour, ContactColour;

        // =========================================================
        // Snapshot drawing commands without reading texture pixels.
        public void Refresh(
            WorldLightingSettings profile,
            Transform2D canvas, Rect2 viewport)
        {
            Ready = false;

            if (!GodotObject.IsInstanceValid(Owner) ||
                !GodotObject.IsInstanceValid(Visual) ||
                !GodotObject.IsInstanceValid(Source) ||
                Source.Texture == null)
                return;

            Texture = Source.Texture;
            Rectangle = Source.GetRect();

            if (Rectangle.Size.X <= 0f || Rectangle.Size.Y <= 0f)
                return;

            Region = Source.RegionEnabled
                ? Source.RegionRect
                : new Rect2(Vector2.Zero, Texture.GetSize());

            Vector2 frameSize = Region.Size /
                new Vector2(Source.Hframes, Source.Vframes);

            Region.Position += frameSize *
                new Vector2(Source.FrameCoords.X, Source.FrameCoords.Y);
            Region.Size = frameSize;

            Vector2 anchor = Visual.GlobalPosition;

            // The artwork jumps, while its contact shadow remains on the ground.
            if (Owner is Player player)
                anchor += Vector2.Down * player.JumpHeight;

            anchor += Settings?.GroundOffset ?? Vector2.Zero;

            float size = Mathf.Max(0.1f, Visual.GlobalScale.Abs().X);
            Vector2 dimensions = Settings?.ContactSize ??
                (Owner is Grass
                    ? new Vector2(24f, 10f)
                    : Owner is Plant
                        ? new Vector2(64f, 24f)
                        : new Vector2(36f, 14f));

            ContactTransform = new Transform2D(
                new Vector2(dimensions.X * size / 64f, 0f),
                new Vector2(0f, dimensions.Y * size / 32f),
                anchor);

            ContactColour = new Color(
                0.015f, 0.025f, 0.04f,
                Mathf.Clamp(Settings?.ContactOpacity ??
                    (Owner is Grass ? 0.14f : 0.3f), 0f, 1f));

            Vector2 direction = -profile.GetDirection();
            if (Mathf.Abs(direction.Y) < 0.05f)
                direction.Y = direction.Y < 0f ? -0.05f : 0.05f;

            float length = Mathf.Max(0.01f,
                profile.ShadowLength *
                (Settings?.LengthMultiplier ?? 0.6f));

            Transform2D artwork = Source.GlobalTransform;

            CastTransform = new Transform2D(
                Project(artwork.X, direction, length),
                Project(artwork.Y, direction, length),
                anchor + Project(
                    artwork.Origin - Visual.GlobalPosition,
                    direction, length));

            // Sprite flips affect the image, independently of node scaling.
            CastTransform *= new Transform2D(
                new Vector2(Source.FlipH ? -1f : 1f, 0f),
                new Vector2(0f, Source.FlipV ? -1f : 1f),
                new Vector2(
                    Source.FlipH
                        ? Rectangle.Position.X + Rectangle.End.X : 0f,
                    Source.FlipV
                        ? Rectangle.Position.Y + Rectangle.End.Y : 0f));

            CastColour = new Color(
                0.015f, 0.025f, 0.04f,
                Mathf.Clamp(profile.ShadowOpacity *
                    (Settings?.OpacityMultiplier ?? 0.75f), 0f, 1f));

            Visible = Source.Visible;

            if (Moving)
            {
                Rect2 screen = Bounds(
                    canvas * ContactTransform,
                    new Rect2(-32f, -16f, 64f, 32f));

                if (Cast)
                    screen = screen.Merge(
                        Bounds(canvas * CastTransform, Rectangle));

                Visible &= Owner.IsVisibleInTree() &&
                    screen.Intersects(viewport.Grow(64f));
            }

            Ready = true;
        }

        // =========================================================
        // Flatten illustrated height into a fixed ground projection.
        private static Vector2 Project(
            Vector2 point, Vector2 direction, float length)
        {
            return new Vector2(point.X, 0f) -
                direction * point.Y * length;
        }

        // =========================================================
        // Calculate screen bounds for moving shadows only.
        private static Rect2 Bounds(Transform2D transform, Rect2 rectangle)
        {
            Rect2 bounds = new(
                transform * rectangle.Position, Vector2.Zero);

            bounds = bounds.Expand(transform *
                new Vector2(rectangle.End.X, rectangle.Position.Y));
            bounds = bounds.Expand(transform * rectangle.End);
            return bounds.Expand(transform *
                new Vector2(rectangle.Position.X, rectangle.End.Y));
        }
    }

    #endregion
}