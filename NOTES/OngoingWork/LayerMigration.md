# World Layer Migration

## Goal

Extend the surface/cave system to support multiple underground depths
without hardcoding a separate system for each depth.

Examples:
- surface
- underground_1
- underground_2
- underground_3

These are stable internal identities. Each layer also has a separate
player-facing display name, such as Surface, Upper Caverns or Deep Caverns.

Keep the existing surface and first cave layer working throughout
the migration.

## Working Rules

- Keep shared layer coordination in WORLD/Layers/.
- Keep underground generation in WORLD/Generation/Caves/.
- Keep layer definitions in WORLD/Layers/Definitions/.
- Reuse shared generation, streaming and transition mechanics.
- Keep each layer's settings, seed and biome distribution independent.
- Prefer focused components over expanding one large controller.
- Update all affected consumers during each pass.
- Remove obsolete files, names and temporary adapters within that pass.
- Do not leave duplicate implementations or long-lived compatibility code.
- Provide complete replacement files or complete replacement functions.
- Review the latest GitHub push before preparing each code pass.

Performance tuning is separate work. This migration should preserve
existing streaming behaviour rather than introduce a new scheduling
system at the same time.

## Current Status

- Pass 1: Implemented and tested.
- Pass 2: Implemented; not fully tested yet.
- Pass 3: Planned; not implemented.

These statuses describe migration passes, not playable depth levels.
Deep Caverns is registered but is not yet reachable through gameplay.

## Current Foundation

The game has:
- A surface world.
- Surface entrances connecting to Upper Caverns.
- Independent surface and cave biome selection.
- Layer switching for visibility, processing and collision.
- Chunk streaming around the player.
- Stable string identities resolved through WorldLayerCatalog.
- WorldLayerRuntime ownership of independent underground instances.

Pass 2 registers Upper Caverns and Deep Caverns as separate runtime worlds.
Existing ramps still connect the surface to Upper Caverns only.

Dormant underground layers do not build chunks until activated or
explicitly preloaded.

## Pass 1 — Layer Identities and Definitions

Status: Implemented and tested.

Introduce extensible layer identities and data-driven layer definitions.

Each definition provides:
- Stable internal ID.
- Display name.
- Depth index.
- Generation kind.
- References to generation settings and the biome catalog.

The existing surface and cave are registered as:
- surface — depth 0.
- underground_1 — depth 1.

WorldLayerCatalog is the authoritative registry for resolving definitions.

Current layer consumers use the new identities. The fixed layer enum
has been removed while preserving the existing surface/cave transition.

### Verification

The existing surface/cave flow was tested successfully after Pass 1.

Repeat these checks after later migration changes:
- The surface loads normally.
- Entering and leaving the existing cave works.
- Visibility and collision switch correctly.
- Existing actors remain associated with the correct layer.
- Layer definitions resolve consistently.
- No unused enum, duplicate identity system or migration adapter remains.

## Pass 2 — Independent Layer Worlds

Status: Implemented; not fully tested yet.

World ownership and runtime services now resolve explicit layer IDs.

Each underground layer owns its:
- Generator and deterministic seed.
- Biome distribution.
- Loaded chunks.
- Collision and navigation data.
- Runtime object root.

Actors, projectiles and interactions must distinguish exact layers.
Two actors being underground does not mean they occupy the same layer.

WorldLayerRuntime reuses CaveWorld for multiple independent instances.
There is no separate CaveWorld implementation for each depth.

Pass 2 adds:
- WORLD/Layers/WorldLayerRuntime.cs
- WORLD/Layers/Definitions/Underground2/Underground2.tres
- WORLD/Layers/Definitions/Underground2/Underground2Generation.tres

Each underground definition now owns FloorElevation.
The global CaveFloorElevation export has been removed.

SurfaceEntranceLayerId in the catalog selects the destination of
existing surface ramps.

Navigation, elevation, shadows, drops and wrecks resolve exact layer IDs.
Deeper layers do not prepare surface basin or entrance metadata.

Stable identity hashes separate underground seeds. Existing cave layouts
may therefore change for the same world seed. Surface generation is unchanged.

Adding a layer definition does not automatically create an entrance
or implement its content. Deep Caverns is not yet reachable.

### Pending Verification

Do not mark this pass fully tested until the relevant checks pass:

- The project builds without errors.
- Surface → Upper Caverns → Surface still works.
- Returning through a different surface entrance works.
- Visibility, collision and player ownership switch correctly.
- Enemy navigation, combat and pursuit still work.
- Drops, wrecks and shadows use the correct layer.
- Two underground instances exist independently.
- Deep Caverns remains dormant when unused.
- Underground layouts use independent deterministic seeds.

Cross-depth traversal and its interaction checks require Pass 3.

## Pass 3 — Connections Between Layers

Status: Planned; not implemented.

Generalize entrances into explicit connections.

Each connection identifies:
- Source layer.
- Source location.
- Destination layer.
- Destination location.

Reuse the transition mechanism for:
- surface ↔ underground_1.
- underground_1 ↔ underground_2.
- Further connections when added later.

Prepare the destination before transferring the player.
Validate the landing location using the destination's terrain and collision.

Update visibility and activation so the correct layer becomes active,
while allowing the departure layer to remain visible during a transition.

Preserve the existing entrance presentation where appropriate.
Different connection types may receive their own artwork and behaviour later.

### Completion Checks

- Travel from surface to underground_1 works.
- Travel from underground_1 to underground_2 works.
- Returning through both connections works.
- The correct destination chunks load.
- The player lands in a valid position.
- Visibility, collision and actor layer identity update together.
- Combat and interactions do not cross between depths.
- The old surface-only entrance assumptions are removed.

## Outside This Migration

These are separate future tasks:
- Investigating and reducing transition frame spikes.
- A shared generation work scheduler.
- Additional cave content and species.
- Depth-based danger and resource progression.
- New entrance artwork.
- Save/load support for multiple depths.
- Layer-qualified persistent object IDs.

The migration must make room for these features without claiming
they are already implemented.

## Handoff

Before continuing:
1. Read this README.
2. Check the latest GitHub push.
3. Confirm which pass has actually been applied and tested.
4. Review affected scenes and resources as well as C# scripts.
5. Complete and clean up the current pass before starting the next.

Current next step: finish Pass 2 verification, then prepare Pass 3.

Update the status and completion checks after each verified pass.