# Fractured Horizons — Player Combat Modularization (Phase 1)

> **Status: PLANNED.** This Markdown is a work plan only; no code or gameplay changes have yet been made.
>
> **Reviewed baseline:** `main` at `88a4976324c665a4cc2e43de16f5f9cd1d4dbc67` (2026-10-10). Reinspect the latest HEAD before implementing.
>
> **Related architecture:** `COMBAT/COMBAT_ARCHITECTURE_PLAN.md`.
>
> **Objective:** Extract only the player's combat input/facing/firing coordination from `PLAYER/Player.cs` into one reusable lightweight helper, without altering gameplay order, inventory, save data, resources or scene trees.

## 1. Scope: extremely small, save-neutral pass

Target:

```text
PLAYER/
├── PLAYER_COMBAT_PHASE1_PLAN.md       [this plan]
├── Player.cs                          [existing; minimal integration edits]
├── PlayerInput.cs                     [existing; UNCHANGED]
├── Player.tscn                        [existing; UNCHANGED]
├── Combat/
│   └── PlayerCombatController.cs      [NEW, plain C# helper; NOT a Node]
├── Equipment/                        [existing; UNCHANGED]
├── Inventory/                        [existing; UNCHANGED]
├── Stats/                            [existing; UNCHANGED]
├── Movement/                         [existing; UNCHANGED]
├── Gathering/                        [existing; UNCHANGED]
└── Survival/                         [existing; UNCHANGED]
```

**Only two code files may change once approved:** add
`PLAYER/Combat/PlayerCombatController.cs` and minimally edit
`PLAYER/Player.cs`. Do not add a scene node or change any node paths.
If a broader change seems necessary, stop and obtain approval.

### Responsibilities

- `PlayerInput` retains input bindings, state capture, UI/gameplay gating.
- `Player.cs` remains the orchestration point for the physics update,
  movement, jumping, terrain artwork, consumption, placement and survival.
- New `PlayerCombatController` is a plain `sealed class` and reads the
  already-captured input; it owns player-facing/aim-direction presentation,
  active-attack intent capture, requests to fire, and projectile-success
  work reporting. It holds references to the existing `Player`, `Weapon`
  and `TerrainVisual`, not additional scene nodes.
- Existing `COMBAT/Weapons/Weapon.cs` remains the ONLY authority for
  cooldown, muzzle offsets, height/cover-aware aiming, and attack delivery.
- `PlayerEquipment` and `PlayerHotbar` remain the ONLY authorities for
  item selection; their `SyncAttack` logic, including mining-emitter
  shutdown on weapon switches, remains untouched.
- The new helper must **not** resolve damage, implement raycasts, apply
  shield/armour, calculate new weapon stats, or modify shared attack resources.

Avoid speculative abstractions, extra update loops and needless allocations.
A future `PlayerAimController` or `PlayerMovement` can be considered in
its own pass, not here.

## 2. Audited existing update order: MUST preserve

At reviewed HEAD, `Player._PhysicsProcess(delta)` has this meaningful order:

1. Lazily resolve Survival, Consumption, Placement.
2. `_weapon.Tick(delta)` **before** `Controls.Read()`.
   This cooldown tick also runs on frames where the player is dead.
3. Read controls; update jump; report jump work to survival.
4. Early dead branch ticks survival/consumption/placement, clears velocity,
   updates artwork and jump pose, and returns **without firing**.
5. Update surface state, sprint multiplier, movement, terrain constraints,
   `MoveAndSlide`, artwork pose and environmental exposure.
6. Recheck death after exposure; early return when dead.
7. Capture **`AttackDefinition attack = _weapon.Attack`** and
   **`bool firing = Controls.UseHeld && attack != null`**.
8. Only **after that capture**, tick `_consumption.Tick(...)` and
   `_placement.Tick(...)`. Those actions can change the selected item.
9. While firing, horizontal facing uses the mouse's X relative to the
   player; otherwise facing follows movement X. No flip on zero X.
   Keep `_visual.Scale = new Vector2(_facing, 1f)` semantics.
10. If the previously captured firing intent was true, attempt
    `_weapon.TryFireAtCursor()` using existing aim/cooldown logic.
11. On successful shot **only when the captured attack is
    `ProjectileAttack`**, call
    `ReportWork(System.Math.Max(0.03, attack.Cooldown))`.
    Do not add identical work reporting for mining/digging; they have
    their own successful-work handlers.
