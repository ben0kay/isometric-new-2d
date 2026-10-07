// Identifies world membership and preserves inactive-layer settings.
// Nested managed roots, such as streamed chunks, own their own activation.
using Godot;
using System.Collections.Generic;

public partial class WorldLayerMember : Node
{
    #region State
    public WorldLayer Layer { get; private set; }
    private Node _root;
    private bool _active = true;

    private readonly List<(Node Node, ProcessModeEnum Mode)> _process = new();
    private readonly List<(CollisionObject2D Body, uint Layer, uint Mask)> _physics = new();
    private readonly List<(CanvasItem Item, Color Colour, bool Visible)> _canvas = new();
    #endregion

    #region Membership
    // =========================================================
    // Reuse one membership helper attached to a managed root.
    public static WorldLayerMember Attach(Node root, WorldLayer layer)
    {
        WorldLayerMember member =
            root.GetNodeOrNull<WorldLayerMember>("WorldLayerMember");
        if (member != null) return member;

        member = new WorldLayerMember
        {
            Name = "WorldLayerMember",
            Layer = layer,
            _root = root
        };
        root.AddChild(member);
        return member;
    }

    // =========================================================
    // Resolve ownership; the shared player follows the active location state.
    public static WorldLayer For(Node node)
    {
        for (Node current = node; current != null; current = current.GetParent())
        {
            if (current is Player)
                return WorldLayerController.Find(current)?.Current
                    ?? WorldLayer.Surface;

            WorldLayerMember member =
                current.GetNodeOrNull<WorldLayerMember>("WorldLayerMember");
            if (member != null) return member.Layer;
        }
        return WorldLayer.Surface;
    }

    // =========================================================
    // Reject interactions between different world layers.
    public static bool Same(Node a, Node b)
    {
        return For(a) == For(b);
    }
    #endregion

    #region Activation
    // =========================================================
    // Snapshot on departure and restore the original settings on return.
    public void SetActive(bool active)
    {
        if (_active == active) return;

        if (!active)
        {
            _process.Clear();
            _physics.Clear();
            _canvas.Clear();
            Capture(_root, false);

            foreach (var record in _physics)
            {
                record.Body.CollisionLayer = 0;
                record.Body.CollisionMask = 0;
            }
            foreach (var record in _process)
                record.Node.ProcessMode = ProcessModeEnum.Disabled;
        }
        else
        {
            foreach (var record in _process)
                if (GodotObject.IsInstanceValid(record.Node))
                    record.Node.ProcessMode = record.Mode;

            foreach (var record in _physics)
            {
                if (!GodotObject.IsInstanceValid(record.Body)) continue;
                record.Body.CollisionLayer = record.Layer;
                record.Body.CollisionMask = record.Mask;
            }
            SetOpacity(1f);
        }

        _active = active;
    }

    // =========================================================
    // Fade top-level canvas items without multiplying nested alpha.
    public void SetOpacity(float opacity)
    {
        opacity = Mathf.Clamp(opacity, 0f, 1f);

        foreach (var record in _canvas)
        {
            if (!GodotObject.IsInstanceValid(record.Item)) continue;
            Color colour = record.Colour;
            colour.A *= opacity;
            record.Item.Modulate = colour;
            record.Item.Visible = record.Visible && opacity > 0.001f;
        }
    }

    // =========================================================
    // Leave independently managed descendants to their own activation helpers.
    private void Capture(Node node, bool beneathCanvas)
    {
        if (node is WorldLayerMember) return;

        if (node != _root &&
            node.GetNodeOrNull<WorldLayerMember>("WorldLayerMember") != null)
            return;

        _process.Add((node, node.ProcessMode));

        if (node is CollisionObject2D body)
            _physics.Add((body, body.CollisionLayer, body.CollisionMask));

        bool canvasFound = beneathCanvas;
        if (node is CanvasItem item)
        {
            if (!beneathCanvas)
                _canvas.Add((item, item.Modulate, item.Visible));
            canvasFound = true;
        }

        foreach (Node child in node.GetChildren())
            Capture(child, canvasFound);
    }
    #endregion
}