// Lightweight toast payload: the UI owns display, timing, stacking and merging.
// Amount > 0 enables additive merging for repeated pickups and crafting output.
public enum ToastTone { Information, Inventory, Crafted, Warning }

public sealed class ToastNotification
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public ToastTone Tone { get; set; } = ToastTone.Information;
    public int Amount { get; set; }
    public float DurationSeconds { get; set; } = 3.2f;
}
