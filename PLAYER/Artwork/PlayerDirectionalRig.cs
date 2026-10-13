// Chooses one visible pivot rig; world mirroring/elevation stay on TerrainVisual.
using Godot;

public partial class PlayerDirectionalRig : Node2D
{
    [Export] public Texture2D FrontShadow { get; set; }
    [Export] public Texture2D RearShadow { get; set; }
    private Player _player;
    private Node2D _front, _rear;
    private AnimationPlayer _frontAnimations, _rearAnimations;
    private Sprite2D _shadow;
    private bool _rearFacing;
    private string _state = "";
    public bool RearFacing => _rearFacing;

    public override void _Ready()
    {
        for (Node ancestor = GetParent(); ancestor != null; ancestor = ancestor.GetParent())
            if (ancestor is Player player) { _player = player; break; }
        _front = GetNode<Node2D>("Front");
        _rear = GetNode<Node2D>("Rear");
        _frontAnimations = _front.GetNode<AnimationPlayer>("AnimationPlayer");
        _rearAnimations = _rear.GetNode<AnimationPlayer>("AnimationPlayer");
        _shadow = GetNode<Sprite2D>("ShadowSource");
        UpdatePresentation(Vector2.Zero, false, false);
        SetProcess(_player != null);
    }

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(_player) || _player.IsQueuedForDeletion()) return;
        UpdatePresentation(_player.Velocity, _player.IsAirborne,
            _player.Controls?.SprintHeld == true);
    }

    // Screen-space movement: retain the previous front/rear view at rest or moving sideways.
    public void UpdatePresentation(Vector2 velocity, bool airborne, bool sprinting)
    {
        bool rear = _rearFacing;
        if (velocity.Y < -2f) rear = true;
        else if (velocity.Y > 2f) rear = false;
        bool moving = velocity.LengthSquared() > 4f;
        string state = airborne ? "jump" : !moving ? "idle" : sprinting ? "run" : "walk";
        if (state == _state && rear == _rearFacing) return;

        AnimationPlayer previous = _rearFacing ? _rearAnimations : _frontAnimations;
        double phase = previous.IsPlaying() ? previous.CurrentAnimationPosition : 0;
        bool keepPhase = rear != _rearFacing && state == _state;
        _rearFacing = rear;
        _front.Visible = !rear; _rear.Visible = rear;
        _frontAnimations.Active = !rear; _rearAnimations.Active = rear;
        _shadow.Texture = rear ? RearShadow : FrontShadow;
        AnimationPlayer active = rear ? _rearAnimations : _frontAnimations;
        active.Play(state, customBlend: 0.08);
        if (keepPhase)
            active.Seek(phase % active.GetAnimation(state).Length, update: true);
        _state = state;
    }
}
