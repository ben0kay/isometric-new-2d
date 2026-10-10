# Data-Driven Items and Harvesting — Codex Assessment

Reviewed repository: ben0kay/isometric-new-2d
Reviewed commit: fc09adffd29daea18295b28105804fd292e2559c

Status: original assessment was planning-only. See the Progress Record below
for subsequent implementation. No local gameplay verification has been
performed by the planning assessment.

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

## Six-Phase Delivery and ChatGPT Handoff Plan

Planning update: 2026-10-10. This restores the original six phases.
At this planning update all phases were proposed. See the Progress Record
below for implementation status and outstanding local checks.
The assessment above describes fc09adf; the planning file was refreshed
from main at c677eca. Future implementers must inspect the latest source
and current save notes before generating replacements.

### Delivery Rules for Every Phase

Deliver one reviewable change set per phase and perform local Godot testing
before proceeding. Use a numbered PS1 installer for larger local migrations;
small phases can instead be committed directly to GitHub when requested.
Phase 1 uses one direct GitHub commit, not an installer.
Internal tasks below are work packages, not extra mandatory phases.

Suggested installer names: InstallContentPass1.ps1 through
InstallContentPass6.ps1.

Each installer must:
- Locate and validate the Godot project root.
- Support preview without writing project files.
- Check every affected file against the inspected baseline before writes.
- Reject unexpected changes without partially applying the pass.
- Do not make separate backup copies; Git history supplies rollback.
- Preserve unrelated changes, including newer save and layer work.
- Apply exact UTF-8 payloads with valid PowerShell quoting.
- Safely recognize a complete previous application.
- Reject partially installed or conflicting states with an actionable report.
- Validate its payload and affected references before reporting success.
- Never commit, push, delete saves or change profiles automatically.

Update this same Markdown with affected files, installer baseline,
implementation status, checks actually performed, local checks still
needed, and the next phase. Do not label supplied code as locally verified.

For each handoff, supply the exact current source files and dependencies.
Request complete replacement files or a PS1 installer, not guessed edits
against an older commit. Integrate one task at a time and push the result.
Two chats must not independently replace the same file against different
baselines.

### Phase Overview and Dependencies

| Phase | Dependency | Installer | Natural handoff |
|---|---|---|---|
| 1. External item registration | Latest source inspection | Direct GitHub commit | Suitable for a whole regular ChatGPT phase |
| 2. Existing item migration | Phase 1 conventions | InstallContentPass2.ps1 | Resource conversion and artwork extraction |
| 3. Harvest configuration | Item identity contract settled | InstallContentPass3.ps1 | Species data after shared engine integration |
| 4. Ores and ground resources | Phase 3 reward contract | InstallContentPass4.ps1 | Data audit; extraction transactions need careful integration |
| 5. External recipes | Phase 2 item definitions | InstallContentPass5.ps1 | Suitable for a whole regular ChatGPT phase |
| 6. Discovery and verification | Final item/drop/recipe formats | InstallContentPass6.ps1 | Documentation, fixtures and relationship examples |

The normal application order is 1–6. Phase 5 can be prepared after Phase 2
if it avoids shared-file conflicts. Phase 6 documentation can be drafted
earlier, but the final index must reflect the installed formats.

### Phase 1 — Detailed Work Packages

1. Inspect ItemDefinition, ItemCatalog, ItemCatalog.tres and the current
   inventory/pickup save resolution. Confirm existing registration is
   sufficient; change C# only where a demonstrated gap requires it.
2. Agree folder conventions for new inventory items and category catalogs.
   Prefer explicit catalog references and stable IDs. Document that copying
   a file into a folder alone does not register it.
3. Create one external Bark item using a distinct ID such as bark.
   Set valid stack, weight and volume values. Use an existing fallback icon
   or a simple placeholder; new artwork is outside scope.
4. Register Bark through the existing catalog. Keep built-ins enabled.
   Avoid introducing a second item database or runtime folder scanning.
5. Document the minimum fields and exact registration workflow.

Expected footprint: Bark resource, master/category catalog resources,
this note, and only necessary validation changes.

Check: catalog initialization repeated safely; duplicate ID rejection;
pickup and inventory resolution; Bark quantity survives save/load.
If Bark is not naturally obtainable yet, use a clearly documented test
fixture rather than silently changing tree rewards.

