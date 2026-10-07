// Sweeps visible projectile segments against actor combat hitboxes.
// Existing ground-path obstruction checks remain separate from actor targeting.
using Godot;

public partial class Projectile : Node2D
{
    #region State
    private static ImageTexture _texture;
    private readonly PhysicsRayQueryParameters2D _actorQuery = new();
    private readonly PhysicsRayQueryParameters2D _worldQuery = new();
    private readonly Godot.Collections.Array<Rid> _excluded = new();

    private ProjectilePool _pool;
    private Sprite2D _sprite;
    private Vector2 _direction;
    private float _speed, _remaining, _visualHeight, _launchHeight;
    private int _damage;
    private DamageType _damageType;
    private bool _active;
    #endregion

    #region Lifecycle
    // =========================================================
    // Prepare reusable queries and run after actors synchronize their hitboxes.
    public override void _Ready()
    {
        EnsureTexture();
        ProcessPhysicsPriority = 20;

        _sprite = new Sprite2D
        {
            Name = "Visual",
            Texture = _texture,
            TextureFilter = TextureFilterEnum.Nearest
        };
        AddChild(_sprite);

        _actorQuery.CollideWithBodies = false;
        _actorQuery.CollideWithAreas = true;
        _actorQuery.HitFromInside = true;

        _worldQuery.CollideWithBodies = true;
        _worldQuery.CollideWithAreas = false;
        _worldQuery.HitFromInside = true;
        _worldQuery.CollisionMask = 1u;

        Hide();
        SetPhysicsProcess(false);
    }

// =========================================================
// Release reusable query wrappers when the world is destroyed.
public override void _ExitTree()
{
    _actorQuery.Dispose();
    _worldQuery.Dispose();
    _excluded.Clear();
}

// =========================================================
// Reject shots from previous layers before checking any impacts.
public override void _PhysicsProcess(double delta)
{
    if (!_active) return;

    int epoch = WorldLayerController.Find(this)?.Epoch ?? 0;
    int launchedEpoch = HasMeta("world_layer_epoch")
        ? GetMeta("world_layer_epoch").AsInt32() : 0;

    if (epoch != launchedEpoch)
    {
        Release();
        return;
    }

    float step = Mathf.Min((float)delta, _remaining);
    if (step <= 0f) { Release(); return; }

    Vector2 start = GlobalPosition;
    Vector2 next = start + _direction * _speed * step;
    Vector2 visualOffset = Vector2.Up * (_launchHeight + _visualHeight);

    _worldQuery.From = start;
    _worldQuery.To = next;
    var worldHit = GetWorld2D().DirectSpaceState.IntersectRay(_worldQuery);
    var actorHit = FindActorHit(start + visualOffset, next + visualOffset);

    float worldDistance = float.PositiveInfinity;
    float actorDistance = float.PositiveInfinity;

    if (worldHit.Count > 0)
        worldDistance =
            (worldHit["position"].AsVector2() - start).Dot(_direction);

    if (actorHit.Count > 0)
        actorDistance = (
            actorHit["position"].AsVector2() -
            (start + visualOffset)).Dot(_direction);

    if (worldHit.Count > 0 && worldDistance <= actorDistance)
    {
        Release();
        return;
    }

    if (actorHit.Count > 0)
    {
        CombatHitbox hitbox =
            actorHit["collider"].AsGodotObject() as CombatHitbox;
        hitbox?.ReceiveDamage(_damage, _damageType);
        Release();
        return;
    }

    GlobalPosition = next;
    _remaining -= step;
    if (_remaining <= 0f) Release();
}

// =========================================================
// Capture the current layer epoch so shots cannot cross a later transition.
public void Launch(
    ProjectilePool pool, Vector2 origin, Vector2 direction,
    ProjectileAttack attack, uint mask, float sourceHeight)
{
    _pool = pool;
    GlobalPosition = origin;
    _direction = direction.Normalized();
    _speed = Mathf.Max(1f, attack.Speed);
    _remaining = Mathf.Max(0.05f, attack.Lifetime);
    _visualHeight = attack.VisualHeight;
    _launchHeight = sourceHeight;
    _damage = System.Math.Max(0, attack.Damage);
    _damageType = attack.Type;

    SetMeta("world_layer_epoch",
        WorldLayerController.Find(this)?.Epoch ?? 0);

    _actorQuery.CollisionMask = mask;
    _excluded.Clear();
    _actorQuery.Exclude = _excluded;

    _sprite.Modulate = attack.Tint;
    _sprite.Rotation = _direction.Angle();
    _sprite.Position = Vector2.Up * (_launchHeight + _visualHeight);

    _active = true;
    Show();
    SetPhysicsProcess(true);
}

    // =========================================================
    // Return this shot to the existing pool exactly once.
    private void Release()
    {
        if (!_active) return;
        _active = false;
        Hide();
        SetPhysicsProcess(false);
        _pool.Recycle(this);
    }
    #endregion

    #region Actor Queries
    // =========================================================
    // Ignore inactive or recently removed targets without absorbing the shot.
    private Godot.Collections.Dictionary FindActorHit(
        Vector2 from, Vector2 to)
    {
        _actorQuery.From = from;
        _actorQuery.To = to;
        _excluded.Clear();
        _actorQuery.Exclude = _excluded;

        var space = GetWorld2D().DirectSpaceState;

        for (int attempt = 0; attempt < 64; attempt++)
        {
            var hit = space.IntersectRay(_actorQuery);
            if (hit.Count == 0) return hit;

            CombatHitbox hitbox =
                hit["collider"].AsGodotObject() as CombatHitbox;

            if (hitbox != null && hitbox.CanReceiveProjectile)
                return hit;

            _excluded.Add(hit["rid"].AsRid());
            _actorQuery.Exclude = _excluded;
        }

        return new Godot.Collections.Dictionary();
    }
    #endregion

    #region Artwork
    // =========================================================
    // Generate one bolt texture shared by every pooled projectile.
    private static void EnsureTexture()
    {
        if (_texture != null) return;

        using Image image = Image.CreateEmpty(
            16, 8, false, Image.Format.Rgba8);
        image.Fill(Colors.Transparent);

        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 16; x++)
        {
            float dx = (x - 7.5f) / 7.5f;
            float dy = (y - 3.5f) / 3.5f;
            float radius = dx * dx + dy * dy;
            if (radius > 1f) continue;

            image.SetPixel(x, y, new Color(
                1f, 1f, 1f, radius < 0.45f ? 1f : 0.25f));
        }

        _texture = ImageTexture.CreateFromImage(image);
    }
    #endregion
}