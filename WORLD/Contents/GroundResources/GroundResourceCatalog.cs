// Groups ground-resource definitions in one editable catalog.
// Runtime deposit quantities are stored separately by GroundResourceWorld.
using Godot;
using System;
using System.Collections.Generic;

[Tool, GlobalClass]
public partial class GroundResourceCatalog : Resource
{
    #region Configuration
    [Export] public Godot.Collections.Array<GroundResourceDefinition> Materials
        { get; set; } = new();
    #endregion

    #region Validation
    // =========================================================
    // Reject malformed placement or extraction data before chunk generation.
    public void Validate(ItemCatalog items)
    {
        if (items == null || Materials == null || Materials.Count == 0 || Materials.Count > 256)
            throw new InvalidOperationException("Ground resource catalog is empty.");

        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (GroundResourceDefinition material in Materials)
        {
            if (material == null || string.IsNullOrWhiteSpace(material.Id) ||
                !ids.Add(material.Id) || string.IsNullOrWhiteSpace(material.ItemId) ||
                items.Get(material.ItemId) == null ||
                !float.IsFinite(material.RadiusTiles.X) ||
                !float.IsFinite(material.RadiusTiles.Y) ||
                material.RadiusTiles.X <= 0 ||
                material.RadiusTiles.Y < material.RadiusTiles.X ||
                !float.IsFinite(material.ClearanceTiles) ||
                material.ClearanceTiles < 0 ||
                !float.IsFinite(material.MaximumHeightVariation) ||
                material.MaximumHeightVariation < 0 ||
                material.UnitsPerDeposit < 1 || material.UnitsPerDeposit > 100000 ||
                !float.IsFinite(material.WorkPerUnit) ||
                material.WorkPerUnit <= 0 ||
                material.RequiredShovelStrength < 1)
                throw new InvalidOperationException(
                    $"Invalid ground material: '{material?.Id}'.");
        }
    }
    #endregion
}
