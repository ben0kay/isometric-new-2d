# Data-Driven Items and Harvesting — Codex Assessment

Reviewed repository: ben0kay/isometric-new-2d
Reviewed commit: fc09adffd29daea18295b28105804fd292e2559c

Status: source inspection and planning only.
No implementation or gameplay verification performed for this assessment.

## Objective

Make inventory items, harvesting rewards and crafting recipes editable
through external Godot resources.

Prepare these resources for a future BEN_TOOLBOX editor that can find
items, their sources and their recipes.

Do not build that PowerShell editor during this refactor.

## Overall Size

This is a medium refactor, concentrated in item content, harvesting,
resource definitions, validation and documentation.

The inventory, pickup, crafting and save foundations already exist.
Reuse them.

The main work is:

- Moving existing content definitions out of C#.
- Introducing independent, configurable harvest rewards.
- Preserving the different extraction rules for ordinary resources and ores.
- Keeping save identities and reward transactions correct.
- Making all relevant relationships discoverable from resource data.

Configurable harvesting and its persistence checks carry the most risk.
Basic item and recipe registration require relatively little new machinery.

## What Already Exists

### Items

ItemDefinition already is an Inspector-editable Resource.

It supports stable IDs, names, icons, stack sizes, weight, volume and
attack, consumable and placeable capabilities.

ItemCatalog already supports:
- Explicit item resources.
- Nested category catalogs.
- Cached ID lookup.
- Duplicate and invalid-definition checks.
- Built-in item compatibility.

External item definitions can override built-in definitions with the
same stable ID.

The current ItemCatalog.tres relies on built-in registrations rather
than listing the ordinary material items externally.

Therefore, adding a new external item does not require inventing a new
catalog system. The existing registration route should be used.

### Existing Content

The reviewed C# content modules register:
- 10 natural materials.
- 3 processed materials.
- 1 raw food.
- 1 placeable test cube.

Some equipment already exists as external resources, including the
shovel, weapons and starter backpack.

Preserve their existing capability resources and paths.

Some inventory icons are generated from SVG strings in C#.
Those drawings can be exported to SVG assets during migration, preserving
their appearance without requiring replacement artwork.

### Harvesting

Plants, trees and rocks share ResourceHarvest.

Their actor scripts currently supply fallback item IDs:
- Plants: plant_fiber.
- Trees: carbon.
- Rocks: rock.

WorldObjectDefinition already provides a primary reward, quantity,
harvest work, mining strength and bonus item IDs.

Current limitations:
- Hand-gathered plants force the primary quantity to one.
- Bonus items are guaranteed single-unit rewards.
- Independent chances and quantity ranges are unavailable.

Plants, trees and rocks pay out when harvesting completes.
Repeated mining actions accumulate work; they are not separate payouts.

### Ores and Ground Deposits

OreDeposit has a separate extraction system:
- Work accumulates toward a batch.
- A deposit has remaining units.
- A successful batch removes units.
- Inventory acceptance happens before units are consumed.

Ground deposits also have their own definition and extraction path.
They do not simply use WorldObjectDefinition.

Keep these distinctions.

### Crafting

CraftingRecipe already is a Resource with ingredients, an output,
quantities and duration.

CraftingCatalog already accepts external recipe resources.

The test-cube recipe currently comes from C# registration.

Unlike ItemCatalog's built-in override behavior, adding an external recipe
with the same ID while keeping the built-in recipe enabled produces a
duplicate. Recipe migration needs an explicit transition.

### Saves

Persistence already covers relevant inventory, crafting, resource changes
and world drops.

Compatibility depends on more than item IDs:
- Item IDs must remain stable.
- Recipe IDs must remain stable.
- Existing world-resource definition paths must remain stable.
- Generated object identities must remain stable.

ResourceChanges records partial work and depletion.

ResourceWorld also tracks pending deferred pickups so an immediate save
can capture them. Preserve this behavior.

The ongoing save notes still identify unfinished connection, liquid and
combined verification work. Coordinate shared-file edits with that work.

## Recommended Stages

| Stage | Scope | Relative size |
|---|---|---|
| 1 | Prove external item registration | Small |
| 2 | Migrate existing item content | Moderate |
| 3 | Configurable plant, tree and rock rewards | Largest |
| 4 | Ore and ground-resource integration | Moderate; larger if ore gains multiple rewards |
| 5 | External recipe migration | Small |
| 6 | Discovery, validation and final documentation | Moderate |

Each stage should leave the game usable and have a clear stopping point.

## Stage 1 — External Item Registration

Use the existing ItemDefinition and ItemCatalog.

Create Bark.tres and register it through the master catalog or a category
catalog.

Document one predictable folder and registration convention.

Prefer explicit catalog references. Automatic folder discovery is not
required to achieve resource-driven content.

Keep existing built-ins enabled during this initial stage.

Completion checks:
- Bark resolves through the normal cached catalog lookup.
- Inventory and pickups accept it.
- Saving and loading preserves its ID and quantity.
- Adding it requires no new C# content-registration entry.

This is the recommended first implementation pass.

## Stage 2 — Migrate Existing Items

Move ordinary materials into external item resources, then migrate food
and the test cube.

Preserve:
- IDs and names.
- Stack, weight and volume values.
- Existing icon appearance.
- Consumable settings.
- Placeable scenes and placement settings.

Audit existing equipment registration without moving its files merely
for consistency.

Keep built-ins as a temporary fallback while comparing migrated content.

