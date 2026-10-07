// Preserves processing, collision and visibility when switching world layers.
// Each root is captured once per deactivation rather than rescanned every frame.
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
        for (Node current = node;
            current != null; current = current.GetParent())
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

    // =========================================================
    // Identify descendants already included in this root's snapshot.
    public bool Covers(Node node)
    {
        return _known.Contains(node);
    }
    #endregion

    #region Activation
    // =========================================================
    // Snapshot and pause once; restore the original settings on activation.
    public void SetActive(bool active)
    {
        if (_active == active) return;

        if (!active)
        {
            _known.Clear();
            _process.Clear();
            _physics.Clear();
            _canvas.Clear();
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

        Set