12. Tick survival after computing actual movement, and preserve
    death/velocity/jump reset behaviour.

**Critical regression trap:** never recapture `_weapon.Attack` after
consumption/placement: this would silently change how using the last
item or switching hotbar slots behaves. Preserve the phase ordering,
including the weapon cooldown ticking before reading controls and both
dead checks. Do not move projectile-fire checks ahead of movement.

## 3. Recommended implementation contract

Introduce `PlayerCombatController` as a simple helper constructed
**after** async `PlaceholderAtlas.EnsureReady(this)` has completed and
`TerrainVisual.Attach` has provided a valid `_visual`.

Suggested responsibilities/methods (exact signatures up to implementer):

- `TickWeapon(double delta)`: calls the existing `Weapon.Tick(delta)`
  at the original point in the player's physics update.
- `CaptureIntent(bool useHeld)`: captures the current
  `Weapon.Attack` and the boolean firing intent *before* the
  consumption and placement operations. Return a compact value-type
  struct or equivalent, not a newly allocated reference each frame.
- `UpdateFacingAndTryFire(intent, float movementX)`: uses mouse/movement
  horizontal direction to update existing `TerrainVisual` facing and
  invokes `Weapon.TryFireAtCursor()` at its original step. On accepted
  projectile fire, forwards work duration through existing
  `Player.ReportWork(double)`.

Example **pseudocode only**, not drop-in code:

```csharp
// Player._Ready, after terrain visual creation:
_combat = new PlayerCombatController(this, _weapon, _visual);

// Player._PhysicsProcess:
_combat.TickWeapon(delta); // before Controls.Read
Controls.Read();
// existing jump, movement, visuals, exposure, both dead guards...
var intent = _combat.CaptureIntent(Controls.UseHeld);
_consumption.Tick(delta, Controls.UseHeld);
_placement.Tick(delta);
_combat.UpdateFacingAndTryFire(intent, direction.X);
// existing survival/movement updates...
```

Keep `Player.cs`'s other runtime references intact. Transfer only
the facing variable if doing so does not change its lifetime or value.
No new Godot nodes, autoloads, singleton/event bus, subscriptions,
timers, signals, `.tres` resources or global config in this pass.

Code style: read `NOTES/CodePreferences.md`: short file-top purpose
comment, optional regions, comment headers above important methods and
`// =========================================================` separators; compact, readable methods.

## 4. Save work that must remain untouched

**Do not change any of the following in Phase 1:**

- `SYSTEMS/Saving/**`, including `CampaignData.cs` and
  `PlayerSaveSections.cs`; no schema changes, version bump, migration,
  changed JSON keys or restoration order.
- `PLAYER/Inventory/**`, `PLAYER/Equipment/**`, `PLAYER/Stats/**`;
  no hotbar, item identity, capacity, modifier or tool-slot changes.
- `SYSTEMS/Crafting/**`, ingredients, recipe output and timed queue.
- `ITEMS/**`, `ItemDefinition`, catalogs and stable item IDs.
- `COMBAT/**` live C# implementation, `PlayerBlaster.tres`,
  `MiningLaser.tres`, shovel resources, and audio/visual feedback.
- `PLAYER/Player.tscn` and all other `.tscn`/`.tres` references and UIDs.
- `PlayerInput` mapping, player movement/sprint/jump/exposure, survival
  and current combat hitbox/cover rules.

Your existing save stores item IDs and stack quantities, tool/hotbar
references, Health, player stats and crafting state. Do not make any of
these assumptions incompatible as part of this organizational refactor.

**Important future concern, NOT Phase 1:** manufactured weapon modules
and refinements need unique per-item-instance data. The existing stack,
hotbar and save approach does not distinguish two differently upgraded
weapons of the same base ID. This requires a separate coordinated
inventory/equipment/save migration, with legacy save compatibility. It
must not be partially implemented in this pass.

## 5. Implementation checklist for a future Codex run

### Inspect and prepare

- [ ] Read this document, `COMBAT/COMBAT_ARCHITECTURE_PLAN.md`,
      and `NOTES/CodePreferences.md`.
- [ ] Reinspect current HEAD `Player.cs`, `PlayerInput.cs`,
      `Weapon.cs`, `PlayerEquipment.cs`, `PlayerHotbar.cs` and
      all relevant saving adapters; reconcile differences from baseline.
