// Preserves world-layer processing, collision and visibility.
// Inactive snapshots can include new descendants created by destination preloading.
using Godot;
using System.Collections.Generic;

public partial class WorldLayerMember : Node
{
    #region State
    public WorldLayer Layer { get; private set; }
    private Node _root;
    private bool _active = true;

    private readonly HashSet<Node> _known = new();
    private readonly List<(Node Node, ProcessModeEnum Mode)> _process = new();
    private readonly List<(CollisionObject2D Body, uint Layer, uint Mask)> _physics = new();
    private readonly List<(CanvasItem Item, Color Colour, bool Visible)> _canvas = new();
    #endregion

    #region Membership
    // =========================================================
    // Reuse one helper on each managed root.
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
    // Resolve ownership while the shared player follows location state.
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
    // Require matching world layers for interaction.
    public static bool Same(Node a, Node b)
    {
        return For(a) == For(b);
    }
    #endregion

    #region Activation
    // =========================================================
    // Pause new descendants as well as previously captured objects.
    public void SetActive(bool active)
    {
        if (!active)
        {
            if (_active)
            {
                _known.Clear();
                _process.Clear();
                _physics.Clear();
                _canvas.Clear();
            }

            Capture(_root, false);
            PauseCaptured();
            _active = false;
            return;
        }

        if (_active) return;

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
    // Preserve completed initialization changes, then suppress inactive physics.
    private void PauseCaptured()
    {
        for (int i = 0; i < _process.Count; i++)
        {
            var record = _process[i];
            if (!GodotObject.IsInstanceValid(record.Node)) continue;

            if (record.Node.ProcessMode != ProcessModeEnum.Disabled)
            {
                record.Mode = record.Node.ProcessMode;
                _process[i] = record;
            }
            record.Node.ProcessMode = ProcessModeEnum.Disabled;
        }

        for (int i = 0; i < _physics.Count; i++)
        {
            var record = _physics[i];
            if (!GodotObject.IsInstanceValid(record.Body)) continue;

            if (record.Body.CollisionLayer != 0)
                record.Layer = record.Body.CollisionLayer;
            if (record.Body.CollisionMask != 0)
                record.Mask = record.Body.CollisionMask;

            _physics[i] = record;
            record.Body.CollisionLayer = 0;
            record.Body.CollisionMask = 0;
        }
    }

    // =========================================================
    // Fade only the top canvas item in each branch.
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
    // Capture new descendants without overwriting their existing original settings.
    private void Capture(Node node, bool beneathCanvas)
    {
        if (node is WorldLayerMember || node.IsQueuedForDeletion()) return;

        if (node != _root &&
            node.GetNodeOrNull<WorldLayerMember>("WorldLayerMember") != null)
            return;

        bool added = _known.Add(node);
        if (added)
        {
            _process.Add((node, node.ProcessMode));

            if (node is CollisionObject2D body)
                _physics.Add((body, body.CollisionLayer, body.CollisionMask));
        }

        bool canvasFound = beneathCanvas;
        if (node is CanvasItem item)
        {
            if (added && !beneathCanvas)
                _canvas.Add((item, item.Modulate, item.Visible));
            canvasFound = true;
        }

        foreach (Node child in node.GetChildren())
            Capture(child, canvasFound);
    }
    #endregion
}