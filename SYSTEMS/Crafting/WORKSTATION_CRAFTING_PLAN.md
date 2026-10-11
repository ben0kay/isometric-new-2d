# Fractured Horizons — Crafting Stations & Material Processing Plan

> **Status: PLANNED, NOT IMPLEMENTED.** This is a Codex implementation guide, not evidence of existing station functionality.
>
> **Repository reviewed:** `ben0kay/isometric-new-2d` at `e6ff3ec75a155756a2872b8580d45eeba164e8c9`. Reinspect HEAD and current feature documentation before changing code.
>
> **Goal:** Extend the existing data-driven crafting foundation with **reusable workstations and, later, material processors**, beginning with a **craftable, placeable Improvised Field Workbench**. Do not develop a separate parallel crafting engine.

## 1. Intended experience

Read root `README.md` (primary vision), `GAME_DIRECTION.md` and `NOTES/CodePreferences.md`.

The player is a capable scientist/expedition engineer **unexpectedly stranded on a dangerous alien planet**. They have scarce surviving expedition supplies and basic tools, but not a functioning fabrication industry. Technology is known; resources, power, precision equipment and infrastructure must be recovered or built.

Progression should resemble **ARK survival breadth + the satisfying material chains of modded Minecraft**, without becoming a Factorio/Satisfactory production-line simulator. Workstations transform what is possible, not merely numbers. Realistic principles, accessible execution; future modest automation/turrets are optional.

**Design distinction:**

- **Hand crafting:** Simple field repairs, lashings, survival supplies and construction of the first improvised workbench using basic expedition tools.
- **Manual workstations:** Require presence and a capable station to manufacture/assemble better tools, components and equipment.
- **Processing stations:** Furnaces, reduction rigs, recyclers, chemical processors and fabricators may have their own inputs, outputs, fuel/power and **persisted jobs**; can operate independently where appropriate.
- **Upgrades:** Higher-grade stations may provide several capabilities, shorten chains or improve output. Do not lock recipes to an individual station scene/class name.

## 2. Current functionality — preserve and reuse

| Existing system | Verified current responsibility |
| --- | --- |
| `SYSTEMS/Crafting/CraftingRecipe.cs` | External `Resource` recipe ID, ingredient list, output, quantity, duration, validation |
| `SYSTEMS/Crafting/CraftingCatalog.cs` and `CraftingCatalog.tres` | Explicit recipe registration and duplicate/invalid-ID rejection |
| `SYSTEMS/Crafting/PlayerCrafting.cs` | Player-owned timed **hand-crafting** queue; ingredients remain in backpack until a successful end-of-job exchange |
| `UI/Inventory/Tabs/CraftingTab.cs` | Lists **all** catalog recipes today; uses `PlayerCrafting.TryQueue` and player inventory |
| `SYSTEMS/Saving/PlayerSaveSections.cs` | Persists player queue by recipe ID, remaining work and elapsed time |
| `SYSTEMS/Placement/{PlaceableDefinition,PlayerPlacement,PlacementWorld,PlacedObject}.cs` | Existing item-to-world placement, footprint, destruction, occupied cells and saved structures |
| `SYSTEMS/Interaction/{IInteractionProvider,InteractionTarget,InteractionResolver}.cs` | Cursor selection; currently primary-action-only providers |
| `WORLDABLES/Objects/Storage/UI/StorageHud.cs` | Existing storage UI and transfers; not a manufacturing window |
| `SYSTEMS/Saving/{WorldObjectSaves,CampaignRecipe}.cs` | World-object save identity, resource fingerprints and compatibility checks |
| `ITEMS/Definitions/README.md` and `SYSTEMS/Crafting/Phase5README.md` | Item/recipe authoring conventions and existing resource-based migration notes |

**Gaps:** No workstation capability requirements on recipes, no station definition/operation component, no station-specific recipe filtering, no station crafting UI/ownership, and no persisted station processing inventory/job model. **Do not confuse player hand jobs with machine jobs.**

Current IDs to reuse: `alien_resin` exists; other existing materials include Scrap Metal, Rock and Plant Fiber. **Verify that Alien Resin actually drops from intended enemies and that the first workbench can be completed without a progression softlock**; existence of the item is not proof of an obtainable drop.

## 3. Proposed scalable folder ownership

Create only folders/files needed for a tested pass. Suggested destination names, subject to inspecting current conventions:

```text
SYSTEMS/
  Crafting/
    WORKSTATION_CRAFTING_PLAN.md          [this design document]
    CraftingRecipe.cs                     [existing: add requirement metadata carefully]
    CraftingCatalog.cs                    [existing; keep one recipe registry]
    PlayerCrafting.cs                     [existing: preserve hand-crafting behaviour]
    Stations/
      StationDefinition.cs                [new: resource/capabilities/config]
      StationCrafting.cs                  [new: station gameplay component]
      StationInteractionProvider.cs       [new: adapter for shared interaction]
      StationJob.cs                       [optional once jobs exist]
    Processing/
      ProcessingFuel.cs                   [future: fuel, if required]
      ProcessingPower.cs                  [future: power, if required]

UI/
  Crafting/
    StationCraftingHud.cs                 [new: ONE shared station crafting window]
    StationCraftingHud.tscn               [optional, if a scene helps inspector authoring]

WORLDABLES/
  Objects/
    Workstations/
      FieldWorkbench/
        FieldWorkbench.tscn               [placed gameplay object, health/systems]
        FieldWorkbenchArtwork.tscn        [visuals only; Sprite2D/painted asset]
        FieldWorkbench.png                [future/imported sprite only if needed]
      SalvageRig/                          [future, not empty placeholder folders]
      MetalworkingStation/                 [future]
      ElectronicsBench/                    [future]
    Processing/
      OreReductionFurnace/                 [future]
      AdvancedSmelter/                     [future]

ITEMS/
  Definitions/
    Placeables/
      Workstations/
        FieldWorkbench/
          FieldWorkbench.tres             [new placeable inventory item]
          FieldWorkbenchRecipe.tres       [new HAND-CRAFTED recipe]
          FieldWorkbenchPlaceable.tres    [new placement settings]
          FieldWorkbenchIcon.svg          [provisional icon]
    Components/
      Structural/
        <ActualWorkbenchOutput>/          [only after agreeing first output]
```

**Ownership rules:**

- `SYSTEMS/Crafting/` knows recipes, capabilities, validation and **operations**, not appearance.
- `WORLDABLES/Objects/` holds station-specific scene trees and artwork. A new visual variant **does not require copying the crafting engine**.
- `UI/Crafting/` provides the reused crafting window, not a unique interface per station.
- `ITEMS/Definitions/` owns the inventory item, its stable ID, placement configuration, icon and output-adjacent recipe; catalogs explicitly register resources.
- `SYSTEMS/Interaction/` remains the one cursor-action pipeline: adapter exposes `Interact` to open the **exact** station. Do not create extra input handlers or a second global target query.

## 4. First real workstation — Improvised Field Workbench

**Appearance:** A rough, compact expedition workbench bolted together from crash-pod metal, local rock supports and improvised lashings. Exposed fasteners and salvaged tool holders, perhaps a damaged diagnostic display as decoration. **Core work is manual; electricity is NOT required.** A scientist's emergency toolkit/hand tools make assembly plausible.

**Proposed hand-crafted recipe** (balance numbers are provisional):

| Material | Count | Narrative use |
| --- | ---: | --- |
| Scrap Metal | 6 | Work surface, frame and attachment brackets |
| Rock | 4 | Stabilising base/supports |
| Plant Fiber | 5 | Straps, wraps and lashings |
| **Alien Resin** | **2** | Tough adhesive / joint reinforcement |

- The Field Workbench itself is crafted using the **existing hand-crafting system**; no station requirement on this recipe. Suggested `20 s` duration (adjust through resource).
- The resin requirement is **intentional early danger**: the player should need to encounter/fight suitable creatures to obtain it. Confirm/author a deterministic-enough gameplay acquisition route and sensible nearby spawn availability; never rely on impossible or vanishing loot. Do **not** invent a new enemy or change global drop tables without explicit scope approval.
- All ingredient quantities and item IDs must resolve through `ITEMS/ItemCatalog.tres`. New item ID proposal: `field_workbench`; one stable recipe ID proposal: `field_workbench` or `field_workbench_handcraft`, **check collisions before committing**.
- A completed workbench becomes one inventory-placeable item, **not** an automatically spawned free structure.
- Placing it uses existing `PlaceableDefinition` / `PlacementWorld` and a `PlacedObject`-compatible scene, with health/footprint/lighting aligned to other placeables. Reuse existing destroy/placement save behaviour.
- Verify the stationary workbench can be placed and restored in an existing campaign without changing previously fingerprinted resource bytes. Do not mutate old `TestCube` artwork/scene merely to reuse it.
- Basic workbench capabilities proposed: `assembly_basic` and perhaps `repair_basic` **once a real recipe uses them**. No ore smelting, electronics manufacturing or powered fabrication at this bench.
- First demonstrable **station-only** output should be one modest component (e.g. a `Structural Bracket`) using registered materials. Agree final ingredients and output before adding content. The point is to demonstrate **the station requirement**, not introduce a dozen items.

## 5. Station capabilities and recipe gating (most important architecture)

Extend the **existing** `CraftingRecipe` with an Inspector-friendly capability requirement, such as a validated collection of stable string IDs:

- `RequiredCapabilities = []` means **hand-craftable**. It appears in the existing Crafting tab and may also be available through a capable station only if that is intentional.
- `RequiredCapabilities = ["assembly_basic"]` means it can **only** be queued/executed while interacting with a suitable manual station.
- Other future IDs: `salvage_processing`, `ore_reduction`, `metalworking`, `electronics`, `advanced_fabrication`.
- `StationDefinition` advertises the capabilities it actually provides, plus optional reach/working conditions. A station is eligible only if it satisfies **all** required capabilities; don't bake class/scene names into recipe IDs.
- Use consistent validation: distinguish missing, duplicate, unknown and unsupported capability IDs before recipe execution. Keep capability IDs centrally documented/validated rather than silently accepting typos.
- **Critical:** `CraftingTab` currently lists every catalog recipe. Filter its hand-craft recipe list and enforce the gate in `PlayerCrafting.TryQueue` (and again at the point of exchange). **UI filtering is not security/eligibility validation.** A station-only recipe must not be executed from an old player job or through a direct method call with no valid station.
- Preserve old recipe identity and behaviour. Existing `test_cube` and `plant_fiber_from_bark` recipes remain valid hand crafts unless separately approved.
- Don't make a second recipe catalog or duplicate material exchange logic.

**Queue ownership decision:** Manual assembly and unattended processing are different. For first proof-of-concept, choose a clear **station-attended** execution model and document it. A player-owned station-tagged job must remain station-eligible throughout work and **cannot silently continue after walking away or opening another unrelated station**. If a queue requires new save fields, prefer a separate backwards-compatible extension and test old player jobs.

Later **true processing stations** should have independent, durable production state bound to a persistent world-object ID, with explicit input/output buffers, fuel/power and offline/unloaded behaviour. Do not pretend the existing `PlayerCrafting` queue is already a universal machine queue.

## 6. Interaction and UI

- Use `StationInteractionProvider` with the existing `IInteractionProvider` and runtime `InteractionTarget.Attach`; primary label can be `Interact` for consistency.
- The controller selects the hovered station; execute validates the exact `PlacedObject`, player reach, layer, life, and station operating state before opening a shared `StationCraftingHud`.
- The window shows station name, allowed recipes, owned/required ingredients, output, crafting time, action button and progress/queue if supported in the chosen pass.
- Use `InputModes` to handle an **open** modal window and block unintended weapon use while it is open. A visible radial menu remains nonmodal; don't disable movement merely on hover. Close on Escape, close button, death, out of range, layer switch, object destruction or unload; release modal claims reliably.
- Use a single station HUD and shared handlers. No per-station `_Process` for cursor detection and no bespoke CraftingTab clone if shared UI cells/helpers can be used.
- Do not add Pick Up/Dismantle to the workbench until persistent contents/jobs and transaction/refund rules have been designed.

## 7. Station inventories, jobs and save compatibility — future processing foundation

Before building the ore furnace, define how stationary processing owns items and progress:

1. **Identity:** Bind each station state to its existing placed object's stable `PersistentId` and world layer. A save must not resurrect a removed/destroyed workstation.
2. **Items:** Reuse existing inventory/stack primitives or `WorldStorage` where suitable, but keep **input, output and fuel responsibilities** unambiguous. Neither full buffers nor cancelling a job may lose or duplicate materials.
3. **Job lifecycle:** Validate ingredients and capabilities; commit/lock inputs at a documented point; progress on a bounded tick; commit output once; handle blocked output and cancellation explicitly.
4. **Persistence:** Restore materials, outputs, fuel, job ID, quantities and elapsed time. No rerolls, double production or charging inputs twice across save, quit, chunk unload/reload or destruction.
5. **Simulation:** Avoid processing one tick per distant unloaded object. Prefer one owner-managed active-station scheduler or elapsed-time restoration with a defined policy. Decide whether work advances while unloaded/offline **before** coding.
6. **Save evolution:** `WorldObjectSaves` currently saves placed structures (identity, scenes, health) and ordinary storage separately; it does not encode station processing queues. Adding this is a **real schema change**. Audit `CampaignRecipe` resource fingerprints, `WorldObjectSaves.ReadSection` version checks and old-campaign behaviour. Explicit backwards compatibility is mandatory; never silently bypass hash validation.

Examples beyond this first workbench:

| Station | Capability | Processing example |
| --- | --- | --- |
| Salvage Processing Rig | `salvage_processing` | Scrap/wreck salvage → reusable metal and components |
| Improvised Reduction Furnace | `ore_reduction` | Iron Ore + suitable reductant + heat → crude iron |
| Field Metalworking Station | `metalworking` | Crude iron → bars, plates and fittings |
| Electronics Bench | `electronics` | Wiring and components → circuits/devices |
| Advanced Fabricator | Multiple capabilities | Higher-end precision components and gear |

