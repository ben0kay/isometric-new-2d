// Opens the exact hovered workstation through the existing primary cursor action.
public sealed class StationInteractionProvider : IInteractionProvider
{
    private readonly StationCrafting _station;
    public InteractionAction PrimaryAction => new("open_station", "Interact");
    public string DisplayName => _station.Definition.DisplayName;

    // =========================================================
    // Keep the source station bound to this provider.
    public StationInteractionProvider(StationCrafting station) { _station = station; }

    // =========================================================
    // Reject removed stations before reading their gameplay state.
    public bool CanInteract(Player player) => Godot.GodotObject.IsInstanceValid(_station) && _station.CanUse(player);

    // =========================================================
    // Delegate presentation to one shared player HUD rather than a per-station interface.
    public bool Execute(Player player, string actionId) => actionId == "open_station" && CanInteract(player) &&
        StationCraftingHud.Ensure(player).TryOpen(_station);
}
