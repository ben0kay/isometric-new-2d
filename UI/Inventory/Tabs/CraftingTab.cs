// Builds the crafting tab inside the existing inventory window.
// Displays backpack requirements, batch quantity, progress, and cancellable jobs.
using Godot;

public partial class CraftingTab : HBoxContainer
{
    #region Configuration
    public UIInventoryMaster Hud { get; set; }
    public PlayerCrafting Crafting { get; set; }
    #endregion

    #region State
    private ItemList _recipes, _queue;
    private TextureRect _icon;
    private Label _title, _details, _requirements, _status;
    private SpinBox _quantity;
    private ProgressBar _progress;
    private Button _craft, _cancel;
    private CraftingRecipe _selected;
    private double _refreshTick;
    #endregion

    #region Lifecycle
    // =========================================================
    // Build recipe, requirements, and queue panels using the shared HUD style.
    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 12);

        BuildRecipes();
        BuildDetails();
        BuildQueue();

        Refresh();
    }

    // =========================================================
    // Refresh the visible tab four times per second.
    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;

        _refreshTick += delta;
        if (_refreshTick < 0.25) return;

        _refreshTick = 0.0;
        Refresh();
    }
    #endregion

    #region Layout
    // =========================================================
    // List the registered recipes without embedding recipe logic in the UI.
    private void BuildRecipes()
    {
        VBoxContainer section = Hud.Section(this, "RECIPES");

        _recipes = new ItemList
        {
            CustomMinimumSize = new Vector2(240, 250),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        section.AddChild(_recipes);

        foreach (CraftingRecipe recipe in Crafting.Catalog.Recipes)
        {
            ItemDefinition output = Crafting.Items.Get(recipe.Output.Id);

            _recipes.AddItem(
                $"{recipe.Category} / {recipe.Output.DisplayName}",
                output.Icon);
        }

        _recipes.ItemSelected += SelectRecipe;

        if (Crafting.Catalog.Recipes.Count > 0)
        {
            _recipes.Select(0);
            SelectRecipe(0);
        }
    }

    // =========================================================
    // Show the selected recipe and let the player choose a batch size.
    private void BuildDetails()
    {
        VBoxContainer section = Hud.Section(this, "HAND CRAFTING");

        _title = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        section.AddChild(_title);

        _icon = new TextureRect
        {
            CustomMinimumSize = new Vector2(80, 80),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
        };
        section.AddChild(_icon);

        _details = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        section.AddChild(_details);

        _requirements = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        section.AddChild(_requirements);

        section.AddChild(new Label { Text = "CRAFTING OPERATIONS" });

        _quantity = new SpinBox
        {
            MinValue = 1,
            MaxValue = Crafting.MaximumBatchSize,
            Step = 1,
            Value = 1,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        section.AddChild(_quantity);
        _quantity.ValueChanged += _ => Refresh();

        _craft = new Button
        {
            Text = "ADD TO QUEUE",
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0, 38)
        };
        section.AddChild(_craft);

        _craft.Pressed += () =>
        {
            Crafting.TryQueue(_selected, (int)_quantity.Value);
            Refresh();
        };
    }

    // =========================================================
    // Display active progress and allow any queued batch to be cancelled.
    private void BuildQueue()
    {
        VBoxContainer section = Hud.Section(this, "CRAFTING QUEUE");

        _progress = new ProgressBar
        {
            MaxValue = 100,
            CustomMinimumSize = new Vector2(0, 22),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        section.AddChild(_progress);

        _queue = new ItemList
        {
            CustomMinimumSize = new Vector2(240, 250),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        section.AddChild(_queue);

        _cancel = new Button
        {
            Text = "CANCEL SELECTED BATCH",
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0, 38)
        };
        section.AddChild(_cancel);

        _cancel.Pressed += () =>
        {
            int[] selected = _queue.GetSelectedItems();

            if (selected.Length > 0)
                Crafting.Cancel(selected[0]);

            Refresh();
        };

        _status = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        section.AddChild(_status);
    }
    #endregion

    #region Refresh
    // =========================================================
    // Change the inspected recipe without affecting the active crafting job.
    private void SelectRecipe(long index)
    {
        _selected = Crafting.Catalog.Recipes[(int)index];

        if (_title != null)
            Refresh();
    }

    // =========================================================
    // Refresh ingredient totals and queue state from their gameplay owners.
    private void Refresh()
    {
        if (_craft == null || _queue == null) return;

        int quantity = (int)_quantity.Value;

        if (_selected == null)
        {
            _title.Text = "No recipe selected";
            _icon.Texture = null;
            _details.Text = "";
            _requirements.Text = "";
            _craft.Disabled = true;
        }
        else
        {
            ItemDefinition output = Crafting.Items.Get(_selected.Output.Id);

            _title.Text = output.DisplayName;
            _icon.Texture = output.Icon;

            _details.Text =
                $"{_selected.Description}\n\n" +
                $"Output: {(long)_selected.OutputCount * quantity}\n" +
                $"Time: {_selected.DurationSeconds * quantity:0.#} seconds";

            string text = "INGREDIENTS — OWNED / REQUIRED\n";

            foreach (CraftingIngredient ingredient in _selected.Ingredients)
            {
                ItemDefinition item = Crafting.Items.Get(ingredient.ItemId);
                int owned = Hud.Inventory.GetBagItemCount(ingredient.ItemId);
                long required = (long)ingredient.Count * quantity;

                text += $"\n{item.DisplayName}: {owned} / {required}";
            }

            _requirements.Text = text;

            bool enough = Hud.Inventory.HasCraftingIngredients(
                _selected, quantity);

            _requirements.Modulate = enough
                ? new Color("#a5ddb4")
                : new Color("#eea1a1");

            _craft.Disabled = !enough ||
                Crafting.Jobs.Count >= Crafting.MaximumQueueEntries;
        }

        int[] selectedJobs = _queue.GetSelectedItems();
        int selectedIndex = selectedJobs.Length > 0 ? selectedJobs[0] : -1;

        _queue.Clear();

        for (int i = 0; i < Crafting.Jobs.Count; i++)
        {
            PlayerCrafting.CraftJob job = Crafting.Jobs[i];

            _queue.AddItem(
                $"{(i == 0 ? "ACTIVE" : "QUEUED")} — " +
                $"{job.Recipe.Output.DisplayName} ×" +
                $"{(long)job.Remaining * job.Recipe.OutputCount}");
        }

        if (_queue.ItemCount > 0)
        {
            _queue.Select(selectedIndex >= 0
                ? Mathf.Min(selectedIndex, _queue.ItemCount - 1)
                : 0);
        }

        _cancel.Disabled = _queue.ItemCount == 0;

        _progress.Value = Crafting.Jobs.Count > 0
            ? Crafting.Jobs[0].Progress * 100.0
            : 0.0;

        _status.Text = Crafting.Status;
    }
    #endregion
}