Disable the fallback only after every required item is registered
externally and its behavior has been checked.

Do not delete an entire content class just because its registration
methods become obsolete. PlaceableItems also contains world artwork code.

Completion checks:
- Every migrated item retains its existing properties.
- Existing saves resolve the same IDs.
- Food, tools and placement still work.
- Ordinary item definitions can be discovered without interpreting C#.

## Stage 3 — Configurable Harvest Rewards

Introduce small, Inspector-editable reward resources.

Each reward entry should specify:
- Stable item ID.
- Minimum quantity.
- Maximum quantity.
- Independent chance.

A reusable harvest configuration should contain the reward entries.
Keep harvesting requirements on the species definition unless moving
them has a concrete benefit.

Use external resources and predictable serialization for future tooling.

Connect this configuration to ResourceHarvest, then migrate existing
plant, tree and rock species without changing their current yields.

During migration, retain a clear legacy fallback for unmigrated species.
Remove it only after the content audit is complete.

Do not reuse the existing weighted LootTable as the harvest model:
it selects entries per roll, whereas independent harvest rewards can
produce several items together.

Preserve the shared ResourceWorld and WorldPickup pipeline.

Reward rules must address:
- Validation before depletion.
- Complete reward preparation before spawning.
- No partial payout when one item reference is invalid.
- No duplicate rewards after saving or chunk regeneration.
- Stable outcomes across retries and reloads.
- Intentional completion when a valid optional-only configuration rolls
  no rewards.

Choose and document the random-outcome strategy before implementation.
Avoid adding a save-format change unless the selected strategy needs it.

Completion checks:
- Carbon and Bark can drop together.
- Bark quantities and chance are resource-editable.
- A second species references the same Bark item.
- Existing species retain their original rewards.
- Partial harvest work survives unloading and reloading.
- Saving immediately after harvest preserves pending drops.
- Depleted resources do not return or pay out again.

## Stage 4 — Ores and Ground Resources

Integrate these separately from ordinary harvest completion.

First preserve existing extraction:
- Ore work per batch.
- Remaining deposit units.
- Units per batch.
- Tool requirements.
- Full-inventory behavior.
- Saved partial work and depletion.

Use the shared reward-entry format where appropriate, while keeping
the extraction lifecycle separate.

If ores gain multiple rewards, prepare and accept the complete batch
before consuming deposit units.

A failed collection attempt must not reroll rewards or consume ore.

Ground-resource definitions should participate in item-reference
validation and relationship discovery even if their current single-item
extraction format remains sufficient.

Completion checks:
- A full inventory consumes no ore units.
- Successful batches consume exactly the intended units.
- Partial and depleted deposits restore correctly.
- Ground digging retains its existing quantities and work requirements.

This stage can initially retain single-item ore payouts.
Multi-reward ore extraction should be an explicit additional scope choice.

## Stage 5 — External Recipes

Move the test-cube recipe into an external CraftingRecipe resource.

Register it through CraftingCatalog and remove or disable its corresponding
built-in registration without introducing duplicate recipe IDs.

Use registered item identities consistently for ingredients and outputs.

Prove that another resource-only recipe can consume Bark and produce an
existing item.

Preserve existing inventory exchange and crafting queue behavior.

Completion checks:
- Recipes require no new C# registration entry.
- Ingredients and outputs resolve through the catalog.
- Full-inventory failures remain safe.
- Saved crafting queues restore using unchanged recipe IDs.

Furnaces, fuel, stations and production machines remain future work.

## Stage 6 — Discovery, Validation and Documentation

Make these relationships discoverable:
- Item to source definitions.
- Source definition to possible rewards.
- Item to recipes consuming it.
- Item to recipes producing it.

Include exact resource paths, quantities and probabilities.

Treat resource definitions as the source of truth.

If a machine-readable index is introduced, generate it from registered
resources. Do not maintain a separate manual relationship list.

Distinguish registered content from currently biome-enabled content:
a species can exist without being selected by a biome.

Validate:
- Duplicate and missing IDs.
- Broken resource references.
- Invalid quantities and chances.
- Invalid harvest and extraction settings.
- Missing recipe inputs and outputs.
- Catalog cycles and conflicting registrations.

Perform discovery and validation during loading or explicit tooling work.
Do not scan project files every frame.

Write a README explaining:
- Item creation and registration.
- Harvest configuration and species assignment.
- Recipe creation and registration.
- Folder conventions.
- Identity and save-compatibility rules.
- Future BEN_TOOLBOX integration.

## Final Verification

Verify both existing saves and a new campaign.

Cover:
- Inventory and equipment.
- Food and placement.
- Plant gathering.
- Tree and rock harvesting.
- Ore extraction with available and full inventory.
- Ground digging.
- Crafting and saved queues.
- Loose and pending pickups.
- Partial harvesting followed by chunk unloading.
- Depletion followed by save, quit and reload.
- Relevant layer transitions.

Keep biome generation settings and species paths stable during this
refactor so failures can be traced to the actual changes.

## Scope Boundaries

Do not include:
- The PowerShell editor.
- New artwork.
- Biome spawn redesign.
- New ore generation.
- Furnaces or production stations.
- A general inventory rewrite.
- Unrelated save-system completion.

## Suggested Next Instruction

Implement Stage 1 only: create and register an external Bark item using
the existing catalog, preserve all current items and saves, verify the
registration path, and document the convention.

Review that completed stage before beginning the broader migration.