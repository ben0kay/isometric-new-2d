// Tests elevated world artwork against the viewport's visible rectangle.
// Callers cache the result and decide how frequently to refresh it.
using Godot;

public static class ScreenVisibility
{
    #region Visibility
    // =========================================================
    // Project world-space artwork bounds into screen space with a safety margin.
    public static bool Intersects(
        Node2D actor, Rect2 visualBounds,
        float visualScale, float marginPixels)
    {
        if (!GodotObject.IsInstanceValid(actor) ||
            !actor.IsInsideTree() || !actor.IsVisibleInTree())
            return false;

        Viewport viewport = actor.GetViewport();
        if (viewport.GetCamera2D() == null) return true;

        Vector2 origin = actor.GlobalPosition + Vector2.Up *
            WorldLayerController.HeightFor(actor, actor.GlobalPosition);

        float scale = Mathf.Max(0.01f, visualScale);
        Rect2 bounds = new(
            visualBounds.Position * scale,
            visualBounds.Size * scale);

        Transform2D canvas = actor.GetCanvasTransform();
        Rect2 screenBounds = new(
            canvas * (origin + bounds.Position), Vector2.Zero);

        screenBounds = screenBounds.Expand(canvas * (
            origin + new Vector2(bounds.End.X, bounds.Position.Y)));
        screenBounds = screenBounds.Expand(canvas * (origin + bounds.End));
        screenBounds = screenBounds.Expand(canvas * (
            origin + new Vector2(bounds.Position.X, bounds.End.Y)));

        return screenBounds.Intersects(
            viewport.GetVisibleRect().Grow(Mathf.Max(0f, marginPixels)));
    }
    #endregion
}