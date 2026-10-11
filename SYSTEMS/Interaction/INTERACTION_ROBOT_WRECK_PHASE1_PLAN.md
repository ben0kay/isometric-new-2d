# Fractured Horizons — Universal Interaction, Robot Wreck Vertical Slice

> **Status: PLANNED — documentation only.** Nothing in this plan is gameplay code.
>
> **Inspected repository:** `ben0kay/isometric-new-2d`, `main` at `9593545ab5a28550685c8ac1f8f7f3b019b08bfe` (2026-10-11 UTC). Re-read **current HEAD** and any in-progress Codex changes before editing.
>
> **Goal:** Prove one reusable, inexpensive cursor-targeted action system using an **actual robot death wreck**. Hover a wreck in range to show a faint cyan wheel with a central **Interact** action; **E or clicking the centre** opens the *existing* loot UI. No crafting station is required.
>
> **Read also:** `NOTES/CodePreferences.md` and `PLAYER/PLAYER_COMBAT_PHASE1_PLAN.md`. Combat-controller extraction is a separate plan: do not independently rewrite `Player.cs` / `PlayerInput.cs` against old baselines if another Codex task is editing them.

## 1. Why this first slice

The robot loot workflow already exists:

`ENTITIES/Death/EntityDeathLoot.cs` → `SYSTEMS/Loot/LootWorld.cs` → `WORLDABLES/Objects/Loot/RobotWrecks/Basic/BasicRobotWreck.tscn` → `SYSTEMS/Loot/LootContainer.cs` / `SYSTEMS/Storage/WorldStorage.cs` → `WORLDABLES/Objects/Storage/UI/StorageHud.cs`.

- Entity death records a persistent wreck identity and layer.
- LootWorld instantiates/restores the existing wreck and preserves generated contents.
- WorldStorage validates same-layer, live-player and interaction range, and performs inventory transactions.
- StorageHud currently scans the `world_storage` group **10 times/second**, chooses the nearest eligible container, displays a top-screen `[E]` prompt, and directly handles physical `Key.E`.
- PlayerInput defines `player_use` on LMB, and polls **held** input every physics tick. Player.cs uses this for firing, mining, consuming and placement.
- InputModes already owns modal input claims and mouse-release suppression. Reuse it, but **do not enter a movement-blocking modal mode simply because the radial is visible**.

**Migration goal:** change *selection, activation, prompt and LMB arbitration*, not existing death, loot, storage or saved world object ownership.

## 2. User-facing experience — acceptance contract

1. Kill a robot naturally. The ordinary saved wreck appears and contains its normal generated loot.
2. Within its existing configured interaction reach, hover its **visible artwork**, not just its logical origin. The compact translucent cyan action circle appears near the wreck's visible centre.
3. The centre action reads **Interact**; optional context text may read **Robot Wreck / Salvage**. For the first prototype, there are **no fake secondary actions**.
4. Press **E** (central `player_interact` action) or **click the actual centre button**. Both open the existing take-only loot window for **that exact wreck**. No new loot generation.
5. While the radial is visible, left mouse use is blocked: **no shooting, mining laser, consuming, placement, or other LMB world action**. This includes a click on radial background or outside its centre button.
6. Leaving the valid hover/wheel region, leaving reach, opening another UI, changing layer, dying, or losing/unloading the target hides the radial and restores normal control safely.
7. The player can still **move/sprint** while the nonmodal wheel is visible; other movement input such as **jump** must not be inadvertently suppressed by the LMB-only gate.
8. If LMB is held when the radial disappears or closes, do **not** fire until LMB is released and newly pressed.
9. Outside the radial, existing LMB weapons, mining, food use and placement work exactly as before. Normal E/GUI input for other existing containers must not break.
10. The same interaction contract can later provide **Open, Pick Up, Inspect, Repair, Refuel, Enter, Manage** actions for different objects, without hardcoded game-wide switch statements.

**Not now:** hover glow/outline, arbitrary building pickup, crafting stations, robot dismantling, extra salvage rewards, station inventories, vehicle refuelling, full key-remapping UI, content economy edits.

## 3. Intended modular structure

Only add files proven necessary; avoid empty placeholder folders and unnecessary scene nodes.

