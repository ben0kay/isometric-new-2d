// Drives the editor-editable cutout rig without changing Player movement or jump height.
using Godot;

public partial class PlayerRigAnimator : Node2D
{
    private Player _player;
    private AnimationPlayer _animations;
    private string _current = "";

    public override void _Ready()
    {
        _player = GetParent()?.GetParent() as Player;
        _animations = GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
        // Standalone editor scene: use AnimationPlayer's preview controls.
        SetProcess(_player != null && _animations != null);
        if (_player != null && _animations != null)
            PlayState("idle");
    }

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(_player) ||
            _player.IsQueuedForDeletion() || _animations == null)
            return;

        bool moving = _player.Velocity.LengthSquared() > 4f;
        bool running = moving && _player.Controls?.SprintHeld == true;

        string next = _player.IsAirborne ? "jump"
            : !moving ? "idle"
            : running ? "run" : "walk";

        PlayState(next);
    }

    private void PlayState(string name)
    {
        if (_current == name || !_animations.HasAnimation(name))
            return;

        _current = name;
        _animations.Play(name, customBlend: 0.08);
    }
}
