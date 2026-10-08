// Projects cached sprite silhouettes onto the owner's ground plane.
// Moving actors follow each frame; static objects refresh on staggered checks.
using Godot;

public partial class GroundShadow : Node2D
{
    #region State
    private Node2D _owner;
    private TerrainVisual _visual;
    private Sprite2D _source;
    private Sprite2D _cast;
    private Sprite2D _contact;
    private GroundShadowSettings _settings;
    private WorldLighting _lighting;
    private bool _moving;

    private Texture2D _lastTexture;
    private int _lastFrame = -1;
    private int _lastColumns;
    private int _lastRows;
    private Rect2 _lastRegion;
    private bool _lastRegionEnabled;
    #endregion

    #region Installation
    // =========================================================
    // Install shared shadows without duplicating existing obstacle polygons.
    public static void Attach(
        Node2D owner, TerrainVisual visual, VisualDefinition definition)
    {
        GroundShadowSettings settings = definition?.Shadows;
        GroundShadowMode mode =
            settings?.Mode ?? GroundShadowMode.Automatic;

        if (owner is Obstacle)
        {
            if (mode == GroundShadowMode.Automatic) return;
            owner.SetMeta("shared_shadow_override", true);
        }

        if (mode == GroundShadowMode.Disabled) return;

        bool shortGrass = owner is Grass grass &&
            grass.Definition.BakedHeight == GrassHeight.Short;

        bool cast = mode == GroundShadowMode.Sprite ||
            (mode == GroundShadowMode.Automatic && !shortGrass);

        bool contact = mode == GroundShadowMode.ContactOnly ||
            (settings?.ContactEnabled ?? true);

        InstallSprites(owner, visual,
            visual.GetNode<Node>("Artwork"), settings, cast, contact);
    }

    // =========================================================
    // Support imported scenes containing several Sprite2D artwork pieces.
    private static void InstallSprites(
        Node2D owner, TerrainVisual visual, Node artwork,
        GroundShadowSettings settings, bool cast, bool contact)
    {
        if (artwork is Sprite2D sprite && sprite.Texture != null)
        {
            owner.AddChild(new GroundShadow
            {
                Name = "GroundShadow",
                _owner = owner,
                _visual = visual,
                _source = sprite,
                _settings = settings,
                _moving = visual.FollowMovement,
                _cast = cast ? NewSprite("CastShadow") : null,
                _contact = contact ? NewSprite("ContactShadow") : null
            });

            // One root contact patch is enough for a multipart scene.
            contact = false;
        }

        foreach (Node child in artwork.GetChildren())
            InstallSprites(owner, visual, child, settings, cast, contact);
    }

    // =========================================================
    // Keep every shadow beneath world artwork with inexpensive filtering.
    private static Sprite2D NewSprite(string name)
    {
        return new Sprite2D
        {
            Name = name,
            Centered = false,
            ZAsRelative = false,
            ZIndex = -1,
            TextureFilter = TextureFilterEnum.Linear
        };
    }
    #endregion

    #region Lifecycle
    // =========================================================
    // Bind shared resources once and select the appropriate update schedule.
    public override void _Ready()
    {
        _lighting = WorldLighting.Find(this);
        ProcessPriority = 90;

        if (_cast != null)
        {
            RefreshMask();
            if (_cast.Texture != null)
                SunShadow.Attach(this, _cast);
            else
            {
                _cast.Free();
                _cast = null;
            }
        }

        if (_contact != null)
        {
            _contact.Texture = GroundShadowTextures.GetContact();
            _contact.Centered = true;
            SunShadow.Attach(this, _contact, false);
        }

        UpdateProjection();
        SetProcess(_moving);
        SetPhysicsProcess(!_moving);
    }

    // =========================================================
    // Follow moving artwork after its terrain-height update.
    public override void _Process(double delta)
    {
        UpdateProjection();
    }

    // =========================================================
    // Refresh static scale and lighting edits without per-frame work.
    public override void _PhysicsProcess(double delta)
    {
        if (StaggeredUpdate.DueSeconds(this, 0.1, 19))
            UpdateProjection();
    }
    #endregion

