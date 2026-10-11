// Adapts existing container transactions to the shared interaction contract.
using Godot;
public sealed class StorageInteractionProvider : IInteractionProvider
{
    private readonly WorldStorage _storage;
    public InteractionAction PrimaryAction => new("open", "Interact");
    public string DisplayName => _storage.Definition.DisplayName;
    // =========================================================
    // Retain the existing storage owner, contents and range rules.
    public StorageInteractionProvider(WorldStorage storage) { _storage = storage; }
    // =========================================================
    // Reject removed containers before delegating eligibility.
    public bool CanInteract(Player player) => GodotObject.IsInstanceValid(_storage) &&
        !_storage.IsQueuedForDeletion() && _storage.CanInteract(player);
    // =========================================================
    // Open the exact selected container through its existing inventory UI.
    public bool Execute(Player player, string actionId) => actionId == "open" &&
        CanInteract(player) && player.GetNode<StorageHud>("StorageHud").TryOpen(_storage);
}
