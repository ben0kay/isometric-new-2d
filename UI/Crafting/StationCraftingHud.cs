// One modal workstation interface per player; contains no material exchange or machine inventory.
using Godot;
using System.Collections.Generic;

public partial class StationCraftingHud : CanvasLayer
{
    private Player _player;
    private PlayerCrafting _crafting;
    private PlayerInventory _inventory;
    private InventoryHud _backpack;
    private InputModes _modes;
    private StationCrafting _station;
    private Control _root;
    private PanelContainer _window;
    private Label _title, _description, _ingredients, _status;
    private ItemList _list;
    private TextureRect _icon;
    private ProgressBar _progress;
    private SpinBox _quantity;
    private Button _start;
    private readonly List<CraftingRecipe> _recipes = new();
    private CraftingRecipe _selected;
    private double _refresh;

    // =========================================================
    // Lazily create the shared window without replacing or fingerprinting player scene files.
    public static StationCraftingHud Ensure(Player player)
    {
        StationCraftingHud existing = player.GetNodeOrNull<StationCraftingHud>("StationCraftingHud");
        if (existing != null) return existing;
        StationCraftingHud hud = new() { Name = "StationCraftingHud" };
        player.AddChild(hud); return hud;
    }

    // =========================================================
    // Resolve the existing crafting and inventory owners; only process while this window is open.
    public override void _Ready()
    {
        _player = GetParent<Player>();
        _crafting = _player.GetNode<PlayerCrafting>("Systems/Crafting");
        _inventory = _player.GetNode<PlayerInventory>("Systems/Inventory");
        _backpack = _player.GetNode<InventoryHud>("InventoryHud");
        _modes = InputModes.For(this); Layer = 23;
        BuildUi(); GetViewport().SizeChanged += Fit; Fit(); SetProcess(false);
    }

    // =========================================================
    // Open only the selected living station and filter canonical recipes by all capabilities.
    public bool TryOpen(StationCrafting station)
    {
        if (!_modes.GameplayAllowed || _backpack.IsOpen || _backpack.ExternalWindowOpen || _station != null ||
            !GodotObject.IsInstanceValid(station) || !station.CanUse(_player)) return false;
        _station = station; _recipes.Clear(); _list.Clear();
        foreach (CraftingRecipe recipe in _crafting.Catalog.Recipes)
            if (station.Definition.Provides(recipe))
            { _recipes.Add(recipe); _list.AddItem(recipe.Output.DisplayName, recipe.Output.Icon); }
        _selected = null;
        if (_recipes.Count > 0) { _list.Select(0); _selected = _recipes[0]; }
        _quantity.Value = 1;
        _modes.Push(this, PlayerInputMode.Station);
        _backpack.SetExternalWindowOpen(true);
        _root.Show(); SetProcess(true); _refresh = 0; Refresh(); return true;
    }

    // =========================================================
    // Attendance is exact-station access; another modal claim can pause work without changing ownership.
    public bool IsAttending(StationCrafting station) => IsInsideTree() && !IsQueuedForDeletion() &&
        _station == station && GodotObject.IsInstanceValid(station) && _root.Visible && station.CanUse(_player);

    // =========================================================
    // Only the top modal workstation owner can queue or complete an operation.
    public bool CanWorkAt(StationCrafting station) => IsAttending(station) && _modes.OwnsInput(this);

    // =========================================================
    // Cancel unpaid work and release this interface's input claim on every closing path.
    public void Close()
    {
        if (_station == null) return;
        if (GodotObject.IsInstanceValid(_crafting)) _crafting.CancelStation(_station);
        _station = null; _root.Hide(); SetProcess(false);
        if (GodotObject.IsInstanceValid(_modes)) _modes.Release(this);
        if (GodotObject.IsInstanceValid(_backpack)) _backpack.SetExternalWindowOpen(false);
    }

    // =========================================================
    // Close before PauseMenu sees Escape; hovering alone never claims this modal mode.
    public override void _Input(InputEvent input)
    {
        if (_station == null || !_modes.OwnsInput(this) || input is not InputEventKey key ||
            !key.Pressed || key.Echo || key.PhysicalKeycode != Key.Escape) return;
        Close(); GetViewport().SetInputAsHandled();
    }

    // =========================================================
    // Check proximity/life/layer and refresh only the active window at ten hertz.
    public override void _Process(double delta)
    {
        _refresh += delta; if (_refresh < 0.1) return; _refresh = 0;
        if (!GodotObject.IsInstanceValid(_station) || !_station.CanUse(_player)) { Close(); return; }
        Refresh();
    }