**Iron realism:** Ore reduction requires chemistry as well as heat; for early equipment, a carbon-based reductant and controlled airflow can produce an iron bloom/sponge material for further shaping. Generic `carbon` should not automatically be treated as charcoal without world-design approval. Players handle ingredients and equipment, not manual chemistry simulations.

## 8. Codex implementation sequence (stop after each pass)

### Pass A — Placeable Field Workbench, with existing systems

- [ ] Reinspect HEAD and confirm an Alien Resin source is actually obtainable from intended enemy loot.
- [ ] Register the new placeable workbench `ItemDefinition` in the existing catalog; add one **hand crafting** recipe in the existing `CraftingCatalog` (no duplicate IDs).
- [ ] Add the world scene `PlacedObject` + `Systems/Health` and separate visual scene. Honor the existing placement pipeline, scale, obstruction, shadows and collision.
- [ ] Verify manually: collect the recipe materials (including enemy-sourced resin), craft the item in the **normal Crafting tab**, put it on hotbar, place it and save/continue with it present.
- [ ] **Stop for player testing.** Do not add station production, powered equipment or new enemies in Pass A.

### Pass B — Working manual station recipe and shared UI

- [ ] Implement the station capability contract and add required-capability data on recipes; preserve old hand recipes.
- [ ] Split/filter available hand versus station recipes **and enforce station eligibility in gameplay code**, not only UI.
- [ ] Add an interaction adapter and **one** shared workstation window. Hover/click the placed Field Workbench to see its recipes.
- [ ] Add just **one** real workbench-only recipe using an agreed registered output item. Use existing inventory transaction semantics; implement a clear station-attended timing/queue policy with tested cancellation and save behaviour.
- [ ] Verify cannot craft the station-only item by hand, from another unqualified station, too far away, across layers, while dead, or after removing the station.
- [ ] **Stop for player testing.** Do not build the ore reduction furnace yet.

### Pass C — First actual material processor (separate approval)

- [ ] Design independent inputs/outputs, fuel, job ownership and station persistence.
- [ ] Add one real salvaging or ore-processing station using the same capability system and UI conventions.
- [ ] Verify save/restore, input payment, blocked output, chunk unload, partial processing and station destruction with no duplication.
- [ ] Only then generalize efficiency upgrades, multiple outputs, power, advanced machines or small-scale automation.

## 9. Non-negotiable regression checklist

- [ ] Existing test-cube and bark-to-fiber hand recipes remain visible, craftable and save-compatible.
- [ ] Old crafting queues restore; no change to original recipe IDs or meanings without an approved migration.
- [ ] All item output/ingredient transactions are atomic with full bag/slot/weight/volume restrictions preserved.
- [ ] Workbench cannot be crafted unless player has the actual required materials; Alien Resin acquisition is real and accessible.
- [ ] Workbench placement, health, destruction and persistence behave like existing placed structures.
- [ ] Missing artwork or badly configured station capability fails clearly, without infinite polling or broken start-up.
- [ ] Interaction wheel suppresses LMB attacks when appropriate; a station window closes and releases input ownership cleanly.
- [ ] Unloading/reloading a chunk does not duplicate the workbench or any contents/jobs.
- [ ] Test against a pre-change campaign; recipe/resource fingerprint compatibility must be checked, not disabled.
- [ ] Build C#, run relevant existing craft/placement tests, and **play-test** the craft → place → interact → produce loop. Report tests actually run separately from tests only recommended.

## 10. Copy-ready first Codex task

> Read `SYSTEMS/Crafting/WORKSTATION_CRAFTING_PLAN.md`, root `README.md`, `NOTES/CodePreferences.md`, `SYSTEMS/Crafting/Phase5README.md` and current source. Implement **Pass A only**: a hand-craftable, placeable Improvised Field Workbench, costing 6 Scrap Metal, 4 Rock, 5 Plant Fiber and 2 Alien Resin, with 20 seconds provisional craft time. Use existing resource catalogs, `PlayerCrafting`, `PlaceableDefinition`, `PlacementWorld` and `PlacedObject`; keep its gameplay scene and artwork scene separate for future art replacement. First confirm Alien Resin can really be obtained from enemies; if not, report the gap and propose the smallest separate fix, without silently rewriting enemy drops. Do not implement the station UI/logic, recipe requirement migration, save schema or future furnaces yet. Preserve existing content and test save/continue. Show actual changed paths and verification, then stop for local testing.

---

**Document maintenance:** Mark a pass complete only after inspecting committed files and recording the build and gameplay evidence. Future workbench/furnace discussions should extend this plan rather than creating a second independent crafting foundation.
