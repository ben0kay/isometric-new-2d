// Applies surface movement penalties and waterline masking to the current player sprite.
// Queries are driven by Player movement rather than another per-frame process.
using Godot;

public partial class PlayerSurfaceEffects : Node
{
    #region Configuration
    [Export] public Shader SubmersionShader { get; set; }
    #endregion

    #region State
    public float MovementMultiplier { get; private set; } = 1f;
    private Player _player;
    private SurfaceWorld _world;
    private Sprite2D _artwork;
    private Material _originalMaterial;
    private ShaderMaterial _material;
    private bool _triedArtwork, _submerged;
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve the player without starting an independent update loop.
    public override void _Ready()
    {
        _player = GetParent().GetParent<Player>();
        SetProcess(false);
    }

    // =========================================================
    // Restore artwork ownership when this component is removed.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_artwork))
            _artwork.Material = _originalMaterial;
    }
    #endregion

    #region Updates
    // =========================================================
    // Combine surface effects with normal movement and update the visible waterline.
    public void UpdateState(TerrainVisual visual)
    {
        _world ??= SurfaceWorld.Find(this);
        SurfaceWorld.SurfaceSample sample = _world == null
            ? new SurfaceWorld.SurfaceSample(1f, 0f, Colors.White)
            : _world.Sample(_player.GlobalPosition);

        MovementMultiplier = sample.MovementMultiplier;

        if (!_triedArtwork && visual != null)
        {
            _triedArtwork = true;
            _artwork = visual.GetNodeOrNull<Sprite2D>("Artwork");

            // Preserve custom artwork shaders instead of replacing them.
            if (_artwork != null && SubmersionShader != null &&
                _artwork.Material == PlaceholderAtlas.BakedMaterial)
            {
                _originalMaterial = _artwork.Material;
                _material = new ShaderMaterial { Shader = SubmersionShader };
                _artwork.Material = _material;
            }
            else
                GD.PushWarning(
                    "Surface effects: movement is enabled, but waterline masking " +
                    "requires the current baked player sprite.");
        }

        if (_material == null || visual == null) return;

        bool wet = sample.SubmersionPixels > 0.01f;
        if (wet != _submerged)
        {
            _submerged = wet;
            _material.SetShaderParameter("submerged", wet);
        }

        if (!wet) return;
        _material.SetShaderParameter("waterline_y",
            visual.GlobalPosition.Y - sample.SubmersionPixels);
        _material.SetShaderParameter("water_color", sample.Tint);
    }
    #endregion
}