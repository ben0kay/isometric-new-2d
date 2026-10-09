// Owns shared static shadow batches and a bounded moving-shadow updater.
// No individual plant, grass tuft or shadow receives processing callbacks.
using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;

public partial class GroundShadowWorld : Node
{
    #region Work Limits

    private const double WorkBudgetMs = 0.5;
    private const int MaximumMovingChecks = 128;
    private const int MaximumStaticUpdates = 64;
    private const int StaticBatchRedrawsPerFrame = 2;
    private const float BatchSize = 768f;

    #endregion

    #region Batch Data

    public sealed class Group
    {
        public (string Layer, Vector2I Cell, bool Moving) Key;
        public readonly List<GroundShadow.Entry> Entries = new();
        public GroundShadowBatch Cast, Contact;
        public bool Dirty, Alive = true;
    }

    #endregion

    #region State

    private WorldLighting _lighting;
    private Node _world;
    private Node2D _surfaceRoot;
    private readonly Dictionary<string, Node2D> _layerRoots = new();
    private WorldLayerController _layers;

    private ShaderMaterial _sunMaterial;
    private CanvasItemMaterial _contactMaterial;
    private Texture2D _contactTexture;
    private bool _boundEclipse, _stopping;

    private string _lastLayer;
    private Vector2 _lastDirection;
    private float _lastLength = float.NaN, _lastOpacity = float.NaN;
    private double _profileTimer;
    private int _movingCursor;

    private readonly Dictionary<
        (string Layer, Vector2I Cell, bool Moving), Group> _groups = new();

    private readonly Dictionary<Node2D, List<GroundShadow.Entry>> _owners = new();
    private readonly Dictionary<Node2D, Action> _ownerExit = new();
    private readonly HashSet<GroundShadow.Entry> _static = new();
    private readonly List<GroundShadow.Entry> _moving = new();
    private readonly Queue<GroundShadow.Entry> _pending = new();
    private readonly Queue<Group> _dirty = new();

    private readonly Dictionary<CanvasItem,
        (bool Sunlight, bool Visible, Action Exit)> _existing = new();

    #endregion

    #region Installation

// =========================================================
// Return a fully initialized service before accepting shadow registrations.
public static GroundShadowWorld Ensure(Node context)
{
    WorldLighting lighting = WorldLighting.Find(context);
    if (lighting == null) return null;

    GroundShadowWorld service =
        lighting.GetNodeOrNull<GroundShadowWorld>("GroundShadowWorld");

    if (service == null)
    {
        service = new GroundShadowWorld
        {
            Name = "GroundShadowWorld",
            _lighting = lighting,
            _world = lighting.GetParent().GetParent()
        };

        lighting.AddChild(service);
    }

    // The lighting parent may not have completed its own ready sequence.
    service.Initialize();
    return service;
}

// =========================================================
// Normal scene readiness uses the same guarded initialization.
public override void _Ready()
{
    Initialize();
}

// =========================================================
// Create resources and the drawing root once, regardless of ready order.
private void Initialize()
{
    if (GodotObject.IsInstanceValid(_surfaceRoot))
        return;

    ProcessPriority = 110;
    SetPhysicsProcess(false);

    _sunMaterial = new ShaderMaterial
    {
        Shader = GD.Load<Shader>(
            "res://VISUALS/Shadows/SunShadow.gdshader")
    };

    _contactMaterial = new CanvasItemMaterial
    {
        LightMode = CanvasItemMaterial.LightModeEnum.Unshaded
    };

    _contactTexture = GroundShadowTextures.GetContact();

    Node2D ground = _world.GetNode<Node2D>("GroundChunks");
    Node2D root = new()
    {
        Name = "SharedGroundShadows"
    };

    ground.AddChild(root);
    _surfaceRoot = root;
}