```text
SYSTEMS/
  Interaction/
    INTERACTION_ROBOT_WRECK_PHASE1_PLAN.md  [THIS DOCUMENT]
    IInteractionProvider.cs                  [new: action provider contract]
    InteractionAction.cs                     [new: small action descriptor]
    InteractionTarget.cs                     [new: selectable Area2D/anchor]
    InteractionResolver.cs                   [new: shared point-query + selection]
    StorageInteractionProvider.cs            [new: adapter for WorldStorage]

PLAYER/
  Interaction/
    PlayerInteractionController.cs           [new: input + state arbitration]
  PlayerInput.cs                             [modify narrowly]
  Player.tscn                                [modify only if necessary]

UI/
  Interaction/
    InteractionRadialHud.cs                  [new: one shared cyan wheel]
    InteractionRadialHud.tscn                [optional; not mandatory]

WORLDABLES/
  Objects/Storage/UI/StorageHud.cs           [modify: open explicit target]
  Objects/Loot/RobotWrecks/Basic/
    BasicRobotWreck.tscn                     [PREFER UNCHANGED, see save warning]

SYSTEMS/Input/InputModes.cs                 [extend only if needed]
SYSTEMS/Loot/LootWorld.cs                   [optional minimal attach/integration]
SYSTEMS/Loot/LootContainer.cs               [optional minimal adapter hookup]
```

**Responsibilities:**

- `IInteractionProvider` describes available stable action IDs (e.g., `open` / `salvage`), primary action, display label, eligibility and execution. The object owns its behaviour; no direct global input polling in the provider.
- `InteractionAction` is a lightweight descriptor; no per-frame `Node` for each possible action. Support a deterministic primary action and future secondary actions. Avoid per-frame allocations if possible.
- `InteractionTarget` is the cursor-detectable, nonblocking hit area and optional visual anchor. It associates a selectable host with its provider.
- `InteractionResolver` queries candidates under the mouse using an **interaction-only collision mask**, filters invalid targets and resolves overlap by a deterministic priority/tie-breaker.
- `PlayerInteractionController` owns current hovered target, valid range/layer checks, central E, wheel visibility and the LMB-use blocking contract. One controller per player, not one processor per object.
- `InteractionRadialHud` draws one shared CanvasLayer/Control anchored to the selected object. The circular background is subtle; centre is a real clickable UI control, not a passive label. The wheel prevents world-click leakage.
- `StorageInteractionProvider` adapts the existing `WorldStorage` and `LootContainer` to `StorageHud`. It does NOT duplicate `CanInteract`, loot rolling, storage contents or inventory transfer code.
- `StorageHud` keeps its existing container UI, close behaviour, events and InputModes ownership **after** opening.

Future world objects implement or adapt the same provider contract and can declare multiple actions. Workbench actions belong to the workbench, door actions to the door, etc.

## 4. Important save-compatibility trap: robot scene hashes

`LootWorld.InitializeServices()` calls `WorldObjectSaves.Ensure(...).RememberRecipe(_wreckScene)` for `BasicRobotWreck.tscn`. `CampaignRecipe.CaptureObjectDefinitions()` stamps saved object resource paths **and dependent resource bytes**; `CampaignRecipe.Validate()` rejects changed hashes except narrowly reviewed compatibility cases.

**A direct edit to `BasicRobotWreck.tscn` can therefore invalidate an existing saved campaign, even if the change is only an extra nonblocking Area2D.**

**First-pass preference:** attach the interaction target **at runtime** to the instantiated wreck, or use an equivalent stable, save-neutral adapter. Use the existing `LootWorld.RestoreWreck` / `LootContainer` lifecycle after scene creation to register it, and unregister naturally on scene removal. Preserve the original `.tscn` bytes and resource paths where feasible.

Check that scene modifications to `Player.tscn` or any scene stamped by the saving system do not unexpectedly cause recipe validation failures. If any stamped scene/resource must change, **stop and report which paths and old saves are affected**. Do not bypass validation, silently reset a campaign, or broadly whitelist scene hashes. Backward compatibility requires separate review.

No edits to `SYSTEMS/Saving/**`, `EntityDeaths` identity/delivery, `LootWorld` seeded roll or retained content logic, `WorldObjectSaves` schema, `LootContainer` persistent storage key, resource IDs or loot tables in this pass.

## 5. Targeting the visible isometric sprite

The wreck is a `Node2D` with a baked/drawn `192×144` style footprint, and its `TerrainVisual` applies a terrain-elevation offset. It has no blocking collision.

- Prefer a dedicated `Area2D`/`CollisionShape2D` (interaction-only bit, `Monitorable` where appropriate, never blocking movement or combat). Do not hijack the player's collision or mining/weapon raycasts.
- Target a **visible-artwork-aligned shape**, which may be offset upward from the wreck's ground position and must stay aligned while on varying terrain heights. Avoid making players hover an invisible point at the object's feet.
- The radial anchor can be a `Marker2D` or computed screen position at the visible object's approximate centre. It should follow camera motion and viewport resizing without jitter; clamp within viewport margins.
- Require `WorldStorage.CanInteract(player)` at selection and **again immediately before execution** (distance, same layer, live/valid nodes and player alive). Mouse position alone never confers infinite reach.
- When several objects overlap, use deterministic priority and sensible nearest cursor hit/depth ordering; never randomly cycle when the mouse is stationary.
- Keep a stable target while the cursor moves from the wreck shape onto its wheel. Treat the wheel region as belonging to the active target; avoid flicker or accidental dismissal during a button click.
- Choose bounded query work: one shared cached query at cursor-change/roughly 10–15 Hz (or comparable throttling), with a **fresh target validity check at click**. No group scan of every object each rendered frame, no repeated scene allocation, no per-wreck `_Process` for input.
- If the target is unloaded, freed or moved to another layer while hovered, clear it immediately and never execute a stale action.

