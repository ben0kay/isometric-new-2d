// Tracks player jump height and draws a separate ground shadow.
// Player drives physics and artwork updates; no extra update loop is used.
using Godot;

public partial class PlayerJump : Node2D
{
    #region State
    public float Height { get; private set; }
    public bool IsAirborne { get; private set; }

    private readonly Vector2[] _shadowPoints = new Vector2[32];
    private Node2D _artwork;
    private Vector2 _artworkOrigin;
    private float _elapsed, _duration, _peak;
    private bool _rightHeld;
    private int _layerEpoch;
    #endregion

    #region Lifecycle
    // =========================================================
    // Build reusable shadow geometry and disable independent processing.
    public override void _Ready()
    {
        for (int i = 0; i < _shadowPoints.Length; i++)
        {
            float angle = Mathf.Tau * i / _shadowPoints.Length;
            _shadowPoints[i] = new Vector2(
                Mathf.Cos(angle) * 19f, Mathf.Sin(angle) * 8.55f);
        }

        _layerEpoch = WorldLayerController.Find(this)?.Epoch ?? 0;
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Remember the artwork's original offset before applying jump height.
    public void Bind(TerrainVisual visual)
    {
        _artwork = visual.GetNodeOrNull<Node2D>("Artwork");
        if (_artwork != null) _artworkOrigin = _artwork.Position;
    }
    #endregion

    #region Jump
    // =========================================================
    // Start once per RMB press and advance a smooth takeoff-and-landing arc.
    public void Tick(
        double delta, bool inputAllowed, bool alive,
        float peakHeight, float duration)
    {
        bool rightHeld = Input.IsMouseButtonPressed(MouseButton.Right);
        bool pressed = rightHeld && !_rightHeld;
        _rightHeld = rightHeld;

        int epoch = WorldLayerController.Find(this)?.Epoch ?? 0;
        if (!alive || epoch != _layerEpoch)
        {
            _layerEpoch = epoch;
            Reset();
            return;
        }

        if (pressed && inputAllowed && !IsAirborne)
        {
            _elapsed = 0f;
            _peak = Mathf.Max(1f, peakHeight);
            _duration = Mathf.Max(0.1f, duration);
            IsAirborne = true;
        }

        if (!IsAirborne) return;

        _elapsed = Mathf.Min(_duration, _elapsed + (float)delta);
        float progress = _elapsed / _duration;
        Height = 4f * _peak * progress * (1f - progress);

        if (_elapsed >= _duration)
        {
            Height = 0f;
            IsAirborne = false;
        }

        QueueRedraw();
    }

    // =========================================================
    // Reset airborne state after death or a world-layer transition.
    public void Reset()
    {
        Height = _elapsed = 0f;
        IsAirborne = false;
        if (_artwork != null) _artwork.Position = _artworkOrigin;
        QueueRedraw();
    }

    // =========================================================
    // Keep the shadow at terrain height and lift only the player's artwork.
    public void UpdatePose(TerrainVisual visual)
    {
        Position = visual.Position;
        if (_artwork != null)
            _artwork.Position = _artworkOrigin + Vector2.Up * Height;
    }
    #endregion

    #region Shadow
    // =========================================================
    // Draw cached oval geometry with a smaller, lighter airborne shadow.
    public override void _Draw()
    {
        float progress = _peak > 0f ? Height / _peak : 0f;
        float size = Mathf.Lerp(1f, 0.7f, progress);
        float opacity = Mathf.Lerp(0.35f, 0.2f, progress);

        DrawSetTransform(Vector2.Zero, 0f, Vector2.One * size);
        DrawColoredPolygon(_shadowPoints, new Color(0f, 0f, 0f, opacity));
    }
    #endregion
}