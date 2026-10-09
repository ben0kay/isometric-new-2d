# World Layer Migration

## Goal

Extend the existing surface/cave system to support multiple underground
depths without hardcoding a separate system for each depth.

Examples:
- surface
- underground_1
- underground_2
- underground_3

These are stable internal identities. Each layer also has a separate
player-facing display name, such as Surface, Caverns or Deep Caverns.

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

## Current Foundation

The game currently has:
- A surface world.
- One underground cave world.
- Cave entrances connecting those two worlds.
- Independent surface and cave biome selection.
- Layer switching for visibility, processing and collision.
- Chunk streaming around the player.

The current layer identity is a fixed Surface/Cave distinction.

The additional depths described below are planned work, not implemented
features.

## Pass 1 — Layer Identities and Definitions

Status: Next pass. Code not yet applied.

Introduce extensible layer identities and data-driven layer definitions.

Each definition should provide:
- Stable internal ID.
- Display name.
- Depth index.
- Generation kind.
- References to the appropriate generation settings and biome catalog.

Register the existing surface and cave as:
- surface — depth 0.
- underground_1 — depth 1.

Use one authoritative registry for resolving layer definitions by ID.

Update current layer consumers to use the new identities. Preserve the
existing surface/cave transition while removing the fixed layer enum
and obsolete references once their consumers have migrated.

Do not create another playable underground depth in this pass.

### Completion Checks

- The surface loads normally.
- Entering and leaving the existing cave still works.
- Visibility and collision switch correctly.
- Existing actors remain associated with the correct layer.
- Layer definitions resolve consistently.
- No unused enum, duplicate identity system or migration adapter remains.

## Pass 2 — Independent Layer Worlds

Status: Planned.

Make world ownership and runtime services work with explicit layer IDs.

Each underground layer should own its:
- Generator and deterministic seed.
- Biome distribution.
- Loaded chunks.
- Collision and navigation data.
- Runtime content.

Actors, projectiles and interactions must distinguish exact layers.
Two actors being underground does not mean they occupy the same layer.

Reuse the underground world implementation for multiple instances.
Avoid copying CaveWorld into a separate class for every depth.

Update visibility and activation so the correct layer is active, while
allowing the departure layer to remain visible during a transition.

Adding a layer definition alone does not automatically create an entrance
or guarantee that its content has been implemented.

### Completion Checks

- Two underground layer instances can exist independently.
- Their generated layouts use independent seeds.
- Chunk ownership includes the layer identity.
- Collision and targeting do not cross between depths.
- Existing surface/cave behaviour still works.
- Obsolete single-cave assumptions are removed from migrated systems.

## Pass 3 — Connections Between Layers

Status: Planned.

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

Prepare the destination before transferring the player. Validate the
landing location using the destination layer's terrain and collision.

Preserve the existing entrance presentation where appropriate. Different
connection types may receive their own artwork and behaviour later.

### Completion Checks

- Travel from surface to underground_1 works.
- Travel from underground_1 to underground_2 works.
- Returning through both connections works.
- The correct destination chunks load.
- The player lands in a valid position.
- Visibility, collision and actor layer identity update together.
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
3. Confirm which pass has actually been applied.
4. Review affected scenes and resources as well as C# scripts.
5. Complete and clean up the current pass before starting the next.

Update the status and completion checks after each verified pass.