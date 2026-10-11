// Adds a nonblocking cursor hit area beneath visible artwork at runtime.
using Godot;
using System;
public partial class InteractionTarget : Area2D
{
    public const uint Mask = 1u << 20;
    public IInteractionProvider Provider { get; private set; }
    public Vector2 Anchor => GlobalPosition;
    // =========================================================
    // Wait once for asynchronous artwork setup; no per-object input processing.
    public static async void AttachWhenReady(WorldStorage storage)
    {
        try
        {
            while (GodotObject.IsInstanceValid(storage) && storage.IsInsideTree() &&
                !storage.IsQueuedForDeletion() && GodotObject.IsInstanceValid(storage.Host))
            {
                Node2D visual = storage.Host.GetNodeOrNull<Node2D>("Visual");
                if (visual != null)
                {
                    Node2D artwork = visual.GetNodeOrNull<Node2D>("Artwork") ?? visual;
                    if (artwork.GetNodeOrNull<InteractionTarget>("InteractionTarget") != null) return;
                    Rect2 rect = artwork is Sprite2D sprite ? sprite.GetRect()
                        : new Rect2(-48, -72, 96, 72);
                    Attach(artwork, rect, new StorageInteractionProvider(storage));
                    return;
                }
                await storage.ToSignal(storage.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
        catch (Exception error) { GD.PushError($"Interaction target setup failed: {error.Message}"); }
    }
    // =========================================================
    // Let other object types register their own provider and artwork-aligned shape.
    public static InteractionTarget Attach(Node2D artwork, Rect2 rect, IInteractionProvider provider)
    {
        InteractionTarget existing = artwork.GetNodeOrNull<InteractionTarget>("InteractionTarget");
        if (existing != null) return existing;
        InteractionTarget target = new()
        {
            Name = "InteractionTarget", Provider = provider,
            CollisionLayer = Mask, CollisionMask = 0, Monitoring = false,
            Monitorable = true, Position = rect.GetCenter()
        };
        artwork.AddChild(target);
        target.AddChild(new CollisionShape2D
        { Shape = new RectangleShape2D { Size = rect.Size } });
        target.SetProcess(false); target.SetPhysicsProcess(false);
        return target;
    }

}
