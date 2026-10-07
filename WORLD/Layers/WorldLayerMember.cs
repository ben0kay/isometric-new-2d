// Preserves processing, collision and presentation when switching world layers.
// Captures each branch once per deactivation and skips unchanged state writes.
using Godot;
using System.Collections.Generic;

public partial class WorldLayerMember : Node
{
    #region State
    public WorldLayer Layer { get; private set; }

    private Node _root;
    private bool _active = true;
    private float _opacity = float.NaN;

    private readonly HashSet<Node> _known = new();
    private readonly List<(Node Node, ProcessModeEnum Mode)> _process = new();
    private readonly List<(CollisionObject2D Body, uint Layer, uint Mask)> _physics = new();
    private readonly List<(CanvasItem Item, Color Colour)> _canvas = new();
    private readonly Dictionary<CanvasItem, bool> _hidden = new();
    #endregion

    #region Membership
    // =========================================================
    // Reuse one helper on each independently managed branch.
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
    // Resolve ownership while the shared player follows its current layer.
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
    // Require matching layers for interactions.
    public static bool Same(Node a, Node b)
    {
        return For(a) == For(b);
    }

    // =========================================================
    // Identify nodes already included in this branch's snapshot.
    public bool Covers(Node node)
    {
        return _known.Contains(node);
    }
    #endregion

    #region Activation
    // =========================================================
    // Pause once and restore original processing and collision settings.
    public void SetActive(bool active)
    {
        if (_active == active) return;

        if (!active)
        {
            SetOpacity(1f);
            _known.Clear();
            _process.Clear();
            _physics.Clear();
            _canvas.Clear();
            _hidden.Clear();
            _opacity = float.NaN;

            Capture(_root, false);

            foreach (var record in _process)
                if (GodotObject.IsInstanceValid(record.Node))
                    record.Node.ProcessMode = ProcessModeEnum.Disabled;

            foreach (var record in _physics)
            {
                if (!GodotObject.IsInstanceValid(record.Body)) continue;
                record.Body.CollisionLayer = 0;
                record.Body.CollisionMask = 0;
            }

            _active = false;
            return;
        }

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
        _active = true;
    }

    // =========================================================
    // Fade top-level artwork and hide it only when fully faded.
    public void SetOpacity(float opacity)
    {
        opacity = Mathf.Clamp(opacity, 0f, 1f);
        if (!float.IsNaN(_opacity) &&
            Mathf.IsEqualApprox(_opacity, opacity))
            return;

        _opacity = opacity;
        bool hide = opacity <= 0.001f;

        foreach (var record in _canvas)
        {
            if (!GodotObject.IsInstanceValid(record.Item)) continue;

            Color colour = record.Colour;
            colour.A *= opacity;
            record.Item.Modulate = colour;

            if (hide)
            {
                if (!_hidden.ContainsKey(record.Item))
                    _hidden.Add(record.Item, record.Item.Visible);
                record.Item.Visible = false;
            }
            else if (_hidden.TryGetValue(record.Item, out bool visible))
            {
                record.Item.Visible = visible;
                _hidden.Remove(record.Item);
            }
        }
    }

    // =========================================================
    // Snapshot this branch, excluding independently managed descendants.
    private void Capture(Node node, bool beneathCanvas)
    {
        if (!GodotObject.IsInstanceValid(node) ||
            node is WorldLayerMember || node.IsQueuedForDeletion())
            return;

        if (node != _root &&
            node.GetNodeOrNull<WorldLayerMember>("WorldLayerMember") != null)
            return;

        _known.Add(node);
        _process.Add((node, node.ProcessMode));

        if (node is CollisionObject2D body)
            _physics.Add((body, body.CollisionLayer, body.CollisionMask));

        bool canvasFound = beneathCanvas;
        if (node is CanvasItem item)
        {
            if (!beneathCanvas)
                _canvas.Add((item, item.Modulate));
            canvasFound = true;
        }

        foreach (Node child in node.GetChildren())
            Capture(child, canvasFound);
    }
    #endregion
}