Regular ChatGPT prompt:
> Read Phase 1 of NOTES/FutureWork/DataDrivenItems.md and the latest relevant
> repository files. Implement only external Bark registration using the
> existing catalog. Supply a focused GitHub change set or a previewable,
> conflict-checked PS1 installer. Preserve built-in items and saves.
> Report checks honestly.

### Phase 2 — Detailed Work Packages

1. Build a migration manifest from all six C# item-content modules and
   existing equipment resources. Record every ID, property, capability,
   icon source, current registration route and proposed external path.
   Empty content modules should not produce invented items.
2. Convert the ten natural materials and three processed materials to
   external ItemDefinition resources with identical values.
3. Extract generated icon SVGs to imported SVG files. Include the full
   original canvas/wrapper from ItemArtwork, not just inner drawing paths.
   Preserve icon size, tint and fallback appearance.
4. Convert alien_berry and its consumable capability. Preserve consumption
   timing and survival effects.
5. Convert test_cube and its placeable capability, retaining world/artwork
   scenes, footprint and placement settings. Keep any drawing code still
   referenced by those scenes.
6. Audit shovel, weapons and backpack registration. Preserve subtype fields
   and existing paths; don't flatten BackpackDefinition into ItemDefinition.
7. Register the migrated content through catalogs and compare definitions
   with the manifest. Disable built-in item registration only when catalog
   coverage is complete. Remove dead registration dependencies safely.

Expected footprint: item resources/icons/catalogs and necessary registration
cleanup. The built-in recipe may remain until Phase 5; verify it still
resolves the migrated test_cube.

Check: all 15 reviewed built-in IDs retain their values; existing equipment
remains accessible; existing saves load; food, attacks, shovel and placement
work. Confirm explicit icons no longer depend on generating built-in items.

Good smaller handoffs: migration manifest, natural-material conversion,
processed-material conversion, SVG extraction, berry capability conversion.
Integrate catalog and fallback removal as one coordinated step.

### Phase 3 — Detailed Work Packages

1. Define the reward contract before migrating species. Suggested concepts
   are HarvestDropEntry and HarvestProfile; exact names are implementation
   decisions. Entries hold item ID, minimum/maximum quantity and independent
   chance. Document whether chance uses 0–1 or 0–100 consistently.
2. Define null profile versus explicit empty profile semantics. A null
   profile may temporarily use legacy behavior; an intentionally empty
   profile must not silently activate the legacy reward.
3. Validate finite chances, bounded quantities and registered IDs before
   payout. Specify duplicate-entry policy and whether repeated items merge.
4. Implement a shared evaluator that returns a complete reward batch.
   Guaranteed entries and optional entries are independent. Do not alter
   weighted container loot semantics.
5. Choose a stable random strategy using persistent source/event identity,
   or persist prepared outcomes if necessary. Specify behavior on retries,
   unloading, old saves and configuration edits. Do not use transient node
   IDs or current time as the persistent seed.
6. Integrate ResourceHarvest and ResourceWorld. Prepare and validate the
   entire payout before depletion; preserve layer ownership, deferred
   pickup registration and immediate-save capture.
7. Audit all plant, tree and rock definitions, including currently inactive
   species. Convert current primary/bonus rewards exactly, including the
   existing one-unit hand-gather result.
8. Prove configurable Carbon plus Bark on CarbonTree, and the same Bark
   item on another tree. Remove hardcoded species fallback only after full
   migration coverage. Keep all species paths and generation identities.
9. Preserve work and tool requirements and shared artwork behavior.

Check: 0% and 100% entries; quantity boundaries; multiple simultaneous
rewards; optional-only empty roll completes once; bad IDs yield no partial
payout; partial work restores; immediate save retains rewards; exhausted
sources stay exhausted after chunk reload and a fresh process.

Good smaller handoffs: species inventory, profile resource creation and
species conversion after the schema is finalized. Shared reward evaluation,
transaction integration and random/save behavior should be reviewed together.

### Phase 4 — Detailed Work Packages

1. Inspect OreDefinition, OreDeposit, mining callers, inventory acceptance
   and ResourceChanges. Audit GroundResourceDefinition and its extraction
   callers separately.