- [ ] Establish a clean build baseline or record preexisting errors.
- [ ] Keep a backup of a known-good campaign save before testing.

### Implement — only once explicitly approved

- [ ] Create the one new plain `PlayerCombatController.cs`.
- [ ] Make the minimal integration edit to `Player.cs` only.
- [ ] Preserve all twelve ordered behaviours in §2.
- [ ] No save, scene, inventory, crafting, equipment, stat,
      shared-combat or item/catalog edits.
- [ ] No additional processing nodes or unnecessary allocations each tick.
- [ ] Build Godot C# and fix any errors introduced by this pass.
- [ ] Review changed file list: expected **two code files**.

### In-game acceptance tests

- [ ] Open a new game; load an existing campaign; no errors or broken paths.
- [ ] Player blaster: click/hold, aim, projectile speed/direction and cooldown
      unchanged. Fire while moving and jumping.
- [ ] Player sprite faces properly left/right when moving and when firing;
      does not flip unpredictably at zero horizontal input.
- [ ] Switch hotbar across blaster, mining laser, shovel, consumables
      and empty slot. Switching tools stops an old mining beam.
- [ ] Mining, digging, item consumption and placement work as before;
      no extra attack or changed work/fatigue reporting.
- [ ] Movement, sprint, jump, surface elevation, exposure and survival
      continue normally.
- [ ] Death prevents attacks and respawn restores expected behaviour.
- [ ] Move/swap inventory items, hotbar references and tool selection.
- [ ] Queue and complete a regular crafting recipe.
- [ ] Save, exit, reload: verify health, inventory, selected hotbar,
      equipped tools, crafting queue, stats, world position/layer and
      survival state; no new save fields should be present.
- [ ] Check debugger for warnings, exceptions and resource/scene errors.

### Review gate

- [ ] Report actual build/playtest results (do not claim unrun tests).
- [ ] Record exact changed paths and commit SHA.
- [ ] Update status/checklist only after successful implementation.
- [ ] **Stop for review**, do not begin Phase 2 automatically.

## 6. Later phases — separate approval required

1. **Combat foundation:** `COMBAT/` damage packets/results, optional
   Shield → Armour → Health/Hull, impacts, status effects, areas,
   projectiles, beams and mines (see separate combat plan).
2. **Weapon stat architecture:** independent base weapon data plus a
   cached runtime resolver; do not mutate shared Godot resources to
   customize a single weapon.
3. **Unique manufactured weapons:** per-instance identity plus inventory,
   hotbar, crafting and backward-compatible save support designed as a
   single migration; protect existing campaigns.
4. **Modules and refinements:** compatible slots, removable physical
   modules, permanent improvements, manufacturing quality and eventual
   ammo/charge, heat and condition as explicitly approved.
5. **Optional Player folder cleanup:** break out movement/aim controllers
   only if they actually reduce complexity after the above passes.
6. **Player defensive equipment:** armour, shield generators and optional
   modifiers feeding the same shared COMBAT layered-defence system. See below.

### Future player armour, shield generators and defensive equipment (NOT Phase 1)

**Design status: FUTURE / NOT IMPLEMENTED.** This section records the long-term
player equipment contract. It must **not** broaden the save-neutral
`PlayerCombatController` extraction in Phase 1. Coordinate implementation with
`COMBAT/COMBAT_ARCHITECTURE_PLAN.md` after the common defence and damage
resolution layers are approved.

**Default player:** only **Health** (core vitality) is present. No automatic
Shield or Armour pool, no permanent shield capacity, and no default armour
capacity. The core vitality uses the existing `Health.cs` behaviour;
`VitalityKind.Health` vs `VitalityKind.Hull` is a presentation/label distinction,
**not two separate underlying health systems**. Creatures may use Health;
mechanical entities may show Hull. The player's core remains labelled Health.

**Equipped player gear may add optional defence layers** in this hit order:

`incoming hit → Shield (if equipped) → Armour (if equipped) → Health`

- **Armour equipment:** future helmet, chest armour, legs and/or a suit
  can supply Armour capacity, damage-type resistances and possibly trade-offs
  such as carrying weight and mobility. Equipping gear activates or adjusts
  the optional Armour layer; removing all providers returns the player to
  Health-only (or Shield → Health when a shield is still equipped).
