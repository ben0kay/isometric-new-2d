// A small, immutable-in-spirit notification payload (the toast display may merge Amount).
// DurationSeconds <= 0 means to use the central NotificationSettings default.
public enum ToastTone { Information, Inventory, Crafted, Warning }

public sealed class ToastNotification
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public ToastTone Tone { get; set; } = ToastTone.Information;
    public int Amount { get; set; }
    public float DurationSeconds { get; set; } = 0f;
}