## 6. Input priority and the E migration

### Central key binding

Add `PlayerInput.Interact = "player_interact"` with **E as the default binding**, following the existing conditional `InputMap.HasAction` / `AddKeys` pattern. The interaction system should listen for `player_interact`, **not raw physical `Key.E`**.

Do not make a second input action for each object. Later remapping should automatically affect the shared primary interaction.

### One owner for E

Today `StorageHud._UnhandledInput` directly opens the nearest eligible `WorldStorage` on physical E and its `_Input` closes a container with E/Escape/Delete. If the new controller also receives E, **one press must never open two windows or select a different object from the one highlighted.**

Prefer a coordinated single-controller ownership model: the interaction controller dispatches the in-world E request to a specific provider/`WorldStorage`, while `StorageHud` handles the **already-open modal container window** and its own close behavior. Reuse the existing storage adapter for ordinary crates and robot wrecks where this avoids duplicate E paths.

A narrowly scoped transitional exclusion is acceptable only if clearly documented and verified: migrated wrecks must not also be opened by the old "nearest storage" E scan, and unmigrated crates must remain usable. The displayed target and activated target must always match.

Expose a small explicit `StorageHud.TryOpen(WorldStorage target)`-style method, and preserve existing ownership, target recheck, event subscriptions, display, `Close()`, inventory transfer and close-on-out-of-range behaviour.

### LMB arbitration is mandatory

**Consuming a Godot GUI click is NOT enough.** `PlayerInput.Read()` polls physical `player_use` held state each physics frame; `Player.cs` then sends it to firing, `PlayerConsumption.Tick` and `PlayerPlacement.Tick`.

Implement one shared "world use blocked by visible interaction wheel" gate **before** `UseHeld` and `UsePressed` become true. Preserve physics tick ordering and existing `PLAYER/PLAYER_COMBAT_PHASE1_PLAN.md` attack-capture-before-consumption/placement semantics.

- Radial visible: block **all** LMB world use even if click misses the button.
- Radial hidden while mouse still held: remain blocked until button release; no deferred shot or missed first-release firing.
- Clicking Interact opens the existing loot UI and claims its normal container input mode. No shot on the same frame or on release.
- Show/hide wheel while holding mining laser: stop any old beam appropriately; normal laser resumes only on a new valid press after release.
- Keep movement and preferably jump intact. **Beware**: current `PlayerInput.Read()` ties `JumpPressed` to `useAllowed`; a naive reuse of `useAllowed=false` for wheel blocking can inadvertently disable RMB jump. Separate the interaction-only world-use gate from unrelated jumping/movement.
- GUI consumes its own button presses; pause/inventory/container UI always take precedence.
- Avoid setting `PlayerInputMode.Inventory` or `Container` just for the wheel. They would unnecessarily block walking.
- Continue to use InputModes' existing release guard wherever appropriate rather than layering conflicting global booleans.

## 7. Staged Codex delivery

### Pass 1A — Functional foundation (stop for local test)

- [ ] Reinspect all source at current HEAD; confirm no overlapping Codex changes in `Player.cs` / `PlayerInput.cs` / `StorageHud.cs`.
- [ ] Create the shared provider/action/target/resolver and player controller with minimal responsibilities.
- [ ] Add dedicated cursor targeting **at runtime** for robot wrecks, preserving existing saved scene resource bytes if possible.
- [ ] Register `player_interact` default E; give the controller authoritative in-world E selection.
- [ ] Add explicit, validated `StorageHud` opening by selected `WorldStorage`. Keep ordinary crates and the modal close key working; avoid double handlers.
- [ ] Provide a basic temporary near-object primary action prompt if the full radial is not ready, without rewiring loot.
- [ ] Test death → wreck → hover → E → loot → save/reload; verify normal LMB gameplay.
- [ ] **Stop and report changed files, tests and save compatibility** before visual pass 1B.

### Pass 1B — Cyan clickable wheel + input gate (stop for local test)