    // =========================================================
    // Register data and one removal callback per owner.
    public void Register(GroundShadow.Entry entry)
    {
        if (!_owners.TryGetValue(entry.Owner, out List<GroundShadow.Entry> entries))
        {
            entries = new();
            _owners.Add(entry.Owner, entries);

            Node2D owner = entry.Owner;
            Action callback = () => RemoveOwner(owner);
            _ownerExit.Add(owner, callback);
            owner.TreeExiting += callback;
        }

        entries.Add(entry);

        if (entry.Moving)
            _moving.Add(entry);
        else
            _static.Add(entry);

        AttachToGroup(entry);
        _pending.Enqueue(entry);
    }

    // =========================================================
    // Preserve existing polygon shadows without creating culling helpers.
    public void RegisterExisting(CanvasItem drawing, bool sunlight)
    {
        if (_existing.ContainsKey(drawing)) return;

        Action callback = () => _existing.Remove(drawing);
        _existing.Add(drawing, (sunlight, drawing.Visible, callback));
        drawing.TreeExiting += callback;
        drawing.Material = sunlight ? _sunMaterial : _contactMaterial;

        string layer = _layers?.Current ?? WorldLayerId.Surface;
        if (sunlight && layer != WorldLayerId.Surface)
            drawing.Visible = false;
    }

    // =========================================================
    // Request a rebuild after changing a static object's artwork or transform.
    public void Invalidate(Node2D owner)
    {
        if (!_owners.TryGetValue(owner, out List<GroundShadow.Entry> entries))
            return;

        foreach (GroundShadow.Entry entry in entries)
            if (!entry.Moving)
                _pending.Enqueue(entry);
    }

    #endregion

    #region Shared Updates

    // =========================================================
    // Update actors and prepare static projections under one soft work budget.
    public override void _Process(double delta)
    {
        _layers ??= WorldLayerController.Find(this);
        string current = _layers?.Current ?? WorldLayerId.Surface;

        BindEclipseMaterial();

        if (current != _lastLayer)
        {
            _lastLayer = current;
            ApplyLayerVisibility();
        }

        WorldLightingSettings profile = _lighting.Settings;
        _profileTimer -= delta;

        if (_profileTimer <= 0)
        {
            _profileTimer = 0.2;
            Vector2 direction = profile.GetDirection();

            if (direction != _lastDirection ||
                !Mathf.IsEqualApprox(profile.ShadowLength, _lastLength) ||
                !Mathf.IsEqualApprox(profile.ShadowOpacity, _lastOpacity))
            {
                _lastDirection = direction;
                _lastLength = profile.ShadowLength;
                _lastOpacity = profile.ShadowOpacity;

                foreach (GroundShadow.Entry entry in _static)
                    _pending.Enqueue(entry);
            }
        }

        Transform2D canvas = GetViewport().GetCanvasTransform();
        Rect2 viewport = GetViewport().GetVisibleRect();
        long started = Stopwatch.GetTimestamp();

        // Keep the player responsive before servicing other actors.
        foreach (GroundShadow.Entry entry in _moving)
            if (entry.Alive && entry.Owner is Player)
                Refresh(entry, profile, canvas, viewport);

        int checks = Mathf.Min(MaximumMovingChecks, _moving.Count);

        for (int i = 0; i < checks && WithinBudget(started); i++)
        {
            if (_movingCursor >= _moving.Count) _movingCursor = 0;
            GroundShadow.Entry entry = _moving[_movingCursor++];

            if (entry.Alive && entry.Owner is not Player)
                Refresh(entry, profile, canvas, viewport);
        }

        int updates = 0;

        while (_pending.Count > 0 &&
            updates < MaximumStaticUpdates && WithinBudget(started))
        {
            GroundShadow.Entry entry = _pending.Dequeue();
            if (!entry.Alive) continue;

            Refresh(entry, profile, canvas, viewport);
            updates++;
        }

        // Moving batches redraw regularly; static batches redraw only when dirty.
        foreach (Group group in _groups.Values)
        {
            if (!group.Alive || !group.Key.Moving) continue;
            group.Cast.QueueRedraw();
            group.Contact.QueueRedraw();
        }

        int redraws = 0;

        while (_dirty.Count > 0 && redraws < StaticBatchRedrawsPerFrame)
        {
            Group group = _dirty.Dequeue();
            group.Dirty = false;

            if (!group.Alive) continue;
            group.Cast.QueueRedraw();
            group.Contact.QueueRedraw();
            redraws++;
        }
    }

