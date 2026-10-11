# Content Pass 5: external recipes

Test Cube uses `ITEMS/Definitions/Placeables/Structures/TestCube/TestCubeRecipe.tres`.
Its stable `test_cube` recipe ID, Building category, description, two Scrap Metal
cost, one cube output and two-second duration match the previous C# recipe.

`ITEMS/Definitions/RawMaterials/Organics/PlantFiber/PlantFiberFromBarkRecipe.tres`
is example gameplay content: one Bark becomes one Plant Fiber in two seconds.
Its recipe ID is `plant_fiber_from_bark`, category Materials. Adjust/remove the
example registration if that conversion does not suit your game.

## Authoring

Keep each recipe beside its output item's definition and artwork, following
`ITEMS/Definitions/<Category>/<Subcategory>/<Item>/`. Multiple recipes can
produce the same item; give each a distinct stable ID and descriptive filename.
The item category catalog registers inventory items. Separately,
`SYSTEMS/Crafting/CraftingCatalog.tres` lists the external recipe resources in
its Recipes array. Adding an item does not automatically make it craftable.

Duplicate an existing recipe resource. Set its ID, category, description,
Output (the exact registered ItemDefinition resource), OutputCount,
DurationSeconds and Ingredients (ItemId plus Count). Register it in the master
crafting catalog. No C# registration entry is needed. Counts are 1–999,
1–64 unique ingredients are required, and duration must be finite and positive.
The output must be the catalog resource, not an embedded copy sharing its ID.
The category string groups recipes in the crafting UI; it need not mirror
inventory folder categories. Built-in recipe registration is retired; keep
IncludeBuiltInRecipes false. Validation never appends recipes.

## Saves and transactions

PlayerCrafting, PlayerInventory and PlayerSaveSections are unchanged.
Jobs still store recipe ID, remaining quantity and elapsed seconds. Existing
Test Cube jobs resolve to the new resource with identical duration and cost.
This migration needs no save format or world generation hash bypass.

Recipe definitions are not snapshotted in queued jobs. Later recipe edits
can change the meaning of existing jobs; deleting/renaming an ID used by a
saved job prevents restore, and reducing duration below saved elapsed work
also prevents restore. Preserve stable IDs and consider saved jobs when editing.

Inventory still stages an exchange in a cloned storage and commits only when
all ingredients and output fit. Ingredients remain in the backpack while a
job is unfinished; cancelling does not consume or refund them. No new furnace,
fuel, station, weapon instance or attachment system is part of this pass.

## Verification before Pass 6

1. Close Godot, pull, preview the installer, apply and rebuild. Review git diff.
2. Run `Tests/CraftingRecipeChecks.tscn` with F6. It checks external resource
   registration, repeat validation, invalid content, legacy cube values and
   saved queue ID/quantity/progress/cancellation. These checks do not run the
   player's inventory transaction or the entire campaign loader.
3. In the game, craft a cube from two Scrap Metal; verify one cube, two seconds,
   correct icon and placement/destruction. Craft fiber from Bark; verify cost
   and output. Try insufficient ingredients and queued batches.
4. Fill storage so the output cannot fit even after ingredients are removed.
   Verify a completed job waits, retaining all ingredients; free capacity and
   verify exactly one successful exchange. Cancel a partial job and verify no
   inventory changes. Verify equipped gear does not satisfy backpack inputs.
5. Continue an existing campaign with a partially finished Test Cube job.
   Check its elapsed progress and remaining quantity. Save a mixed cube/fiber
   queue, fully quit, Continue and verify no duplicate payouts or lost jobs.
6. Recheck ore harvesting, pickups, ordinary inventory, equipment and placement.

The installer has normalized source hash guards, complete preflight, preview
mode and rollback on write failure. No separate backup folder or save edits.
Static resource/semantic/payload checks were performed during preparation;
PowerShell, .NET compilation and Godot gameplay could not be executed here.
