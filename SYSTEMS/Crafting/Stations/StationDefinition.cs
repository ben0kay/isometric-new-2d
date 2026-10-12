// Describes manual workstation eligibility and cursor bounds independently of artwork.
using Godot;
using System;
using System.Linq;

[Tool, GlobalClass]
public partial class StationDefinition : Resource
{
    [Export] public string DisplayName { get; set; } = "Workstation";
    [Export] public string[] Capabilities { get; set; } = Array.Empty<string>();
    [Export] public float InteractionRange { get; set; } = 160f;
    [Export] public Rect2 CursorBounds { get; set; } = new(-72, -116, 144, 156);

    // =========================================================
    // Reject unusable reach, bounds and unsupported capability definitions before interaction.
    public void Validate()
    {
        CraftingCapabilities.Validate(Capabilities, ResourcePath + "/Capabilities", true);
        if (string.IsNullOrWhiteSpace(DisplayName) || !float.IsFinite(InteractionRange) || InteractionRange <= 0 ||
            !float.IsFinite(CursorBounds.Position.X) || !float.IsFinite(CursorBounds.Position.Y) ||
            !float.IsFinite(CursorBounds.Size.X) || !float.IsFinite(CursorBounds.Size.Y) ||
            CursorBounds.Size.X <= 0 || CursorBounds.Size.Y <= 0)
            throw new InvalidOperationException("Invalid station name, range or cursor bounds: " + ResourcePath);
    }

    // =========================================================
    // Manual stations offer only recipes requiring all of their advertised capabilities.
    public bool Provides(CraftingRecipe recipe) => recipe != null && recipe.RequiredCapabilities != null &&
        recipe.RequiredCapabilities.Length > 0 && Capabilities != null &&
        recipe.RequiredCapabilities.All(id => id == CraftingCapabilities.BasicAssembly && Capabilities.Contains(id));
}
