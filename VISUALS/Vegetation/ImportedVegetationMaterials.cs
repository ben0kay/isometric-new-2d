// Shares imported vegetation materials within one world.
// Lighting updates are periodic; wind and brushing execute in the GPU shader.
using Godot;
using System.Collections.Generic;

public partial class ImportedVegetationMaterials : Node
{
    #region State
    private sealed class Entry
    {
        public ShaderMaterial Material;
        public int Users;
    }

    private readonly Dictionary<VisualDefinition, Entry> _entries = new();
    private WorldAtmosphere _atmosphere;
    private Shader _shader;
    private double _remaining;

    private Vector2 _brushPosition;
    private Vector2 _brushDirection = Vector2.Right;
    private Vector2 _brushRadius = new(42f, 24f);
    private float _brushStrength;
    #endregion

    #region World Service
    // =========================================================
    // Find this world's existing material service.
    private static ImportedVegetationMaterials Find(Node context)
    {
        WorldConfig config = WorldConfig.TryFind(context);
        return config?.GetParent()
            .GetNodeOrNull<ImportedVegetationMaterials>(
                "Systems/ImportedVegetationMaterials");
    }

    // =========================================================
    // Create one service only when an imported vegetation image needs it.
    private static ImportedVegetationMaterials GetOrCreate(Node context)
    {
        ImportedVegetationMaterials existing = Find(context);
        if (existing != null) return existing;

        Node systems = WorldConfig.Find(context).GetParent().GetNode("Systems");
        ImportedVegetationMaterials service = new()
        {
            Name = "ImportedVegetationMaterials"
        };
        systems.AddChild(service);
        return service;
    }

    // =========================================================
    // Assign a shared material and release its reference on sprite removal.
    public static void Attach(
        Node context, Sprite2D sprite, VisualDefinition definition)
    {
        if (definition?.Vegetation == null) return;

        ImportedVegetationMaterials service = GetOrCreate(context);
        Entry entry = service.Acquire(definition);
        sprite.Material = entry.Material;

        sprite.TreeExiting += () =>
        {
            if (GodotObject.IsInstanceValid(service) && service.IsInsideTree())
                service.Release(definition);
        };
    }

    // =========================================================
    // Feed the existing world's player-brush information to shared materials.
    public static void UpdateBrush(
        Node context, Vector2 position, Vector2 direction,
        Vector2 radius, float strength)
    {
        ImportedVegetationMaterials service = Find(context);
        if (service == null) return;

        service._brushPosition = position;
        service._brushDirection = direction;
        service._brushRadius = radius;
        service._brushStrength = strength;

        foreach (Entry entry in service._entries.Values)
            service.ApplyBrush(entry.Material);
    }
    #endregion

    #region Lifecycle
    // =========================================================
    // Start idle until the first material is requested.
    public override void _Ready()
    {
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Refresh a small number of shared lighting materials five times per second.
    public override void _Process(double delta)
    {
        _remaining -= delta;
        if (_remaining > 0.0) return;
        _remaining = 0.2;

        ResolveAtmosphere();
        foreach (var pair in _entries)
            ApplySettings(pair.Key, pair.Value.Material);
    }

    // =========================================================
    // Drop the world's registry when its scene ends.
    public override void _ExitTree()
    {
        _entries.Clear();
    }
    #endregion

    #region Materials
    // =========================================================
    // Cache one material for all sprites using the same visual definition.
    private Entry Acquire(VisualDefinition definition)
    {
        if (!_entries.TryGetValue(definition, out Entry entry))
        {
            _shader ??= GD.Load<Shader>(
                "res://VISUALS/Drawings/Vegetation/ImportedVegetation.gdshader");

            entry = new Entry
            {
                Material = new ShaderMaterial { Shader = _shader }
            };
            _entries.Add(definition, entry);

            ResolveAtmosphere();
            ApplySettings(definition, entry.Material);
            ApplyBrush(entry.Material);
        }

        entry.Users++;
        SetProcess(true);
        return entry;
    }

    // =========================================================
    // Stop material updates when no imported vegetation remains.
    private void Release(VisualDefinition definition)
    {
        if (!_entries.TryGetValue(definition, out Entry entry)) return;
        if (--entry.Users <= 0) _entries.Remove(definition);
        SetProcess(_entries.Count > 0);
    }

    // =========================================================
    // Resolve the atmosphere belonging to this world's Systems node.
    private void ResolveAtmosphere()
    {
        if (GodotObject.IsInstanceValid(_atmosphere)) return;

        foreach (Node child in GetParent().GetChildren())
            if (child is WorldAtmosphere atmosphere)
            {
                _atmosphere = atmosphere;
                return;
            }
    }

    // =========================================================
    // Apply resource tuning and the world's existing sunlight settings.
    private void ApplySettings(
        VisualDefinition definition, ShaderMaterial material)
    {
        VegetationVisualSettings settings = definition.Vegetation;
        if (settings == null) return;

        bool atmosphere = GodotObject.IsInstanceValid(_atmosphere);

        material.SetShaderParameter("lighting_enabled", settings.LightingEnabled);
        material.SetShaderParameter("normal_enabled", settings.NormalMap != null);
        if (settings.NormalMap != null)
            material.SetShaderParameter("surface_normal", settings.NormalMap);

        material.SetShaderParameter("normal_strength",
            Mathf.Clamp(settings.NormalStrength, 0f, 2f));
        material.SetShaderParameter("brightness", Mathf.Max(0f, settings.Brightness));

        material.SetShaderParameter("sun_direction",
            atmosphere ? _atmosphere.LightDirection
                : new Vector2(-1f, -0.7f).Normalized());
        material.SetShaderParameter("sun_color",
            atmosphere ? _atmosphere.SunTint : Colors.White);
        material.SetShaderParameter("ambient_strength",
            atmosphere ? _atmosphere.FaceAmbient : 0.65f);
        material.SetShaderParameter("sunlight_strength",
            atmosphere ? _atmosphere.FaceSunStrength : 0.55f);

        material.SetShaderParameter("wind_enabled", settings.WindEnabled);
        material.SetShaderParameter("wind_strength",
            Mathf.Max(0f, settings.WindStrength));
        material.SetShaderParameter("wind_speed",
            Mathf.Max(0f, settings.WindSpeed));
        material.SetShaderParameter("brush_enabled", settings.BrushingEnabled);
        material.SetShaderParameter("brush_amount",
            Mathf.Max(0f, settings.BrushStrength));
        material.SetShaderParameter("image_height",
            Mathf.Max(1f, definition.Image.GetHeight()));
        material.SetShaderParameter("image_anchor_y",
            Mathf.Max(0.001f, definition.ImageAnchor.Y));
    }

    // =========================================================
    // Update contact uniforms without iterating individual world plants.
    private void ApplyBrush(ShaderMaterial material)
    {
        material.SetShaderParameter("brush_position", _brushPosition);
        material.SetShaderParameter("brush_direction", _brushDirection);
        material.SetShaderParameter("brush_radius", _brushRadius);
        material.SetShaderParameter("brush_strength", _brushStrength);
    }
    #endregion
}