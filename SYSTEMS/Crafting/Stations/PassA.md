# Improvised Field Workbench — Pass A

This pass provides an earned, hand-crafted, inventory-placeable workbench.
**It does not yet open a station UI or produce station-only items.** That is
Pass B in WORKSTATION_CRAFTING_PLAN.md. Nothing grants a free placed bench.

## Current loop

1. Salvage Scrap Metal from destroyed robot wrecks or other existing loot.
2. Gather Rock and Plant Fiber using the existing harvesting routes.
3. Gather Alien Resin from Fronds or Alien Shrubs. Each profile currently
   guarantees one Resin. These species are selected in Wilds content, but
   terrain placement and world seed determine where they actually appear.
4. In the normal Crafting tab, select the Workstations category and craft
   **Improvised Field Workbench**: 6 Scrap Metal, 4 Rock, 5 Plant Fiber,
   2 Alien Resin; 20 seconds; output one `field_workbench` item.
5. Put it on the hotbar and use the existing placement preview to place it
   on clear, sufficiently flat ground within reach. Placement consumes one.
6. Save, fully quit and Continue: the placed structure should restore through
   the existing building save identity, scenes, footprint and health.

The recipe uses ordinary player queue and atomic staged inventory exchange.
Unfinished ingredients stay in the backpack; cancellation is unchanged.
Insufficient output space/weight/volume waits without charging ingredients.
Workbench storage values are provisional: stack 1, 5 kg, 6 litres. Default
Field Backpack capacity is 30 litres and maximum weight is 28 kg, so the
bench is not inherently uncarryable. Other carried gear can still fill it.

## Important acquisition gap

The plan expected enemy-sourced Resin. The reviewed robot wreck table contains
Scrap Metal, Copper Wiring and Circuit Board, **not Resin**. The item currently
comes from two plant profiles. This pass preserves that acquisition route and
all existing drops; it does not claim to implement the planned combat-only
resin gate. Robot salvage still provides a current Scrap Metal route.

Smallest separately approved option: assign Resin to a chosen existing enemy
loot resource and explain its origin (e.g. salvaged adhesive carried by robots).
Simply adding a weighted entry is not guaranteed loot. Choose whether enemies
should guarantee it, how much, and whether plant Resin should remain available.
Removing existing plant drops can affect saved generation fingerprints. Do not
silently rewrite those resources or bypass compatibility checks. No new enemy,
loot engine or harvest change is included here.

## Resource ownership

- Inventory item, icon, hand recipe and placement settings:
  `ITEMS/Definitions/Placeables/Workstations/FieldWorkbench/`.
- New category catalog: `.../Workstations/WorkstationsCatalog.tres`, linked
  from the existing PlaceablesCatalog; master ItemCatalog remains unchanged.
- Hand recipe registration: existing `SYSTEMS/Crafting/CraftingCatalog.tres`.
- Gameplay scene: `WORLDABLES/Objects/Workstations/FieldWorkbench/FieldWorkbench.tscn`.
  Existing PlacedObject script, StaticBody2D and Systems/Health (150 HP).
- Artwork: adjacent FieldWorkbenchArtwork.tscn and provisional SVG. Placement
  instantiates artwork once; collision, terrain position, lighting, shadow,
  obstruction fading and destruction remain shared. No per-frame station scan.
- Replace the artwork Sprite texture with an imported sprite later. Keep its
  logical origin at ground contact, and update ArtworkScale/ObstructionOutline
  together when its dimensions change. No artwork is baked into TestCube.

The world scene is intentionally not a unique workstation C# engine. Pass B
will add shared components under SYSTEMS/Crafting/Stations and UI/Crafting.
No capability requirements, processors, buffers, machine jobs, station pickup
or dismantle/refund mechanics are implemented in Pass A.

## Saves and compatibility

Existing item/recipe IDs and existing item/world/loot/harvest resource bytes
are preserved. Only the item-category and crafting catalog links change.
The workbench uses existing StructureSaveData and no save schema change.
Once saved, the new item, placement definition, world/artwork scenes and
artwork dependencies participate in existing object recipe fingerprints.
Do not rename/rewrite these after saving a bench without considering campaign
compatibility. No general hash exception or migration bypass is added.

Continue a pre-change campaign as part of local verification. If it refuses a
changed catalog fingerprint, report the exact resource path; do not disable
CampaignRecipe validation. Newly placed benches must restore without billing
inventory twice, and destroyed benches must remain absent after saving.

## Apply and verify before Pass B

Close Godot, preview/apply InstallWorkbenchPassA.ps1, then reopen/import/build.

```powershell
powershell -ExecutionPolicy Bypass -File .\InstallWorkbenchPassA.ps1 -ProjectRoot "$PWD"
powershell -ExecutionPolicy Bypass -File .\InstallWorkbenchPassA.ps1 -ProjectRoot "$PWD" -Apply
```

Run Stations/Tests/FieldWorkbenchChecks.tscn with F6. Existing crafting and
content-discovery fixtures have been updated for 21 items / 3 recipes; their
older cube/fiber checks remain. These scenes test resource contracts, not full
placement/inventory/save gameplay.

Manual checks:

- Test an existing campaign and a new campaign. Obtain the actual materials.
- Craft one bench; check counts, 20-second timing, waiting for output capacity,
  queue quantity and cancellation. Save an unfinished job and resume it.
- Add to hotbar, preview placement, reject obstruction/out-of-range/steep ground,
  place and check exactly one item consumed. Check collision, light/shadow and
  player obstruction fading. Confirm Test Cube still crafts/places normally.
- Save with bench in inventory and with a placed bench; fully quit/Continue.
  Check item identity, footprint, position and health. Travel away/back and
  transition supported layers; verify no duplicates or layer changes.
- Damage/destroy the bench; save and Continue. It must remain removed and its
  cells become available for another placement. No refund is promised here.
- Regenerate ContentIndex.json. Workbench should be a registered item and its
  hand recipe visible to the external inspector.

Static checks were performed during preparation. Godot, .NET and PowerShell
were unavailable, so local compilation and gameplay are still required.
After this passes, proceed to Pass B with one agreed station-only output.
