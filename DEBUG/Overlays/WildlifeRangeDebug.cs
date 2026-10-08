// Displays wildlife wander areas and herd anchors using cached circle geometry.
// Drawings are independent of creature flipping and only update while enabled.
using Godot;
using System;

public partial class WildlifeRangeDebug : Node2D
{
    #region State
    private Entity _entity;
    private EntityHerd _herd;
    private bool _enabled;
    private float _radius = -1f;
    private Vector2[] _circle = Array.Empty<Vector2>();
    private Vector2 _herdOffset;
    private string _label = "";

    private const int Segments = 64;
    private static readonly Color WanderColour = new("#68dce8");
    private static readonly Color HerdColour = new("#e8bd68");
    #endregion

    #region Lifecycle
    // =========================================================
    // Draw above world artwork without inheriting the parent's scale or flip.
    public override void _Ready()
    {
        TopLevel = true;
        ZIndex = 4093;
        ZAsRelative = false;
        Visible = false;
        SetProcess(false);
    }

    // =========================================================
    // Follow a moving wander centre without rebuilding its cached circle.
    public override void _Process(double delta)
    {
        UpdatePresentation();
    }
    #endregion

    #region Configuration
// =========================================================
// Display the creature's current solo or shared herd wander radius.
public void Configure(Entity entity, bool enabled)
{
    _entity = entity;
    _herd = null;

    string mode = entity.HasHerd ? "herd wander" : "solo wander";
    SetGeometry(entity.ActiveWanderRadius,
        $"{entity.Definition.DisplayName} {mode}");

    SetEnabled(enabled);
}

    // =========================================================
    // Display the herd's fixed home anchor and roaming radius.
    public void Configure(EntityHerd herd, bool enabled)
    {
        _entity = null;
        _herd = herd;
        SetGeometry(herd.RoamRadius, $"Herd: {herd.HerdId}");
        SetEnabled(enabled);

        Vector2 offset = herd.GlobalPosition - herd.Home;
        if (offset == _herdOffset) return;

        _herdOffset = offset;
        QueueRedraw();
    }

    // =========================================================
    // Rebuild circle vertices only when the radius or label changes.
    private void SetGeometry(float radius, string label)
    {
        if (_radius == radius && _label == label) return;
        _radius = radius;
        _label = label;
        _circle = Array.Empty<Vector2>();

        if (float.IsFinite(radius) && radius > 0f)
        {
            _circle = new Vector2[Segments + 1];
            for (int i = 0; i < Segments; i++)
                _circle[i] = Vector2.FromAngle(
                    Mathf.Tau * i / Segments) * radius;

            _circle[Segments] = _circle[0];
        }

        QueueRedraw();
    }

    // =========================================================
    // Suspend debug processing while retaining the cached drawing.
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        SetProcess(enabled);
        UpdatePresentation();
    }

    // =========================================================
    // Restrict presentation to the active world layer and living creatures.
    private void UpdatePresentation()
    {
        Node2D owner = GodotObject.IsInstanceValid(_entity)
            ? _entity : _herd;

        if (!_enabled || !GodotObject.IsInstanceValid(owner) ||
            !owner.IsInsideTree() || owner.IsQueuedForDeletion())
        {
            Visible = false;
            return;
        }

        WorldLayer current =
            WorldLayerController.Find(this)?.Current ?? WorldLayer.Surface;

        bool alive = _entity == null ||
            _entity.Health?.IsAlive == true;

        Visible = alive && owner.IsVisibleInTree() &&
            WorldLayerMember.For(owner) == current;

        Vector2 centre = _entity != null
            ? _entity.Centre : _herd.Home;
        GlobalTransform = new Transform2D(0f, centre);
    }
    #endregion

    #region Drawing
    // =========================================================
    // Draw the retained wander circle or herd anchor and moving centre.
    public override void _Draw()
    {
        bool herd = _herd != null;
        Color colour = herd ? HerdColour : WanderColour;

        if (_circle.Length > 1)
            DrawPolyline(_circle, colour, 1.5f, true);

        DrawLine(new Vector2(-7, 0), new Vector2(7, 0), colour, 2f);
        DrawLine(new Vector2(0, -7), new Vector2(0, 7), colour, 2f);
        DrawString(ThemeDB.FallbackFont, new Vector2(10, -10),
            _label, HorizontalAlignment.Left, -1, 14, colour);

        if (!herd) return;

        DrawLine(Vector2.Zero, _herdOffset, colour, 1f, true);
        DrawCircle(_herdOffset, 5f, colour);
    }
    #endregion
}