// Displays wildlife wander areas and herd anchors using cached circle geometry.
// Drawings are independent of creature flipping and only update while enabled.
using Godot;
using System;

public partial class WildlifeRangeDebug : Node2D
{
    #region State
    private Entity _entity;
private EntityGroup _group;
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
// Display the active area owned by the creature's wandering component.
public void Configure(Entity entity, bool enabled)
{
    _entity = entity;
    _group = null;

    string mode = entity.Wandering.UsesSharedArea ? "group wander" : "solo wander";
    SetGeometry(entity.Wandering.Radius,
        $"{entity.Definition.DisplayName} {mode}");
    SetEnabled(enabled);
}

// =========================================================
// Display a group's optional roaming anchor and moving centre.
public void Configure(EntityGroup group, bool enabled)
{
    _entity = null;
    _group = group;
    SetGeometry(group.Roaming.RoamRadius, group.DisplayName);
    SetEnabled(enabled);

    Vector2 offset = group.GlobalPosition - group.Roaming.Home;
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
// Present active-layer creatures and groups using their component-owned centres.
private void UpdatePresentation()
{
    Node2D owner = GodotObject.IsInstanceValid(_entity) ? _entity : _group;

    if (!_enabled || !GodotObject.IsInstanceValid(owner) ||
        !owner.IsInsideTree() || owner.IsQueuedForDeletion())
    {
        Visible = false;
        return;
    }

    WorldLayer current =
        WorldLayerController.Find(this)?.Current ?? WorldLayer.Surface;

    bool alive = _entity == null || _entity.Health?.IsAlive == true;
    Visible = alive && owner.IsVisibleInTree() &&
        WorldLayerMember.For(owner) == current;

    Vector2 centre = _entity != null
        ? _entity.Wandering.Centre : _group.Roaming.Home;
    GlobalTransform = new Transform2D(0f, centre);
}
    #endregion

    #region Drawing
// =========================================================
// Draw cached wander outlines or group anchors and moving centre markers.
public override void _Draw()
{
    bool group = _group != null;
    Color colour = group ? HerdColour : WanderColour;

    if (_circle.Length > 1)
        DrawPolyline(_circle, colour, 1.5f, true);

    DrawLine(new Vector2(-7, 0), new Vector2(7, 0), colour, 2f);
    DrawLine(new Vector2(0, -7), new Vector2(0, 7), colour, 2f);
    DrawString(ThemeDB.FallbackFont, new Vector2(10, -10),
        _label, HorizontalAlignment.Left, -1, 14, colour);

    if (!group) return;

    DrawLine(Vector2.Zero, _herdOffset, colour, 1f, true);
    DrawCircle(_herdOffset, 5f, colour);
}
    #endregion
}