2. Make current ore and ground item references validate against the external
   catalog and participate in relationship discovery.
3. Preserve primary ore units and work-per-batch behavior. Document whether
   reward quantities are per batch or per extracted unit, especially for a
   final partial batch.
4. Decide whether this delivery includes multi-reward ore batches. The
   minimum phase preserves single-item extraction and documents its limit.
   Do not claim ore supports independent extras unless it actually does.
5. If multiple rewards are included, extend acceptance to a complete batch:
   capacity must be checked for all rewards together, and deposit mutation
   must happen only after the complete inventory transaction succeeds.
6. Preserve prepared outcomes on blocked attempts and reload. Base event
   identity on stable deposit/progress information or persist it explicitly;
   validate the chosen scheme with existing saved units and work.
7. Keep ground digging's native extraction format where sufficient. Do not
   force shader-backed deposits into the ordinary plant/tree host lifecycle.

Check: exact batch sizes, final partial batch, weak tools, full inventory,
combined weight/volume/slot limits, blocked attempt followed by save/reload,
partial work restoration, depleted deposits and ground quantities.

Good smaller handoffs: ore/ground reference manifests and validation data.
Multi-item inventory acceptance and persistence are coupled engine work.

### Phase 5 — Detailed Work Packages

1. Inspect CraftingCatalog, CraftingRecipe, CraftingIngredient, PlayerCrafting,
   inventory exchanges and crafting save restoration.
2. Create an external test_cube recipe with its existing ID, ingredients,
   output count, duration and category.
3. Remove/disable corresponding built-in recipe registration without
   duplicating recipes on repeated validation. Preserve artwork code.
4. Ensure ingredients and outputs refer to registered item identities.
   Avoid separate conflicting copies of an item sharing the same ID.
5. Add a Bark-consuming example recipe using an existing output. Keep
   quantities bounded and document it as example content.
6. Make all recipe inputs/outputs inspectable through their resources.
   No new furnace, fuel or station system is needed.

Check: repeated validation, missing ingredient/output, duplicate recipe,
atomic exchange with insufficient capacity, cancellation, queued quantities
and saved elapsed crafting progress with unchanged recipe IDs.

Regular ChatGPT prompt:
> Implement Phase 5 only against the latest installed Phase 2 resources.
> Preserve the crafting engine and save queue behavior. Externalize the
> test-cube recipe, add the documented Bark example, and provide one safe
> PS1 installer with exact resource payloads and checks.

### Phase 6 — Detailed Work Packages

1. Define discovery coverage: registered inventory items, recipe resources,
   harvest-enabled species, ore yields and ground yields. Include inactive
   species where useful, clearly distinguishing defined, registered and
   biome-enabled content.
2. If an index is needed, specify a versioned machine-readable schema:
   IDs, resource paths, capability types, source kind, reward quantities,
   chances, extraction context, recipe inputs/outputs and registration
   locations. Generate it from canonical resources.
3. Ensure tooling can identify the exact resource to edit, including shared
   profiles and subresources. Mark shared-profile edits as affecting all
   referencing species; never duplicate an item to assign it to a source.
4. Provide an explicit validation/index command or editor operation with no
   per-frame scans. A lightweight export step may be used; no BEN_TOOLBOX
   UI or full PowerShell content editor is part of this phase.
5. Consolidate validation from earlier phases. Add only missing checks and
   contextual errors identifying the offending path and field.
6. Complete the README and examples: new item registration, adding a drop,
   changing probability, adding a recipe and reverse lookup.
7. Run combined regression checks on a new campaign and an existing save.
   Include unloaded chunks, pending drops and available supported layers.
   Record any local tests that remain outstanding.
8. Clean up this note into an accurate progress record without erasing
   unresolved limitations or claiming unfinished save work is complete.

Good smaller handoffs: index schema draft, expected Bark relationship
examples, README sections and manual test checklist. Final index generation
and validation must be checked against the actual installed formats.

### Progress Record

