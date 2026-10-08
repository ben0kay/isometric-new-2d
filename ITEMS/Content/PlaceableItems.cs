// Defines placeable item content, recipes, inventory icons and world artwork.
// Shared placement code still owns previews, collision, placement and destruction.
using Godot;
using System.Collections.Generic;

public partial class PlaceableItems : Node2D
{
    #region World Artwork Settings
    [Export] public Vector2 BaseSize { get; set; } = new(128f, 64f);
    [Export] public float CubeHeight { get; set; } = 96f;

    private const string LeftColour = "#587880";
    private const string RightColour = "#344d58";
    private const string TopColour = "#9bb7b8";
    private const string EdgeColour = "#20333d";
    #endregion

    #region Definitions
    // =========================================================
    // Register placeable items with their world scenes and footprint settings.
    public static void Register(List<ItemDefinition> items)
    {
        ItemDefinition cube = ItemArtwork.Create(
            "test_cube", "Test Cube", "CUBE",
            20, 0.25f, 0.25f, Colors.White, CubeIcon());

        cube.Placeable = new PlaceableDefinition
        {
            WorldScene = GD.Load<PackedScene>(
                "res://WORLDABLES/Objects/Building/TestCube/TestCube.tscn"),
            ArtworkScene = GD.Load<PackedScene>(
                "res://WORLDABLES/Objects/Building/TestCube/TestCubeArtwork.tscn"),
            ArtworkScale = Vector2.One,
            Cells = Vector2I.One,
            MaximumHeightDifference = 8f,
            CoverHeight = 96f,
            ObstructionOutline = new[]
            {
                new Vector2(-64f, -96f), new Vector2(0f, -128f),
                new Vector2(64f, -96f), new Vector2(64f, 0f),
                new Vector2(0f, 32f), new Vector2(-64f, 0f)
            }
        };

        items.Add(cube);
    }

    // =========================================================
    // Register recipes using the same item definitions as the master catalog.
    public static void RegisterRecipes(
        ItemCatalog items, Godot.Collections.Array<CraftingRecipe> recipes)
    {
        recipes.Add(new CraftingRecipe
        {
            Id = "test_cube",
            Category = "Building",
            Description =
                "A temporary building block for testing crafting and placement.",
            Output = items.Get("test_cube"),
            OutputCount = 1,
            DurationSeconds = 2.0,
            Ingredients = new Godot.Collections.Array<CraftingIngredient>
            {
                new CraftingIngredient { ItemId = "scrap_metal", Count = 2 }
            }
        });
    }
    #endregion

    #region Inventory Artwork
    // =========================================================
    // Draw the cube's existing inventory icon inside the shared icon canvas.
    private static string CubeIcon()
    {
        return
            $"<g stroke='{EdgeColour}' stroke-width='1.5' stroke-linejoin='round'>" +
            $"<path d='M5 16 L24 6 L43 16 L24 26Z' fill='{TopColour}'/>" +
            $"<path d='M5 16 L24 26 L24 43 L5 33Z' fill='{LeftColour}'/>" +
            $"<path d='M24 26 L43 16 L43 33 L24 43Z' fill='{RightColour}'/>" +
            "</g>";
    }
    #endregion

    #region World Artwork
    // =========================================================
    // Draw the world cube with its bottom centred on the placement cell.
    public override void _Draw()
    {
        Vector2 back = new(0f, -BaseSize.Y * 0.5f);
        Vector2 right = new(BaseSize.X * 0.5f, 0f);
        Vector2 front = new(0f, BaseSize.Y * 0.5f);
        Vector2 left = new(-BaseSize.X * 0.5f, 0f);
        Vector2 lift = Vector2.Up * CubeHeight;

        DrawColoredPolygon(
            new[] { left, front, front + lift, left + lift },
            new Color(LeftColour));

        DrawColoredPolygon(
            new[] { front, right, right + lift, front + lift },
            new Color(RightColour));

        DrawColoredPolygon(
            new[] { back + lift, right + lift, front + lift, left + lift },
            new Color(TopColour));

        Color edge = new(EdgeColour);
        DrawPolyline(
            new[] { left, front, right, right + lift, back + lift,
                left + lift, left },
            edge, 2f, true);

        DrawLine(left + lift, front + lift, edge, 2f, true);
        DrawLine(front + lift, right + lift, edge, 2f, true);
        DrawLine(front, front + lift, edge, 2f, true);
    }
    #endregion
}