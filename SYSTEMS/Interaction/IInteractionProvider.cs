// Supplies object-owned primary actions without polling input or owning the UI.
using Godot;
public interface IInteractionProvider
{
    InteractionAction PrimaryAction { get; }
    string DisplayName { get; }
    bool CanInteract(Player player);
    bool Execute(Player player, string actionId);
}
public readonly struct InteractionAction
{
    public string Id { get; }
    public string Label { get; }
    // =========================================================
    // Keep stable action identity separate from its display label.
    public InteractionAction(string id, string label) { Id = id; Label = label; }
}