| Phase | Status | Applied baseline/commit | Evidence / remaining checks |
|---|---|---|---|
| 1 | Implemented in repository; local Godot checks pending | 072e048 (pre-change baseline) | Bark and nested category catalogs committed; in-engine inventory, pickup, save/load and duplicate tests remain local |
| 2 | Installed in reviewed source; local gameplay verification not independently confirmed | 5552ff1 | Phase 2 migration present; user reported installer application |
| 3 | Installer supplied; local application/compilation/gameplay pending | 5552ff1 | 12 species/profiles, deterministic independent rewards, cached compilation and narrow recipe migration bridge |
| 4 | Planned | — | Multi-reward ore scope remains a decision |
| 5 | Planned | — | Not implemented |
| 6 | Planned | — | Not implemented |

After each phase, replace its status with the precise state:
supplied, applied, compiled, locally tested, or complete. Record failures
and follow-up work before handing the next phase to another chat.

folders organize items, but item IDs and capabilities determine how they work.
ITEMS/
└── Definitions/
    │
    ├── RawMaterials/
    │   ├── Ores/
    │   ├── Minerals/
    │   ├── Organics/
    │   ├── Gases/
    │   └── Liquids/
    │
    ├── RefinedMaterials/
    │   ├── Ingots/
    │   ├── Alloys/
    │   ├── Sheets/
    │   ├── Polymers/
    │   ├── Chemicals/
    │   ├── Textiles/
    │   ├── GlassCeramics/
    │   └── Composites/
    │
    ├── Components/
    │   ├── Fasteners/
    │   ├── Mechanical/
    │   ├── Structural/
    │   ├── Electrical/
    │   ├── Electronics/
    │   ├── Power/
    │   ├── Communications/
    │   ├── Hydraulics/
    │   └── PipesValves/
    │
    ├── Technology/
    │   ├── Computing/
    │   ├── Sensors/
    │   ├── Modules/
    │   ├── Robotics/
    │   ├── Navigation/
    │   ├── AdvancedTech/
    │   └── AlienTech/
    │
    ├── Combat/
    │   ├── Ammunition/
    │   ├── Explosives/
    │   ├── Grenades/
    │   ├── Mines/
    │   └── WeaponAttachments/
    │
    ├── Equipment/
    │   ├── Tools/
    │   ├── Weapons/
    │   ├── Armor/
    │   ├── Clothing/
    │   ├── Backpacks/
    │   └── Gadgets/
    │
    ├── Survival/
    │   ├── Sleeping/
    │   ├── Camping/
    │   ├── Shelter/
    │   ├── Containers/
    │   └── Environmental/
    │
    ├── Food/
    │   ├── Raw/
    │   ├── Cooked/
    │   ├── Ingredients/
    │   ├── Preserved/
    │   └── Drinks/
    │
    ├── Consumables/
    │   ├── Medical/
    │   ├── Utility/
    │   └── Fuel/
    │
    ├── Placeables/
    │   ├── Workstations/
    │   ├── Production/
    │   ├── Power/
    │   ├── Storage/
    │   ├── Structures/
    │   ├── Furniture/
    │   ├── Lighting/
    │   ├── Defenses/
    │   ├── Agriculture/
    │   └── Communications/
    │
    └── Vehicles/
        ├── Parts/
        ├── Engines/
        ├── Propulsion/
        ├── Chassis/
        └── Deployables/

### Phase 1 — Implementation Record (2026-10-10)

Applied directly to GitHub at the user's request (without a PS1 installer
or separate backup copies). Changes are limited to resource definitions,
catalog references and documentation.

- Added `ITEMS/Definitions/RawMaterials/Organics/Bark/Bark.tres` as a
  standard `ItemDefinition`, ID `bark`, stack 40, weight 0.1 kg,
  volume 0.2 litres, with the existing placeholder icon fallback.
- Added `OrganicsCatalog.tres` and `RawMaterialsCatalog.tres` with
  explicit nested catalog references.
- Registered the RawMaterials catalog from `ITEMS/ItemCatalog.tres`.
  The master still has `IncludeBuiltInItems = true`.
- Added `ITEMS/Definitions/README.md` with approved sci-fi categories,
  per-item folder naming, stable IDs, registration steps and local checks.
- No gameplay C# files, inventory transactions, harvest logic, save
  formats or existing item identifiers were changed.
- Do not create additional empty category folders; future tooling should
  follow the documented paths when adding new content.

