// Displays live player reserves, calculated attributes, and existing inventory load.
// Rebuilds text only after changes, at a limited refresh frequency.
using Godot;
using System.Text;

public partial class PlayerStatsHud : CanvasLayer
{
    #region Configuration
    [ExportGroup("Display")]
    [Export] public Vector2 ScreenPosition { get; set; } = new(16, 110);
    [Export] public double RefreshInterval { get; set; } = 0.25;
    #endregion

    #region State
    private PlayerStats _stats;
    private PlayerVitals _vitals;
    private PlayerInventory _inventory;
    private Label _label;
    private readonly StringBuilder _text = new(700);
    private double _timer;
    private bool _dirty = true;
    #endregion

    #region Lifecycle
    // =========================================================
    // Build one mouse-transparent label and subscribe to cached gameplay state.
    public override void _Ready()
    {
        _stats = GetNode<PlayerStats>("../Systems/Stats");
        _vitals = GetNode<PlayerVitals>("../Systems/Vitals");
        _inventory = GetNode<PlayerInventory>("../Systems/Inventory");

        _label = new Label
        {
            Position = ScreenPosition,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color("#a4eff3")
        };
        _label.AddThemeFontSizeOverride("font_size", 14);
        _label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.95f));
        _label.AddThemeConstantOverride("shadow_offset_x", 1);
        _label.AddThemeConstantOverride("shadow_offset_y", 1);
        AddChild(_label);

        _stats.Changed += MarkDirty;
        _vitals.Changed += MarkDirty;
        _inventory.Changed += MarkDirty;
        Refresh();
    }

    // =========================================================
    // Remove subscriptions when the HUD leaves the scene.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_stats)) _stats.Changed -= MarkDirty;
        if (GodotObject.IsInstanceValid(_vitals)) _vitals.Changed -= MarkDirty;
        if (GodotObject.IsInstanceValid(_inventory)) _inventory.Changed -= MarkDirty;
    }

    // =========================================================
    // Limit text updates while leaving unchanged text cached.
    public override void _Process(double delta)
    {
        _timer -= delta;
        if (!_dirty || _timer > 0.0) return;
        _timer = System.Math.Max(0.1, RefreshInterval);
        Refresh();
    }

    // =========================================================
    // Request a later text refresh without rebuilding during gameplay events.
    private void MarkDirty()
    {
        _dirty = true;
    }
    #endregion

    #region Display
    // =========================================================
    // Show current reserves, base-to-final values, and physical backpack usage.
    private void Refresh()
    {
        _dirty = false;
        _text.Clear();
        _text.AppendLine("PLAYER STATS");
        _text.AppendLine("Capacity: base → final");
        _text.AppendLine();

        _text.AppendLine(
            $"Health  {_vitals.Health.Current} / {_vitals.Health.MaxHealth}" +
            $"   ({_stats.GetBase(PlayerStat.MaxHealth):0.#} → " +
            $"{_stats.Get(PlayerStat.MaxHealth):0.#})");

        AppendReserve("Stamina", PlayerReserve.Stamina);
        AppendReserve("Oxygen", PlayerReserve.Oxygen);
        AppendReserve("Energy", PlayerReserve.Energy);
        AppendReserve("Food", PlayerReserve.Food);
        AppendReserve("Water", PlayerReserve.Water);
        AppendReserve("Fatigue", PlayerReserve.Fatigue);

        _text.AppendLine();
        float speed = _stats.Get(PlayerStat.MovementSpeed);
        _text.AppendLine(
            $"Move speed  {_stats.GetBase(PlayerStat.MovementSpeed):0.#} → {speed:0.#}");
        _text.AppendLine(
            $"Loaded speed  {speed * _inventory.MovementFactor:0.#}");
        _text.AppendLine(
            $"Mining  {_stats.GetBase(PlayerStat.MiningEfficiency) * 100f:0.#}% → " +
            $"{_stats.Get(PlayerStat.MiningEfficiency) * 100f:0.#}%");

        BackpackDefinition pack = _inventory.Equipment.Backpack;
        _text.AppendLine();
        _text.AppendLine(
            $"Weight  {_inventory.TotalWeightKg:0.#} / " +
            $"{(pack?.MaximumWeightKg ?? _inventory.Rules.MaximumWeightKg):0.#} kg");
        _text.AppendLine(
            $"Volume  {_inventory.UsedVolumeLitres:0.#} / " +
            $"{(pack?.CapacityLitres ?? 0f):0.#} L");

        _label.Text = _text.ToString();
    }

    // =========================================================
    // Append a reserve and its unmodified and calculated capacities.
    private void AppendReserve(string name, PlayerReserve reserve)
    {
        PlayerStat stat = (PlayerStat)((int)reserve + (int)PlayerStat.MaxStamina);
        _text.AppendLine(
            $"{name}  {_vitals.GetCurrent(reserve):0.#} / " +
            $"{_vitals.GetMaximum(reserve):0.#}" +
            $"   ({_stats.GetBase(stat):0.#} → {_stats.Get(stat):0.#})");
    }
    #endregion
}