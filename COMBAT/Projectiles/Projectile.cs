// Moves a pooled projectile and checks its full travel segment for impacts.
// A shared texture is generated once; visual height follows the terrain surface.
using Godot;

public partial class Projectile : Node2D
{
    #region State
    private static ImageTexture _texture;
    private readonly PhysicsRayQueryParameters2D _query = new();
    private ProjectilePool _pool;
    private TerrainElevation _elevation;
    private Sprite2D _sprite;
    private Vector2 _direction;
    private float _speed, _remaining, _visualHeight;
    private int _damage;
    private bool _active;
    #endregion

    #region Lifecycle
    // =========================================================
    // Prepare reusable physics parameters and the shared projectile sprite.
    public override void _Ready()
    {
        EnsureTexture();
        _sprite = new Sprite2D
        {
            Name = "Visual",
            Texture = _texture,
            TextureFilter = TextureFilterEnum.Nearest
        };
        AddChild(_sprite);
        _query.CollideWithAreas = false;
        _query.HitFromInside = true;
        Hide();
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Release this instance's physics query when the world is destroyed.
    public override void _ExitTree()
    {
        _query.Dispose();
    }

    // =========================================================
    // Sweep the complete movement segment and resolve its first impact.
    public override void _PhysicsProcess(double delta)
    {
        if (!_active) return;
        float step = Mathf.Min((float)delta, _remaining);
        Vector2 next = GlobalPosition + _direction * _speed * step;
        _query.From = GlobalPosition;
        _query.To = next;

        var hit = GetWorld2D().DirectSpaceState.IntersectRay(_query);
        if (hit.Count > 0)
        {
            Node collider = hit["collider"].AsGodotObject() as Node;
            Health health = collider?.GetNodeOrNull<Health>("Systems/Health");
            health?.Damage(_damage);
            Release();
            return;
        }

        GlobalPosition = next;
        _remaining -= (float)delta;
        if (_remaining <= 0f) { Release(); return; }
        UpdateArtwork();
    }
    #endregion

    #region Pool Operations
    // =========================================================
    // Snapshot attack settings so existing bullets do not change with the resource.
    public void Launch(
        ProjectilePool pool, Vector2 origin, Vector2 direction,
        ProjectileAttack attack, uint mask)
    {
        _pool = pool;
        GlobalPosition = origin;
        _direction = direction.Normalized();
        _speed = Mathf.Max(1f, attack.Speed);
        _remaining = Mathf.Max(0.05f, attack.Lifetime);
        _visualHeight = attack.VisualHeight;
        _damage = System.Math.Max(0, attack.Damage);
        _query.CollisionMask = mask;
        _sprite.Modulate = attack.Tint;
        _sprite.Rotation = _direction.Angle();
        _elevation ??= GetTree().GetFirstNodeInGroup("terrain_elevation") as TerrainElevation;
        _active = true;
        UpdateArtwork();
        Show();
        SetPhysicsProcess(true);
    }

    // =========================================================
    // Deactivate once and return this instance for a later shot.
    private void Release()
    {
        if (!_active) return;
        _active = false;
        Hide();
        SetPhysicsProcess(false);
        _pool.Recycle(this);
    }
    #endregion

    #region Artwork
    // =========================================================
    // Position the sprite above its logical ground location.
    private void UpdateArtwork()
    {
        float height = _elevation != null
            ? _elevation.SampleWorldHeight(GlobalPosition) : 0f;
        _sprite.Position = new Vector2(0f, -height - _visualHeight);
    }

    // =========================================================
    // Generate one small white bolt texture for all projectile instances.
    private static void EnsureTexture()
    {
        if (_texture != null) return;
        using Image image = Image.CreateEmpty(16, 8, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);

        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 16; x++)
        {
            float dx = (x - 7.5f) / 7.5f, dy = (y - 3.5f) / 3.5f;
            float radius = dx * dx + dy * dy;
            if (radius > 1f) continue;
            image.SetPixel(x, y, new Color(1f, 1f, 1f, radius < 0.45f ? 1f : 0.25f));
        }
        _texture = ImageTexture.CreateFromImage(image);
    }
    #endregion
}