- **Shield equipment:** a generator, suit or optional shield module can
  supply Shield capacity, recharge rate, recharge delay and type-specific
  resistance. Without any active shield provider, **no Shield layer exists**.
- **Health never disappears** when gear is equipped. Health reaches zero
  under the shared death system; empty/broken Shield or Armour does not
  independently kill the player.
- **Do not create player-only damage math.** Equipment contributes immutable
  resource data to the shared `COMBAT/Defenses/DefenseController`,
  `DefenseLayerDefinition` and runtime per-actor defence state. The common
  resolver handles absorption, damage type, penetration and overflow.
- Armour and shield item definitions remain ordinary catalogued
  `ITEMS/Definitions/Equipment/Armor/**` and
  `ITEMS/Definitions/Equipment/Gadgets/**` or another agreed item category.
  `PLAYER/Equipment/` owns the *player's slot management and equip actions*,
  not duplicate armour/shield resolution. A future dedicated
  `PLAYER/Equipment/Armor/` or `Shields/` folder is optional **only if there
  is actual player-specific code to put there**; don't create empty folders.
- Equipment benefits may combine with character stats and later
  modules/refinements, but should be recomputed **when loadout changes**,
  not for every damage tick or each rendered frame. Shared Godot `.tres`
  definitions must never carry an individual's mutable shield/armour damage.
- HUD can later show Shield, Armour and Health separately, with correct
  damage-number and shield-break feedback, while a starting player still
  shows their existing Health display unchanged.
- Defence pieces may have **their own item condition/durability** in the
  future. If implemented, distinguish the condition of a specific piece
  from the current combined Armour pool. Do not silently conflate these
  two forms of state or duplicate them in saves.

**Unresolved choices (ask before implementing):**

1. Do multiple armour pieces contribute to a **single combined Armour pool**,
   separate pools per body slot, or resistances without a pool? Proposed
   first version: one combined optional Armour pool, with per-item condition
   deferred.
2. If the maximum Shield/Armour capacity changes while damaged, how should
   current points change (keep absolute amount, keep percentage, or clamp)?
   Also define what happens on unequip/re-equip to prevent free repairs.
3. How do shield/armour resistances combine across pieces, and how do
   penetrating attacks bypass individual layers?
4. Which equipment slots exist, and can a suit generate both Armour and
   Shield? Prefer data-driven provision over hardcoded player-specific types.
5. Decide the policy for recharge, out-of-combat repair, death, respawn,
   and removing equipment while layers are damaged.

**Save compatibility is non-negotiable for this future pass.** Current
campaigns have player Health and existing equipment/quickslots only.
Adding armour/shield equipment slots and defence values needs a coordinated,
backwards-compatible extension to equipment, inventory and save/restore.
Older saves must load as Health-only with valid existing tool selections;
new saves must preserve worn gear, shield charge, armour damage and relevant
modifications. No save change is authorized by this planning update.

**Future implementation acceptance checks:**

- [ ] Brand-new player has Health only; old campaigns continue to load.
- [ ] Wearing armour adds only Armour; a shield generator adds Shield.
- [ ] Different loadouts correctly resolve Shield → Armour → Health.
- [ ] Removing equipment recalculates capacity without duplicating repairs.
- [ ] A shield-only hit does not report false Health loss or trigger death.
- [ ] Unequip, inventory transfer, save/reload and respawn retain expected
      gear identity and layer values.
- [ ] Existing player input, tools, crafting and survival behaviour remain
      unaffected by defensive equipment work.

## 7. Copy-ready Codex hand-off

> Read `PLAYER/PLAYER_COMBAT_PHASE1_PLAN.md`,
> `COMBAT/COMBAT_ARCHITECTURE_PLAN.md` and
> `NOTES/CodePreferences.md`. Compare with current main first.
> Implement **only Player Phase 1**: add a plain
> `PLAYER/Combat/PlayerCombatController.cs` and make the minimal
> integration edit in `PLAYER/Player.cs`. Retain exact input, firing,
> attack-capture-before-consumption/placement, facing, jump, cooldown,
> mining/digging and `ReportWork` behaviour. Do not touch scenes,
> `SYSTEMS/Saving/`, items, inventories, crafting, stats, equipment or
> shared combat code. Build/test if possible, give explicit Godot
> regression checks, and **stop for review** before another phase.

---

**Maintenance:** This plan records intentions, not implemented features.
When executing, update the baseline and checkboxes with verified facts.
