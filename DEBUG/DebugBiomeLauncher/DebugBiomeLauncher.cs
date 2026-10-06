// Presents enabled biomes and launches an isolated natural-world biome test.
// Requests are consumed once by the debug world, without changing normal WorldTest.
using Godot;
using System;
using System.Collections.Generic;

public partial class DebugBiomeLauncher : Control
{
    #region Configuration
    [Export] public BiomeCatalog Catalog { get; set; }
    [Export(PropertyHint.File, "*.tscn")]
    public string TestScenePath { get; set; } =
        "res://DEBUG/DebugBiomeLauncher/DebugBiomeWorld.tscn";
    #endregion

    #region Request
    public readonly struct Request
    {
        public readonly string BiomeId;
        public readonly uint Seed;

        // =========================================================
        // Store this launch's requested biome and seed.
        public Request(string biomeId, uint seed)
        {
            BiomeId = biomeId;
            Seed = seed;
        }
    }

    private static Request? _pending;
    public static string LastMessage { get; set; } = "";

    // =========================================================
    // Consume the request once so it cannot affect a later normal scene launch.
    public static bool TryTakeRequest(out Request request)
    {
        request = _pending.GetValueOrDefault();
        bool available = _pending.HasValue;
        _pending = null;
        return available;
    }
    #endregion

    #region State
    private List<BiomeDefinition> _biomes;
    private OptionButton _selection;
    private LineEdit _seed;
    private Label _status;
    private Button _generate;
    #endregion

    #region Lifecycle
    // =========================================================
    // Build the small standalone launcher from the current biome catalog.
    public override void _Ready()
    {
        BuildUi();

        try
        {
            if (Catalog == null)
                throw new InvalidOperationException(
                    "Assign the Biome Catalog to the launcher.");

            _biomes = Catalog.GetEnabledBiomes();

            foreach (BiomeDefinition biome in _biomes)
                _selection.AddItem(biome.DisplayName);

            _selection.Select(0);
            _status.Text = string.IsNullOrEmpty(LastMessage)
                ? "Choose your starting biome. The rest of the world remains mixed."
                : LastMessage;
            LastMessage = "";
        }
        catch (Exception error)
        {
            _generate.Disabled = true;
            _status.Text = error.Message;
        }
    }
    #endregion

    #region Layout
    // =========================================================
    // Create the launcher without coupling it to the inventory HUD.
    private void BuildUi()
    {
        ColorRect background = new()
        {
            Color = new Color("#081218"),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(background);
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        CenterContainer centre = new();
        AddChild(centre);
        centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        PanelContainer panel = new()
        {
            CustomMinimumSize = new Vector2(520, 0)
        };
        centre.AddChild(panel);

        MarginContainer margin = new();
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 24);
        panel.AddChild(margin);

        VBoxContainer contents = new();
        contents.AddThemeConstantOverride("separation", 14);
        margin.AddChild(contents);

        Label title = new() { Text = "BIOME TEST LAUNCHER" };
        title.AddThemeFontSizeOverride("font_size", 24);
        contents.AddChild(title);

        contents.AddChild(new Label { Text = "Starting biome" });
        _selection = new OptionButton();
        contents.AddChild(_selection);

        contents.AddChild(new Label
        {
            Text = "Seed — leave blank for a random world"
        });

        _seed = new LineEdit
        {
            PlaceholderText = "Random seed",
            MaxLength = 10
        };
        contents.AddChild(_seed);

        _generate = new Button { Text = "GENERATE WORLD" };
        _generate.Pressed += Launch;
        contents.AddChild(_generate);

        _status = new Label
        {
            CustomMinimumSize = new Vector2(470, 0),
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        contents.AddChild(_status);
    }
    #endregion

    #region Launching
    // =========================================================
    // Choose a seed and pass one request to the isolated debug world.
    private void Launch()
    {
        if (_biomes == null || _selection.Selected < 0) return;

        uint seed;
        string entered = _seed.Text.Trim();

        if (entered.Length == 0)
        {
            using RandomNumberGenerator rng = new();
            rng.Randomize();
            seed = rng.Randi();
        }
        else if (!uint.TryParse(entered, out seed))
        {
            _status.Text = "Enter a whole seed from 0 to 4294967295.";
            return;
        }

        _pending = new Request(
            _biomes[_selection.Selected].Id, seed);

        Error result = GetTree().ChangeSceneToFile(TestScenePath);

        if (result != Error.Ok)
        {
            _pending = null;
            _status.Text = $"Could not load the debug world: {result}";
        }
    }
    #endregion
}