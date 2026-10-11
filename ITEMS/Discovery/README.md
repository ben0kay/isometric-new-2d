# Content discovery and authoring

Run `ContentReportExport.tscn` in Godot with F6 after importing/building the
project. It creates `ContentIndex.json` and `ContentIndex.md` under
`user://ContentDiscovery`, prints the absolute folder in Output, then closes.
Use Godot's **Project → Open User Data Folder** to find that directory.
F5 continues to run your normal game; the report scene is not attached to it.

For external tooling, use your Godot **.NET console executable**:

```powershell
.\ITEMS\Discovery\ExportContentReport.ps1 -GodotExe 'C:\Tools\Godot\Godot_mono_console.exe'
```

Supply your actual executable path. Optional `-OutputDirectory` chooses the
report folder; the wrapper defaults to `%LOCALAPPDATA%\BenTools\ContentReports\isometric-new-2d`.
Godot must have imported and compiled the project first. The wrapper does not
install dependencies, compile code or edit resources. Direct CLI equivalent:

```powershell
& 'C:\Tools\Godot\Godot_mono_console.exe' --headless --path "$PWD" 'res://ITEMS/Discovery/ContentReportExport.tscn' -- '--content-report-dir=C:\ContentReports'
```

An invalid export still writes diagnostics and partial rows when possible,
then exits with code 1. Inspect Godot Output as well as the report. A failure
to load/serialize/write can stop export before both files exist. Do not treat
an older report left in the folder as successful output from a failed run.
Warnings do not make validation fail. Generated reports are snapshots;
regenerate after every change and avoid committing them as an authoritative
second database.

## Coverage

The explicit export scans `.tres`/`.res` files in ITEMS, PLAYER, WORLD, CONFIG,
SYSTEMS/Crafting and SYSTEMS/Harvesting. It uses Godot's resource loader and
class defaults; it does not guess values from text or instantiate scenes.
Dependencies are checked through ResourceLoader. Definitions are reported
from their files; registration is separately traced from the master item and
crafting catalogs. Embedded ground definitions are obtained from their catalog.

Sources cover harvestable plants, trees, rocks, ore primary/bonus rewards and
ground digging. Profiles include unused files, making orphan content visible.
Biome bindings use resolved Content accessors, including legacy fallback:
shared content takes precedence over leftover embedded legacy vegetation.
All scanned biome catalogs are listed, including cave catalogs. Grass has no
item harvesting path and is excluded from item-source rows, although biome
vegetation validation checks its species lists. Entity loot, chest loot,
liquids, dropped-item lifetime, weapon modules and live inventories are outside
this phase's discovery scope.

**Defined, registered and biome-selected are different.** An item or recipe
file can exist without registration. A species can exist without any biome
binding. ConfiguredForSelection means a scanned catalog lists the biome,
Enabled and SelectionWeight permit it, the family's base counts permit it,
and the species weight is positive. This does not prove the catalog is assigned
to the active layer/campaign, or account for placement frequency overrides,
climate, geometry, distance or terrain rejection. It is not an actual spawn rate.
Ore settings are reported as defined options, not proof of active assignment.
Older campaigns may retain disabled generated ore. Ground registration points
at the currently hardcoded GroundResourceCatalog. Registered item output means
the exact master-catalog resource, not a copy with the same ID.

Validation reuses ItemCatalog.Initialize, CraftingCatalog.Validate,
CraftingRecipe.Validate, HarvestProfile.Compile, OreBatchPlan,
GroundResourceCatalog.Validate, BiomeSpecies and vegetation validation.
It adds contextual paths, definition identity/variation checks, conflicting
file identities, missing dependency reporting and registration warnings.
Catalog initialization now also rejects null arrays and retired built-in
flags on nested categories. It does not change valid gameplay data.
If item initialization fails, dependent validators cannot resolve canonical
items; fix that diagnostic and rerun before relying on the partial report.

## Schema version 1

JSON uses C# property names (PascalCase). Root fields are SchemaVersion, Scope,
ScanRoots, Valid, Diagnostics, Items, Sources, Recipes, Profiles and OreSpawns.
No manual relationship list is required; reverse lookups are generated.

- Items: Id, Path, DisplayName, Type, storage values, Capabilities, Registered,
  RegistrationPaths and SourcePaths/ConsumedBy/ProducedBy. Reverse links cover
  defined content, including inactive/unregistered definitions; inspect target
  registration/binding fields before presenting them as obtainable in-game.
- Sources: Id, Path, Kind, Extraction, Work, RequiredStrength, TotalUnits,
  UnitsPerBatch, RegistrationPaths, Rewards and Biomes. Zero total/batch for
  one-shot vegetation means not applicable, not a zero yield.
- Rewards: ItemId, EditPath, Field, Context, MinimumCount, MaximumCount, Chance.
  Chance is 0–1 conditional on successful extraction. Each profile entry rolls
  independently. Ore primary bounds represent possible final/full batches;
  ordinary batches use min(remaining, UnitsPerBatch). Bonuses are once per
  successful ore batch, not multiplied by extracted units. Ground pays one
  item per successful dig; UnitsPerDeposit is depletion capacity.
- Recipes: stable Id, Path, Category, Description, output identity/path/count,
  DurationSeconds, Registered, RegistrationPaths, Ingredients. Each ingredient
  includes its ItemId, Count and EditPath.
- Profiles: Path, SourcePaths, Shared and Rewards. Shared means multiple source
  definitions reference this profile; all are affected by an edit. Individual
  entries can also be shared: compare repeated reward EditPath values.
