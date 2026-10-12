# Pass B — attended Field Workbench assembly

The first workstation loop is now implemented: gather/salvage → hand-craft
Field Workbench → place → hover/click **Interact** → assemble Structural Bracket.
The normal crafting tab retains Test Cube, Bark-to-Fiber and Field Workbench.
Structural Bracket appears only at a suitable manual station:
**2 Scrap Metal → 1 bracket, 6 seconds**, with `assembly_basic` required.
The bracket is a modest intermediate component for later recipes, not a new
weapon, powered machine or complete technology progression chain.

## Ownership and attendance

Existing PlayerCrafting owns its saved hand queue plus one transient attended
station batch (1–100 operations, using its MaximumBatchSize). It reuses the
CraftJob timing model and PlayerInventory's one staged transaction. The station
owns eligibility/definition and the cursor provider; it does not own inventory,
fuel or a machine queue. Only the shared StationCraftingHud presents the UI.

The player must keep this exact living station window open, stay alive, in
range and on the same layer. The modal window blocks gameplay movement/item
use through InputModes and the inventory's external-window block. Hovering
remains nonmodal. Escape/Close, death, layer change, range loss, station
removal/unload or disabling its component cancel unfinished station work.
Another modal claim pauses station progress while it owns input.

Ingredients remain in the backpack. Each completed operation atomically removes
two scrap and inserts one bracket, subject to existing slots/weight/volume
rules. Insufficient materials/capacity leave the batch waiting without partial
payment. Cancelled operations cost nothing and lose their elapsed work; already
completed outputs remain yours. There is no dismantle/refund action.

Hand work pauses while an attended station batch is active and resumes afterward.
Opening the station without starting a batch does not pause an existing hand
queue. Queues do not reserve ingredients, matching current hand behavior;
other uses may make a batch wait for materials.

## Save policy

Completed outputs save through normal inventory state. Hand jobs retain their
existing ID/remaining/elapsed serialization. **Unfinished station work is not
saved or restored.** Saving does not discard the currently open session, but
quitting/reloading cancels it; no ingredients were paid for unfinished operations.
Station-only recipes cannot restore into a hand job. A future unattended
processor will require separate persistent inputs/outputs/jobs and explicit
schema design; this pass does not pretend hand jobs are machine jobs.

The placed workbench continues using existing structure identity, scene paths,
footprint and health. Its world scene gains Systems/Station, which changes the
saved scene fingerprint. StationRecipeCompatibility allows only reviewed Pass A
scene hashes and the exact reviewed new scene plus station-definition hash.
Other changed resources still fail normal CampaignRecipe validation. A successful
subsequent Save records current scene/dependencies. Editing the station resource
later can invalidate campaign fingerprints; use disposable campaigns for tuning.
The item/placement definition/artwork and old recipe bytes remain unchanged.

## Adding stations and recipes

- Existing CraftingRecipe exports `RequiredCapabilities`. Empty means hand only.
  The first supported capability is `assembly_basic`. Central validation rejects
  null, duplicate, unknown or reserved unimplemented capability identifiers.
- StationDefinition advertises capabilities and reach/cursor bounds. All recipe
  requirements must be provided; a scene/class name is not a requirement.
- Add Systems/Station with StationCrafting and a StationDefinition to a
  PlacedObject-compatible world scene. Shared setup waits once for host Ready,
  then attaches the existing InteractionTarget beneath terrain-adjusted Visual.
  No per-station process or cursor scanner is created.
- Register new items/recipes through the existing item/crafting catalogs. The
  hand UI, hand queue, hand payout and restore path enforce the same capability
  gate. Station queue and payout recheck the actual player's HUD/host/layer/life.
- StationCraftingHud is created once per player on first use; player scene bytes
  are unchanged. Buttons/styles and ingredient formatting use shared helpers.
- Discovery exports additive `Recipes[].RequiredCapabilities` metadata in schema
  v1. Older toolbox readers ignore it; the game still validates station recipes.

StructuralBracket item/icon/recipe are under
`ITEMS/Definitions/Components/Structural/StructuralBracket/`. Field workbench
capabilities live in `SYSTEMS/Crafting/Stations/Definitions/FieldWorkbenchStation.tres`.
Artwork remains separate in WORLDABLES/Objects/Workstations/FieldWorkbench.

## Checks before the next pass

Pull and rebuild Godot. No installer is needed for this pass.

Run `Stations/Tests/StationCraftingChecks.tscn` with F6, along with the existing
crafting, Field Workbench and content-discovery scenes. Fixtures now expect
22 registered items and 4 recipes. Source contract checks do not run a real
inventory/station session or full campaign loader.

Play-test:

1. Hand recipes still work; Structural Bracket is absent from hand crafting.
2. At a placed bench on clear ground, hover and click the existing radial
   Interact action. Check range and cursor alignment on elevated terrain.
3. With two scrap in the backpack, assemble a bracket in six seconds. Check
   exactly one output and two scrap spent. Try a multi-operation batch.
4. Cancel partway: ingredients remain and progress resets. Close/reopen: no
   silent continuation at this or another bench; old hand queue resumes.
5. Force insufficient output slots/weight/volume after queuing. Completion waits,
   materials remain; free capacity and verify exactly one operation commits.
6. Death, teleport out of range, layer change, disabling the component, destroying
   or unloading the bench must cancel unpaid work and close/release the HUD.
   Held LMB on closing must not shoot unexpectedly. Check storage/inventory UI.
7. Continue a pre-Pass-B campaign containing a placed Pass A bench. Verify
   position, health, interaction and no duplicate structure; save again and
   fully quit/Continue. No generic fingerprint bypass should be needed.
8. Save while a station batch is partial, fully quit and Continue. Completed
   bracket stacks persist; unfinished batch does not resume or consume scrap.
   Verify an old partially completed Test Cube hand queue still resumes.
9. Regenerate ContentIndex.json; bracket/recipe relationships and required
   capability metadata should appear.

Static resource/contract/transaction/fingerprint checks were performed remotely;
Godot/.NET are unavailable here, so compilation and gameplay remain local.
Alien Resin still comes from Fronds/Alien Shrubs; no loot or rock-generation
changes are bundled. Pass C (actual independently processing station) remains
separately scoped. First decide the next useful recipe/material chain and its
persisted job/buffer/fuel policy.
