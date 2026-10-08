// Attaches existing shadow artwork to the shared shadow service.
// Preserves polygon and sprite APIs without per-shadow processing nodes.
using Godot;

public partial class SunShadow : Node
{
    #region Installation

    // =========================================================
    // Preserve existing rock, tree and building polygon shadows.
    public static void Attach(Node2D owner, Polygon2D polygon)
    {
        Install(owner, polygon, true);
    }

    // =========================================================
    // Support existing sprite and contact-shadow callers.
    public static void Attach(
        Node2D owner, Sprite2D sprite, bool sunlight = true)
    {
        Install(owner, sprite, sunlight);
    }

    // =========================================================
    // Share material binding and layer visibility without culling callbacks.
    private static void Install(
        Node2D owner, Node2D drawing, bool sunlight)
    {
        owner.AddChild(drawing);

        GroundShadowWorld world = GroundShadowWorld.Ensure(owner);
        world?.RegisterExisting(drawing, sunlight);
    }

    #endregion
}