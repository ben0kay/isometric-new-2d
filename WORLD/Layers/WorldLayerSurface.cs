// Pauses and fades surface scenery while shared connections handle traversal.
// New streamed branches are captured incrementally; actors remain pursuit-owned.
using Godot;
using System.Collections.Generic;

public partial class WorldLayerSurface : Node
{
    #region State
    private Node _world;
    private Node2D _ground, _objects;
    private Player _player;
    private WorldConfig _config;
    private SceneTree _tree;
    private bool _paused;
    private float _surfaceOpacity = 1f;
    private readonly List<WorldLayerMember> _surface = new();
    private readonly Dictionary<Node, WorldLayerMember> _roots = new();
    private readonly HashSet<WorldLayerMember> _inheritedFade = new();
    private readonly Queue<Node> _addedSurface = new();
    #endregion

    #region Lifecycle
    // =========================================================
    // Bind existing branches without creating another surface world.
    public void Configure(Node world, Player player)
    {
        _world = world; _player = player;
        _ground = world.GetNode<Node2D>("GroundChunks");
        _objects = world.GetNode<Node2D>("WorldObjects");
        _config = WorldConfig.Find(world);
        _tree = GetTree();
        _tree.NodeAdded += OnNodeAdded;
        SetProcess(false);
    }

    // =========================================================
    // Pause surface simulation once on leaving surface.
    public void Pause()
    {
        if (_paused) return;
        CaptureSurface();
        _paused = true;
        foreach (WorldLayerMember member in _surface) member.SetActive(false);
    }

    // =========================================================
    // Restore captured process and physics settings on returning.
    public void Resume()
    {
        Drain();
        _paused = false;
        foreach (WorldLayerMember member in _surface)
        {
            if (!GodotObject.IsInstanceValid(member)) continue;
            member.SetActive(true);
        }
        SetOpacity(1f);
    }

    // =========================================================
    // Write presentation only when the requested opacity actually changes.
    public void SetOpacity(float opacity)
    {
        if (Mathf.IsEqualApprox(_surfaceOpacity, opacity)) return;
        _surfaceOpacity = opacity;
        foreach (WorldLayerMember member in _surface)
            if (GodotObject.IsInstanceValid(member) && !_inheritedFade.Contains(member))
                member.SetOpacity(opacity);
    }

    // =========================================================
    // Restore managed branches and disconnect the scene listener.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_tree)) _tree.NodeAdded -= OnNodeAdded;
        foreach (WorldLayerMember member in _surface)
            if (GodotObject.IsInstanceValid(member))
            {
                member.SetActive(true); member.SetOpacity(1f);
            }
    }
    #endregion

    #region Incremental Surface Registration
// =========================================================
// Queue surface scenery while leaving independently managed actors alone.
private void OnNodeAdded(Node node)
{
    if (!_paused || node is WorldLayerMember)
        return;

    bool surface = _ground.IsAncestorOf(node) || _objects.IsAncestorOf(node);

    for (Node parent = node; parent != null; parent = parent.GetParent())
    {
        if (parent is Player || parent is Entity || parent is Projectile)
            return;
        if (_roots.ContainsKey(parent)) surface = true;
    }

    if (surface) _addedSurface.Enqueue(node);
}

    // =========================================================
    // Pause new branches without revisiting existing surface hierarchies.
    public void Drain()
    {
        while (_addedSurface.Count > 0)
        {
            Node node = _addedSurface.Dequeue();
            if (!GodotObject.IsInstanceValid(node) ||
                node.IsQueuedForDeletion() || !node.IsInsideTree())
                continue;

            bool covered = false;
            for (Node parent = node; parent != null; parent = parent.GetParent())
            {
                if (!_roots.TryGetValue(parent, out WorldLayerMember member))
                    continue;
                covered = member.Covers(node);
                break;
            }

            if (covered) continue;

            WorldLayerMember added = RegisterRoot(node);
            added.SetActive(false);
            if (!_inheritedFade.Contains(added))
                added.SetOpacity(_surfaceOpacity);
        }
    }

    // =========================================================
    // Register a branch and detect inherited canvas fading.
    private WorldLayerMember RegisterRoot(Node node)
    {
        if (_roots.TryGetValue(node, out WorldLayerMember existing))
            return existing;

        bool inherited = false;
        for (Node parent = node.GetParent(); parent != null; parent = parent.GetParent())
        {
            if (parent is CanvasItem && _roots.ContainsKey(parent))
            {
                inherited = true;
                break;
            }
        }

        WorldLayerMember member = WorldLayerMember.Attach(node, WorldLayerId.Surface);
        _roots.Add(node, member);
        _surface.Add(member);
        if (inherited) _inheritedFade.Add(member);
        return member;
    }

    // =========================================================
    // Include branches independently registered during earlier visits.
    private void RegisterExistingBranches(Node node)
    {
        if (node is WorldLayerMember || node.IsQueuedForDeletion()) return;

        WorldLayerMember member =
            node.GetNodeOrNull<WorldLayerMember>("WorldLayerMember");
        if (member != null && member.Layer == WorldLayerId.Surface)
            RegisterRoot(node);

        foreach (Node child in node.GetChildren())
            RegisterExistingBranches(child);
    }

// =========================================================
// Pause scenery and population work while pursuit owns enemy activation.
private void CaptureSurface()
{
    _surface.Clear();
    _roots.Clear();
    _inheritedFade.Clear();
    _addedSurface.Clear();

    RegisterRoot(_ground);
    RegisterExistingBranches(_ground);

    foreach (Node child in _objects.GetChildren())
    {
        if (child == _player || child is Entity || child is Projectile ||
            child.IsQueuedForDeletion())
            continue;

        RegisterRoot(child);
        RegisterExistingBranches(child);
    }

    Node systems = _world.GetNode("Systems");
    foreach (string name in new[]
    {
        "ChunkController", "EnemyPopulation", "Atmosphere",
        "GroundFog", "VegetationInteraction", "Surfaces"
    })
    {
        Node node = systems.GetNodeOrNull<Node>(name);
        if (node == null) continue;
        RegisterRoot(node);
        RegisterExistingBranches(node);
    }

    Node fading = _config.GetNodeOrNull<Node>("PlayerObstructionFade");
    if (fading != null) RegisterRoot(fading);
}
    #endregion

        // =========================================================
    // Remove retired surface registrations during long underground journeys.
    public void Prune()
    {
        List<Node> remove = new();

        foreach (var pair in _roots)
            if (!GodotObject.IsInstanceValid(pair.Key) ||
                !GodotObject.IsInstanceValid(pair.Value))
                remove.Add(pair.Key);

        foreach (Node node in remove)
        {
            WorldLayerMember member = _roots[node];
            _roots.Remove(node);
            _surface.Remove(member);
            _inheritedFade.Remove(member);
        }
    }
}
