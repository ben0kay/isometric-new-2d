// Fades foreground trees and buildings covering the player's visible body.
// Uses one spatially indexed manager instead of processing each object separately.
using Godot;
using System.Collections.Generic;

public partial class PlayerObstructionFade : Node
{
    #region State
    private const float CellSize = 256f;
    private const float CheckInterval = 0.1f;

    private sealed class Entry
    {
        public Node2D Host;
        public Node2D Artwork;
        public Sprite2D Sprite;
        public Vector2[] Outline;

        public Color OriginalModulate;
        public Rect2 Bounds;

        public readonly List<Vector2I> Cells = new();

        public float Alpha = 1f;
        public float Target = 1f;
    }

    private GlobalConfig _config;
    private Player _player;
    private TerrainVisual _playerVisual;
    private float _checkRemaining;

    private readonly List<Entry> _pending = new();
    private readonly Dictionary<Vector2I, HashSet<Entry>> _cells = new();
    private readonly HashSet<Entry> _candidates = new();
    private readonly HashSet<Entry> _active = new();
    private readonly List<Entry> _finished = new();
    #endregion

    #region Attachment
    // =========================================================
    // Find or create the world's shared obstruction fade manager.
    private static PlayerObstructionFade FindManager(Node2D host)
    {
        WorldConfig config = WorldConfig.TryFind(host);

        if (config == null)
        {
            GD.PushWarning(
                "Obstruction fading requires the world's CONFIG node.");
            return null;
        }

        PlayerObstructionFade manager =
            config.GetNodeOrNull<PlayerObstructionFade>(
                "PlayerObstructionFade");

        if (manager == null)
        {
            manager = new PlayerObstructionFade
            {
                Name = "PlayerObstructionFade",
                _config = config
            };
            config.AddChild(manager);
        }

        return manager;
    }

    // =========================================================
    // Register player artwork or an existing tree sprite.
    public static void Attach(Node2D host, TerrainVisual visual)
    {
        if (host is not Player && host is not Tree) return;

        PlayerObstructionFade manager = FindManager(host);
        if (manager == null) return;

        if (host is Player player)
        {
            manager._player = player;
            manager._playerVisual = visual;
            return;
        }

        Sprite2D sprite = visual.GetNodeOrNull<Sprite2D>("Artwork");

        if (sprite == null)
        {
            GD.PushWarning(
                $"Obstruction fading skipped {host.Name}: " +
                "Artwork must be Sprite2D.");
            return;
        }

        manager.Register(new Entry
        {
            Host = host,
            Artwork = sprite,
            Sprite = sprite,
            OriginalModulate = sprite.Modulate
        });
    }

    // =========================================================
    // Register procedural building artwork using its local silhouette.
    public static void Attach(
        Node2D host, Node2D artwork, Vector2[] outline)
    {
        if (outline == null || outline.Length < 3)
        {
            GD.PushWarning(
                $"Obstruction fading skipped {host.Name}: " +
                "provide an outline with at least three points.");
            return;
        }

        foreach (Vector2 point in outline)
        {
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
            {
                GD.PushWarning(
                    $"Obstruction fading skipped {host.Name}: " +
                    "outline contains an invalid point.");
                return;
            }
        }

        PlayerObstructionFade manager = FindManager(host);
        if (manager == null) return;

        manager.Register(new Entry
        {
            Host = host,
            Artwork = artwork,
            Outline = (Vector2[])outline.Clone(),
            OriginalModulate = artwork.Modulate
        });
    }

    // =========================================================
    // Queue indexing and remove the entry when its world object exits.
    private void Register(Entry entry)
    {
        _pending.Add(entry);

        entry.Host.TreeExiting += () =>
        {
            if (GodotObject.IsInstanceValid(this))
                Remove(entry);
        };
    }
    #endregion

    #region Lifecycle
    // =========================================================
    // Check nearby artwork periodically and smoothly animate active fades.
    public override void _Process(double delta)
    {
        IndexPending();

        _checkRemaining -= (float)delta;

        if (_checkRemaining <= 0f)
        {
            _checkRemaining = CheckInterval;
            CheckObstructions();
        }

        float step = (float)delta /
            Mathf.Max(0.01f, _config.ObstructionFadeSeconds);

        _finished.Clear();

        foreach (Entry entry in _active)
        {
            if (!GodotObject.IsInstanceValid(entry.Artwork))
            {
                _finished.Add(entry);
                continue;
            }

            entry.Alpha = Mathf.MoveToward(
                entry.Alpha, entry.Target, step);

            // Modulate also fades children of procedural artwork nodes.
            Color color = entry.OriginalModulate;
            color.A *= entry.Alpha;
            entry.Artwork.Modulate = color;

            if (entry.Alpha == 1f && entry.Target == 1f)
                _finished.Add(entry);
        }

        foreach (Entry entry in _finished)
            _active.Remove(entry);
    }
    #endregion

    #region Spatial Index
    // =========================================================
    // Convert a visible world position into a spatial lookup cell.
    private static Vector2I CellAt(Vector2 point)
    {
        return new Vector2I(
            Mathf.FloorToInt(point.X / CellSize),
            Mathf.FloorToInt(point.Y / CellSize));
    }