**Validation performed:** compared referenced project paths and Godot
resource syntax against existing resource patterns and re-read repository
files after commit. No Godot or .NET execution was available; the actual
Godot resource load, inventory collection, duplicate rejection and save/load
tests require local verification. Bark has no natural drop in Phase 1.

**Next:** after local Phase 1 checks, proceed to Phase 2 item migration
using the category convention in `ITEMS/Definitions/README.md`.


### Phase 2 — Installer Delivery (2026-10-10)

InstallContentPass2.ps1 migrates 15 built-in items using the approved
ITEMS/Definitions/<Category>/<Subcategory>/<Item>/<Item>.tres structure.
Icons are exact extracted 48x48 SVGs beside each item. Consumable and
placeable capabilities are external resources beside their items.
Equipment catalogs reference existing equipment paths without copying them.
The master is resource-only; IncludeBuiltInItems=true now fails explicitly.
The old natural/processed/food and empty content modules remain inactive
historical source, with no catalog dependency. ItemArtwork remains for
legacy source compilation and missing-icon fallback. Test Cube retains its
world drawing and built-in recipe until Phase 5.
Phase2Migration.json records IDs, properties and destinations for comparison.

Static checks: 15 unique migrated IDs; SVG XML/canvas validation; explicit
20-item catalog coverage (15 migrated + Bark + 4 equipment); resource path
and reference closure; original cube recipe/world drawing preserved.
No Godot, .NET or PowerShell runtime was available in the delivery environment.
Installer application, compilation, Godot resource loading and gameplay are
not claimed as tested. Run preview, apply with Godot closed, then check:
old save inventory/equipment; gathering and mining; berry use; crafting and
placing Test Cube; dropped items; save/reload and queued crafting restoration.
Git history supplies rollback; installer makes no separate backup copies.
Next: Phase 3 after local Phase 2 checks.


### Phase 3 — Installer Delivery (2026-10-10)

Installer refreshed against 24eaeaf30031ceb824ac283cd5be4806c6eaf3b9.
Phase 2 is present. The reviewed loading-screen additions to ChunkController
are accepted by the prerequisite check; this installer does not modify that file.
InstallContentPass3.ps1 supplies the shared reward resources, evaluator,
resource-only species migration, source-layer batch pipeline, README,
audit manifest and optional Godot check scene. It does not update GitHub.

- All 12 plant/tree/rock species have local external HarvestProfiles;
  quantity ranges and independent chances use stable existing catalog IDs.
- Old primary quantities and plant bonuses remain. CarbonTree and Wilds
  Tree01 additionally demonstrate Bark 1–3 at 75%; their Carbon stays one.
- No ordinary actor item-ID fallback remains. Explicit empty profiles yield
  no rewards and complete; missing profiles fail visibly without depletion.
- Profiles compile once per world. Randomness runs only at completion and
  uses stable world seed/source identity/species path. Retries keep the batch;
  reloads reproduce it with unchanged settings. No live hosts retained by cache.
- The existing pending-pickup save path and depletion/work records remain.
  Campaign save version and resource-change section format are unchanged.
- Save species fingerprints require a narrowly scoped migration allowance
  for exact reviewed old definitions and exact new species/profile payloads.
  Other generation edits stay rejected. Resaving uses normal profile stamps.
  Subsequent profile changes may require a new campaign; no broad save bypass.
- Ore/ground extraction, weighted loot, artwork, biome weights, generation
  identities and equipment/crafting mechanics remain unchanged.
- Old sprite-batch exports that only write HarvestItemId need an explicit
  profile before harvesting; toolbox updates are outside this phase.

Validation performed: reference/call-site audit across current C#/scenes,
complete species coverage, unique registered item IDs, preservation of all
non-reward species fields, typed-resource reference closure, deterministic
seed/quantity vectors and exact recipe migration fingerprint checks.
No PowerShell, .NET or Godot runtime was available. The included test scene
has not been executed here; compilation, installer application and full
world/save testing must be performed locally. Do not mark Phase 3 complete
until the README's checks pass. The installer defaults to preview and keeps
Git as rollback history; a failed write restores original bytes in memory.

Next: Phase 4 after local verification. Phase 4 handles ore/ground-specific
batch extraction and decides whether ore supports multiple independent rewards.
