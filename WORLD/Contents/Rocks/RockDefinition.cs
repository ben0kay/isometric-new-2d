// Defines a solid rock's base dimensions and footprint depth.
// Species variation and optional artwork are inherited from the shared definition.
using Godot;
using System;

[Tool, GlobalClass]
public partial class RockDefinition : WorldObjectDefinition
{
    #region Shape
    [ExportGroup("Shape")]
    [Export] public Vector2 WidthRange { get; set; } = new(64, 104);
    [Export] public Vector2 HeightRange { get; set; } = new(56, 96);
    [Export] public float FootprintDepthRatio { get; set; } = 0.5f;
    #endregion
    #region Artwork Variants
    // Empty only for retained legacy definitions; new families author explicit variants.
    [ExportGroup("Variants")]
    [Export] public bool RequireImportedVariants { get; set; }
    [Export] public Godot.Collections.Array<RockVariantDefinition> Variants { get; set; } = new();

    // =========================================================
    // Validate imported families once during world initialization, before spawning.
    public void ValidateVariants()
    {
        if (Variants == null) throw new InvalidOperationException($"{Id}: missing rock variant array.");
        if (Variants.Count == 0)
        {
            if (RequireImportedVariants)
                throw new InvalidOperationException($"{Id}: imported rock variants are required; primitive fallback is disabled.");
            return;
        }
        if (!float.IsFinite(SizeRange.X) || !float.IsFinite(SizeRange.Y) ||
            SizeRange.X < 0.1f || SizeRange.Y < SizeRange.X ||
            !float.IsFinite(MirrorChance) || MirrorChance < 0 || MirrorChance > 1)
            throw new InvalidOperationException($"{Id}: invalid size or mirror settings.");
        float total = 0;
        foreach (RockVariantDefinition variant in Variants)
        {
            if (variant == null) throw new InvalidOperationException($"{Id}: missing rock variant.");
            variant.Validate(); total += variant.SpawnWeight;
        }
        if (!float.IsFinite(total) || total <= 0)
            throw new InvalidOperationException($"{Id}: at least one rock variant must have a positive spawn weight.");
    }

    // =========================================================
    // Choose using the chunk RNG; no image scans or per-instance resource allocation.
    public RockVariantDefinition RollVariant(RandomNumberGenerator rng)
    {
        if (Variants.Count == 0) return null;
        float total = 0;
        foreach (RockVariantDefinition variant in Variants) total += variant.SpawnWeight;
        float roll = rng.Randf() * total;
        RockVariantDefinition last = null;
        foreach (RockVariantDefinition variant in Variants)
        {
            if (variant.SpawnWeight <= 0) continue;
            last = variant; roll -= variant.SpawnWeight;
            if (roll < 0) return variant;
        }
        return last;
    }
    #endregion
}