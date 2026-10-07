// Sweeps projectile segments against actor hitboxes and height-based ground cover.
// Short obstacles remain solid for walking but allow shots to pass.
using Godot;

public partial class Projectile : Node2D
{
    #region State
    private static ImageTexture _texture;
    private readonly PhysicsRayQueryParameters2D _actorQuery = new();
    private readonly PhysicsRayQueryParameters2D _worldQuery = new();
    private readonly Godot.Collections.Array<Rid> _excluded = new();
    private readonly Godot.Collections.Array<Rid> _worldExcluded = new();

    private ProjectilePool _pool;
    private Sprite2D _sprite;
    private Vector2 _direction;
    private float _speed, _remaining, _visualHeight, _launchHeight;
    private float _coverHeight;
    private WorldLayer _layer;
    private int _damage;
    private DamageType _damageType;
    private bool _active;
    #endregion

    #region Lifecycle
    // =========================================================
    // Prepare reusable queries and run after actor hitboxes synchronize.
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

        Hide();
        SetPhysicsProcess(false);
    }

// =========================================================
// Dispose query wrappers and clear their reusable exclusion lists.
public override void _ExitTree()
{
    _actorQuery.Dispose();
    _worldQuery.Dispose();
    _excluded.Clear();
    _worldExcluded.Clear();
}

    // =========================================================
    // Sweep cover and actor hits, resolving whichever occurs first.
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
        Vector2 visualOffset =
            Vector2.Up * (_launchHeight + _visualHeight);

        var worldHit = CombatCover.FindHit(
            GetWorld2D().DirectSpaceState,
            _worldQuery, _worldExcluded,
            start, next, _coverHeight, _layer);

        var actorHit = FindActorHit(
            start + visualOffset, next + visualOffset);

        float worldDistance = float.PositiveInfinity;
        float actorDistance = float.PositiveInfinity;

        if (worldHit.Count > 0)
            worldDistance =
                (worldHit["position"].AsVector2() - start).Dot(_direction);

        if (actorHit.Count > 0)
            actorDistance =
                (actorHit["position"].AsVector2() -
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
    // Reset every pooled shot's captured combat and layer settings.
    public void Launch(
        ProjectilePool pool, Vector2 origin, Vector2 direction,
        ProjectileAttack attack, uint mask, float sourceHeight,
        float coverHeight = CombatCover.DefaultHeight,
        WorldLayer layer = WorldLayer.Surface)
    {
        _pool = pool;
        GlobalPosition = origin;
        _direction = direction.Normalized();
        _speed = Mathf.Max(1f, attack.Speed);
        _remaining = Mathf.Max(0.05f, attack.Lifetime);
        _visualHeight = attack.VisualHeight;
        _launchHeight = sourceHeight;
        _coverHeight = float.IsFinite(coverHeight) && coverHeight > 0f
            ? coverHeight : CombatCover.DefaultHeight;
        _layer = layer;
        _damage = System.Math.Max(0, attack.Damage);
        _damageType = attack.Type;

        SetMeta("world_layer_epoch",
            WorldLayerController.Find(this)?.Epoch ?? 0);

        _actorQuery.CollisionMask = mask;
        _excluded.Clear();
        _worldExcluded.Clear();
        _actorQuery.Exclude = _excluded;

        _sprite.Modulate = attack.Tint;
        _sprite.Rotation = _direction.Angle();
        _sprite.Position =
            Vector2.Up * (_launchHeight + _visualHeight);

        _active = true;
        Show();
        SetPhysicsProcess(true);
    }

    // =========================================================
    // Return an active shot to the pool exactly once.
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
    // Skip inactive actors and hitboxes belonging to another layer.
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

            if (hitbox != null && hitbox.CanReceiveProjectile &&
                hitbox.MatchesLayer(_layer))
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