    // =========================================================
    // Refresh one record and move actors between layer batches when necessary.
    private void Refresh(
        GroundShadow.Entry entry, WorldLightingSettings profile,
        Transform2D canvas, Rect2 viewport)
    {
        if (entry.Moving)
        {
            string layer = WorldLayerMember.For(entry.Owner);

            if (layer != entry.Layer)
            {
                DetachFromGroup(entry);
                entry.Layer = layer;
                AttachToGroup(entry);
            }
        }

        entry.Refresh(profile, canvas, viewport);
        MarkDirty(entry.Group);
    }

    // =========================================================
    // Bound projection work; renderer work is measured separately in Godot.
    private static bool WithinBudget(long started)
    {
        return (Stopwatch.GetTimestamp() - started) * 1000.0 /
            Stopwatch.Frequency < WorkBudgetMs;
    }

    // =========================================================
    // Adopt the eclipse's shared material once initialization has completed.
    private void BindEclipseMaterial()
    {
        if (_boundEclipse) return;

        WorldEclipse eclipse = WorldEclipse.Find(this);
        if (eclipse?.ShadowMaterial == null) return;

        ShaderMaterial previous = _sunMaterial;
        _sunMaterial = eclipse.ShadowMaterial;
        _boundEclipse = true;

        foreach (Group group in _groups.Values)
            group.Cast.Material = _sunMaterial;

        foreach (var pair in _existing)
            if (pair.Value.Sunlight &&
                GodotObject.IsInstanceValid(pair.Key))
                pair.Key.Material = _sunMaterial;

        previous.Dispose();
    }

    // =========================================================
    // Hide inactive layer batches and suppress sunlight underground.
    private void ApplyLayerVisibility()
    {
        foreach (Group group in _groups.Values)
        {
            group.Contact.Visible = group.Key.Layer == _lastLayer;
            group.Cast.Visible = group.Key.Layer == _lastLayer &&
                _lastLayer == WorldLayerId.Surface;
        }

        foreach (var pair in _existing)
            if (GodotObject.IsInstanceValid(pair.Key))
                pair.Key.Visible = pair.Value.Visible &&
                    (!pair.Value.Sunlight ||
                        _lastLayer == WorldLayerId.Surface);
    }

    #endregion

    #region Batch Ownership

    // =========================================================
    // Share one pair of drawing nodes per static area or moving layer.
    private void AttachToGroup(GroundShadow.Entry entry)
    {
        Vector2 position = entry.Owner.GlobalPosition;
        Vector2I cell = entry.Moving ? Vector2I.Zero : new Vector2I(
            Mathf.FloorToInt(position.X / BatchSize),
            Mathf.FloorToInt(position.Y / BatchSize));

        var key = (entry.Layer, cell, entry.Moving);

        if (!_groups.TryGetValue(key, out Group group))
        {
            group = new Group { Key = key };
            Node2D root = RootFor(entry.Layer);

            group.Cast = CreateBatch(group, root, false);
            group.Contact = CreateBatch(group, root, true);
            _groups.Add(key, group);

            string current = _layers?.Current ?? WorldLayerId.Surface;
            group.Contact.Visible = entry.Layer == current;
            group.Cast.Visible = entry.Layer == current &&
                current == WorldLayerId.Surface;
        }

        entry.Group = group;
        group.Entries.Add(entry);
        MarkDirty(group);
    }

