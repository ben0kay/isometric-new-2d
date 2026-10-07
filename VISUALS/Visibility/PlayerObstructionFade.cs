// Fades foreground trees covering the player's visible body.
// Uses a spatial index and shared manager instead of a process per tree.
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
        public Sprite2D Sprite;
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
    // Register player artwork or an eligible tree with the world's manager.
    public static void Attach(Node2D host, TerrainVisual visual)
    {
        if (host is not Player && host is not Tree) return;

        WorldConfig config = WorldConfig.TryFind(host);
        if (config == null)
        {
            GD.PushWarning("Obstruction fading requires the world's CONFIG node.");
            return;
        }

        PlayerObstructionFade manager =
            config.GetNodeOrNull<PlayerObstructionFade>("PlayerObstructionFade");

        if (manager == null)
        {
            manager = new PlayerObstructionFade
            {
                Name = "PlayerObstructionFade",
                _config = config
            };
            config.AddChild(manager);
        }

        if (host is Player player)
        {
            manager._player = player;
            manager._playerVisual = visual;
            return;
        }

        // Current baked trees and custom image trees use Sprite2D artwork.
        Sprite2D sprite = visual.GetNodeOrNull<Sprite2D>("Artwork");
        if (sprite == null)
        {
            GD.PushWarning(
                $"Obstruction fading skipped {host.Name}: Artwork must be Sprite2D.");
            return;
        }

        Entry entry = new() { Host = host, Sprite = sprite };
        manager._pending.Add(entry);
        host.TreeExiting += () => manager.Remove(entry);
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
            if (!GodotObject.IsInstanceValid(entry.Sprite))
            {
                _finished.Add(entry);
                continue;
            }

            entry.Alpha = Mathf.MoveToward(entry.Alpha, entry.Target, step);
            Color color = entry.Sprite.SelfModulate;
            color.A = entry.Alpha;
            entry.Sprite.SelfModulate = color;

            if (entry.Alpha == 1f && entry.Target == 1f)
                _finished.Add(entry);
        }

        foreach (Entry entry in _finished) _active.Remove(entry);
    }
    #endregion

    #region Spatial Index
    // =========================================================
    // Convert a visible world position into a lookup cell.
    private static Vector2I CellAt(Vector2 point)
    {
        return new Vector2I(
            Mathf.FloorToInt(point.X / CellSize),
            Mathf.FloorToInt(point.Y / CellSize));
    }

    // =========================================================
    // Index after creation so tree size and mirroring have been applied.
    private void IndexPending()
    {
        foreach (Entry entry in _pending)
        {
            Sprite2D sprite = entry.Sprite;
            if (!GodotObject.IsInstanceValid(sprite)) continue;

            Rect2 rect = sprite.GetRect();
            Vector2 a = sprite.ToGlobal(rect.Position);
            Vector2 b = sprite.ToGlobal(
                rect.Position + new Vector2(rect.Size.X, 0f));
            Vector2 c = sprite.ToGlobal(rect.End);
            Vector2 d = sprite.ToGlobal(
                rect.Position + new Vector2(0f, rect.Size.Y));

            Vector2 min = new(
                Mathf.Min(Mathf.Min(a.X, b.X), Mathf.Min(c.X, d.X)),
                Mathf.Min(Mathf.Min(a.Y, b.Y), Mathf.Min(c.Y, d.Y)));
            Vector2 max = new(
                Mathf.Max(Mathf.Max(a.X, b.X), Mathf.Max(c.X, d.X)),
                Mathf.Max(Mathf.Max(a.Y, b.Y), Mathf.Max(c.Y, d.Y)));

            // Small allowance for the tree shader's wind displacement.
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
    // Remove harvested or streamed-out trees from every indexed cell.
    private void Remove(Entry entry)
    {
        _pending.Remove(entry);
        _active.Remove(entry);
        _candidates.Remove(entry);

        foreach (Vector2I key in entry.Cells)
        {
            if (!_cells.TryGetValue(key, out HashSet<Entry> bucket)) continue;
            bucket.Remove(entry);
            if (bucket.Count == 0) _cells.Remove(key);
        }
    }
    #endregion

    #region Obstruction Checks
    // =========================================================
    // Find nearby foreground trees whose opaque artwork covers the player.
    private void CheckObstructions()
    {
        foreach (Entry entry in _active) entry.Target = 1f;
        _candidates.Clear();

        if (!_config.ObstructionFadingEnabled ||
            !GodotObject.IsInstanceValid(_player) ||
            !GodotObject.IsInstanceValid(_playerVisual) ||
            !_player.IsVisibleInTree())
            return;

        _playerVisual.UpdateHeight();

        Vector2 centre = _playerVisual.ToGlobal(new Vector2(0f, -30f));
        Rect2 body = new Rect2(centre - new Vector2(12f, 24f),
            new Vector2(24f, 48f)).Grow(8f);

        Vector2I first = CellAt(body.Position);
        Vector2I last = CellAt(body.End);

        for (int y = first.Y; y <= last.Y; y++)
        for (int x = first.X; x <= last.X; x++)
        {
            if (!_cells.TryGetValue(new Vector2I(x, y),
                    out HashSet<Entry> bucket)) continue;
            foreach (Entry entry in bucket) _candidates.Add(entry);
        }

        float opacity = Mathf.Clamp(
            _config.ObstructingSpriteOpacityPercent / 100f, 0f, 1f);

        foreach (Entry entry in _candidates)
        {
            if (!entry.Host.IsVisibleInTree() ||
                !entry.Bounds.Intersects(body)) continue;

            // Existing world objects use logical ground Y for their draw order.
            if (entry.Host.GlobalPosition.Y <= _player.GlobalPosition.Y)
                continue;

            if (!CoversPlayer(entry.Sprite)) continue;
            entry.Target = opacity;
            _active.Add(entry);
        }
    }

    // =========================================================
    // Sample head, torso and lower body against actual sprite opacity.
    private bool CoversPlayer(Sprite2D sprite)
    {
        for (int y = 0; y < 3; y++)
        for (int x = -1; x <= 1; x++)
        {
            Vector2 point = _playerVisual.ToGlobal(
                new Vector2(x * 8f, -50f + y * 18f));

            if (sprite.IsPixelOpaque(sprite.ToLocal(point)))
                return true;
        }
        return false;
    }
    #endregion
}