    // =========================================================
    // Release input and the viewport subscription when the player/window leaves the tree.
    public override void _ExitTree()
    { Close(); GetViewport().SizeChanged -= Fit; }

    // =========================================================
    // Use the shared button and recipe-text helpers rather than cloning the inventory crafting tab.
    private void BuildUi()
    {
        _root = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        AddChild(_root); _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _window = new PanelContainer(); _root.AddChild(_window);
        _window.SetAnchorsPreset(Control.LayoutPreset.Center);
        _window.OffsetLeft = -410; _window.OffsetRight = 410;
        _window.OffsetTop = -285; _window.OffsetBottom = 285; _window.PivotOffset = new Vector2(410, 285);
        _window.AddThemeStyleboxOverride("panel", UIButtonFactory.Box(UIButtonFactory.Ink, UIButtonFactory.Accent));
        VBoxContainer content = new(); _window.AddChild(content);
        HBoxContainer heading = new(); content.AddChild(heading);
        _title = UIButtonFactory.Label("WORKSTATION", 24); _title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; heading.AddChild(_title);
        heading.AddChild(UIButtonFactory.Create("Close [Esc]", Close, 36));
        HBoxContainer body = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; content.AddChild(body);
        _list = new ItemList { CustomMinimumSize = new Vector2(250, 320), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddChild(_list); _list.ItemSelected += index => { _selected = _recipes[(int)index]; Refresh(); };
        ScrollContainer scroll = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; body.AddChild(scroll);
        VBoxContainer info = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; scroll.AddChild(info);
        _icon = new TextureRect { CustomMinimumSize = new Vector2(64, 64), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered }; info.AddChild(_icon);
        _description = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; info.AddChild(_description);
        _ingredients = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; info.AddChild(_ingredients);
        info.AddChild(UIButtonFactory.Label("OPERATIONS", 14));
        _quantity = new SpinBox { MinValue = 1, MaxValue = _crafting.MaximumBatchSize, Value = 1, Step = 1 };
        info.AddChild(_quantity); _quantity.ValueChanged += _ => Refresh();
        _start = UIButtonFactory.Create("ASSEMBLE", () => { _crafting.TryQueueAtStation(_station, _selected, (int)_quantity.Value); Refresh(); }); info.AddChild(_start);
        info.AddChild(UIButtonFactory.Create("CANCEL UNFINISHED BATCH", () => { _crafting.CancelStation(_station); Refresh(); }, 36));
        _progress = new ProgressBar { MaxValue = 100, CustomMinimumSize = new Vector2(0, 22) }; content.AddChild(_progress);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; content.AddChild(_status);
        content.AddChild(UIButtonFactory.Label("Manual assembly · stay at this bench · closing cancels unpaid work", 13));
    }

    // =========================================================
    // Scale the window without changing station range or interaction hit bounds.
    private void Fit()
    {
        Vector2 size = GetViewport().GetVisibleRect().Size;
        _window.Scale = Vector2.One * Mathf.Clamp(Mathf.Min((size.X - 24) / 820f, (size.Y - 40) / 570f), 0.2f, 1);
    }

    // =========================================================
    // Present current backpack requirements and this station's transient batch progress.
    private void Refresh()
    {
        if (_start == null || _station == null) return;
        _title.Text = _station.Definition.DisplayName;
        int quantity = (int)_quantity.Value;
        _icon.Texture = _selected?.Output.Icon;
        _description.Text = _selected == null ? "No supported recipes." :
            $"{_selected.Output.DisplayName}\n{_selected.Description}\nOutput: {(long)_selected.OutputCount * quantity}\nTime: {_selected.DurationSeconds * quantity:0.#} s";
        _ingredients.Text = _selected == null ? "" : CraftingRecipeText.Requirements(_selected, quantity, _inventory, _crafting.Items);
        _start.Disabled = _selected == null || _crafting.StationJob != null || !CanWorkAt(_station) ||
            !_inventory.HasCraftingIngredients(_selected, quantity);
        PlayerCrafting.CraftJob job = _crafting.ActiveStation == _station ? _crafting.StationJob : null;
        _progress.Value = job?.Progress * 100 ?? 0;
        _status.Text = _crafting.Status + (job == null ? "" : $"\nRemaining operations: {job.Remaining}");
    }
}
