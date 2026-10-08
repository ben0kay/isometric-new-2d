// Selects a custom visual scene, custom image, or baked atlas fallback.
// Keeps artwork placement separate from collision, movement and facing.
using Godot;

public partial class TerrainVisual : Node2D
{
    #region State
    private TerrainElevation _elevation;
    private Node2D _host;
    private Vector2 _lastPosition;
    private bool _sampled;
    public bool FollowMovement { get; set; }
    #endregion

    #region Creation
// =========================================================
// Attach artwork, lighting, shared shadows, hitboxes and obstruction fading.
public static TerrainVisual Attach(
    Node2D host, Rect2 region, Vector2 origin, Vector2 scale,
    bool followMovement, VisualDefinition definition = null,
    Texture2D fallbackTexture = null, Material fallbackMaterial = null)
{
    TerrainVisual visual = new()
    {
        Name = "Visual",
        FollowMovement = followMovement
    };

    if (!visual.TryAttachCustom(definition))
    {
        if (fallbackTexture == null)
            visual.AttachBaked(region, origin, scale);
        else
        {
            visual.AddChild(new Sprite2D
            {
                Name = "Artwork",
                Texture = new AtlasTexture
                {
                    Atlas = fallbackTexture,
                    Region = region
                },
                Centered = false,
                Offset = origin,
                Scale = scale,
                Material = fallbackMaterial ?? PlaceholderAtlas.BakedMaterial,
                TextureFilter = TextureFilterEnum.Linear
            });
        }
    }

    host.AddChild(visual);

    WorldLightingMaterials.Attach(
        host, visual.GetNode<Node>("Artwork"), definition);

    GroundShadow.Attach(host, visual, definition);
    CombatHitbox.Attach(host, visual);

    bool fade = definition?.Vegetation?.FadeBehindPlayer
        ?? (host is Tree || host is Plant);

    PlayerObstructionFade.Attach(host, visual, fade);

    return visual;
}

// =========================================================
    // Prefer a custom scene, then an imported image, then the baked fallback.
    private bool TryAttachCustom(VisualDefinition definition)
    {
        if (definition == null) return false;

        if (definition.VisualScene != null)
        {
            Node instance = definition.VisualScene.Instantiate();
            if (instance is Node2D artwork)
            {
                artwork.Name = "Artwork";
                artwork.Position += definition.Offset;
                artwork.Scale *= definition.ArtworkScale;
                AddChild(artwork);
                return true;
            }

            instance.Free();
            GD.PushWarning(
                "VisualDefinition requires a Node2D scene root. " +
                "Trying image or baked fallback.");
        }

        if (definition.Image == null) return false;

        Vector2 size = definition.Image.GetSize();
        AddChild(new Sprite2D
        {
            Name = "Artwork",
            Texture = definition.Image,
            Centered = false,
            Offset = new Vector2(
                -size.X * definition.ImageAnchor.X,
                -size.Y * definition.ImageAnchor.Y),
            Position = definition.Offset,
            Scale = definition.ArtworkScale,
            TextureFilter = definition.ImageFilter
        });
        return true;
    }

    // =========================================================
    // Reuse the baked atlas and its premultiplied-alpha material.
    private void AttachBaked(Rect2 region, Vector2 origin, Vector2 scale)
    {
        AddChild(new Sprite2D
        {
            Name = "Artwork",
            Texture = new AtlasTexture
            {
                Atlas = PlaceholderAtlas.Texture,
                Region = region
            },
            Centered = false,
            Offset = origin,
            Scale = scale,
            Material = PlaceholderAtlas.BakedMaterial,
            TextureFilter = TextureFilterEnum.Nearest
        });
    }
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve terrain elevation and position the artwork for its owner.
    public override void _Ready()
    {
        _host = GetParent<Node2D>();
        _elevation = GetTree().GetFirstNodeInGroup(
            "terrain_elevation") as TerrainElevation;
        UpdateHeight();
        SetProcess(FollowMovement);
    }

    // =========================================================
    // Update moving artwork without rebuilding its sprite or scene.
    public override void _Process(double delta)
    {
        UpdateHeight();
    }

// =========================================================
// Follow the owner's active elevation provider and invalidate on layer changes.
public void UpdateHeight()
{
    if (_elevation == null)
    {
        _elevation = GetTree().GetFirstNodeInGroup(
            "terrain_elevation") as TerrainElevation;
        if (_elevation != null) _sampled = false;
    }

    int epoch = WorldLayerController.Find(this)?.Epoch ?? 0;
    bool changedLayer = !HasMeta("height_layer_epoch") ||
        GetMeta("height_layer_epoch").AsInt32() != epoch;

    Vector2 point = _host.GlobalPosition;
    if (_sampled && point == _lastPosition && !changedLayer) return;

    _sampled = true;
    _lastPosition = point;
    SetMeta("height_layer_epoch", epoch);

    float height = WorldLayerController.HeightFor(_host, point);
    Position = new Vector2(0f, -height);
}
    #endregion
}