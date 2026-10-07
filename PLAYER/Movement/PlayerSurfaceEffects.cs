// Applies liquid resistance, immersion artwork and timed environmental damage.
// Player drives updates so exposure is processed exactly once per physics frame.
using Godot;

public partial class PlayerSurfaceEffects : Node
{
    #region Configuration
    [Export] public Shader SubmersionShader { get; set; }
    #endregion

    #region State
    public float MovementMultiplier { get; private set; } = 1f;
    private Player _player;
    private Health _health;
    private SurfaceWorld _world;
    private Sprite2D _artwork;
    private Material _originalMaterial;
    private ShaderMaterial _material;
    private LiquidDefinition _exposureLiquid;
    private float _damageRate;
    private double _damageElapsed, _damageAmount;
    private bool _triedArtwork, _submerged;
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve actor systems without starting another update loop.
    public override void _Ready()
    {
        _player = GetParent().GetParent<Player>();
        _health = GetParent().GetNode<Health>("Health");
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Restore artwork only when this component replaced its material.
    public override void _ExitTree()
    {
        if (_material != null && GodotObject.IsInstanceValid(_artwork))
            _artwork.Material = _originalMaterial;
    }
    #endregion

    #region Sampling And Artwork
// =========================================================
// Apply surface liquid effects only while occupying the surface world.
public void UpdateState(TerrainVisual visual)
{
    _world ??= SurfaceWorld.Find(this);

    bool surface = WorldLayerMember.For(_player) == WorldLayer.Surface;
    SurfaceWorld.SurfaceSample sample = !surface || _world == null
        ? new SurfaceWorld.SurfaceSample(1f, 0f, Colors.White)
        : _world.Sample(_player.GlobalPosition);

    MovementMultiplier = sample.MovementMultiplier;

    if (_exposureLiquid != sample.ExposureLiquid)
    {
        _exposureLiquid = sample.ExposureLiquid;
        _damageElapsed = _damageAmount = 0.0;
    }
    _damageRate = sample.DamagePerSecond;

    if (!_triedArtwork && visual != null)
    {
        _triedArtwork = true;
        _artwork = visual.GetNodeOrNull<Sprite2D>("Artwork");

        if (_artwork != null && SubmersionShader != null &&
            _artwork.Material == PlaceholderAtlas.BakedMaterial)
        {
            _originalMaterial = _artwork.Material;
            _material = new ShaderMaterial { Shader = SubmersionShader };
            _artwork.Material = _material;
        }
        else
            GD.PushWarning(
                "Surface effects: immersion masking requires the baked player sprite.");
    }

    if (_material == null || visual == null) return;

    bool wet = sample.SubmersionPixels > 0.01f;
    if (wet != _submerged)
    {
        _submerged = wet;
        _material.SetShaderParameter("submerged", wet);
    }

    if (!wet) return;
    _material.SetShaderParameter(
        "waterline_y", visual.GlobalPosition.Y - sample.SubmersionPixels);
    _material.SetShaderParameter("water_color", sample.Tint);
}
    #endregion

    #region Exposure
    // =========================================================
    // Accumulate fractional exposure and apply damage once per elapsed second.
    public void TickExposure(double delta)
    {
        if (!_health.IsAlive || _exposureLiquid == null || _damageRate <= 0f)
        {
            _damageElapsed = _damageAmount = 0.0;
            return;
        }

        _damageElapsed += delta;
        _damageAmount += _damageRate * delta;
        if (_damageElapsed < 1.0) return;

        _damageElapsed %= 1.0;
        int amount = (int)System.Math.Min(
            int.MaxValue, System.Math.Floor(_damageAmount));
        if (amount <= 0) return;

        _damageAmount -= amount;
        _health.DamageEnvironment(
            amount, _exposureLiquid.ExposureDamageType);
    }
    #endregion
}