    // =========================================================
    // Keep drawing nodes grouped beneath the appropriate terrain layer.
    private Node2D RootFor(string layer)
    {
        if (layer == WorldLayerId.Surface) return _surfaceRoot;
        if (_layerRoots.TryGetValue(layer, out Node2D root) &&
            GodotObject.IsInstanceValid(root)) return root;

        WorldLayerRuntime runtime = WorldLayerRuntime.Find(this)
            ?? throw new InvalidOperationException("Missing layer runtime for shadows.");
        root = new Node2D { Name = "SharedGroundShadows" };
        runtime.GetUnderground(layer).Root.AddChild(root);
        _layerRoots[layer] = root;
        return root;
    }

    // =========================================================
    // Use shared materials and retain the existing ground-shadow draw order.
    private GroundShadowBatch CreateBatch(
        Group group, Node2D root, bool contact)
    {
        GroundShadowBatch batch = new()
        {
            Name = $"{(contact ? "Contact" : "Cast")}_" +
                $"{group.Key.Cell.X}_{group.Key.Cell.Y}_" +
                $"{(group.Key.Moving ? "Actors" : "Static")}",
            Contact = contact,
            ContactTexture = _contactTexture,
            Entries = group.Entries,
            ZAsRelative = false,
            ZIndex = -1,
            Material = contact ? _contactMaterial : _sunMaterial,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear
        };

        root.AddChild(batch);
        return batch;
    }

    // =========================================================
    // Queue a static batch once, regardless of how many records changed.
    private void MarkDirty(Group group)
    {
        if (group == null || !group.Alive ||
            group.Key.Moving || group.Dirty)
            return;

        group.Dirty = true;
        _dirty.Enqueue(group);
    }

    // =========================================================
    // Release empty batches when their owning world objects unload.
    private void DetachFromGroup(GroundShadow.Entry entry)
    {
        Group group = entry.Group;
        if (group == null) return;

        group.Entries.Remove(entry);
        entry.Group = null;

        if (group.Entries.Count == 0)
        {
            group.Alive = false;
            _groups.Remove(group.Key);
            group.Cast.QueueFree();
            group.Contact.QueueFree();
        }
        else
            MarkDirty(group);
    }

    // =========================================================
    // Remove harvested, dead or retired owners without scanning every record.
    private void RemoveOwner(Node2D owner)
    {
        if (_stopping ||
            !_owners.TryGetValue(owner, out List<GroundShadow.Entry> entries))
            return;

        _owners.Remove(owner);

        if (_ownerExit.Remove(owner, out Action callback))
            owner.TreeExiting -= callback;

        foreach (GroundShadow.Entry entry in entries)
        {
            entry.Alive = false;
            _static.Remove(entry);
            if (entry.Moving) _moving.Remove(entry);
            DetachFromGroup(entry);
        }
    }

    // =========================================================
    // Disconnect callbacks and release world-owned drawing batches.
    public override void _ExitTree()
    {
        _stopping = true;

        foreach (var pair in _ownerExit)
            if (GodotObject.IsInstanceValid(pair.Key))
                pair.Key.TreeExiting -= pair.Value;

        foreach (var pair in _existing)
            if (GodotObject.IsInstanceValid(pair.Key))
                pair.Key.TreeExiting -= pair.Value.Exit;

        foreach (Group group in _groups.Values)
        {
            group.Alive = false;
            if (GodotObject.IsInstanceValid(group.Cast))
                group.Cast.QueueFree();
            if (GodotObject.IsInstanceValid(group.Contact))
                group.Contact.QueueFree();
        }

        _owners.Clear();
        _ownerExit.Clear();
        _existing.Clear();
        _groups.Clear();
        _moving.Clear();
        _static.Clear();
        _pending.Clear();
        _dirty.Clear();

        if (GodotObject.IsInstanceValid(_surfaceRoot))
            _surfaceRoot.QueueFree();
        foreach (Node2D root in _layerRoots.Values)
            if (GodotObject.IsInstanceValid(root)) root.QueueFree();
        _layerRoots.Clear();
    }

    #endregion
}