    // =========================================================
    // Index static artwork after its terrain offset and scale are applied.
    private void IndexPending()
    {
        foreach (Entry entry in _pending)
        {
            if (!GodotObject.IsInstanceValid(entry.Artwork))
                continue;

            Vector2[] points;

            if (entry.Sprite != null)
            {
                Rect2 rect = entry.Sprite.GetRect();

                points = new[]
                {
                    rect.Position,
                    rect.Position + new Vector2(rect.Size.X, 0f),
                    rect.End,
                    rect.Position + new Vector2(0f, rect.Size.Y)
                };
            }
            else
            {
                points = entry.Outline;
            }

            Vector2 firstPoint = entry.Artwork.ToGlobal(points[0]);
            Vector2 min = firstPoint;
            Vector2 max = firstPoint;

            foreach (Vector2 point in points)
            {
                Vector2 worldPoint = entry.Artwork.ToGlobal(point);

                min = new Vector2(
                    Mathf.Min(min.X, worldPoint.X),
                    Mathf.Min(min.Y, worldPoint.Y));

                max = new Vector2(
                    Mathf.Max(max.X, worldPoint.X),
                    Mathf.Max(max.Y, worldPoint.Y));
            }

            // Allow for tree wind displacement and small artwork outlines.
            entry.Bounds = new Rect2(min, max - min).Grow(8f);

            Vector2I first = CellAt(entry.Bounds.Position);
            Vector2I last = CellAt(entry.Bounds.End);

            for (int y = first.Y; y <= last.Y; y++)
            for (int x = first.X; x <= last.X; x++)
            {
                Vector2I key = new(x, y);

                if (!_cells.TryGetValue(key, out HashSet<Entry> bucket))
                {
                    bucket = new HashSet<Entry>();
                    _cells.Add(key, bucket);
                }

                bucket.Add(entry);
                entry.Cells.Add(key);
            }
        }

        _pending.Clear();
    }

    // =========================================================
    // Remove destroyed or streamed-out objects from the spatial index.
    private void Remove(Entry entry)
    {
        _pending.Remove(entry);
        _active.Remove(entry);
        _candidates.Remove(entry);

        if (GodotObject.IsInstanceValid(entry.Artwork))
            entry.Artwork.Modulate = entry.OriginalModulate;

        foreach (Vector2I key in entry.Cells)
        {
            if (!_cells.TryGetValue(key, out HashSet<Entry> bucket))
                continue;

            bucket.Remove(entry);

            if (bucket.Count == 0)
                _cells.Remove(key);
        }

        entry.Cells.Clear();
    }
    #endregion

    #region Obstruction Checks
    // =========================================================
    // Find nearby foreground objects whose artwork covers the player.
    private void CheckObstructions()
    {
        foreach (Entry entry in _active)
            entry.Target = 1f;

        _candidates.Clear();

        if (!_config.ObstructionFadingEnabled ||
            !GodotObject.IsInstanceValid(_player) ||
            !GodotObject.IsInstanceValid(_playerVisual) ||
            !_player.IsVisibleInTree())
            return;

        _playerVisual.UpdateHeight();

        Vector2 centre = _playerVisual.ToGlobal(
            new Vector2(0f, -30f));

        Rect2 body = new Rect2(
            centre - new Vector2(12f, 24f),
            new Vector2(24f, 48f)).Grow(8f);

        Vector2I first = CellAt(body.Position);
        Vector2I last = CellAt(body.End);

        for (int y = first.Y; y <= last.Y; y++)
        for (int x = first.X; x <= last.X; x++)
        {
            if (!_cells.TryGetValue(
                new Vector2I(x, y), out HashSet<Entry> bucket))
                continue;

            foreach (Entry entry in bucket)
                _candidates.Add(entry);
        }

        float opacity = Mathf.Clamp(
            _config.ObstructingSpriteOpacityPercent / 100f,
            0f, 1f);

        foreach (Entry entry in _candidates)
        {
            if (!GodotObject.IsInstanceValid(entry.Host) ||
                !GodotObject.IsInstanceValid(entry.Artwork) ||
                !entry.Host.IsVisibleInTree() ||
                !entry.Artwork.IsVisibleInTree() ||
                !entry.Bounds.Intersects(body))
                continue;

            // World draw order uses logical ground Y.
            if (entry.Host.GlobalPosition.Y <= _player.GlobalPosition.Y)
                continue;

            if (!CoversPlayer(entry))
                continue;

            entry.Target = opacity;
            _active.Add(entry);
        }
    }

    // =========================================================
    // Sample the player's body against sprite opacity or a building silhouette.
    private bool CoversPlayer(Entry entry)
    {
        for (int y = 0; y < 3; y++)
        for (int x = -1; x <= 1; x++)
        {
            Vector2 point = _playerVisual.ToGlobal(
                new Vector2(x * 8f, -50f + y * 18f));

            Vector2 localPoint = entry.Artwork.ToLocal(point);

            if (entry.Sprite != null)
            {
                if (entry.Sprite.IsPixelOpaque(localPoint))
                    return true;
            }
            else if (Geometry2D.IsPointInPolygon(
                localPoint, entry.Outline))
            {
                return true;
            }
        }

        return false;
    }
    #endregion
}