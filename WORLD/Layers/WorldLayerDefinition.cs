// Describes one world layer independently from its runtime world instance.
using Godot;
using System;

public enum WorldLayerKind { Surface, Underground }

[Tool, GlobalClass]
public partial class WorldLayerDefinition : Resource
{
    #region Identity
    [ExportGroup("Identity")]
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export] public int DepthIndex { get; set; }
    [Export] public WorldLayerKind Kind { get; set; }
    #endregion

    #region Generation
    [ExportGroup("Generation")]
    [Export] public BiomeCatalog Biomes { get; set; }
    [Export] public CaveGenerationSettings CaveSettings { get; set; }
    #endregion

    #region Validation
    // =========================================================
    // Validate ownership and required resources before world construction.
    public void Validate()
    {
        WorldLayerId.Validate(Id);

        if (string.IsNullOrWhiteSpace(DisplayName) || Biomes == null)
            throw new InvalidOperationException(
                $"Layer '{Id}' requires a display name and biome catalog.");

        if (Kind == WorldLayerKind.Surface)
        {
            if (Id != WorldLayerId.Surface || DepthIndex != 0 ||
                CaveSettings != null)
                throw new InvalidOperationException(
                    "The surface layer must use ID 'surface', depth 0 and no cave settings.");
        }
        else if (Kind == WorldLayerKind.Underground)
        {
            if (Id == WorldLayerId.Surface || DepthIndex < 1 ||
                CaveSettings == null)
                throw new InvalidOperationException(
                    $"Underground layer '{Id}' requires a positive depth and cave settings.");
        }
        else
            throw new InvalidOperationException($"Layer '{Id}' has an invalid kind.");

        foreach (BiomeDefinition biome in Biomes.GetEnabledBiomes())
            if ((biome is CaveBiomeDefinition) !=
                (Kind == WorldLayerKind.Underground))
                throw new InvalidOperationException(
                    $"Biome '{biome.Id}' belongs to the wrong generation kind for '{Id}'.");
    }

    // =========================================================
    // Give the runtime its own settings while the definition owns biome selection.
    public CaveGenerationSettings CreateCaveSettings()
    {
        if (Kind != WorldLayerKind.Underground || CaveSettings == null)
            throw new InvalidOperationException($"Layer '{Id}' is not underground.");

        CaveGenerationSettings settings =
            (CaveGenerationSettings)CaveSettings.Duplicate();
        settings.Biomes = Biomes;
        settings.Validate();
        return settings;
    }
    #endregion
}