    #region Projection
    // =========================================================
    // Reuse the same silhouette until the artwork's region or frame changes.
    private void RefreshMask()
    {
        if (_cast == null) return;

        if (_lastTexture == _source.Texture &&
            _lastFrame == _source.Frame &&
            _lastColumns == _source.Hframes &&
            _lastRows == _source.Vframes &&
            _lastRegion == _source.RegionRect &&
            _lastRegionEnabled == _source.RegionEnabled)
            return;

        _lastTexture = _source.Texture;
        _lastFrame = _source.Frame;
        _lastColumns = _source.Hframes;
        _lastRows = _source.Vframes;
        _lastRegion = _source.RegionRect;
        _lastRegionEnabled = _source.RegionEnabled;

        _cast.Texture = _source.Texture == null
            ? null : GroundShadowTextures.GetSilhouette(_source);
    }

    // =========================================================
    // Flatten sprite height into a fixed ground projection away from the sun.
    private void UpdateProjection()
    {
        if (!GodotObject.IsInstanceValid(_source) ||
            !GodotObject.IsInstanceValid(_visual))
            return;

        WorldLightingSettings profile =
            _lighting?.Settings ?? WorldLighting.DefaultProfile;

        Vector2 anchor = _visual.GlobalPosition;

        // Jumping lifts artwork while the shadow stays on the ground.
        if (_owner is Player player)
            anchor += Vector2.Down * player.JumpHeight;

        anchor += _settings?.GroundOffset ?? Vector2.Zero;

        float size = Mathf.Max(0.1f, _visual.GlobalScale.Abs().X);

        if (_contact != null)
        {
            Vector2 dimensions =
                _settings?.ContactSize ?? DefaultContactSize();

            _contact.GlobalTransform = new Transform2D(
                new Vector2(dimensions.X * size / 64f, 0f),
                new Vector2(0f, dimensions.Y * size / 32f),
                anchor);

            _contact.SelfModulate = new Color(
                0.015f, 0.025f, 0.04f,
                Mathf.Clamp(_settings?.ContactOpacity ?? 0.3f, 0f, 1f));
        }

        RefreshMask();
        if (_cast?.Texture == null) return;

        float defaultLength = _owner is Grass ? 0.3f : 0.6f;
        float defaultOpacity = _owner is Grass ? 0.4f : 0.75f;

        float length = Mathf.Max(0.01f,
            profile.ShadowLength *
            (_settings?.LengthMultiplier ?? defaultLength));

        Vector2 direction = -profile.GetDirection();

        // Avoid a degenerate flat transform with a purely horizontal sun.
        if (Mathf.Abs(direction.Y) < 0.05f)
            direction.Y = direction.Y < 0f ? -0.05f : 0.05f;

        Rect2 rectangle = _source.GetRect();
        Vector2 units = rectangle.Size / _cast.Texture.GetSize();
        Transform2D artwork = _source.GlobalTransform;

        Vector2 origin = artwork * rectangle.Position -
            _visual.GlobalPosition;

        _cast.GlobalTransform = new Transform2D(
            Project(artwork.X, direction, length) * units.X,
            Project(artwork.Y, direction, length) * units.Y,
            anchor + Project(origin, direction, length));

        _cast.FlipH = _source.FlipH;
        _cast.FlipV = _source.FlipV;
        _cast.SelfModulate = new Color(
            0.015f, 0.025f, 0.04f,
            Mathf.Clamp(profile.ShadowOpacity *
                (_settings?.OpacityMultiplier ?? defaultOpacity), 0f, 1f));
    }

    // =========================================================
    // Preserve horizontal silhouette width and project illustrated height.
    private static Vector2 Project(
        Vector2 point, Vector2 direction, float length)
    {
        return new Vector2(point.X, 0f) -
            direction * point.Y * length;
    }

    // =========================================================
    // Use smaller contact patches for grass and ordinary moving actors.
    private Vector2 DefaultContactSize()
    {
        if (_owner is Grass) return new Vector2(24, 10);
        if (_owner is Plant) return new Vector2(64, 24);
        return new Vector2(36, 14);
    }
    #endregion
}