// Assigns shared surface materials to sprites and primitive world drawings.
// Existing wind shaders keep their movement and use the common lighting include.
using Godot;
using System.Collections.Generic;

public sealed class WorldLightingMaterials
{
    #region Cache
    private readonly Dictionary<string, ShaderMaterial> _materials = new();
   private readonly Dictionary<(VisualDefinition Definition, int Height),
    ShaderMaterial> _vegetation = new();
    private Shader _ordinaryShader, _premultShader, _vegetationShader;
    #endregion

    #region Attachment
    // =========================================================
    // Attach lighting to an artwork branch, including sprites in custom scenes.
    public static void Attach(
        Node context, Node artwork, VisualDefinition definition = null,
        Rect2? drawingBounds = null)
    {
        WorldLighting lighting = WorldLighting.Find(context);
        if (lighting == null || artwork == null) return;

        WorldLightingMaterials cache = lighting.Materials;
        cache.AttachBranch(artwork, definition);

        Rect2 bounds = drawingBounds
            ?? definition?.Lighting?.DrawingBounds
            ?? new Rect2();

        if (artwork is CanvasItem canvas && artwork is not Sprite2D &&
            bounds.Size.X > 0f && bounds.Size.Y > 0f)
        {
            canvas.Material = cache.GetMaterial(
                canvas.Material, bounds, definition);
        }
    }

// =========================================================
// Attach surface lighting and wind to single images or folder variants.
private void AttachBranch(Node node, VisualDefinition definition)
{
    if (node is Sprite2D sprite && sprite.Texture != null)
    {
        Material source = sprite.Material;

        string folder = (definition?.ImageFolder ?? "").Trim().TrimEnd('/');
        string path = sprite.Texture.ResourcePath ?? "";

        bool folderImage = folder.Length > 0 &&
            path.StartsWith(folder + "/", System.StringComparison.Ordinal) &&
            path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase);

        bool singleImage = definition?.Image != null &&
            sprite.Texture == definition.Image;

        if (definition?.Vegetation != null && (singleImage || folderImage))
            source = GetVegetationMaterial(definition, sprite.Texture);

        Rect2 bounds = sprite.GetRect();
        Rect2 custom = definition?.Lighting?.DrawingBounds ?? new Rect2();

        if (custom.Size.X > 0f && custom.Size.Y > 0f)
            bounds = custom;

        sprite.Material = GetMaterial(source, bounds, definition);
    }

    foreach (Node child in node.GetChildren())
        AttachBranch(child, definition);
}
    #endregion

    #region Materials
// =========================================================
// Reuse one surface material for matching artwork styles and local bounds.
private Material GetMaterial(
    Material source, Rect2 bounds, VisualDefinition definition)
{
    ShaderMaterial existing = source as ShaderMaterial;

    if (existing?.Shader != null &&
        !existing.Shader.Code.Contains("WorldSurface.gdshaderinc"))
    {
        GD.PushWarning(
            $"World lighting: custom shader '{existing.Shader.ResourcePath}' " +
            "must include WorldSurface.gdshaderinc to retain its effects.");
        return source;
    }

    ulong sourceId = source?.GetInstanceId() ?? 0;
    ulong definitionId = definition?.GetInstanceId() ?? 0;

    string key = System.FormattableString.Invariant(
        $"{sourceId}|{definitionId}|{bounds.Position.X:R}|{bounds.Position.Y:R}|{bounds.Size.X:R}|{bounds.Size.Y:R}");

    if (_materials.TryGetValue(key, out ShaderMaterial cached))
        return cached;

    ShaderMaterial material;
    if (existing != null)
        material = (ShaderMaterial)existing.Duplicate();
    else
    {
        bool premult = source is CanvasItemMaterial canvas &&
            canvas.BlendMode ==
                CanvasItemMaterial.BlendModeEnum.PremultAlpha;

        _ordinaryShader ??= GD.Load<Shader>(
            "res://VISUALS/Lighting/WorldSurface.gdshader");
        _premultShader ??= GD.Load<Shader>(
            "res://VISUALS/Lighting/WorldSurfacePremult.gdshader");

        material = new ShaderMaterial
        {
            Shader = premult ? _premultShader : _ordinaryShader
        };
    }

    ConfigureSurface(material, bounds, definition?.Lighting);
    _materials.Add(key, material);
    return material;
}

// =========================================================
// Share wind materials by visual definition and actual sprite height.
private ShaderMaterial GetVegetationMaterial(
    VisualDefinition definition, Texture2D texture)
{
    int height = Mathf.Max(1, texture.GetHeight());
    var key = (definition, height);

    if (_vegetation.TryGetValue(key, out ShaderMaterial cached))
        return cached;

    _vegetationShader ??= GD.Load<Shader>(
        "res://VISUALS/Vegetation/ImportedVegetation.gdshader");

    VegetationVisualSettings settings = definition.Vegetation;
    ShaderMaterial material = new() { Shader = _vegetationShader };

    material.SetShaderParameter("wind_enabled", settings.WindEnabled);
    material.SetShaderParameter("wind_strength",
        Mathf.Max(0f, settings.WindStrength));
    material.SetShaderParameter("wind_speed",
        Mathf.Max(0f, settings.WindSpeed));
    material.SetShaderParameter("brush_enabled", settings.BrushingEnabled);
    material.SetShaderParameter("brush_amount",
        Mathf.Max(0f, settings.BrushStrength));
    material.SetShaderParameter("image_height", (float)height);
    material.SetShaderParameter("image_anchor_y",
        Mathf.Max(0.001f, definition.ImageAnchor.Y));

    _vegetation.Add(key, material);
    return material;
}

    // =========================================================
    // Configure artwork geometry and optional normal maps independently of light.
    private static void ConfigureSurface(
        ShaderMaterial material, Rect2 bounds, VisualLightingSettings settings)
    {
        material.SetShaderParameter("surface_rect", new Vector4(
            bounds.Position.X, bounds.Position.Y,
            Mathf.Max(0.001f, bounds.Size.X),
            Mathf.Max(0.001f, bounds.Size.Y)));

        material.SetShaderParameter("surface_lighting_enabled",
            settings?.Enabled ?? true);
        material.SetShaderParameter("surface_normal_enabled",
            settings?.NormalMap != null);

        if (settings?.NormalMap != null)
            material.SetShaderParameter("surface_normal", settings.NormalMap);

        material.SetShaderParameter("surface_normal_strength",
            Mathf.Max(0f, settings?.NormalStrength ?? 1f));
        material.SetShaderParameter("surface_shape_strength",
            Mathf.Max(0f, settings?.ShapeStrength ?? 1f));
        material.SetShaderParameter("surface_brightness",
            Mathf.Max(0f, settings?.Brightness ?? 1f));
    }

    // =========================================================
    // Drop cached materials when their owning world closes.
    public void Clear()
    {
        _materials.Clear();
        _vegetation.Clear();
    }
    #endregion
}