- Biomes: BiomeId, BiomePath, CatalogPaths, EditPath, Family, Weight,
  BiomeEnabled, FamilyEnabled and ConfiguredForSelection.
- Diagnostics: Severity (`error`/`warning`), Path, Field, Message.

`res://file.tres::SubResourceId` locates an embedded Godot subresource. A
`#Field[index]` suffix is a contextual fallback for resources without an engine
path. A future editor should open the containing file (before `::` or `#`) and
use the locator/field to select the right block. Never create an OS filename
from the full locator. Definition/profile edits are made in the resources,
not in the exported JSON. Nonfinite numeric values in invalid rows are replaced
with zero to permit JSON serialization; the diagnostics retain validation
failure. Never interpret such a report as valid content.

## Authoring examples

**New inventory item:** follow `ITEMS/Definitions/README.md`. Create
`ITEMS/Definitions/<Category>/<Subcategory>/<Item>/<Item>.tres` with a unique
stable lower-snake-case ID. Add it to the appropriate subcategory ItemCatalog,
then connect parent catalogs up to ITEMS/ItemCatalog.tres. A folder alone does
not register an item. Keep all built-in flags false and preserve item subtype
and capability resources. Existing equipment paths are intentionally retained.

**Add Bark to a source:** open the species definition's HarvestDrops profile.
Add a HarvestDropEntry with ItemId `bark`, MinimumCount 1, MaximumCount 3,
Chance 0.75. Do not add a duplicate Bark entry to a profile already containing
one. Ensure Bark is registered. Assign the profile to the species, then add a
BiomeSpecies entry with a positive weight to an appropriate biome Content if
it should spawn. Use TreeDefinition for trees and PlantDefinition for plants.
A profile's reward settings do not register/enable a species in a biome.

**Change a probability:** select the exact reward EditPath from the report
and edit Chance in that resource. Check Profiles.SourcePaths and repeated
reward EditPath values before editing shared data. CarbonTree and Wilds Tree01
currently both yield Carbon plus independent Bark 1–3 at 75%, using separate
profiles. Their drop chances apply when harvested, not per chunk.

**Ore bonus:** edit IronBonusHarvest.tres. Current Rock bonus is 1–2 at 35%
per successful ore batch. Primary iron uses IronDeposit's YieldItemId,
UnitsPerBatch and TotalUnits. Do not duplicate the primary item in bonuses.
Changing ChancePerChunk in ore spawn settings affects placement, not rewards.

**New recipe:** keep an external CraftingRecipe beside its output item and
register it in SYSTEMS/Crafting/CraftingCatalog.tres. Set a new stable recipe
ID, canonical output resource, positive bounded counts and finite positive
duration. See SYSTEMS/Crafting/Phase5README.md. Current examples are Test Cube
(two Scrap Metal → one cube) and PlantFiberFromBark (one Bark → one Plant Fiber).

**Reverse lookup:** open the generated JSON's Items row for `bark`. SourcePaths
should point to CarbonTree and Wilds Tree01; ConsumedBy should include
PlantFiberFromBarkRecipe.tres. Follow source Rewards for exact counts/chance and
edit locations, and Biomes for selection bindings. A future Avalonia UI can use
these same relationships without duplicating game-specific rules.

## Identity, persistence and future editor integration

Preserve IDs for inventory and recipe save lookup. Source definition paths,
world layers and generated identities also affect persistence and deterministic
rolls. Renaming a species file is not equivalent to renaming its display label.
CampaignRecipe fingerprints generation resources/dependencies. Changing a
harvest profile, species or ore setting may make an existing campaign refuse
Continue; do not bypass fingerprints to enable editing. Use a disposable new
campaign when testing tuning. Narrow previous migration bridges are not general
permission to edit saved world recipes. Crafting saves store recipe ID and
elapsed progress rather than recipe snapshots; altered duration/cost can affect
existing queued jobs. Keep paths/IDs and legacy cube semantics stable.

An external editor can invoke the export, reject error reports, display
relationships, then propose edits to the actual .tres files. Future writing
should check fresh file hashes, preserve unrelated fields/subresources, validate
with Godot again and show diffs before applying. No external editor, auto-write,
per-frame scanning, save mutation or new spawn rules are implemented here.

## Final local verification

1. Rebuild, run ContentReportExport.tscn with F6, inspect JSON/Markdown and
   check zero errors. Verify Bark, Iron, Sand/Clay, cube/fiber and Wilds bindings.
2. Run ITEMS/Discovery/Tests/ContentDiscoveryChecks.tscn with F6. It tests baseline
   relationships, repeat export, invalid profile entries and catalog fixtures.
   Baseline assertions are intentionally content-specific: update them when
   intentionally adding/removing items or changing these example rewards.
3. Run the existing harvest, ore batch, ore spawning and crafting check scenes.
4. New campaign: test inventory/equipment/food/placement; hand-gather plants;
   mine trees/rocks; ore full and final batches with bonuses; dig Sand/Clay;
   craft/cancel/batch/full-capacity cases; gather drops.
5. Existing campaign: save partial work and a crafting queue, fully quit,
   Continue; verify work/quantities. Harvest and immediately save before deferred
   pickups appear; Continue and verify no loss or duplicate rewards.
6. Unload/reload chunks after partial harvest and depletion. Verify sources
   retain progress and depleted sources stay absent. Check supported layer
   transitions, drop ownership and collection on the correct layer.

Prepared with static checks only; Godot/.NET/PowerShell are unavailable here.
This completes the planned content refactor implementation, with local export,
compilation and combined gameplay verification still outstanding. It does not
mark unrelated saving or player architecture work complete.