- [ ] Create one small, faint cyan translucent UI wheel anchored over the active target.
- [ ] Add a real centre **Interact** button with mouse `Filter`/input routing that consumes UI presses.
- [ ] Make **E and LMB centre click** execute the exact same selected action, revalidating source.
- [ ] Build the world-use gate in `PlayerInput` / current input ownership, including mouse-held closure guard.
- [ ] Hold target selection steady while cursor moves into the wheel; dismiss cleanly on hover loss/range/UI/layer/death/unload.
- [ ] Avoid background scans, temporary `Control` churn and per-instance processors.
- [ ] Verify all controls including firing/mining/consumption/placement, jumping and walking.
- [ ] **Stop for review**. No fake Pick Up, Repair, or crafting-station buttons yet.

### Later passes — only after approval

- [ ] Make a radial layout for multiple actions (positions, priority, availability, keyboard/gamepad hints).
- [ ] Migrate remaining container/door/station interaction types to the same contract.
- [ ] Add real Pick Up with **separate** inventory/world-object state and save transaction review.
- [ ] Add crafting stations with real `StationCrafting` actions, power/fuel/reach requirements and persistent queues in their respective feature passes.
- [ ] Add optional hover glow/outline as separate `VISUALS/Feedback` integration, **not** in this interaction slice.

## 8. Manual regressions to run inside Godot

- [ ] Build the C# project; no missing scripts/UIDs, warnings, editor parse errors or accidental node-path changes.
- [ ] Robot dies once, one wreck appears, same seeded loot as before; **no duplicate salvage rewards**.
- [ ] Hover in range above the *rendered body* at different terrain elevations; wheel appears over correct visual position.
- [ ] Hover outside reach / on other layer / over a noninteractive object: no action. Walk into range while hovering: appear; walk away: disappear.
- [ ] Click centre; E primary; click wheel background; click outside wheel; move cursor over wheel edge. No accidental gunshot or mining pulse.
- [ ] Hold LMB while entering/exiting wheel; close loot UI while LMB held; release and newly press to fire. No stuck suppression after release.
- [ ] Move/sprint and **RMB jump** while radial visible; confirm inventory/pause/debug menus have correct priority.
- [ ] Two nearby eligible containers: one stable shown target, action executes that same target; E is handled once.
- [ ] Normal crate opens/closes, stack transfer works; robot wreck is take-only, not a deposit container.
- [ ] Switch between blaster, mining laser, shovel, food and placeables; all remain functional after dismissing wheel.
- [ ] Close container with E/Escape/Delete as intended; walk out of range while open → safe close; no lingering input claim.
- [ ] Save before death, after death, after taking partial loot and after emptying it; quit fully and continue. **No rerolls, duplicate wrecks or lost loot**.
- [ ] Move between surface and caves, unload/reload area and remove a target while hovered; no null deref/stale action.
- [ ] Measure basic FPS with several wrecks visible and check no per-frame global container scans were added.
- [ ] Compare campaign recipe/resource hashes and test an **existing campaign made before the interaction pass**; never silently accept incompatible scene changes.

## 9. Explicit out-of-scope protection

No inventory/save schema changes, ore/vegetation harvesting refactor, item/catalog reorganization, crafting recipes/workstations, player combat overhaul, entity AI, actual structure pickup, new loot types, attack/weapon changes or new sprite/lighting features.

Retain existing `ResourceWorld` drops, `LootWorld` loot retention, `WorldStorage` transactions, `WorldObjectSaves`, `CampaignRecipe`, `PlayerHotbar` and old campaign compatibility. New interactions are presentation and dispatch of existing object behaviour.

Follow `NOTES/CodePreferences.md`: purpose header on each C# file, `// =========================================================` above major methods, sensible regions, minimal scene clutter, modular code and performance-aware processing. Do not introduce speculative global config without asking. In Codex implementation, provide reviewable diffs and record actual build/test evidence; do not claim Godot playtests that were not run.

## 10. Copy-ready Codex handoff

> Read `SYSTEMS/Interaction/INTERACTION_ROBOT_WRECK_PHASE1_PLAN.md`, `NOTES/CodePreferences.md` and the latest relevant source. Start by rechecking current HEAD and whether another Codex task is touching Player/Input/Combat.
>
> Implement **Pass 1A only**: a reusable universal interaction provider/action/target/resolver, player hover controller, centrally bound `player_interact` (E), a safe way to open the exact selected robot wreck's existing `StorageHud`, and basic feedback. Prefer a **runtime-attached Area2D** so `BasicRobotWreck.tscn` stays byte-for-byte unchanged and existing campaigns do not fail resource fingerprint validation. Keep legacy crates working; remove duplicate E handling for migrated targets. No change to robot death, loot generation, item transactions or save formats.
>
> After implementing, show the exact changed paths and either actual Godot tests or explicit local test instructions. **Stop for user approval** before Pass 1B (cyan clickable radial and LMB suppression). Do not silently implement extra actions or workstations.

---

**Maintenance:** This document is a proposed implementation plan, not evidence that interactions were implemented. Update statuses only after inspecting applied code and running or recording actual tests.
